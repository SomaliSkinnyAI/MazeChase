"""
Deep Q-Network (Double DQN) trainer for MazeChase.

Connects to N parallel Unity instances via rl_environment.py, collects
experience, and trains a Q-network with experience replay + target network.

Exports weights in the same .npz format as train_policy.py so the existing
export_weights.py pipeline works unchanged.

Usage:
    python dqn_trainer.py --game path/to/MazeChase.exe --n-envs 4 --total-steps 2000000
"""

import argparse
import json
import time
from pathlib import Path
from typing import Dict, Optional

import numpy as np

from rl_environment import VectorizedUnityEnvironment, UnityEnvironment, OBS_SIZE, NUM_ACTIONS

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def relu(x: np.ndarray) -> np.ndarray:
    return np.maximum(x, 0.0)


def he_init(rng: np.random.Generator, rows: int, cols: int) -> np.ndarray:
    """He normal initialization."""
    std = np.sqrt(2.0 / cols)
    return (rng.standard_normal((rows, cols)) * std).astype(np.float32)


# ---------------------------------------------------------------------------
# Q-Network (pure NumPy, same topology as behavioral cloning MLP)
# ---------------------------------------------------------------------------

def make_params(rng: np.random.Generator, input_size: int, hidden1: int, hidden2: int) -> Dict[str, np.ndarray]:
    return {
        "W1": he_init(rng, hidden1, input_size),
        "b1": np.zeros(hidden1, dtype=np.float32),
        "W2": he_init(rng, hidden2, hidden1),
        "b2": np.zeros(hidden2, dtype=np.float32),
        # Q-head stored as Wp/bp so export_weights.py works unchanged.
        "Wp": he_init(rng, NUM_ACTIONS, hidden2),
        "bp": np.zeros(NUM_ACTIONS, dtype=np.float32),
        # Dummy value/risk heads (zeros) — not used in DQN but needed for export compat.
        "Wv": np.zeros(hidden2, dtype=np.float32),
        "bv": np.zeros(1, dtype=np.float32),
        "Wr": np.zeros(hidden2, dtype=np.float32),
        "br": np.zeros(1, dtype=np.float32),
    }


def copy_params(params: dict) -> dict:
    return {k: v.copy() for k, v in params.items()}


def q_forward(params: dict, x: np.ndarray, legal_mask: np.ndarray):
    """
    Forward pass through Q-network.
    x: (batch, input_size)
    legal_mask: (batch, NUM_ACTIONS) bool
    Returns: q_values (batch, NUM_ACTIONS), q_masked, cache
    """
    z1 = x @ params["W1"].T + params["b1"]
    h1 = relu(z1)
    z2 = h1 @ params["W2"].T + params["b2"]
    h2 = relu(z2)
    q_values = h2 @ params["Wp"].T + params["bp"]
    q_masked = np.where(legal_mask, q_values, -1e9)
    cache = (x, z1, h1, z2, h2)
    return q_values, q_masked, cache


def q_backward(params: dict, cache: tuple, q_values: np.ndarray, targets: np.ndarray, actions: np.ndarray):
    """
    Backward pass with Huber loss on TD error for the taken action only.
    targets: (batch,) — TD targets for the taken action.
    actions: (batch,) int — action indices taken.
    Returns: grads dict.
    """
    x, z1, h1, z2, h2 = cache
    batch_size = x.shape[0]

    # Compute TD error for the taken action.
    q_taken = q_values[np.arange(batch_size), actions]
    td_error = q_taken - targets

    # Huber loss gradient: smooth L1.
    huber_grad = np.clip(td_error, -1.0, 1.0) / batch_size

    # dQ: only the taken action gets gradient.
    dq = np.zeros_like(q_values)
    dq[np.arange(batch_size), actions] = huber_grad

    grads = {}
    grads["Wp"] = dq.T @ h2
    grads["bp"] = np.sum(dq, axis=0)

    dh2 = dq @ params["Wp"]
    dz2 = dh2 * (z2 > 0)
    grads["W2"] = dz2.T @ h1
    grads["b2"] = np.sum(dz2, axis=0)

    dh1 = dz2 @ params["W2"]
    dz1 = dh1 * (z1 > 0)
    grads["W1"] = dz1.T @ x
    grads["b1"] = np.sum(dz1, axis=0)

    # Dummy grads for value/risk heads (not trained in DQN).
    for key in ("Wv", "bv", "Wr", "br"):
        grads[key] = np.zeros_like(params[key])

    return grads


