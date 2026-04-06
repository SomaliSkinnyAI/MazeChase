# AI Training Tools

These scripts train the lightweight autoplay policy that plugs back into the Unity runtime.

Current architecture:

- stateless MLP trunk
- `policy` head for move selection
- `value` head for short-horizon return
- `death-risk` head for "will this likely die soon?"
- runtime confidence fallback to `ExpertLegacy` when the neural policy is too uncertain

## Workflow

1. Record planner data from the game:

```powershell
powershell -ExecutionPolicy Bypass -File tools/run-game.ps1 -GameArgs @("-batchmode", "-nographics", "--ai-headless", "--autoplay", "--autoplay-mode=ResearchPlanner", "--ai-record", "--ai-fast-forward=20", "--ai-seed=101", "--ai-quit-on-game-over")
```

2. Prepare the JSONL dataset:

```powershell
python tools/ai/prepare_dataset.py --input "C:\path\to\policy-dataset.jsonl" --output tools/ai/policy_dataset.npz
```

`prepare_dataset.py` now automatically:

- upweights low-pellet endgame samples so later training passes spend more capacity on finish-the-round behavior
- preserves compatibility with older `83`-feature and `91`-feature JSONL rows
- emits a `death_risk` target by looking ahead `N` committed decisions for a life loss

3. Train the model:

```powershell
python tools/ai/train_policy.py --dataset tools/ai/policy_dataset.npz --output tools/ai/policy_model.npz --hidden1 128 --hidden2 64 --risk-weight 0.35 --risk-positive-weight 3.0
```

4. Evaluate it:

```powershell
python tools/ai/evaluate_model.py --dataset tools/ai/policy_dataset.npz --model tools/ai/policy_model.npz
```

5. Export weights for Unity:

```powershell
python tools/ai/export_weights.py --model tools/ai/policy_model.npz --output MazeChase/StreamingAssets/AI/policy_model.json --source planner-distill-v1
```

6. Launch the game and press `F2` for neural autoplay, `F3` for the rules/planner AI, or `F4` to return to manual control. The status card on screen will name the active brain, including the exact neural checkpoint when `NeuralPolicy` is active and whether fallback control is active.

7. Benchmark seeded runs after export:

```powershell
python tools/ai/run_benchmark.py --mode NeuralPolicy --seeds 101 202 303 --timeout-seconds 45 --fast-forward 8 --output tools/ai/benchmark-neural.json
```

`run_benchmark.py`, `generate_dataset_campaign.py`, and `generate_curriculum_campaign.py` now default to headless launches. Use `--no-headless` if you explicitly want to watch the game window during a run.

The benchmark script launches the built game once per seed, reads the recorded dataset for that run, and summarizes:

- final score
- highest round reached
- minimum and final pellet counts
- death count
- fallback usage
- no-progress stall lengths
- likely tight endgame loop signatures

Current benchmark files kept in the repo workspace:

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
- `tools/ai/benchmark-planner-v2.json`

For long campaigns, `generate_dataset_campaign.py` can also maintain a live status page while it runs:

```powershell
python tools/ai/generate_dataset_campaign.py `
  --mode ResearchPlanner `
  --seeds 2301 2302 2303 2304 `
  --fast-forward 40 `
  --max-rounds 2 `
  --status-json tools/ai/planner_campaign_status.json `
  --status-markdown tools/ai/planner_campaign_status.md `
  --output-merged tools/ai/planner_dataset.jsonl `
  --output-report tools/ai/planner_campaign.json
```

The Markdown file is meant to be opened in the editor during the run so you can see:

- current seed
- completed seeds / total seeds
- elapsed time and ETA
- rows collected so far
- latest per-seed metrics

## Larger Campaign Workflow

For a multi-seed planner recording pass, use `generate_dataset_campaign.py`:

```powershell
python tools/ai/generate_dataset_campaign.py `
  --mode ResearchPlanner `
  --seeds 1201 1202 1203 1204 1205 1206 1207 1208 1209 1210 1211 1212 `
  --fast-forward 40 `
  --max-rounds 1 `
  --timeout-seconds 40 `
  --output-merged tools/ai/planner_dataset_v4.jsonl `
  --output-report tools/ai/planner_campaign_v4.json
