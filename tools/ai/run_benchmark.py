from __future__ import annotations

import argparse
import json
import os
import statistics
import subprocess
import sys
import time
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_GAME_PATH = REPO_ROOT / "MazeChase" / "BuildOutput" / "Win64" / "MazeChase.exe"
DEFAULT_DATASET_DIR = Path(os.environ.get("USERPROFILE", "")) / "AppData" / "LocalLow" / "IndieArcade" / "MazeChase" / "AI" / "datasets"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Launch seeded autoplay runs and summarize the recorded JSONL datasets.")
    parser.add_argument("--game", type=Path, default=DEFAULT_GAME_PATH, help="Path to the built MazeChase player executable.")
    parser.add_argument("--dataset-dir", type=Path, default=DEFAULT_DATASET_DIR, help="Directory where the runtime writes policy-dataset-*.jsonl files.")
    parser.add_argument("--mode", default="NeuralPolicy", help="Autoplay mode to benchmark.")
    parser.add_argument("--seeds", type=int, nargs="+", required=True, help="Deterministic seeds to run.")
    parser.add_argument("--fast-forward", type=float, default=8.0, help="Unity time scale for autoplay runs.")
    parser.add_argument("--timeout-seconds", type=float, default=45.0, help="Hard timeout per seed before the player process is terminated.")
    parser.add_argument("--settle-seconds", type=float, default=1.0, help="Small delay after each run so the dataset file lands on disk.")
    parser.add_argument("--headless", action=argparse.BooleanOptionalAction, default=True, help="Run the player with Unity batchmode/nographics and the in-game headless profile.")
    parser.add_argument("--max-rounds", type=int, default=0, help="Optional round cap passed through to the Unity runtime. Zero means unlimited.")
    parser.add_argument("--low-pellet-threshold", type=int, default=24, help="Pellet count considered an endgame state.")
    parser.add_argument("--loop-window", type=int, default=24, help="Decision window used to flag likely tight loops.")
    parser.add_argument("--loop-unique-tiles", type=int, default=8, help="Maximum distinct tiles inside the loop window before it stops counting as a tight loop.")
    parser.add_argument("--output", type=Path, help="Optional JSON path for the aggregate report.")
    parser.add_argument("--extra-arg", action="append", default=[], help="Additional raw argument to pass through to the game executable.")
    return parser.parse_args()


def existing_dataset_files(dataset_dir: Path) -> set[Path]:
    return {path.resolve() for path in dataset_dir.glob("policy-dataset-*.jsonl")}


def kill_process_tree(pid: int) -> None:
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
    else:
        try:
            os.kill(pid, 9)
        except OSError:
            pass


def discover_dataset(dataset_dir: Path, baseline: set[Path], started_at: float) -> Path:
    new_files = [path for path in dataset_dir.glob("policy-dataset-*.jsonl") if path.resolve() not in baseline]
    if new_files:
        return max(new_files, key=lambda path: path.stat().st_mtime)

    recent_files = [
        path
        for path in dataset_dir.glob("policy-dataset-*.jsonl")
        if path.stat().st_mtime >= started_at - 1.0
    ]
    if recent_files:
        return max(recent_files, key=lambda path: path.stat().st_mtime)

    raise FileNotFoundError(f"No new dataset file found in {dataset_dir}")


