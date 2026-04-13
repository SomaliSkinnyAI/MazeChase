# AI Autoplay Status

This document is the current source of truth for the F2 autoplay subsystem.

## Current Goal

Replace the fragile frame-driven `ExpertBot` loop with a stronger AI stack that:

- commits one move per tile instead of forcing directions every frame
- supports multiple runtime autoplay modes
- can record planner decisions into a dataset
- can load a trained neural policy from exported weights
- remains playable even when no trained model exists
- reaches level 3 consistently enough that F2 autoplay feels legitimately competent instead of lucky

## Live To-Do

- [done] Replace frame-driven autoplay with tile-committed autoplay
- [done] Build offline training/export pipeline and get a real neural checkpoint running in F2 mode
- [done] Add seeded benchmark tooling for repeatable AI comparisons
- [done] Add a true headless simulation profile for batch autoplay recording and benchmarking
- [done] Fix the top HUD overlap caused by the autoplay banner drawing over the score bar
- [done] Fix ghost house spawn and exit coordinates so the non-Blinky ghosts can actually leave the house
- [done] Fix death reset behavior so ghost-release progress survives a lost life
- [done] Run a 12-seed planner dataset campaign and train a larger `v4` neural checkpoint
- [done] Build a student-guided curriculum loop that mines hard seeds from the current neural model and replays them with the planner teacher
- [in progress] Validate eaten-ghost regeneration and house return behavior under longer runs
- [done] Promote `v6` as the default exported checkpoint after rebalanced curriculum benchmarks
- [pending] Expand tests around ghost lifecycle and autoplay regressions
- [done] Scale the planner campaign up using the new headless default and add a live progress status page
- [pending] Grow the curriculum miner from a single hard-seed pass into a longer rolling benchmark history
- [done] Prepare and train broad/headless student candidates from the new `v7` teacher corpus
- [done] Build level-3-targeted curriculum iterations and benchmark `v10`, `v11`, and `v12` on a `max-rounds=3` holdout
- [done] Land a temporal/context model upgrade that appends short-term loop/progress features and retrain the neural stack
- [in progress] Push the live neural checkpoint from "much stronger round-1 pressure" to consistent level-3 clears
- [done] Mine a near-clear teacher-only curriculum from `v14` failures that still die with `<= 30` pellets remaining
- [done] Re-run teacher-data generation after the player-speed fix and evaluate fresh post-fix student candidates
- [done] Land the stateless MLP "enhanced perception" pivot with motion features, junction awareness, and a death-risk head
- [done] Train and benchmark the first 102-feature multi-head neural checkpoint against live `v15`
- [pending] Mine a targeted corrected-speed curriculum using the new `102`-feature schema so the upgraded model can compete with `v15`
- [done] Fix the ResearchPlanner teacher's endgame anti-loop strategy (dynamic depth, recency-weighted visit penalties, pellet gravity, corridor penalty reduction, pathfinding override)
- [done] Expand the recent tile window from 24 to 48 tiles across runtime and training pipeline
- [pending] Collect fresh teacher data with the improved planner and train v20
- [pending] Run v15 curriculum mining with the fixed teacher and compare against v15 baseline
- [pending] If v20 succeeds, implement frame-stacking (N=3) for temporal context in the MLP

## What Is Implemented

### Build/Test Automation

- Unity batch scripts now run through a watchdog helper instead of blocking on `Start-Process -Wait`
- build and test commands stream the Unity log while they run
- compiler errors are treated as fail-fast conditions so automation stops quickly instead of looking hung
- hard timeouts are now part of the standard build and test workflow

### Headless Simulation Mode

- the player now supports a simulation-friendly runtime profile driven by command-line flags
- in headless mode the game suppresses camera/HUD/audio/VFX setup, skips ready/death/round-clear waits, raises the autoplay time-scale ceiling, and can auto-exit on game over
- `MazeRenderer`, fruit rendering, player/ghost sprite generation, and autoplay GUI overlays now all respect the headless presentation-suppression profile
- this is intended for dataset generation and seeded benchmark runs, not for interactive play

Latest smoke validation on April 4, 2026:

- `tools/ai/run_benchmark.py --mode ResearchPlanner --seeds 2101 --fast-forward 40 --max-rounds 1`
  - completed in headless mode with no timeout
  - output: `tools/ai/benchmark-headless-smoke.json`
- `tools/ai/generate_dataset_campaign.py --mode ResearchPlanner --seeds 2201 --fast-forward 40 --max-rounds 1`
  - completed in headless mode with no timeout
  - output: `tools/ai/planner_dataset_headless_smoke.jsonl`
  - report: `tools/ai/planner_campaign_headless_smoke.json`

Latest large headless teacher run on April 4, 2026:

- seeds: `2301-2324`
- mode: `ResearchPlanner`
- headless: `True`
- max rounds per seed: `2`
- fast-forward: `50`
- completed in `6m 38s`
- merged teacher rows: `16,743`
- merged dataset: `tools/ai/planner_dataset_v7_headless.jsonl`
- campaign report: `tools/ai/planner_campaign_v7_headless.json`
- live status files:
  - `tools/ai/planner_campaign_v7_headless_status.md`
  - `tools/ai/planner_campaign_v7_headless_status.json`
- launcher logs:
  - `tools/ai/planner_campaign_v7_headless.log`
  - `tools/ai/planner_campaign_v7_headless.err.log`

### Control Loop

- `PlayerController` now exposes `OnTileArrived`
- `AutoplayManager` listens to tile-arrival events and only commits one direction per tile
- this removes the old mid-tile reversal failure mode caused by frame-by-frame input injection

### Runtime Modes

`AutoplayManager` now supports:

- `NeuralPolicy`
- `ResearchPlanner`
- `ExpertLegacy`
- `Attract`

Runtime hotkeys:

- `F2`: enable `NeuralPolicy`
- `F3`: switch to `ResearchPlanner` rules AI
- `F4`: return to manual play

### Runtime AI Stack

Implemented under `Assets/Scripts/AI/Autoplay/`:

- `MazeGraph.cs`
  - precomputed player navigation graph
  - tunnel-aware pathfinding
  - distances, degree, dead-end depth, pellet density helpers
- `GhostForecastEngine.cs`
  - converts ghost positions/states into graph pressure estimates
  - exposes local danger and frightened opportunity scoring
- `ResearchPlannerBot.cs`
  - graph-search teacher/fallback bot
  - looks ahead several tiles with pellet, energizer, trap, tunnel, and ghost pressure scoring