```

That script will:

- launch one autoplay run per seed
- collect the per-run JSONL dataset files written by the game
- merge them into a single corpus
- emit a JSON summary report with per-seed metrics

The current large-corpus artifacts are:

- `tools/ai/planner_campaign_v4.json`
- `tools/ai/planner_dataset_v4.jsonl`
- `tools/ai/policy_dataset_v4.npz`
- `tools/ai/policy_model_v4.npz`

Current `v4` corpus stats:

- 12 planner seeds
- 7,068 merged planner decisions
- 1,675 endgame-weighted rows after preparation
- evaluation: `accuracy=0.9363`, `top2_accuracy=0.9967`, `value_mse=0.079546`

Latest headless teacher corpus stats:

- campaign report: `tools/ai/planner_campaign_v7_headless.json`
- merged dataset: `tools/ai/planner_dataset_v7_headless.jsonl`
- status page: `tools/ai/planner_campaign_v7_headless_status.md`
- 24 planner seeds
- 16,743 merged planner decisions
- 0 timeouts
- max round reached: `2`

## Latest Training Passes

Broad headless candidate from the new corpus:

```powershell
python tools/ai/prepare_dataset.py `
  --input tools/ai/planner_dataset_v7_headless.jsonl `
  --output tools/ai/policy_dataset_v8.npz

python tools/ai/train_policy.py `
  --dataset tools/ai/policy_dataset_v8.npz `
  --output tools/ai/policy_model_v8.npz `
  --epochs 50
```

Blended candidate that re-injects the older hard-seed teacher-only curriculum:

```powershell
python tools/ai/prepare_dataset.py `
  --input tools/ai/planner_dataset_v7_headless.jsonl tools/ai/planner_dataset_v5_teacher_only.jsonl `
  --input-weight 1.0 2.2 `
  --output tools/ai/policy_dataset_v9.npz `
  --escape-weight-bonus 0.8 `
  --danger-weight-scale 1.1 `
  --low-lives-weight-bonus 0.4 `
  --finish-weight-bonus 0.6

python tools/ai/train_policy.py `
  --dataset tools/ai/policy_dataset_v9.npz `
  --output tools/ai/policy_model_v9.npz `
  --epochs 50
```

Current result:

- both `v8` and `v9` trained and benchmarked successfully
- both were more stable than earlier weak checkpoints, but both underperformed live `v6` on the fresh `1301-1304` holdout
- `v6` therefore remains the active promoted export

## Level-3 Push

The current autoplay goal is no longer just "clear level 1 sometimes." The working target is consistent progress into level 3.

Baseline for that push:

- holdout benchmark: `tools/ai/benchmark-neural-v6-level3-fresh.json`
- seeds: `4301 4302 4303 4304 4305 4306 4307 4308`
- `v6` result: `avgFinalScore=2470.0`, `avgMinPellets=51.0`, `clearRate=0.0`

First targeted curriculum pass:

- campaign artifacts:
  - `tools/ai/planner_curriculum_v10.json`
  - `tools/ai/planner_curriculum_v10_status.md`
  - `tools/ai/planner_dataset_v10_teacher_only.jsonl`
- teacher-only rows: `9,034`
- trained students:
  - `v10`: `tools/ai/policy_dataset_v10.npz`, `tools/ai/policy_model_v10.npz`, `tools/ai/policy_model_v10.json`
  - `v11`: `tools/ai/policy_dataset_v11.npz`, `tools/ai/policy_model_v11.npz`, `tools/ai/policy_model_v11.json`
- offline metrics:
  - `v10`: `accuracy=0.9417`, `top2_accuracy=0.9977`, `value_mse=0.060463`
  - `v11`: `accuracy=0.9454`, `top2_accuracy=0.9981`, `value_mse=0.057329`
- level-3 holdout results:
  - `v10`: `tools/ai/benchmark-neural-v10-level3-fresh.json`
    - `avgFinalScore=3127.5`, `avgMinPellets=25.25`, `clearRate=0.0`, `fallbackRate=0.25`
  - `v11`: `tools/ai/benchmark-neural-v11-level3-fresh.json`
    - `avgFinalScore=2972.5`, `avgMinPellets=24.75`, `clearRate=0.0`, `fallbackRate=0.0`

Second targeted curriculum pass mined from live `v10`:

- campaign artifacts:
  - `tools/ai/planner_curriculum_v12.json`
  - `tools/ai/planner_curriculum_v12_status.md`
  - `tools/ai/planner_dataset_v12_teacher_only.jsonl`