# ---------------------------------------------------------------------------
# Adam optimizer (matches train_policy.py)
# ---------------------------------------------------------------------------

def make_adam_state(params: dict) -> dict:
    return {
        "step": 0,
        "m": {k: np.zeros_like(v) for k, v in params.items()},
        "v": {k: np.zeros_like(v) for k, v in params.items()},
    }


def adam_update(params: dict, grads: dict, state: dict, lr: float, beta1=0.9, beta2=0.999, eps=1e-8):
    state["step"] += 1
    t = state["step"]
    for key in params:
        if key not in grads:
            continue
        state["m"][key] = beta1 * state["m"][key] + (1 - beta1) * grads[key]
        state["v"][key] = beta2 * state["v"][key] + (1 - beta2) * (grads[key] ** 2)
        m_hat = state["m"][key] / (1 - beta1 ** t)
        v_hat = state["v"][key] / (1 - beta2 ** t)
        params[key] -= lr * m_hat / (np.sqrt(v_hat) + eps)


# ---------------------------------------------------------------------------
# Replay Buffer (pre-allocated NumPy arrays)
# ---------------------------------------------------------------------------

class ReplayBuffer:
    def __init__(self, capacity: int, obs_size: int):
        self.capacity = capacity
        self.obs = np.zeros((capacity, obs_size), dtype=np.float32)
        self.actions = np.zeros(capacity, dtype=np.int32)
        self.rewards = np.zeros(capacity, dtype=np.float32)
        self.next_obs = np.zeros((capacity, obs_size), dtype=np.float32)
        self.legal_masks = np.zeros((capacity, NUM_ACTIONS), dtype=bool)
        self.next_legal_masks = np.zeros((capacity, NUM_ACTIONS), dtype=bool)
        self.dones = np.zeros(capacity, dtype=bool)
        self.size = 0
        self.pos = 0

    def add(self, obs, action, reward, next_obs, legal_mask, next_legal_mask, done):
        """Add single transition or batch of transitions."""
        if obs.ndim == 1:
            self.obs[self.pos] = obs
            self.actions[self.pos] = action
            self.rewards[self.pos] = reward
            self.next_obs[self.pos] = next_obs
            self.legal_masks[self.pos] = legal_mask
            self.next_legal_masks[self.pos] = next_legal_mask
            self.dones[self.pos] = done
            self.pos = (self.pos + 1) % self.capacity
            self.size = min(self.size + 1, self.capacity)
        else:
            n = obs.shape[0]
            for i in range(n):
                self.obs[self.pos] = obs[i]
                self.actions[self.pos] = action[i]
                self.rewards[self.pos] = reward[i]
                self.next_obs[self.pos] = next_obs[i]
                self.legal_masks[self.pos] = legal_mask[i]
                self.next_legal_masks[self.pos] = next_legal_mask[i]
                self.dones[self.pos] = done[i]
                self.pos = (self.pos + 1) % self.capacity
                self.size = min(self.size + 1, self.capacity)

    def sample(self, rng: np.random.Generator, batch_size: int):
        indices = rng.integers(0, self.size, size=batch_size)
        return (
            self.obs[indices],
            self.actions[indices],
            self.rewards[indices],
            self.next_obs[indices],
            self.legal_masks[indices],
            self.next_legal_masks[indices],
            self.dones[indices],
        )


# ---------------------------------------------------------------------------
# Epsilon schedule
# ---------------------------------------------------------------------------

def epsilon_schedule(step: int, eps_start: float, eps_end: float, eps_decay_steps: int) -> float:
    if step >= eps_decay_steps:
        return eps_end
    return eps_start + (eps_end - eps_start) * (step / eps_decay_steps)


# ---------------------------------------------------------------------------
# Save / checkpoint
# ---------------------------------------------------------------------------

def save_model(params: dict, path: Path, input_size: int, hidden1: int, hidden2: int):
    np.savez_compressed(
        str(path),
        input_size=input_size,
        hidden1_size=hidden1,
        hidden2_size=hidden2,
        W1=params["W1"],
        b1=params["b1"],
        W2=params["W2"],
        b2=params["b2"],
        Wp=params["Wp"],
        bp=params["bp"],
        Wv=params["Wv"],
        bv=params["bv"],
        Wr=params["Wr"],
        br=params["br"],
    )


