# DQN Training Log — Field Reference

## How to monitor a training run

From PowerShell, live-follow the log:

```powershell
Get-Content "<path-to-output-file>" -Wait -Tail 10
```

Press `Ctrl+C` to stop watching.

---

## Training log fields

### `step = 105000/200000`
**What:** How many game decisions the AI has made out of the total target.
**Think of it as:** Page 105K of a 200K-page book. This is your overall progress bar.

### `eps = 0.501` (epsilon)
**What:** How often the AI picks a **random** move instead of its best guess. Starts at `1.0` (100% random, pure exploration) and decays toward `0.05` (5% random, mostly exploiting what it learned).
**Good trend:** Decreasing over time. That means the AI is gradually trusting its own judgment more.
**Why it matters:** Early on, random exploration helps the AI discover what works. Later, it should be relying on what it learned.

### `loss = 0.3029`
**What:** How wrong the AI's predictions are about future rewards. Think of it like a test score, but inverted — **lower = better**.
**Good trend:** Stable or slowly decreasing. A sudden spike upward would mean something went wrong.
**Don't panic if:** It fluctuates a bit — that's normal. It won't steadily drop to zero like a progress bar.

### `avg_score = 2183`
**What:** Average in-game score across recent training episodes. This includes the noise from random exploration moves (epsilon), so it's an underestimate of the AI's true ability.
**Good trend:** Steadily climbing. This is the most intuitive sign of improvement.
**Context:** A random agent scores ~500. The behavioral cloning model (v21) averages ~10K. A perfect game would be ~30K+.

### `avg_reward = 68.8`
**What:** Average of the shaped training reward per episode. This is the internal signal the AI optimizes — it includes bonuses for eating pellets, penalties for dying, penalties for wasting time, etc.
**Good trend:** Climbing = the agent is getting better at what we designed it to do.
**Note:** This number doesn't map directly to game score. It's the AI's internal "grade."

### `episodes = 181`
**What:** Total completed games (deaths = game over, or stall timeout) since training started.
**Just a counter.** More episodes = more learning data.

### `buffer = 105000/500000`
**What:** How many past experiences are stored in the replay buffer. The AI samples random batches from this "memory" to learn from. Think of it as a study notebook — `105K/500K` means 105K experiences saved, with room for 500K.
**Good trend:** Fills up over time. Once full, old experiences get overwritten with newer ones.

### `sps = 27`
**What:** Steps per second — how fast training is running.
**Good trend:** Consistent. A sudden drop might mean a Unity instance is struggling.
**Context:** With 4 parallel game instances, ~26-30 sps is typical. Total training time ≈ `total_steps / sps` seconds.

### `elapsed = 3944s`
**What:** Wall clock time since training started, in seconds.
**Quick math:** Divide by 60 for minutes, by 3600 for hours.

---

## Evaluation log fields

Every 20K steps, the AI plays 5 games with **zero randomness** (pure greedy policy) to measure its true skill:

```
[DQN] EVAL step=120000  mean_score=2028  max_score=2850  mean_pellets=162  mean_deaths=0.0
```

### `mean_score = 2028`
**What:** Average game score across the 5 eval games. **This is the real scorecard** — no random exploration noise.
**Good trend:** Higher over time.

### `max_score = 2850`
**What:** Best single game in the eval batch. Shows the AI's peak performance.

### `mean_pellets = 162`
**What:** Average pellets eaten per eval game (out of 246 per round).
**Context:** 246 = full round clear. 162 means it's eating about 66% of pellets before dying or timing out.

### `mean_deaths = 0.0`
**What:** Average deaths per eval game.
**If 0.0:** The eval episodes likely ended from stall timeout (500 steps without eating a pellet), not from ghosts. This means the AI is safe but gets stuck/loops in the endgame — a known pattern we're training to fix.

---

## Checkpoints and best model

```
[DQN] New best model! score=2028 -> tools\ai\dqn_checkpoints_200k\dqn_best.npz
```

Whenever an eval beats the previous best mean score, the model weights are saved. Key files:

| File | What |
|------|------|
| `dqn_best.npz` | Best model weights so far (NumPy format) |
| `dqn_best_policy.json` | Same weights in Unity format (loadable by the game) |
| `dqn_final.npz` | Final model at end of training |
| `dqn_final_policy.json` | Same, Unity format |
| `dqn_step*.npz` | Periodic checkpoints |

---

## Error recovery

```
[VecEnv] Env 0 error: timed out
[VecEnv] Restarting env 0 on port 9090...
```

**What happened:** One of the 4 parallel Unity game instances stopped responding (rare edge case in the game-to-Python communication). Python detected the timeout, killed that one instance, relaunched it, and kept training.
**Impact:** Minimal — one lost game transition. Training continues normally.
**If you see many of these:** Might indicate a systematic bug worth investigating. One every 50K-100K steps is normal.

---

## Quick health check

| Sign | Healthy | Unhealthy |
|------|---------|-----------|
| `avg_score` | Trending up | Flat or dropping |
| `loss` | Stable (0.1–0.5 range) | Exploding (>10) or NaN |
| `sps` | Consistent (25-30) | Dropping to single digits |
| `eps` | Decreasing toward 0.05 | Stuck at 1.0 |
| Env restarts | Rare (< 1 per 50K steps) | Frequent (every few K steps) |
| EVAL `mean_score` | Improving over checkpoints | Collapsing to 0 |