- candidate pool: `5301-5332`
- hard seeds selected: `5325 5314 5308 5313 5303 5316 5315 5322 5320 5306 5319 5332`
- teacher-only rows: `10,843`
- trained student:
  - `v12`: `tools/ai/policy_dataset_v12.npz`, `tools/ai/policy_model_v12.npz`, `tools/ai/policy_model_v12.json`
  - offline metrics: `accuracy=0.9442`, `top2_accuracy=0.9978`, `value_mse=0.061785`
  - holdout result: `tools/ai/benchmark-neural-v12-level3-fresh.json`
    - `avgFinalScore=3046.25`, `avgMinPellets=39.875`, `clearRate=0.0`

Current takeaway:

- `v10` is the strongest pre-temporal checkpoint on pellet pressure
- `v11` looked better offline but not in live gameplay
- `v12` did not beat `v10`, so the next iteration needed either a model upgrade or stronger near-clear curriculum

## Temporal Upgrade

The next architecture step was a small but meaningful model upgrade rather than another pure data pass.

What changed:

- `ObservationEncoder` now appends `8` temporal/context features beyond the legacy `83`-feature base input
- those new features capture revisit pressure, repeat age, recent tile diversity, no-progress streaks, low-pellet stalls, direction streaks, reversal flags, and tight-loop pressure
- `prepare_dataset.py` can reconstruct the same temporal features from older JSONL recordings, so existing corpora do not need to be discarded

Temporal candidates trained on April 4, 2026:

- `v13`: `tools/ai/policy_dataset_v13.npz`, `tools/ai/policy_model_v13.npz`, `tools/ai/policy_model_v13.json`
  - offline metrics: `accuracy=0.9554`, `top2_accuracy=0.9984`, `value_mse=0.050486`
  - holdout benchmark: `tools/ai/benchmark-neural-v13-level3-fresh.json`
  - runtime result: `avgFinalScore=3216.25`, `avgMinPellets=40.375`, `clearRate=0.0`
- `v14`: `tools/ai/policy_dataset_v14.npz`, `tools/ai/policy_model_v14.npz`, `tools/ai/policy_model_v14.json`
  - offline metrics: `accuracy=0.9509`, `top2_accuracy=0.9984`, `value_mse=0.047772`
  - holdout benchmark: `tools/ai/benchmark-neural-v14-level3-fresh.json`
  - runtime result: `avgFinalScore=3230.0`, `avgMinPellets=33.5`, `clearRate=0.0`

Current result:

- the temporal upgrade helped on raw score and stability
- `v14` proved the temporal/context upgrade was worthwhile, but it was still too cautious to convert enough near-clears

## Near-Clear Curriculum (v15)

To target the specific failure shape where the student dies with only a few pellets left, the curriculum miner now supports explicit replay filters:

- `--select-max-min-pellets`
- `--select-min-deaths`
- `--select-max-round`
- `--select-max-round-clears`

The `v15` pass used those filters to mine `v14` near-clear failures from seeds `4401-4460`, then replayed the selected seeds with `ResearchPlanner`.

Artifacts:

- `tools/ai/planner_curriculum_v15.json`
- `tools/ai/planner_curriculum_v15.log`
- `tools/ai/planner_curriculum_v15_status.md`
- `tools/ai/planner_curriculum_v15_status.json`
- `tools/ai/planner_dataset_v15_teacher_only.jsonl`
- `tools/ai/policy_dataset_v15.npz`
- `tools/ai/policy_model_v15.npz`
- `tools/ai/policy_model_v15.json`

`v15` training blend:

- `tools/ai/planner_dataset_v7_headless.jsonl` at `1.0`
- `tools/ai/planner_dataset_v10_teacher_only.jsonl` at `2.5`
- `tools/ai/planner_dataset_v15_teacher_only.jsonl` at `4.5`

`v15` results:

- offline metrics: `accuracy=0.9570`, `top2_accuracy=0.9987`, `value_mse=0.046604`
- main level-3 holdout: `tools/ai/benchmark-neural-v15-level3-fresh.json`
  - `avgFinalScore=3870.0`, `avgMinPellets=19.75`, `clearRate=0.25`, `fallbackRate=0.125`
- secondary unseen holdout: `tools/ai/benchmark-neural-v15-level3-4501-4508.json`
  - `avgFinalScore=3377.5`, `avgMinPellets=37.75`, `clearRate=0.0`, `fallbackRate=0.125`