def export_to_unity_json(params: dict, path: Path, input_size: int, hidden1: int, hidden2: int, source: str = "dqn"):
    """Direct export to Unity JSON (bypasses export_weights.py for convenience)."""
    payload = {
        "version": "2.0",
        "source": source,
        "inputSize": int(input_size),
        "hidden1Size": int(hidden1),
        "hidden2Size": int(hidden2),
        "w1": params["W1"].astype(float).reshape(-1).tolist(),
        "b1": params["b1"].astype(float).reshape(-1).tolist(),
        "w2": params["W2"].astype(float).reshape(-1).tolist(),
        "b2": params["b2"].astype(float).reshape(-1).tolist(),
        "policyW": params["Wp"].astype(float).reshape(-1).tolist(),
        "policyB": params["bp"].astype(float).reshape(-1).tolist(),
        "valueW": params["Wv"].astype(float).reshape(-1).tolist(),
        "valueB": params["bv"].astype(float).reshape(-1).tolist(),
        "riskW": params["Wr"].astype(float).reshape(-1).tolist(),
        "riskB": params["br"].astype(float).reshape(-1).tolist(),
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload), encoding="utf-8")


# ---------------------------------------------------------------------------
# Evaluation
# ---------------------------------------------------------------------------

def evaluate(env: UnityEnvironment, params: dict, n_episodes: int = 10) -> dict:
    """Run greedy policy for n_episodes, return stats."""
    scores = []
    pellets_eaten = []
    steps_list = []
    deaths = []

    for ep in range(n_episodes):
        obs = env.reset()
        ep_score = 0
        ep_steps = 0
        ep_deaths = 0
        done = False

        while not done:
            legal = np.ones(NUM_ACTIONS, dtype=bool)  # All legal as fallback.
            _, q_masked, _ = q_forward(params, obs[None], legal[None])
            action = int(np.argmax(q_masked[0]))
            obs, reward, done, info = env.step(action)
            ep_score = info.get("score", ep_score)
            ep_steps += 1
            if info.get("death", False):
                ep_deaths += 1

        scores.append(ep_score)
        total_pellets = info.get("total_pellets", 244)
        remaining = info.get("remaining_pellets", 0)
        pellets_eaten.append(total_pellets - remaining)
        steps_list.append(ep_steps)
        deaths.append(ep_deaths)

    return {
        "mean_score": float(np.mean(scores)),
        "max_score": float(np.max(scores)),
        "mean_pellets": float(np.mean(pellets_eaten)),
        "mean_steps": float(np.mean(steps_list)),
        "mean_deaths": float(np.mean(deaths)),
        "episodes": n_episodes,
    }