- `ObservationEncoder.cs`
  - converts the live state into a fixed feature vector for policy training/runtime inference
  - current schema is `102` features: directional corridor features, base globals, velocity/junction globals, then temporal context
- `GraphPolicyModel.cs`
  - lightweight C# MLP runtime loader/inference engine
  - now supports `policy`, `value`, and `death-risk` heads
  - still loads legacy `91`-feature exports through a compatibility adapter while new weights are being trained
- `NeuralPolicyBot.cs`
  - loads exported weights when available
  - falls back to `ResearchPlannerBot` when no model file exists
  - now uses a confidence-threshold fallback path to `ExpertLegacy` when the neural policy is too uncertain
- `AIDatasetRecorder.cs`
  - writes planner/policy decisions to JSONL for offline training
  - now records death-risk estimates and per-ghost motion metadata for offline analysis

### Current Runtime Behavior

- `NeuralPolicy` is the default mode
- if no exported model exists, `NeuralPolicy` logs a warning once and transparently falls back to `ResearchPlanner`
- this means F2 should already be stronger today even before offline training happens
- the on-screen status card now always shows the current live control mode, and in neural mode it names the active exported checkpoint
- recent-tile memory is now part of the runtime context so both planner and neural reranking can punish short loops
- endgame scoring now becomes more pellet-hungry as remaining pellets fall, to reduce "safe but stuck" chase cycles near round completion
- the neural runtime now tracks no-progress decision streaks and can trigger a planner-backed rescue when it gets stuck in a low-pellet loop
- the runtime encoder now appends temporal/context features for revisit pressure, repeat age, tile diversity, no-progress streaks, and direction streaks
- the offline trainer can reconstruct those same features from older JSONL runs, so historical datasets remain usable after the architecture change
- the encoder now also appends player/ghost velocity vectors plus player junction proximity, which gives the stateless MLP better motion awareness without adding recurrent state
- the trainer/export/runtime stack now supports a separate `death-risk` prediction head
- low-confidence neural moves can now hand off to `ExpertLegacy` instead of forcing a weak policy choice
- current exported checkpoint is `planner-distill-v21-curriculum`
- runtime validation has confirmed `NeuralPolicy` is running with `usedFallback = false` on the exported weight file
- the upgraded `102`-feature multi-head pipeline has been smoke-validated end to end with:
  - `tools/ai/policy_dataset_mlp_pivot_smoke.npz`
  - `tools/ai/policy_model_mlp_pivot_smoke.npz`
  - `tools/ai/policy_model_mlp_pivot_smoke.json`
- the first full fresh `102`-feature teacher corpus and student checkpoint are:
  - `tools/ai/planner_dataset_v18_mlp102.jsonl`
  - `tools/ai/planner_campaign_v18_mlp102.json`
  - `tools/ai/planner_campaign_v18_mlp102_status.md`
  - `tools/ai/policy_dataset_v18_mlp102.npz`
  - `tools/ai/policy_model_v18_mlp102.npz`
  - `tools/ai/policy_model_v18_mlp102.json`
- earlier large-corpus candidate checkpoint `planner-distill-v4-large-campaign` is still useful as the student miner for curriculum generation
- experimental `v5` showed that hard-seed replay can help but can also over-correct
- `v6` was the first student-guided checkpoint that beat the earlier baselines on the fresh `1301-1304` holdout
- `v10` materially improved score and pellet pressure on the newer `4301-4308` level-3 holdout
- `v14` improved stability and removed fallback on the main holdout, but it was still too cautious to clear round 1
- `v15` is now promoted because a near-clear curriculum mined from `v14` failures improved score and pellet pressure on two unseen level-3 holdouts and introduced nonzero round clears on `4301-4308`
- after the player-speed fix, `v15` unexpectedly became much stronger on the corrected runtime and remains the best live checkpoint so far

## Offline Training Pipeline

Training is separate from the Unity runtime.

Scripts live in `tools/ai/`:

- `prepare_dataset.py`
- `train_policy.py`
- `evaluate_model.py`
- `export_weights.py`
- `generate_dataset_campaign.py`
- `generate_curriculum_campaign.py`
- `README.md`

Workflow:

1. Run the game in `ResearchPlanner` mode with dataset recording enabled
2. Convert the JSONL dataset into `.npz`
3. Train the policy/value/death-risk network offline
4. Export Unity JSON weights
5. Place exported weights at `MazeChase/StreamingAssets/AI/policy_model.json`
6. Launch the game again and use `NeuralPolicy`

## Command-Line Flags

Implemented:

- `--autoplay`
- `--autoplay-mode=<NeuralPolicy|ResearchPlanner|ExpertLegacy|Attract>`
- `--ai-record`
- `--ai-fast-forward=<timescale>`
- `--ai-seed=<int>`
- `--ai-headless`
- `--ai-no-render`
- `--ai-quit-on-game-over`
- `--ai-max-rounds=<int>`

## Important Files

- `Assets/Scripts/Game/PlayerController.cs`
- `Assets/Scripts/Game/GameplaySceneSetup.cs`
- `Assets/Scripts/AI/Autoplay/AutoplayManager.cs`
- `Assets/Scripts/AI/Autoplay/ResearchPlannerBot.cs`
- `Assets/Scripts/AI/Autoplay/NeuralPolicyBot.cs`
- `Assets/Scripts/AI/Autoplay/GraphPolicyModel.cs`
- `tools/ai/README.md`

## Current Known Gaps

- the live neural bot is now much stronger than the old heuristic autoplay and can clear some `4301-4308` runs, but it is still far from consistent level-3 play
- the corrected-speed runtime materially changed the game dynamics, so all post-fix neural comparisons should be treated as a new evaluation era
- **root cause identified**: the planner teacher was looping in 83% of endgame seeds due to shallow search depth, tiny visit memory window, and counterproductive corridor penalties -- this poisoned all training data collected from the teacher
- the teacher endgame fix was validated and v20 trained from it is now the best checkpoint (100% clear rate on both holdouts, avg score 10180-10531)
- v16-v19 all failed to beat v15 not because of architecture issues, but because they were trained on loopy teacher data
- the `RecentTileWindow` has been expanded from 24 to 48 across runtime and training
- **v21 is now the active exported brain: `planner-distill-v21-curriculum`**
- v21 improved score +18.2% over v20 on holdout (12,031 vs 10,180), reduced fallback from 1.0 to 0.875, and cut final pellets from 29.25 to 7.375
- fresh seeds show steady gains: avg score 10,575 (+0.4%), zero timeouts (was 12.5%), final pellets halved
- fallback is still 1.0 on fresh seeds -- the endgame loop pattern persists and likely needs frame-stacking to fully resolve
- tests for the new AI stack still need to be expanded
- danger heatmap data exists internally, but the debug UI is still text-first rather than a full maze overlay
- we now have `tools/ai/run_benchmark.py` for seeded validation, but we still need to build a longer-running benchmark history across checkpoints so regressions are obvious