- direct comparison baseline: `tools/ai/benchmark-neural-v14-level3-4501-4508.json`
  - `v14` on the same seeds: `avgFinalScore=2867.5`, `avgMinPellets=50.75`, `clearRate=0.0`

Current result:

- `v15` is now the active exported runtime model: `planner-distill-v15-nearclear-curriculum`
- targeted near-clear curriculum worked better than another broad data sweep
- the model is better, but still not consistently reaching level 3

## Post-Fix Retraining

After the player-speed bug was fixed and stacked energizers were made to restart frightened mode correctly, the runtime dynamics changed enough that the neural student needed a fresh evaluation pass.

Corrected-speed baseline:

- `tools/ai/benchmark-neural-v15-postfix-level3-fresh.json`
  - `avgFinalScore=9208.75`
  - `avgMaxRound=2.375`
  - `clearRate=0.75`
  - `fallbackRate=0.375`

Fresh broad post-fix teacher corpus:

- `tools/ai/planner_campaign_v16_postfix.json`
- `tools/ai/planner_campaign_v16_postfix_status.md`
- `tools/ai/planner_dataset_v16_postfix.jsonl`
- seeds `4701-4724`
- merged rows `20,436`

Broad post-fix student:

- `v16`: `tools/ai/policy_dataset_v16_postfix.npz`, `tools/ai/policy_model_v16_postfix.npz`, `tools/ai/policy_model_v16_postfix.json`
- offline metrics: `accuracy=0.9571`, `top2_accuracy=0.9986`, `value_mse=0.051889`
- main corrected-speed holdout: `tools/ai/benchmark-neural-v16-postfix-level3-fresh.json`
  - `avgFinalScore=7317.5`
  - `avgMaxRound=2.125`
  - `clearRate=0.875`
  - `fallbackRate=1.0`

That looked superficially stronger on clears, but it only got there by leaning on planner rescue constantly, so it was rejected.

Rescue-focused corrected-speed curriculum:

- the miner now supports `--select-require-fallback` and `--select-loop-likely-only`
- `tools/ai/planner_curriculum_v17_postfix.json`
- `tools/ai/planner_curriculum_v17_postfix_status.md`
- `tools/ai/planner_dataset_v17_postfix_teacher_only.jsonl`
- candidate seeds `4801-4860`
- selection filters:
  - `remainingPelletsMin <= 40`
  - `deaths >= 1`
  - `roundMax <= 2`
  - `roundClears <= 1`
  - `usedFallback == true`
- teacher-only rows `10,785`

Rescue-curriculum student:

- `v17`: `tools/ai/policy_dataset_v17_postfix.npz`, `tools/ai/policy_model_v17_postfix.npz`, `tools/ai/policy_model_v17_postfix.json`
- offline metrics: `accuracy=0.9553`, `top2_accuracy=0.9987`, `value_mse=0.050275`
- main corrected-speed holdout: `tools/ai/benchmark-neural-v17-postfix-level3-fresh.json`
  - `avgFinalScore=7618.75`
  - `avgMaxRound=1.875`
  - `clearRate=0.75`
  - `fallbackRate=0.875`
- secondary corrected-speed holdout: `tools/ai/benchmark-neural-v17-postfix-level3-4501-4508.json`
  - `avgFinalScore=8016.25`
  - `avgMaxRound=2.125`
  - `clearRate=0.75`
  - `fallbackRate=0.375`

Current result:

- corrected-speed `v15` remains the best live checkpoint
- `v16` and `v17` are useful post-fix artifacts, but neither displaced `v15`
- the next gain probably needs more selective corrected-speed curriculum, not just broader fresh data

## Enhanced Stateless MLP Pivot

Implemented on April 4, 2026:

- `ObservationEncoder.cs` now appends:
  - player velocity vector
  - player junction proximity
  - ghost velocity vectors for all four ghosts
- `GraphPolicyModel.cs` now supports an optional `death-risk` head in addition to policy/value
- `prepare_dataset.py` writes `death_risk` labels into the dataset
- `train_policy.py` jointly optimizes policy/value/risk losses
- `export_weights.py` now exports the risk head for Unity
- `NeuralPolicyBot.cs` now uses a confidence-threshold fallback to `ExpertLegacy`
- runtime remains stateless and feed-forward