# ---------------------------------------------------------------------------
# Main training loop
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="DQN trainer for MazeChase")
    parser.add_argument("--game", type=str, default=None, help="Path to MazeChase.exe")
    parser.add_argument("--n-envs", type=int, default=4, help="Number of parallel environments")
    parser.add_argument("--base-port", type=int, default=9090)
    parser.add_argument("--total-steps", type=int, default=2_000_000)
    parser.add_argument("--hidden1", type=int, default=192)
    parser.add_argument("--hidden2", type=int, default=96)
    parser.add_argument("--lr", type=float, default=3e-4)
    parser.add_argument("--gamma", type=float, default=0.99)
    parser.add_argument("--batch-size", type=int, default=256)
    parser.add_argument("--buffer-size", type=int, default=500_000)
    parser.add_argument("--learning-starts", type=int, default=10_000)
    parser.add_argument("--train-freq", type=int, default=4, help="Train every N env steps")
    parser.add_argument("--target-update-freq", type=int, default=5_000)
    parser.add_argument("--eps-start", type=float, default=1.0)
    parser.add_argument("--eps-end", type=float, default=0.05)
    parser.add_argument("--eps-decay-steps", type=int, default=200_000)
    parser.add_argument("--eval-freq", type=int, default=20_000)
    parser.add_argument("--eval-episodes", type=int, default=10)
    parser.add_argument("--eval-port", type=int, default=9099, help="Port for eval env")
    parser.add_argument("--checkpoint-dir", type=str, default="tools/ai/dqn_checkpoints")
    parser.add_argument("--time-scale", type=float, default=20.0)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--log-freq", type=int, default=5_000, help="Print stats every N steps")
    args = parser.parse_args()

    rng = np.random.default_rng(args.seed)
    checkpoint_dir = Path(args.checkpoint_dir)
    checkpoint_dir.mkdir(parents=True, exist_ok=True)

    input_size = OBS_SIZE  # 102
    print(f"[DQN] Config: envs={args.n_envs}, steps={args.total_steps}, "
          f"hidden={args.hidden1}x{args.hidden2}, lr={args.lr}, gamma={args.gamma}")

    # Initialize networks.
    online_params = make_params(rng, input_size, args.hidden1, args.hidden2)
    target_params = copy_params(online_params)
    adam_state = make_adam_state(online_params)
    replay = ReplayBuffer(args.buffer_size, input_size)

    # Launch training environments.
    print(f"[DQN] Launching {args.n_envs} training environments...")
    vec_env = VectorizedUnityEnvironment(
        n_envs=args.n_envs,
        game_path=args.game,
        base_port=args.base_port,
        time_scale=args.time_scale,
        base_seed=args.seed,
    )
    obs = vec_env.reset_all()
    # Legal masks — we get these from the state messages on next step.
    # For the initial obs, assume all actions legal (will be corrected on first step).
    legal_masks = np.ones((args.n_envs, NUM_ACTIONS), dtype=bool)

    # Launch eval environment.
    print(f"[DQN] Launching eval environment on port {args.eval_port}...")
    eval_env = UnityEnvironment(
        game_path=args.game,
        port=args.eval_port,
        time_scale=args.time_scale,
        seed=args.seed + 1000,
    )

    # Tracking.
    total_steps = 0
    episode_rewards = [0.0] * args.n_envs
    episode_scores = [0] * args.n_envs
    episode_lengths = [0] * args.n_envs
    completed_episodes = 0
    recent_scores = []
    recent_rewards = []
    best_eval_score = 0.0
    train_losses = []
    t_start = time.monotonic()

    print(f"[DQN] Starting training loop...")

    while total_steps < args.total_steps:
        # Epsilon-greedy action selection.
        eps = epsilon_schedule(total_steps, args.eps_start, args.eps_end, args.eps_decay_steps)
        actions = np.zeros(args.n_envs, dtype=np.int32)
        for i in range(args.n_envs):
            if rng.random() < eps:
                # Random legal action.
                legal_indices = np.where(legal_masks[i])[0]
                if len(legal_indices) > 0:
                    actions[i] = rng.choice(legal_indices)
                else:
                    actions[i] = rng.integers(0, NUM_ACTIONS)
            else:
                _, q_masked, _ = q_forward(online_params, obs[i:i+1], legal_masks[i:i+1])
                actions[i] = int(np.argmax(q_masked[0]))

        # Step all environments.
        next_obs, rewards, dones, infos = vec_env.step(actions)

        # Extract legal masks from info (if available) or default to all-legal.
        next_legal_masks = np.ones((args.n_envs, NUM_ACTIONS), dtype=bool)
        # Note: legal masks come from the state message, which is parsed
        # inside rl_environment.py. We'd need to thread them through.
        # For now, use all-legal (Q-network learns which are bad via low reward).

        # Store transitions in replay buffer.
        replay.add(obs, actions, rewards, next_obs, legal_masks, next_legal_masks, dones)

        # Track episode stats.
        for i in range(args.n_envs):
            episode_rewards[i] += rewards[i]
            episode_lengths[i] += 1
            if "score" in infos[i]:
                episode_scores[i] = infos[i]["score"]

            if dones[i]:
                completed_episodes += 1
                recent_scores.append(episode_scores[i])
                recent_rewards.append(episode_rewards[i])
                if len(recent_scores) > 100:
                    recent_scores = recent_scores[-100:]
                    recent_rewards = recent_rewards[-100:]
                episode_rewards[i] = 0.0
                episode_scores[i] = 0
                episode_lengths[i] = 0

        obs = next_obs
        legal_masks = next_legal_masks
        total_steps += args.n_envs

        # Train.
        if total_steps >= args.learning_starts and total_steps % args.train_freq == 0:
            (batch_obs, batch_actions, batch_rewards, batch_next_obs,
             batch_legal, batch_next_legal, batch_dones) = replay.sample(rng, args.batch_size)

            # Double DQN: online net selects action, target net evaluates.
            _, online_next_masked, _ = q_forward(online_params, batch_next_obs, batch_next_legal)
            best_next_actions = np.argmax(online_next_masked, axis=1)

            target_next_q, _, _ = q_forward(target_params, batch_next_obs, batch_next_legal)
            q_next = target_next_q[np.arange(args.batch_size), best_next_actions]

            td_targets = batch_rewards + args.gamma * (1.0 - batch_dones.astype(np.float32)) * q_next

            q_values, _, cache = q_forward(online_params, batch_obs, batch_legal)
            grads = q_backward(online_params, cache, q_values, td_targets, batch_actions)
            adam_update(online_params, grads, adam_state, args.lr)

            # Track loss.
            q_taken = q_values[np.arange(args.batch_size), batch_actions]
            td_errors = q_taken - td_targets
            loss = float(np.mean(np.minimum(td_errors ** 2, np.abs(td_errors))))
            train_losses.append(loss)

        # Target network update.
        if total_steps % args.target_update_freq == 0:
            target_params = copy_params(online_params)

        # Logging.
        if total_steps % args.log_freq == 0 and total_steps > 0:
            elapsed = time.monotonic() - t_start
            sps = total_steps / elapsed
            avg_loss = float(np.mean(train_losses[-100:])) if train_losses else 0.0
            avg_score = float(np.mean(recent_scores)) if recent_scores else 0.0
            avg_reward = float(np.mean(recent_rewards)) if recent_rewards else 0.0
            print(
                f"[DQN] step={total_steps:>8d}/{args.total_steps}  "
                f"eps={eps:.3f}  loss={avg_loss:.4f}  "
                f"avg_score={avg_score:.0f}  avg_reward={avg_reward:.1f}  "
                f"episodes={completed_episodes}  "
                f"buffer={replay.size}/{args.buffer_size}  "
                f"sps={sps:.0f}  elapsed={elapsed:.0f}s"
            )

        # Evaluation.
        if total_steps % args.eval_freq == 0 and total_steps >= args.learning_starts:
            eval_stats = evaluate(eval_env, online_params, n_episodes=args.eval_episodes)
            print(
                f"[DQN] EVAL step={total_steps}  "
                f"mean_score={eval_stats['mean_score']:.0f}  "
                f"max_score={eval_stats['max_score']:.0f}  "
                f"mean_pellets={eval_stats['mean_pellets']:.0f}  "
                f"mean_deaths={eval_stats['mean_deaths']:.1f}"
            )

            # Save checkpoint.
            ckpt_path = checkpoint_dir / f"dqn_step{total_steps}.npz"
            save_model(online_params, ckpt_path, input_size, args.hidden1, args.hidden2)

            if eval_stats["mean_score"] > best_eval_score:
                best_eval_score = eval_stats["mean_score"]
                best_path = checkpoint_dir / "dqn_best.npz"
                save_model(online_params, best_path, input_size, args.hidden1, args.hidden2)
                # Also export Unity JSON directly.
                json_path = checkpoint_dir / "dqn_best_policy.json"
                export_to_unity_json(online_params, json_path, input_size, args.hidden1, args.hidden2, source="dqn-best")
                print(f"[DQN] New best model! score={best_eval_score:.0f} -> {best_path}")

    # Final save.
    print(f"\n[DQN] Training complete. {total_steps} steps, {completed_episodes} episodes.")
    final_path = checkpoint_dir / "dqn_final.npz"
    save_model(online_params, final_path, input_size, args.hidden1, args.hidden2)
    final_json = checkpoint_dir / "dqn_final_policy.json"
    export_to_unity_json(online_params, final_json, input_size, args.hidden1, args.hidden2, source="dqn-final")
    print(f"[DQN] Final model -> {final_path}")
    print(f"[DQN] Final Unity JSON -> {final_json}")

    if best_eval_score > 0:
        print(f"[DQN] Best eval score: {best_eval_score:.0f}")

    vec_env.close()
    eval_env.close()


if __name__ == "__main__":
    main()
