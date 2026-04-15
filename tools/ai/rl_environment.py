"""
Gym-like Python client for the MazeChase RL environment server.

Provides UnityEnvironment (single instance) and VectorizedUnityEnvironment
(N parallel instances) that communicate with Unity via length-prefixed JSON
over TCP.  Used by dqn_trainer.py.
"""

import json
import os
import socket
import struct
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import numpy as np

# Default paths -----------------------------------------------------------------
_SCRIPT_DIR = Path(__file__).resolve().parent
_PROJECT_ROOT = _SCRIPT_DIR.parent.parent
_DEFAULT_GAME_PATH = _PROJECT_ROOT / "MazeChase" / "BuildOutput" / "Win64" / "MazeChase.exe"

# Observation / action constants ------------------------------------------------
OBS_SIZE = 102          # ObservationEncoder single-frame feature count
NUM_ACTIONS = 4         # Up=1, Down=2, Left=3, Right=4  (0=None, not used)
ACTION_OFFSET = 1       # actions sent as 1-4, not 0-3


class UnityEnvironment:
    """Single MazeChase RL instance with Gym-like step/reset API."""

    def __init__(
        self,
        game_path: Optional[str] = None,
        port: int = 9090,
        headless: bool = True,
        time_scale: float = 20.0,
        seed: Optional[int] = None,
        launch: bool = True,
        connect_timeout: float = 60.0,
    ):
        self.port = port
        self._game_path = game_path or str(_DEFAULT_GAME_PATH)
        self._headless = headless
        self._time_scale = time_scale
        self._seed = seed
        self._connect_timeout = connect_timeout

        self._process: Optional[subprocess.Popen] = None
        self._sock: Optional[socket.socket] = None
        self._closed = False

        if launch:
            self._launch_and_connect()

    # ------------------------------------------------------------------
    # Gym-like API
    # ------------------------------------------------------------------

    def reset(self) -> np.ndarray:
        """Send reset, receive initial state. Returns obs (102,)."""
        self._send({"type": "reset"})
        state = self._recv_state()
        return state["obs"]

    def step(self, action: int) -> Tuple[np.ndarray, float, bool, Dict[str, Any]]:
        """
        Send action (0..3 mapped to Direction 1..4), receive next state.
        Returns (obs, reward, done, info).
        """
        self._send({"type": "action", "action": action + ACTION_OFFSET})
        state = self._recv_state()
        return state["obs"], state["reward"], state["done"], state["info"]

    def close(self):
        if self._closed:
            return
        self._closed = True
        try:
            self._send({"type": "close"})
        except Exception:
            pass
        try:
            self._sock.close()
        except Exception:
            pass
        if self._process is not None:
            try:
                self._process.terminate()
                self._process.wait(timeout=5)
            except Exception:
                try:
                    self._process.kill()
                except Exception:
                    pass
        if hasattr(self, '_log_handle') and self._log_handle:
            self._log_handle.close()

    # ------------------------------------------------------------------
    # Launch & connect
    # ------------------------------------------------------------------

    def _launch_and_connect(self):
        """Launch Unity process and connect via TCP."""
        if not Path(self._game_path).exists():
            raise FileNotFoundError(f"Game executable not found: {self._game_path}")

        cmd = [
            self._game_path,
            "-batchmode",
            "-nographics",
            "--ai-headless",
            "--rl-server",
            f"--rl-port={self.port}",
            f"--ai-fast-forward={self._time_scale}",
        ]
        if self._seed is not None:
            cmd.append(f"--ai-seed={self._seed}")

        log_file = Path(self._game_path).parent / f"rl_unity_{self.port}.log"
        self._log_handle = open(str(log_file), "w")
        self._process = subprocess.Popen(
            cmd,
            stdout=self._log_handle,
            stderr=subprocess.STDOUT,
            cwd=str(Path(self._game_path).parent),
        )

        self._connect_with_retry()

    def _connect_with_retry(self):
        """Retry TCP connection with exponential backoff."""
        deadline = time.monotonic() + self._connect_timeout
        delay = 0.5
        last_err = None

        while time.monotonic() < deadline:
            try:
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.settimeout(60.0)
                sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                sock.connect(("127.0.0.1", self.port))
                self._sock = sock
                return
            except (ConnectionRefusedError, OSError) as e:
                last_err = e
                sock.close()
                time.sleep(delay)
                delay = min(delay * 2, 4.0)

        raise ConnectionError(
            f"Could not connect to Unity on port {self.port} within "
            f"{self._connect_timeout}s: {last_err}"
        )

    # ------------------------------------------------------------------
    # TCP framing: 4-byte LE length prefix + UTF-8 JSON
    # ------------------------------------------------------------------

    def _send(self, obj: dict):
        data = json.dumps(obj, separators=(",", ":")).encode("utf-8")
        header = struct.pack("<I", len(data))
        self._sock.sendall(header + data)

    def _recv_bytes(self, n: int) -> bytes:
        buf = bytearray()
        while len(buf) < n:
            chunk = self._sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("Unity connection closed.")
            buf.extend(chunk)
        return bytes(buf)

    def _recv_state(self) -> dict:
        """Read one length-prefixed JSON message and parse into a state dict."""
        header = self._recv_bytes(4)
        length = struct.unpack("<I", header)[0]
        payload = self._recv_bytes(length)
        raw = json.loads(payload.decode("utf-8"))

        return {
            "obs": np.array(raw["obs"], dtype=np.float32),
            "legal": np.array(raw["legal"], dtype=bool),
            "reward": float(raw["reward"]),
            "done": bool(raw["done"]),
            "info": raw.get("info", {}),
        }