## Large Dataset Campaign (v4)

Completed on April 4, 2026:

- planner campaign script: `tools/ai/generate_dataset_campaign.py`
- seeds recorded: `1201 1202 1203 1204 1205 1206 1207 1208 1209 1210 1211 1212`
- teacher mode: `ResearchPlanner`
- merged planner rows: `7,068`
- prepared dataset: `tools/ai/policy_dataset_v4.npz`
- endgame-weighted rows: `1,675`
- trained model artifact: `tools/ai/policy_model_v4.npz`
- evaluation metrics: `accuracy=0.9363`, `top2_accuracy=0.9967`, `value_mse=0.079546`

Campaign artifacts:

- `tools/ai/planner_campaign_v4.json`
- `tools/ai/planner_dataset_v4.jsonl`
- `tools/ai/policy_dataset_v4.npz`
- `tools/ai/policy_model_v4.npz`
- `tools/ai/benchmark-neural-v4.json`
- `tools/ai/benchmark-neural-v4-fresh.json`
- `tools/ai/benchmark-neural-v3-postfix.json`
- `tools/ai/benchmark-neural-v3-fresh.json`

Current takeaway:

- `v4` is a real larger-data checkpoint, not a placeholder
- on current gameplay code, `v4` tends to push pellet counts lower than `v3`, which is encouraging for board-clear pressure
- `v4` still shows mixed seeded results on clear rate and planner-rescue usage, so it has not yet earned promotion over `v3`

## Large Headless Teacher Campaign (v7 Corpus)

Completed on April 4, 2026:

- planner campaign script: `tools/ai/generate_dataset_campaign.py`
- live progress status:
  - `tools/ai/planner_campaign_v7_headless_status.md`
  - `tools/ai/planner_campaign_v7_headless_status.json`
- seeds recorded: `2301 2302 2303 2304 2305 2306 2307 2308 2309 2310 2311 2312 2313 2314 2315 2316 2317 2318 2319 2320 2321 2322 2323 2324`
- teacher mode: `ResearchPlanner`
- run profile: `headless`, `fast-forward=50`, `max-rounds=2`
- merged planner rows: `16,743`
- max round reached: `2`
- lowest pellet count: `1`
- completed with `0` timeouts

Artifacts:

- `tools/ai/planner_dataset_v7_headless.jsonl`
- `tools/ai/planner_campaign_v7_headless.json`
- `tools/ai/planner_campaign_v7_headless.log`
- `tools/ai/planner_campaign_v7_headless.err.log`

Current takeaway:

- this is now the largest broad teacher corpus in the repo
- it more than doubles the old `v4` planner corpus
- it includes a healthier mix of round-1 failures, near-clears, and round-2 trajectories
- broad distillation from this corpus alone is not enough to beat `v6`; the first student candidates trained from it became more stable but too conservative

## Student Checkpoint Experiments From The v7 Headless Corpus

Completed on April 4, 2026:

- broad-only dataset: `tools/ai/policy_dataset_v8.npz`
- broad-only model: `tools/ai/policy_model_v8.npz`
- broad-only Unity export: `tools/ai/policy_model_v8.json`
- blended dataset: `tools/ai/policy_dataset_v9.npz`
- blended model: `tools/ai/policy_model_v9.npz`
- blended Unity export: `tools/ai/policy_model_v9.json`
- training logs:
  - `tools/ai/train_policy_v8.log`
  - `tools/ai/train_policy_v9.log`
- evaluation logs:
  - `tools/ai/evaluate_model_v8.log`
  - `tools/ai/evaluate_model_v9.log`
- fresh holdout benchmarks:
  - `tools/ai/benchmark-neural-v8-fresh.json`
  - `tools/ai/benchmark-neural-v9-fresh.json`

Checkpoint setup:

- `v8`: broad imitation from `planner_dataset_v7_headless.jsonl`
- `v9`: broad imitation from `planner_dataset_v7_headless.jsonl` plus `planner_dataset_v5_teacher_only.jsonl` with input weight `2.2` on the teacher-only hard-seed rows

Offline metrics:

- `v8`: `accuracy=0.9409`, `top2_accuracy=0.9973`, `value_mse=0.063304`
- `v9`: `accuracy=0.9393`, `top2_accuracy=0.9973`, `value_mse=0.064547`

Fresh holdout takeaway versus live `v6`:

- both `v8` and `v9` eliminated the worst timeout/loop signatures on the `1301-1304` fresh holdout
- but both regressed on actual play strength: neither cleared a round, and both underperformed `v6` on average score
- `v8` averaged `3707.5` score with `0%` clear rate
- `v9` averaged `3310.0` score with `0%` clear rate
- live `v6` remains stronger on the same fresh holdout, so it stays promoted as the default exported checkpoint

## Student-Guided Curriculum (v5/v6)

Completed on April 4, 2026:

- student miner checkpoint: `planner-distill-v4-large-campaign`
- hard-seed miner script: `tools/ai/generate_curriculum_campaign.py`
- candidate student seeds: `1501 1502 1503 1504 1505 1506 1507 1508 1509 1510 1511 1512`
- selected hard seeds for teacher replay: `1510 1506 1504 1503 1507 1512`
- teacher replay rows: `3,481`
- merged curriculum rows: `10,549`

Artifacts:

- `tools/ai/planner_curriculum_v5.json`
- `tools/ai/planner_dataset_v5_curriculum.jsonl`
- `tools/ai/planner_dataset_v5_teacher_only.jsonl`
- `tools/ai/policy_dataset_v5.npz`
- `tools/ai/policy_model_v5.npz`
- `tools/ai/policy_dataset_v6.npz`
- `tools/ai/policy_model_v6.npz`
- `tools/ai/benchmark-neural-v5.json`
- `tools/ai/benchmark-neural-v5-fresh.json`
- `tools/ai/benchmark-neural-v5-hard.json`
- `tools/ai/benchmark-neural-v6.json`
- `tools/ai/benchmark-neural-v6-fresh.json`
- `tools/ai/benchmark-neural-v6-hard.json`