Compatibility note:

- the current promoted runtime checkpoint is still the legacy `91`-feature `v15` export
- the Unity runtime now contains an adapter that can still run legacy `91`-feature models while the new `102`-feature risk-head checkpoint is being trained

First full upgraded-schema run:

- teacher corpus:
  - `tools/ai/planner_campaign_v18_mlp102.json`
  - `tools/ai/planner_campaign_v18_mlp102_status.md`
  - `tools/ai/planner_dataset_v18_mlp102.jsonl`
  - `20,393` rows from seeds `6001-6024`
- prepared dataset:
  - `tools/ai/policy_dataset_v18_mlp102.npz`
  - `576` positive death-risk labels
- trained student:
  - `tools/ai/policy_model_v18_mlp102.npz`
  - `tools/ai/policy_model_v18_mlp102.json`
  - offline eval:
    - `accuracy=0.9569`
    - `top2_accuracy=0.9986`
    - `value_mse=0.054465`
    - `death_risk_bce=0.105027`
    - `death_risk_acc=0.9676`
- live benchmarks:
  - `tools/ai/benchmark-neural-v18-mlp102-level3-fresh.json`
  - `tools/ai/benchmark-neural-v18-mlp102-6101-6108.json`

Result:

- `v18` trained cleanly and the upgraded architecture is real
- but `v18` underperformed corrected-speed `v15` on both the main and fresh unseen holdouts
- `v15` therefore remains the promoted runtime model

First targeted upgraded-schema curriculum pass:

- curriculum miner output:
  - `tools/ai/planner_curriculum_v19_mlp102.json`
  - `tools/ai/planner_curriculum_v19_mlp102_status.md`
  - `tools/ai/planner_dataset_v19_mlp102_curriculum.jsonl`
  - `tools/ai/planner_dataset_v19_mlp102_teacher_only.jsonl`
  - `12,936` teacher-only rows from `12` selected hard seeds
- trained student:
  - `tools/ai/policy_dataset_v19_mlp102.npz`
  - `tools/ai/policy_model_v19_mlp102.npz`
  - `tools/ai/policy_model_v19_mlp102.json`
  - offline eval:
    - `accuracy=0.9517`
    - `top2_accuracy=0.9983`
    - `value_mse=0.058006`
    - `death_risk_bce=0.112351`
    - `death_risk_acc=0.9719`
- live benchmarks:
  - `tools/ai/benchmark-neural-v19-mlp102-level3-fresh.json`
  - `tools/ai/benchmark-neural-v19-mlp102-6101-6108.json`

Result:

- `v19` improved some runtime metrics over `v18`, but it still did not beat corrected-speed `v15`
- the promoted runtime checkpoint remains `planner-distill-v15-nearclear-curriculum`

## Student-Guided Curriculum Workflow

To mine hard seeds from the current student and replay them with the planner teacher:

```powershell
python tools/ai/generate_curriculum_campaign.py `
  --candidate-seeds 1501 1502 1503 1504 1505 1506 1507 1508 1509 1510 1511 1512 `
  --hard-seed-count 6 `
  --timeout-seconds 40 `
  --student-fast-forward 32 `
  --teacher-fast-forward 40 `
  --max-rounds 1 `
  --baseline-jsonl tools/ai/planner_dataset_v4.jsonl `
  --output-merged tools/ai/planner_dataset_v5_curriculum.jsonl `
  --output-teacher-only tools/ai/planner_dataset_v5_teacher_only.jsonl `
  --output-report tools/ai/planner_curriculum_v5.json
```

That script will:

- run the current exported neural policy on the candidate seed pool
- rank the seeds by failure severity
- replay the hardest seeds with `ResearchPlanner`
- optionally emit both a merged curriculum JSONL and a teacher-only JSONL

For a near-clear-only replay pass, add filters like:

```powershell
python tools/ai/generate_curriculum_campaign.py `
  --candidate-seeds 4401 4402 4403 4404 4405 4406 4407 4408 `
  --hard-seed-count 4 `
  --target-round 3 `
  --select-max-min-pellets 30 `
  --select-min-deaths 2 `
  --select-max-round 1 `
  --select-max-round-clears 0 `
  --output-merged tools/ai/planner_dataset_filtered.jsonl
```

`prepare_dataset.py` now also supports multiple input corpora with explicit per-input multipliers:

```powershell
python tools/ai/prepare_dataset.py `
  --input tools/ai/planner_dataset_v4.jsonl tools/ai/planner_dataset_v5_teacher_only.jsonl `
  --input-weight 1.0 1.6 `
  --output tools/ai/policy_dataset_v6.npz `
  --escape-weight-bonus 0.6 `
  --danger-weight-scale 1.0 `
  --low-lives-weight-bonus 0.3
```

Current curriculum artifacts:

- `tools/ai/planner_curriculum_v5.json`
- `tools/ai/planner_dataset_v5_curriculum.jsonl`
- `tools/ai/planner_dataset_v5_teacher_only.jsonl`
- `tools/ai/policy_dataset_v5.npz`
- `tools/ai/policy_model_v5.npz`
- `tools/ai/policy_dataset_v6.npz`
- `tools/ai/policy_model_v6.npz`

Current checkpoint status:

- `v5` was the first student-guided curriculum experiment
- `v6` is still the best checkpoint on the older `1301-1304` holdout
- `v10` remains the most aggressive level-3-focused student on pellet pressure
- `v15` was the active export until v20 replaced it
- `v16`-`v19` all failed to beat `v15` -- root cause identified as teacher looping (83% endgame loop rate), not architecture
- **`v20` is now the active export: `planner-distill-v20-teacherfix`**
  - trained on fixed-teacher data (loop rate 33% vs old 83%) blended with v10/v15 curriculum
  - main holdout (4301-4308): `avgFinalScore=10180`, `clearRate=1.0`, `avgMaxRound=2.875`
  - fresh unseen (6101-6108): `avgFinalScore=10531`, `clearRate=1.0`, `avgMaxRound=3.0`
  - v20 beats v15 by +10.5% score, +33% clear rate, and reaches round 3 consistently
  - remaining issue: fallbackRate=1.0 (relies on improved planner for endgame rescue)

## Teacher Endgame Fix (April 5, 2026)

Root cause of the v15 plateau was identified as teacher data quality, not model architecture. The `ResearchPlannerBot` was looping in ~83% of endgame seeds. The following changes were made:

**ResearchPlannerBot.cs:**
- Dynamic search depth: 8 normally, 12 when `remainingPellets <= 12`
- Extreme endgame pathfinding override: when `remainingPellets <= 4` and stalled, navigates directly to nearest pellet
- Recency-weighted visit penalties: short-cycle revisits (age <= 10) penalized 2.2x, stale visits (age > 24) only 0.4x
- Reduced no-pellet corridor penalty from up to 80f to 20f in endgame
- Strong pellet gravity in terminal evaluation: `80 / (1 + nearestPelletDistance)` for last 12 pellets

**AutoplayTypes.cs:**
- `RecentTileWindow` expanded from 24 to 48

**ObservationEncoder.cs + prepare_dataset.py:**
- `revisitPressure` normalization changed from `/4` to `/6` to match wider window
- `RECENT_TILE_WINDOW` constant updated from 24 to 48

**MazeGraph.cs:**
- Added `FindNearestPelletTile()` for the pathfinding override

**Compatibility:** `prepare_dataset.py` reconstructs temporal features from JSONL history, so old corpora are automatically reprocessed with the new 48-tile window. However, the distributional shift means mixing old and new temporal features may be noisy -- prefer fresh data when possible.

**Next steps:**
1. Validate the fix: run 24+ seeds in ResearchPlanner mode, check loop rate < 20%
2. Mine v15 failure seeds with the fixed teacher
3. Train v20: `prepare_dataset.py` -> `train_policy.py` -> `export_weights.py`
4. Benchmark v20 vs v15: target clearRate > 80%, avgFinalScore > 9500, fallbackRate < 30%

## Notes

- Fast-moving game windows are usually dataset generation or runtime validation, not Python-side training.
- The bulk campaign scripts are now headless by default, so large recording passes should no longer pop visible player windows unless `--no-headless` is set.
- Use different `--ai-seed` values across recording runs to widen the teacher corpus while keeping runs reproducible.
- Runtime model search order is:
  1. `Application.persistentDataPath/AI/policy_model.json`
  2. `Application.streamingAssetsPath/AI/policy_model.json`
- `run_benchmark.py` uses dataset output for evaluation, so it still produces useful summaries even when a run hits the timeout and gets terminated.