class VectorizedUnityEnvironment:
    """
    N parallel Unity environments for faster data collection.
    Each runs on a unique port.  Uses threads for concurrent TCP I/O.
    """

    def __init__(
        self,
        n_envs: int = 4,
        game_path: Optional[str] = None,
        base_port: int = 9090,
        headless: bool = True,
        time_scale: float = 20.0,
        base_seed: Optional[int] = None,
    ):
        self.n_envs = n_envs
        self._game_path = game_path
        self._base_port = base_port
        self._headless = headless
        self._time_scale = time_scale
        self._base_seed = base_seed
        self.envs: List[UnityEnvironment] = []

        for i in range(n_envs):
            port = base_port + i
            seed = (base_seed + i) if base_seed is not None else None
            env = UnityEnvironment(
                game_path=game_path,
                port=port,
                headless=headless,
                time_scale=time_scale,
                seed=seed,
            )
            self.envs.append(env)

    def reset_all(self) -> np.ndarray:
        """Reset all envs. Returns obs array (n_envs, OBS_SIZE)."""
        results = [None] * self.n_envs

        def _reset(idx):
            results[idx] = self.envs[idx].reset()

        threads = [threading.Thread(target=_reset, args=(i,)) for i in range(self.n_envs)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        return np.stack(results)

    def _restart_env(self, idx: int):
        """Kill and relaunch a single environment."""
        port = self._base_port + idx
        seed = (self._base_seed + idx) if self._base_seed is not None else None
        print(f"[VecEnv] Restarting env {idx} on port {port}...")
        try:
            self.envs[idx].close()
        except Exception:
            pass
        time.sleep(2)
        self.envs[idx] = UnityEnvironment(
            game_path=self._game_path,
            port=port,
            headless=self._headless,
            time_scale=self._time_scale,
            seed=seed,
        )

    def step(self, actions: np.ndarray) -> Tuple[np.ndarray, np.ndarray, np.ndarray, List[dict]]:
        """
        Step all envs with given actions (n_envs,).
        Auto-resets environments that are done.
        Returns (obs, rewards, dones, infos) — obs from the *new* episode on reset.
        If an env times out or errors, it is restarted and a zero-reward done is returned.
        """
        assert len(actions) == self.n_envs

        obs_list = [None] * self.n_envs
        reward_list = [0.0] * self.n_envs
        done_list = [False] * self.n_envs
        info_list = [{}] * self.n_envs
        errors = [None] * self.n_envs

        def _step(idx):
            try:
                obs, reward, done, info = self.envs[idx].step(int(actions[idx]))
                reward_list[idx] = reward
                done_list[idx] = done
                info_list[idx] = info
                if done:
                    # Auto-reset and return new episode's first obs.
                    obs = self.envs[idx].reset()
                obs_list[idx] = obs
            except Exception as e:
                errors[idx] = e

        threads = [threading.Thread(target=_step, args=(i,)) for i in range(self.n_envs)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        # Handle any environments that errored — restart and return a synthetic done.
        for idx in range(self.n_envs):
            if errors[idx] is not None:
                print(f"[VecEnv] Env {idx} error: {errors[idx]}")
                self._restart_env(idx)
                obs_list[idx] = self.envs[idx].reset()
                reward_list[idx] = 0.0
                done_list[idx] = True
                info_list[idx] = {"error_restart": True}

        return (
            np.stack(obs_list),
            np.array(reward_list, dtype=np.float32),
            np.array(done_list, dtype=bool),
            info_list,
        )

    def close(self):
        for env in self.envs:
            env.close()


# ---------------------------------------------------------------------------
# Quick smoke test
# ---------------------------------------------------------------------------

def smoke_test(game_path: Optional[str] = None, port: int = 9090, steps: int = 100):
    """Launch one env, take random actions, print stats."""
    print(f"[smoke] Launching Unity environment on port {port}...")
    env = UnityEnvironment(game_path=game_path, port=port)

    print("[smoke] Connected. Sending reset...")
    obs = env.reset()
    print(f"[smoke] Initial obs shape: {obs.shape}, first 5 values: {obs[:5]}")

    total_reward = 0.0
    for step in range(steps):
        action = np.random.randint(0, NUM_ACTIONS)
        obs, reward, done, info = env.step(action)
        total_reward += reward

        # Print every step if reward is unusual (death/ghost eat/round clear)
        notable = abs(reward) > 0.5 or done
        if step % 20 == 0 or done or notable:
            print(
                f"[smoke] step={step:3d}  action={action}  reward={reward:+.3f}  "
                f"done={done}  score={info.get('score', '?')}  "
                f"pellets={info.get('remaining_pellets', '?')}  "
                f"lives={info.get('lives', '?')}"
            )

        if done:
            print(f"[smoke] Episode done at step {step}. Total reward: {total_reward:.2f}")
            obs = env.reset()
            total_reward = 0.0
            print(f"[smoke] Reset complete. Continuing...")

    print(f"[smoke] Finished {steps} steps. Final total_reward: {total_reward:.2f}")
    env.close()
    print("[smoke] Done.")


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="MazeChase RL environment smoke test")
    parser.add_argument("--game", type=str, default=None, help="Path to MazeChase.exe")
    parser.add_argument("--port", type=int, default=9090)
    parser.add_argument("--steps", type=int, default=100)
    args = parser.parse_args()

    smoke_test(game_path=args.game, port=args.port, steps=args.steps)