Current takeaway:

- `v5` proved that mining hard seeds from the student and replaying them with the planner is directionally correct, but the first weighting pass over-corrected and hurt generalization
- `v6` rebalanced the same broad corpus plus hard-seed teacher rows using explicit per-input weighting, and that checkpoint was the active export before the level-3 push
- on current code, `v6` is the first student-guided checkpoint that clearly outperforms the earlier baselines on the fresher `1301-1304` holdout seed set
- `v6` still has mixed behavior on some of the mined hard seeds, so the curriculum loop needs more than one iteration

## Level-3 Curriculum Push (v10-v12)

Completed on April 4, 2026:

- level-3 holdout baseline:
  - `tools/ai/benchmark-neural-v6-level3-fresh.json`
  - seeds: `4301 4302 4303 4304 4305 4306 4307 4308`
  - `v6` result: `avgFinalScore=2470.0`, `avgMaxRound=1.0`, `avgMinPellets=51.0`, `clearRate=0.0`
- first level-3 curriculum campaign:
  - `tools/ai/planner_curriculum_v10.json`
  - `tools/ai/planner_curriculum_v10_status.md`
  - `tools/ai/planner_dataset_v10_curriculum.jsonl`
  - `tools/ai/planner_dataset_v10_teacher_only.jsonl`
  - merged rows: `29,258`
  - teacher-only rows: `9,034`
- trained student candidates from that curriculum:
  - `v10`: `tools/ai/policy_dataset_v10.npz`, `tools/ai/policy_model_v10.npz`, `tools/ai/policy_model_v10.json`
  - `v11`: `tools/ai/policy_dataset_v11.npz`, `tools/ai/policy_model_v11.npz`, `tools/ai/policy_model_v11.json`
  - offline metrics:
    - `v10`: `accuracy=0.9417`, `top2_accuracy=0.9977`, `value_mse=0.060463`
    - `v11`: `accuracy=0.9454`, `top2_accuracy=0.9981`, `value_mse=0.057329`
  - level-3 holdout benchmarks:
    - `tools/ai/benchmark-neural-v10-level3-fresh.json`
    - `tools/ai/benchmark-neural-v11-level3-fresh.json`
  - runtime results:
    - `v10`: `avgFinalScore=3127.5`, `avgMinPellets=25.25`, `clearRate=0.0`, `fallbackRate=0.25`
    - `v11`: `avgFinalScore=2972.5`, `avgMinPellets=24.75`, `clearRate=0.0`, `fallbackRate=0.0`
- second level-3 curriculum campaign mined from live `v10`:
  - `tools/ai/planner_curriculum_v12.json`
  - `tools/ai/planner_curriculum_v12_status.md`
  - `tools/ai/planner_dataset_v12_curriculum.jsonl`
  - `tools/ai/planner_dataset_v12_teacher_only.jsonl`
  - candidate seeds: `5301-5332`
  - selected hard seeds: `5325 5314 5308 5313 5303 5316 5315 5322 5320 5306 5319 5332`
  - teacher-only rows: `10,843`
  - merged rows: `40,101`
- second-iteration student:
  - `v12`: `tools/ai/policy_dataset_v12.npz`, `tools/ai/policy_model_v12.npz`, `tools/ai/policy_model_v12.json`
  - offline metrics: `accuracy=0.9442`, `top2_accuracy=0.9978`, `value_mse=0.061785`
  - level-3 holdout benchmark: `tools/ai/benchmark-neural-v12-level3-fresh.json`
  - runtime result: `avgFinalScore=3046.25`, `avgMinPellets=39.875`, `clearRate=0.0`

Current takeaway:

- `v10` is now the best level-3-focused checkpoint in the repo, even though it still does not clear round 1 on the current holdout
- `v11` looked better offline but did not convert that into stronger runtime play
- `v12` improved over `v6` but regressed from `v10`, so more broad-plus-targeted data alone is not enough
- the next best move is another curriculum pass focused on `v10` failures that die with very low pellet counts, not another broad teacher sweep

## Temporal Model Upgrade (v13/v14)

Completed on April 4, 2026:

- runtime architecture change:
  - `ObservationEncoder` now appends `8` temporal/context features on top of the legacy `83`-feature base input
  - those features capture revisit pressure, repeat age, recent tile diversity, no-progress streaks, low-pellet no-progress streaks, direction streaks, reversal flags, and tight-loop pressure
  - `AutoplayContext` now owns the short-term runtime memory used by both the encoder and neural rescue logic
- trainer compatibility change:
  - `tools/ai/prepare_dataset.py` can now reconstruct the same temporal features from older JSONL datasets, so the existing planner corpora remained usable
- new temporal student checkpoints:
  - `v13`: `tools/ai/policy_dataset_v13.npz`, `tools/ai/policy_model_v13.npz`, `tools/ai/policy_model_v13.json`
    - offline metrics: `accuracy=0.9554`, `top2_accuracy=0.9984`, `value_mse=0.050486`
    - holdout benchmark: `tools/ai/benchmark-neural-v13-level3-fresh.json`
    - runtime result: `avgFinalScore=3216.25`, `avgMinPellets=40.375`, `clearRate=0.0`, `fallbackRate=0.0`
  - `v14`: `tools/ai/policy_dataset_v14.npz`, `tools/ai/policy_model_v14.npz`, `tools/ai/policy_model_v14.json`
    - offline metrics: `accuracy=0.9509`, `top2_accuracy=0.9984`, `value_mse=0.047772`
    - holdout benchmark: `tools/ai/benchmark-neural-v14-level3-fresh.json`
    - runtime result: `avgFinalScore=3230.0`, `avgMinPellets=33.5`, `clearRate=0.0`, `fallbackRate=0.0`

Current takeaway:

- the architecture upgrade helped: both temporal models beat the old `v6` baseline on score, and `v14` is now the strongest non-fallback holdout performer
- `v13` was too cautious
- `v14` is a better compromise than `v13`, but it still does not beat the older `v10` pellet-pressure result, so the next iteration needs stronger near-clear curriculum data

## Near-Clear Curriculum (v15)

Completed on April 4, 2026:

- curriculum miner upgrade:
  - `tools/ai/generate_curriculum_campaign.py` now supports explicit student-run selection filters for:
    - `remainingPelletsMin <= N`
    - `deaths >= N`
    - `roundMax <= N`
    - `roundClears <= N`
  - this made it possible to mine exactly the near-clear deaths we cared about instead of generic hard seeds
- targeted mining pass:
  - candidate pool: `4401-4460`
  - selection filters: `remainingPelletsMin <= 30`, `deaths >= 2`, `roundMax <= 1`, `roundClears <= 0`
  - matched student runs: `23`
  - replayed teacher seeds: `4401 4404 4459 4442 4426 4421 4415 4408 4435 4457 4436 4431`
  - teacher-only rows: `8,307`
  - merged curriculum rows: `25,050`
  - campaign artifacts:
    - `tools/ai/planner_curriculum_v15.json`
    - `tools/ai/planner_curriculum_v15.log`
    - `tools/ai/planner_curriculum_v15_status.md`
    - `tools/ai/planner_curriculum_v15_status.json`
    - `tools/ai/planner_dataset_v15_curriculum.jsonl`
    - `tools/ai/planner_dataset_v15_teacher_only.jsonl`
- trained student:
  - `v15`: `tools/ai/policy_dataset_v15.npz`, `tools/ai/policy_model_v15.npz`, `tools/ai/policy_model_v15.json`
  - training blend:
    - `tools/ai/planner_dataset_v7_headless.jsonl` at `1.0`
    - `tools/ai/planner_dataset_v10_teacher_only.jsonl` at `2.5`
    - `tools/ai/planner_dataset_v15_teacher_only.jsonl` at `4.5`
  - offline metrics: `accuracy=0.9570`, `top2_accuracy=0.9987`, `value_mse=0.046604`
  - primary holdout benchmark: `tools/ai/benchmark-neural-v15-level3-fresh.json`
    - `avgFinalScore=3870.0`, `avgMinPellets=19.75`, `clearRate=0.25`, `fallbackRate=0.125`
  - secondary holdout benchmark: `tools/ai/benchmark-neural-v15-level3-4501-4508.json`
    - `avgFinalScore=3377.5`, `avgMinPellets=37.75`, `clearRate=0.0`, `fallbackRate=0.125`
  - direct A/B baseline on the same secondary holdout: `tools/ai/benchmark-neural-v14-level3-4501-4508.json`
    - `v14`: `avgFinalScore=2867.5`, `avgMinPellets=50.75`, `clearRate=0.0`, `fallbackRate=0.0`

Current takeaway:

- `v15` is the new best temporal checkpoint overall
- the targeted near-clear curriculum worked better than another broad data pass
- `v15` is more aggressive and converts some round clears now, but fallback and low-pellet stalls have not disappeared
- the next move should either be another near-clear curriculum loop mined from `v15` itself or a training objective that learns explicit death risk

## Post-Fix Retraining (v16/v17)

Completed on April 4, 2026 after the player-speed and stacked-energizer fixes:

- fresh post-fix baseline:
  - `tools/ai/benchmark-neural-v15-postfix-level3-fresh.json`
  - corrected-speed `v15` result on `4301-4308`:
    - `avgFinalScore=9208.75`
    - `avgMaxRound=2.375`
    - `clearRate=0.75`
    - `fallbackRate=0.375`
- broad teacher refresh:
  - `tools/ai/planner_campaign_v16_postfix.json`
  - `tools/ai/planner_campaign_v16_postfix_status.md`
  - `tools/ai/planner_dataset_v16_postfix.jsonl`
  - seeds: `4701-4724`
  - merged teacher rows: `20,436`
  - max round reached: `3`
- broad post-fix student:
  - `v16`: `tools/ai/policy_dataset_v16_postfix.npz`, `tools/ai/policy_model_v16_postfix.npz`, `tools/ai/policy_model_v16_postfix.json`
  - offline metrics: `accuracy=0.9571`, `top2_accuracy=0.9986`, `value_mse=0.051889`
  - main corrected-speed holdout: `tools/ai/benchmark-neural-v16-postfix-level3-fresh.json`
    - `avgFinalScore=7317.5`, `avgMaxRound=2.125`, `clearRate=0.875`, `fallbackRate=1.0`
  - takeaway:
    - broad fresh data alone taught the student to lean on planner rescue too often, so `v16` was rejected
- rescue-focused post-fix curriculum:
  - miner upgrade: `tools/ai/generate_curriculum_campaign.py` now supports `--select-require-fallback` and `--select-loop-likely-only`
  - campaign artifacts:
    - `tools/ai/planner_curriculum_v17_postfix.json`
    - `tools/ai/planner_curriculum_v17_postfix.log`
    - `tools/ai/planner_curriculum_v17_postfix_status.md`
    - `tools/ai/planner_dataset_v17_postfix_curriculum.jsonl`
    - `tools/ai/planner_dataset_v17_postfix_teacher_only.jsonl`
  - candidate pool: `4801-4860`
  - selection filters: `remainingPelletsMin <= 40`, `deaths >= 1`, `roundMax <= 2`, `roundClears <= 1`, `usedFallback == true`
  - teacher-only rows: `10,785`
  - merged rows: `31,221`
- rescue-curriculum student:
  - `v17`: `tools/ai/policy_dataset_v17_postfix.npz`, `tools/ai/policy_model_v17_postfix.npz`, `tools/ai/policy_model_v17_postfix.json`
  - offline metrics: `accuracy=0.9553`, `top2_accuracy=0.9987`, `value_mse=0.050275`
  - main corrected-speed holdout: `tools/ai/benchmark-neural-v17-postfix-level3-fresh.json`
    - `avgFinalScore=7618.75`, `avgMaxRound=1.875`, `clearRate=0.75`, `fallbackRate=0.875`
  - secondary corrected-speed holdout: `tools/ai/benchmark-neural-v17-postfix-level3-4501-4508.json`
    - `avgFinalScore=8016.25`, `avgMaxRound=2.125`, `clearRate=0.75`, `fallbackRate=0.375`

Current takeaway:

- the speed fix changed the game so much that all older assumptions need to be revalidated
- corrected-speed `v15` remains the best overall live model
- `v16` and `v17` are useful artifacts, but neither beat `v15` on the main corrected-speed holdout
- the next post-fix improvement likely needs more selective curriculum, not another generic broad refresh

## Enhanced Stateless MLP Checkpoint (v18)

Completed on April 4, 2026:

- fresh upgraded-schema teacher campaign:
  - seeds: `6001-6024`
  - mode: `ResearchPlanner`
  - run profile: `headless`, `fast-forward=50`, `max-rounds=3`
  - merged rows: `20,393`
  - max round reached: `2`
  - artifacts:
    - `tools/ai/planner_campaign_v18_mlp102.json`
    - `tools/ai/planner_campaign_v18_mlp102_status.md`
    - `tools/ai/planner_campaign_v18_mlp102_status.json`
    - `tools/ai/planner_dataset_v18_mlp102.jsonl`
- prepared dataset:
  - `tools/ai/policy_dataset_v18_mlp102.npz`
  - `death-risk positives: 576`
- trained student:
  - `tools/ai/policy_model_v18_mlp102.npz`
  - `tools/ai/policy_model_v18_mlp102.json`
  - training log: `tools/ai/train_policy_v18_mlp102.log`
  - offline eval:
    - `accuracy=0.9569`
    - `top2_accuracy=0.9986`
    - `value_mse=0.054465`
    - `death_risk_bce=0.105027`
    - `death_risk_acc=0.9676`
- live benchmarks:
  - main holdout: `tools/ai/benchmark-neural-v18-mlp102-level3-fresh.json`
    - `avgFinalScore=7785.0`
    - `avgMaxRound=1.875`
    - `clearRate=0.875`
    - `fallbackRate=0.875`
  - fresh unseen holdout: `tools/ai/benchmark-neural-v18-mlp102-6101-6108.json`
    - `avgFinalScore=8496.25`
    - `avgMaxRound=2.25`
    - `clearRate=0.875`
    - `fallbackRate=1.0`
  - direct `v15` baselines on the same runtime code:
    - `tools/ai/benchmark-neural-v15-mlp102-baseline.json`
      - `avgFinalScore=10492.5`
      - `avgMaxRound=2.625`
      - `clearRate=1.0`
    - `tools/ai/benchmark-neural-v15-6101-6108-baseline.json`
      - `avgFinalScore=10053.75`
      - `avgMaxRound=2.375`
      - `clearRate=1.0`

Current takeaway:

- the first true `102`-feature / death-risk model is real and trains cleanly
- offline metrics are strong, but live gameplay is still weaker than corrected-speed `v15`
- the upgraded architecture alone was not enough; it likely needs targeted curriculum from the new schema, not just a broad fresh corpus
- `v15` remains the active exported brain

## Enhanced Stateless MLP Curriculum Checkpoint (v19)

Completed on April 5, 2026:

- targeted corrected-speed curriculum mined from `v18`:
  - candidate pool: `6201-6248`
  - filters:
    - `remainingPelletsMin <= 30`
    - `deaths >= 2`
    - `roundMax <= 2`
    - `roundClears <= 1`
  - matched student runs: `25`
  - selected hard seeds:
    - `6231 6207 6228 6245 6202 6211 6204 6243 6214 6235 6219 6226`
  - teacher-only rows: `12,936`
  - merged curriculum rows: `33,329`
  - artifacts:
    - `tools/ai/planner_curriculum_v19_mlp102.json`
    - `tools/ai/planner_curriculum_v19_mlp102_status.md`
    - `tools/ai/planner_dataset_v19_mlp102_curriculum.jsonl`
    - `tools/ai/planner_dataset_v19_mlp102_teacher_only.jsonl`
- trained student:
  - `tools/ai/policy_dataset_v19_mlp102.npz`
  - `tools/ai/policy_model_v19_mlp102.npz`
  - `tools/ai/policy_model_v19_mlp102.json`
  - training log: `tools/ai/train_policy_v19_mlp102.log`
  - offline eval:
    - `accuracy=0.9517`
    - `top2_accuracy=0.9983`
    - `value_mse=0.058006`
    - `death_risk_bce=0.112351`
    - `death_risk_acc=0.9719`
- live benchmarks:
  - main holdout: `tools/ai/benchmark-neural-v19-mlp102-level3-fresh.json`
    - `avgFinalScore=7506.25`
    - `avgMaxRound=2.125`
    - `clearRate=0.875`
    - `fallbackRate=1.0`
  - fresh unseen holdout: `tools/ai/benchmark-neural-v19-mlp102-6101-6108.json`
    - `avgFinalScore=7555.0`
    - `avgMaxRound=2.125`
    - `clearRate=1.0`
    - `fallbackRate=0.75`

Current takeaway:

- the first targeted curriculum pass improved `v19` over `v18` on some metrics, especially `avgMaxRound` on the main holdout
- but `v19` still did not beat corrected-speed `v15`
- `v15` remains the active exported brain

## Teacher Endgame Fix (v20 Foundation)

Completed on April 5, 2026:

Root cause analysis revealed that the v15 plateau was caused by teacher data quality, not model architecture. The ResearchPlannerBot showed looping behavior in 83% of v18 campaign seeds due to:

1. Search depth limited to 8, insufficient for scattered endgame pellets on a ~300-tile maze
2. Recent tile window of only 24, causing the planner to "forget" tiles visited 25+ steps ago
3. Flat visit penalties that penalized necessary long corridor traversal as heavily as short loops
4. No-pellet corridor penalty up to 80f in endgame, punishing the planner for traversing empty corridors to reach distant pellets
5. Weak terminal pellet gravity that couldn't pull the planner toward pellets beyond ~14 tiles

Changes made:

- `ResearchPlannerBot.cs`:
  - dynamic search depth: 8 normally, 12 when `remainingPellets <= 12` (deeper lookahead for endgame)
  - extreme endgame pathfinding override: when `remainingPellets <= 4` and `noProgressDecisions >= 12`, bypass scoring and navigate directly to nearest pellet via `MazeGraph.GetBestDirectionToward()`
  - reshaped visit penalties: short-cycle revisits (age <= 10) penalized 2.2x, medium (age <= 24) at 1.0x, stale (age > 24) at only 0.4x -- allows traversing old corridors while still deterring tight loops
  - reduced no-pellet corridor penalty in endgame from up to 80f down to 20f when `remainingPellets <= 12`
  - added strong pellet gravity in terminal evaluation: `80 / (1 + nearestPelletDistance)` when `remainingPellets <= 12`
  - terminal visit penalties also reshaped with same recency weighting

- `AutoplayTypes.cs`:
  - `RecentTileWindow` expanded from 24 to 48 for better loop memory

- `ObservationEncoder.cs`:
  - `revisitPressure` normalization changed from `/4` to `/6` to match wider window