def load_rows(dataset_path: Path) -> list[dict]:
    rows: list[dict] = []
    with dataset_path.open("r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def analyze_rows(
    rows: list[dict],
    low_pellet_threshold: int,
    loop_window: int,
    loop_unique_tiles: int,
) -> dict:
    if not rows:
        return {
            "rows": 0,
            "sources": [],
            "usedFallback": False,
            "avgConfidence": None,
            "scoreStart": 0,
            "scoreFinal": 0,
            "scoreMax": 0,
            "roundStart": 1,
            "roundFinal": 1,
            "roundMax": 1,
            "roundClears": 0,
            "livesStart": 3,
            "livesFinal": 3,
            "livesMin": 3,
            "deaths": 0,
            "remainingPelletsStart": 0,
            "remainingPelletsFinal": 0,
            "remainingPelletsMin": 0,
            "maxNoProgressStall": 0,
            "maxLowPelletNoProgressStall": 0,
            "tightLoopEvents": 0,
            "tightLoopLikely": False,
        }

    sources = sorted({str(row.get("source", "")) for row in rows if row.get("source") is not None})
    confidences = [float(row.get("confidence", 0.0)) for row in rows if row.get("confidence") is not None]

    scores = [int(row.get("score", 0)) for row in rows]
    rounds = [int(row.get("round", 1)) for row in rows]
    lives = [int(row.get("lives", 3)) for row in rows]
    pellets = [int(row.get("remainingPellets", 0)) for row in rows]
    positions = [(int(row.get("playerX", 0)), int(row.get("playerY", 0))) for row in rows]

    current_stall = 0
    max_stall = 0
    current_low_stall = 0
    max_low_stall = 0
    tight_loop_events = 0
    inside_tight_loop = False

    for index in range(1, len(rows)):
        previous = rows[index - 1]
        current = rows[index]
        made_progress = (
            int(current.get("score", 0)) != int(previous.get("score", 0))
            or int(current.get("remainingPellets", 0)) != int(previous.get("remainingPellets", 0))
            or int(current.get("round", 1)) != int(previous.get("round", 1))
            or int(current.get("lives", 3)) != int(previous.get("lives", 3))
        )

        if made_progress:
            current_stall = 0
        else:
            current_stall += 1
            max_stall = max(max_stall, current_stall)

        if int(current.get("remainingPellets", 0)) <= low_pellet_threshold:
            if made_progress:
                current_low_stall = 0
            else:
                current_low_stall += 1
                max_low_stall = max(max_low_stall, current_low_stall)
        else:
            current_low_stall = 0

        if index >= loop_window - 1 and int(current.get("remainingPellets", 0)) <= low_pellet_threshold:
            recent_positions = positions[index - loop_window + 1:index + 1]
            unique_tiles = len(set(recent_positions))
            tight_now = current_low_stall >= (loop_window - 1) and unique_tiles <= loop_unique_tiles
            if tight_now and not inside_tight_loop:
                tight_loop_events += 1
            inside_tight_loop = tight_now
        else:
            inside_tight_loop = False

    round_clears = sum(1 for previous, current in zip(rounds, rounds[1:]) if current > previous)
    deaths = sum(1 for previous, current in zip(lives, lives[1:]) if current < previous)

    return {
        "rows": len(rows),
        "sources": sources,
        "usedFallback": any(bool(row.get("usedFallback", False)) for row in rows),
        "avgConfidence": round(statistics.fmean(confidences), 4) if confidences else None,
        "scoreStart": scores[0],
        "scoreFinal": scores[-1],
        "scoreMax": max(scores),
        "roundStart": rounds[0],
        "roundFinal": rounds[-1],
        "roundMax": max(rounds),
        "roundClears": round_clears,
        "livesStart": lives[0],
        "livesFinal": lives[-1],
        "livesMin": min(lives),
        "deaths": deaths,
        "remainingPelletsStart": pellets[0],
        "remainingPelletsFinal": pellets[-1],
        "remainingPelletsMin": min(pellets),
        "maxNoProgressStall": max_stall,
        "maxLowPelletNoProgressStall": max_low_stall,
        "tightLoopEvents": tight_loop_events,
        "tightLoopLikely": tight_loop_events > 0 or max_low_stall >= (loop_window * 2),
    }


def run_seed(args: argparse.Namespace, seed: int) -> dict:
    dataset_dir = args.dataset_dir.resolve()
    dataset_dir.mkdir(parents=True, exist_ok=True)

    baseline = existing_dataset_files(dataset_dir)
    started_at = time.time()
    command = [str(args.game.resolve())]
    if getattr(args, "headless", False):
        command.extend(["-batchmode", "-nographics", "--ai-headless", "--ai-quit-on-game-over"])
    command.extend(
        [
            "--autoplay",
            f"--autoplay-mode={args.mode}",
            "--ai-record",
            f"--ai-fast-forward={args.fast_forward}",
            f"--ai-seed={seed}",
        ]
    )
    max_rounds = int(getattr(args, "max_rounds", 0) or 0)
    if max_rounds > 0:
        command.append(f"--ai-max-rounds={max_rounds}")
    command.extend(args.extra_arg)

    process = subprocess.Popen(command, cwd=str(args.game.resolve().parent))
    timed_out = False

    try:
        exit_code = process.wait(timeout=args.timeout_seconds)
    except subprocess.TimeoutExpired:
        timed_out = True
        kill_process_tree(process.pid)
        try:
            exit_code = process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            exit_code = -1

    time.sleep(max(args.settle_seconds, 0.0))

    dataset_path = discover_dataset(dataset_dir, baseline, started_at)
    rows = load_rows(dataset_path)
    metrics = analyze_rows(rows, args.low_pellet_threshold, args.loop_window, args.loop_unique_tiles)
    metrics.update(
        {
            "seed": seed,
            "mode": args.mode,
            "timedOut": timed_out,
            "exitCode": exit_code,
            "datasetPath": str(dataset_path),
        }
    )
    return metrics