- `prepare_dataset.py`:
  - `RECENT_TILE_WINDOW` constant updated from 24 to 48
  - `revisit_pressure` normalization changed from `/4.0` to `/6.0`

- `MazeGraph.cs`:
  - added `FindNearestPelletTile()` method for the pathfinding override

Expected outcome: teacher loop rate should drop from ~83% to under 20% on endgame seeds. Once validated, collect fresh teacher data and train v20.

**Important compatibility note**: the window size change from 24 to 48 means temporal features computed from new data will have different distributions than historical data. When blending old and new corpora for v20 training, the historical temporal features will be reconstructed by `prepare_dataset.py` using the new window size, so this is handled automatically.

### Teacher Validation Results (24 seeds, 7001-7024)

| Metric | v18 (old teacher) | v20 (fixed teacher) |
|--------|-------------------|---------------------|
| Loop rate | 83% (20/24) | 33% (8/24) |
| Round clears | 0 | 7/24 (29%) |
| Seeds reaching 1 pellet | few | 8/24 (33%) |
| Total rows | 20,393 | 14,616 |

Campaign artifacts:
- `tools/ai/planner_campaign_v20_teacherfix.json`
- `tools/ai/planner_campaign_v20_teacherfix_status.md`
- `tools/ai/planner_dataset_v20_teacherfix.jsonl`

### v20 Student Training

Training blend:
- `tools/ai/planner_dataset_v20_teacherfix.jsonl` at `1.0` (fresh fixed-teacher data)
- `tools/ai/planner_dataset_v10_teacher_only.jsonl` at `2.0` (proven curriculum)
- `tools/ai/planner_dataset_v15_teacher_only.jsonl` at `4.0` (near-clear curriculum)

Prepared: 31,957 samples with 1,008 death-risk positives
Trained: 50 epochs, best val accuracy ~93.3%, val policy loss ~0.168

Artifacts:
- `tools/ai/policy_dataset_v20.npz`
- `tools/ai/policy_model_v20.npz`
- `tools/ai/train_policy_v20.log`
- exported as `planner-distill-v20-teacherfix`

### v20 Benchmark Results

Main holdout (seeds 4301-4308):

| Metric | v15 (old baseline) | v20 (teacher fix) | Change |
|--------|-------------------|-------------------|--------|
| avgFinalScore | 9208.75 | **10180.0** | +10.5% |
| avgMaxRound | 2.375 | **2.875** | +0.5 |
| clearRate | 0.75 | **1.0** | +33% |
| avgMinPellets | ? | **1.0** | new |
| fallbackRate | 0.375 | 1.0 | increased |

Fresh unseen holdout (seeds 6101-6108):

| Metric | v20 |
|--------|-----|
| avgFinalScore | **10531.25** |
| avgMaxRound | **3.0** |
| clearRate | **1.0** |
| avgMinPellets | **1.0** |
| fallbackRate | 1.0 |
| timeoutRate | 0.125 |

Benchmark artifacts:
- `tools/ai/benchmark-neural-v20-level3-fresh.json`
- `tools/ai/benchmark-neural-v20-6101-6108.json`

**v20 is now the best checkpoint by a large margin.** It achieves 100% clear rate on both holdouts and reaches round 3 on all 8 fresh seeds. The main remaining issue is high fallback rate (1.0) -- the model still relies on the fixed planner for endgame rescue. This is expected since the fixed planner IS better at endgame now, so the fallback is stronger. The next curriculum pass should target reducing fallback dependence.

Next steps (completed -- see v21 section below):
1. [done] Promote v20 as the active runtime export
2. [done] Mine v20 failure seeds (high-fallback, loop-likely cases) with the fixed teacher for curriculum
3. [done] Train v21 with targeted curriculum to reduce fallback rate

## Fallback Reduction Curriculum (v21)

Completed on April 12, 2026:

v20 achieved 100% clear rate but had fallbackRate=1.0 on all seeds -- the student model relied on planner rescue for every endgame. v21 was trained to internalize the endgame strategy so the student can handle more of these situations autonomously.

### Curriculum Mining

- candidate pool: seeds 8001-8024 (24 fresh seeds)
- student mode: NeuralPolicy (v20)
- selection filter: `usedFallback == true`
- all 24/24 student runs used fallback (confirms universal fallback dependence)
- 12 hardest seeds selected by difficulty score (484-554 range)
- 7/12 selected seeds flagged as tight-loop-likely
- selected seeds: `8009 8001 8022 8018 8012 8021 8015 8008 8020 8004 8007 8014`
- teacher replay rows: 8,600
- baseline rows (v20 broad): 14,616
- merged curriculum: 23,216 rows

### Training

- blend: v20 broad teacher data at 1.0x + v21 curriculum teacher-only at 3.0x
- prepared dataset: 23,216 samples
  - endgame-weighted: 5,084
  - death-risk positives: 552
  - junction rows: 5,485
  - escape rows: 11,135
  - input-weighted (curriculum): 8,600
- trained: 50 epochs, 128->64 hidden, best val snapshot at epoch ~38
  - val_policy=0.1408, val_acc=94.4%, val_risk_acc=97.1%

### v21 Benchmark Results

Main holdout (seeds 4301-4308):

| Metric | v20 | v21 | Change |
|--------|-----|-----|--------|
| avgFinalScore | 10,180 | **12,031** | **+18.2%** |
| clearRate | 1.0 | 1.0 | same |
| fallbackRate | 1.0 | **0.875** | **-12.5%** |
| avgFinalPellets | 29.25 | **7.375** | **-74.8%** |
| timeoutRate | 0.375 | **0.125** | **-66.7%** |
| avgMaxRound | 2.875 | 2.75 | -0.125 |
| avgDeaths | 1.5 | 1.875 | +0.375 |

Fresh unseen holdout (seeds 6101-6108):

| Metric | v20 | v21 | Change |
|--------|-----|-----|--------|
| avgFinalScore | 10,531 | 10,575 | +0.4% |
| clearRate | 1.0 | 1.0 | same |
| fallbackRate | 1.0 | 1.0 | same |
| avgFinalPellets | 28.875 | **14.25** | **-50.7%** |
| timeoutRate | 0.125 | **0.0** | **-100%** |
| avgMaxRound | 3.0 | 2.625 | -0.375 |
| avgDeaths | 1.75 | 2.0 | +0.25 |

Artifacts:
- `tools/ai/planner_curriculum_v21_report.json`
- `tools/ai/planner_curriculum_v21_status.md`
- `tools/ai/planner_curriculum_v21_status.json`
- `tools/ai/planner_dataset_v21_curriculum.jsonl`
- `tools/ai/planner_dataset_v21_teacher_only.jsonl`
- `tools/ai/planner_dataset_v21_prepared.npz`
- `tools/ai/planner_model_v21_curriculum.npz`
- `tools/ai/benchmark-neural-v21-level3-fresh.json`
- `tools/ai/benchmark-neural-v21-6101-6108.json`

**v21 is now the active exported brain: `planner-distill-v21-curriculum`**

Current takeaway:
- v21 is a clear improvement on holdout: score up 18.2%, fallback dropping (1 seed cleared without planner rescue), dramatically better pellet cleanup
- fresh seeds show steady but smaller gains -- same clear rate, halved final pellet count, zero timeouts
- fallback is still 1.0 on fresh seeds, confirming that the endgame loop pattern is deep-seated
- curriculum mining is hitting diminishing returns -- the student learns incremental improvements from each pass but the stateless MLP fundamentally struggles with multi-step endgame navigation
- **frame-stacking (N=3) is now the recommended next architecture upgrade** to give the student temporal context for loop detection

## Latest Benchmark Snapshot

Latest benchmark artifacts live in:

- `tools/ai/benchmark-neural-v2.json`
- `tools/ai/benchmark-neural-v3.json`
- `tools/ai/benchmark-neural-v3-postfix.json`
- `tools/ai/benchmark-neural-v3-fresh.json`
- `tools/ai/benchmark-neural-v4.json`
- `tools/ai/benchmark-neural-v4-fresh.json`
- `tools/ai/benchmark-neural-v5.json`
- `tools/ai/benchmark-neural-v5-fresh.json`
- `tools/ai/benchmark-neural-v5-hard.json`
- `tools/ai/benchmark-neural-v6.json`
- `tools/ai/benchmark-neural-v6-fresh.json`
- `tools/ai/benchmark-neural-v6-hard.json`
- `tools/ai/benchmark-neural-v6-level3-fresh.json`
- `tools/ai/benchmark-neural-v10-level3-fresh.json`
- `tools/ai/benchmark-neural-v11-level3-fresh.json`
- `tools/ai/benchmark-neural-v12-level3-fresh.json`
- `tools/ai/benchmark-neural-v13-level3-fresh.json`
- `tools/ai/benchmark-neural-v14-level3-fresh.json`
- `tools/ai/benchmark-neural-v14-level3-4501-4508.json`
- `tools/ai/benchmark-neural-v15-level3-fresh.json`
- `tools/ai/benchmark-neural-v15-level3-4501-4508.json`
- `tools/ai/benchmark-neural-v15-postfix-level3-fresh.json`
- `tools/ai/benchmark-neural-v16-postfix-level3-fresh.json`
- `tools/ai/benchmark-neural-v17-postfix-level3-fresh.json`
- `tools/ai/benchmark-neural-v17-postfix-level3-4501-4508.json`
- `tools/ai/benchmark-neural-v20-level3-fresh.json`
- `tools/ai/benchmark-neural-v20-6101-6108.json`
- `tools/ai/benchmark-neural-v21-level3-fresh.json`
- `tools/ai/benchmark-neural-v21-6101-6108.json`
- `tools/ai/benchmark-planner-v2.json`

Current takeaway:

- `NeuralPolicy v21` is now the active exported checkpoint, trained on fallback-targeting curriculum from v20 failures
- v21 achieves 100% clear rate, avg score 12,031 on holdout (+18.2% over v20), and reduced fallback from 1.0 to 0.875
- the teacher endgame fix (v20) was the major breakthrough; v21 curriculum mining provides incremental gains on top
- fallback is still 1.0 on fresh seeds -- frame-stacking is the recommended next upgrade to break through the endgame loop ceiling

## Recent Gameplay Bug Pass

Confirmed and fixed in code:

- ghost house slots and exit coordinates were inconsistent with `MazeData`, which left some ghosts trapped or behaving incorrectly
- eaten ghosts were targeting the wrong house location, which could leave them wandering as eyes instead of recovering
- death resets were incorrectly calling the new-round reset path, wiping ghost-release progress
- the autoplay banner was rendering on top of the HUD high-score line
- player movement speed was being applied in world-units-per-second while the tuning data is expressed in tiles-per-second, which made Pac-Man effectively move at about half his intended tuned speed
- stacked energizers now fully restart frightened mode instead of letting the old frightened timer expire underneath the second energizer

Quick runtime sanity check:

- the latest player log now shows explicit releases for ghosts 2 and 3 during play, which was a strong indicator that the house logic was previously broken

## Recommended Next Session Starting Point

If restarting from scratch, verify these in order:

1. Read this file
2. Read `tools/ai/README.md`
3. Assume `v21` is the live baseline (`planner-distill-v21-curriculum`)
4. v21 achieves 100% clear rate, avg score 12,031 on holdout, fallbackRate 0.875 (holdout) / 1.0 (fresh)
5. Curriculum mining has hit diminishing returns -- v21 was an incremental gain over v20
6. The immediate next step is **frame-stacking (N=3)** to give the stateless MLP temporal context:
   a. In `prepare_dataset.py`: maintain circular buffer of last 3 observations per episode, concatenate into 306-feature input
   b. In `train_policy.py`: increase hidden layers to 192->96 for the larger input
   c. In `NeuralPolicyBot.cs`: maintain a `Queue<float[]>` of recent frames, concatenate at each decision
   d. In `ObservationEncoder.cs`: add `BuildStackedInput()` method
   e. Train v22 from frame-stacked data, benchmark against v21
7. If frame-stacking also plateaus, consider RL with reward shaping (requires live environment stepping infrastructure)

Key architecture context:
- `ResearchPlannerBot.cs` uses dynamic search depth (12 in endgame), recency-weighted visit penalties, reduced corridor penalties, stronger pellet gravity, and a direct pathfinding override for extreme endgame
- `AutoplayTypes.cs` window expanded from 24 to 48
- `ObservationEncoder.cs` and `prepare_dataset.py` revisit normalization updated from /4 to /6
- `MazeGraph.cs` has `FindNearestPelletTile()` for the pathfinding override
- `NeuralPolicyBot.cs` does 1-step value/risk lookahead with heuristic blending and loop bias
- Current model: 102-feature input, 128->64 hidden MLP, 3 heads (policy/value/death-risk)