def aggregate(runs: list[dict]) -> dict:
    if not runs:
        return {"runCount": 0}

    def mean_of(key: str) -> float | None:
        values = [float(run[key]) for run in runs if run.get(key) is not None]
        if not values:
            return None
        return round(statistics.fmean(values), 4)

    cleared_runs = sum(1 for run in runs if int(run.get("roundClears", 0)) > 0 or int(run.get("remainingPelletsMin", 1)) == 0)
    loop_runs = sum(1 for run in runs if run.get("tightLoopLikely"))
    timeout_runs = sum(1 for run in runs if run.get("timedOut"))
    fallback_runs = sum(1 for run in runs if run.get("usedFallback"))

    return {
        "runCount": len(runs),
        "clearedRuns": cleared_runs,
        "clearRate": round(cleared_runs / len(runs), 4),
        "loopLikelyRuns": loop_runs,
        "loopLikelyRate": round(loop_runs / len(runs), 4),
        "timeoutRuns": timeout_runs,
        "timeoutRate": round(timeout_runs / len(runs), 4),
        "fallbackRuns": fallback_runs,
        "fallbackRate": round(fallback_runs / len(runs), 4),
        "avgFinalScore": mean_of("scoreFinal"),
        "avgMaxRound": mean_of("roundMax"),
        "avgMinPellets": mean_of("remainingPelletsMin"),
        "avgFinalPellets": mean_of("remainingPelletsFinal"),
        "avgDeaths": mean_of("deaths"),
        "avgConfidence": mean_of("avgConfidence"),
        "avgMaxNoProgressStall": mean_of("maxNoProgressStall"),
        "avgMaxLowPelletNoProgressStall": mean_of("maxLowPelletNoProgressStall"),
    }


def print_report(report: dict) -> None:
    print(f"Benchmark mode: {report['mode']}")
    print(f"Seeds: {' '.join(str(seed) for seed in report['seeds'])}")
    print(f"Timeout per run: {report['timeoutSeconds']}s")
    print(f"Headless: {report['headless']}")
    if report.get("maxRounds", 0):
        print(f"Max rounds: {report['maxRounds']}")
    print()

    for run in report["runs"]:
        print(
            "seed={seed} score={scoreFinal} maxRound={roundMax} minPellets={remainingPelletsMin} "
            "deaths={deaths} clears={roundClears} fallback={usedFallback} loopLikely={tightLoopLikely} "
            "timeout={timedOut} dataset={dataset}".format(
                seed=run["seed"],
                scoreFinal=run["scoreFinal"],
                roundMax=run["roundMax"],
                remainingPelletsMin=run["remainingPelletsMin"],
                deaths=run["deaths"],
                roundClears=run["roundClears"],
                usedFallback=run["usedFallback"],
                tightLoopLikely=run["tightLoopLikely"],
                timedOut=run["timedOut"],
                dataset=Path(run["datasetPath"]).name,
            )
        )

    aggregate_metrics = report["aggregate"]
    print()
    print("Aggregate:")
    for key in (
        "runCount",
        "clearRate",
        "loopLikelyRate",
        "timeoutRate",
        "fallbackRate",
        "avgFinalScore",
        "avgMaxRound",
        "avgMinPellets",
        "avgFinalPellets",
        "avgDeaths",
        "avgConfidence",
        "avgMaxNoProgressStall",
        "avgMaxLowPelletNoProgressStall",
    ):
        print(f"  {key}: {aggregate_metrics.get(key)}")


def main() -> int:
    args = parse_args()
    if not args.game.exists():
        raise FileNotFoundError(f"Game executable not found: {args.game}")

    runs = [run_seed(args, seed) for seed in args.seeds]
    report = {
        "mode": args.mode,
        "game": str(args.game.resolve()),
        "datasetDir": str(args.dataset_dir.resolve()),
        "seeds": args.seeds,
        "timeoutSeconds": args.timeout_seconds,
        "fastForward": args.fast_forward,
        "headless": args.headless,
        "maxRounds": args.max_rounds,
        "runs": runs,
        "aggregate": aggregate(runs),
    }

    print_report(report)

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print()
        print(f"Wrote {args.output}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
