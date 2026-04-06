from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from run_benchmark import (
    DEFAULT_DATASET_DIR,
    DEFAULT_GAME_PATH,
    analyze_rows,
    discover_dataset,
    existing_dataset_files,
    kill_process_tree,
    load_rows,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run a multi-seed planner dataset collection campaign and merge the resulting JSONL files.")
    parser.add_argument("--game", type=Path, default=DEFAULT_GAME_PATH, help="Path to the built MazeChase player executable.")
    parser.add_argument("--dataset-dir", type=Path, default=DEFAULT_DATASET_DIR, help="Directory where runtime dataset JSONL files are written.")
    parser.add_argument("--mode", default="ResearchPlanner", help="Autoplay mode to record from. Defaults to the planner teacher.")
    parser.add_argument("--seeds", type=int, nargs="+", required=True, help="Deterministic seeds to run.")
    parser.add_argument("--fast-forward", type=float, default=12.0, help="Unity time scale for the recording runs.")
    parser.add_argument("--timeout-seconds", type=float, default=45.0, help="Hard timeout per run before the player process is terminated.")
    parser.add_argument("--settle-seconds", type=float, default=1.0, help="Delay after each run so the dataset file is flushed to disk.")
    parser.add_argument("--headless", action=argparse.BooleanOptionalAction, default=True, help="Run the player with Unity batchmode/nographics and the in-game headless profile.")
    parser.add_argument("--max-rounds", type=int, default=0, help="Optional round cap passed through to the Unity runtime. Zero means unlimited.")
    parser.add_argument("--low-pellet-threshold", type=int, default=24, help="Pellet count considered endgame for summary metrics.")
    parser.add_argument("--loop-window", type=int, default=24, help="Decision window used in the summary loop detector.")
    parser.add_argument("--loop-unique-tiles", type=int, default=8, help="Maximum unique tiles inside the loop window for a tight loop.")
    parser.add_argument("--output-merged", type=Path, required=True, help="Merged JSONL output path.")
    parser.add_argument("--output-report", type=Path, help="Optional JSON report path for the campaign summary.")
    parser.add_argument("--status-json", type=Path, help="Optional live JSON status file updated before and after each seed.")
    parser.add_argument("--status-markdown", type=Path, help="Optional live Markdown status file updated before and after each seed.")
    parser.add_argument("--extra-arg", action="append", default=[], help="Additional raw argument to pass through to the player.")
    return parser.parse_args()


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
            "headless": bool(getattr(args, "headless", False)),
            "maxRounds": int(getattr(args, "max_rounds", 0) or 0),
            "timedOut": timed_out,
            "exitCode": exit_code,
            "datasetPath": str(dataset_path),
        }
    )
    return metrics


def merge_dataset_files(run_metrics: list[dict], output_path: Path) -> int:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    total_lines = 0
    with output_path.open("w", encoding="utf-8", newline="\n") as destination:
        for run in run_metrics:
            source_path = Path(run["datasetPath"])
            with source_path.open("r", encoding="utf-8") as source_handle:
                for line in source_handle:
                    if not line.strip():
                        continue
                    destination.write(line.rstrip("\n"))
                    destination.write("\n")
                    total_lines += 1
    return total_lines


def format_duration(seconds: float | None) -> str:
    if seconds is None or seconds < 0:
        return "n/a"

    total_seconds = int(round(seconds))
    minutes, secs = divmod(total_seconds, 60)
    hours, minutes = divmod(minutes, 60)
    if hours > 0:
        return f"{hours}h {minutes:02d}m {secs:02d}s"
    if minutes > 0:
        return f"{minutes}m {secs:02d}s"
    return f"{secs}s"


def build_status_payload(
    *,
    args: argparse.Namespace,
    phase: str,
    started_at: float,
    run_metrics: list[dict],
    current_seed: int | None = None,
    current_index: int | None = None,
    merged_rows: int | None = None,
    merged_output: Path | None = None,
) -> dict:
    completed = len(run_metrics)
    total = len(args.seeds)
    elapsed_seconds = max(0.0, time.time() - started_at)
    avg_seconds_per_seed = (elapsed_seconds / completed) if completed > 0 else None
    remaining = max(0, total - completed)
    eta_seconds = (avg_seconds_per_seed * remaining) if avg_seconds_per_seed is not None else None
    rows_completed = sum(int(run.get("rows", 0)) for run in run_metrics)
    latest_run = run_metrics[-1] if run_metrics else None

    return {
        "phase": phase,
        "mode": args.mode,
        "headless": bool(args.headless),
        "maxRounds": int(args.max_rounds),
        "startedAtUtc": datetime.fromtimestamp(started_at, timezone.utc).isoformat(),
        "updatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "elapsedSeconds": round(elapsed_seconds, 3),
        "elapsed": format_duration(elapsed_seconds),
        "avgSecondsPerSeed": round(avg_seconds_per_seed, 3) if avg_seconds_per_seed is not None else None,
        "avgPerSeed": format_duration(avg_seconds_per_seed),
        "etaSeconds": round(eta_seconds, 3) if eta_seconds is not None else None,
        "eta": format_duration(eta_seconds),
        "completedSeeds": completed,
        "totalSeeds": total,
        "progressPercent": round((completed / total) * 100.0, 2) if total > 0 else 0.0,
        "rowsCompleted": rows_completed,
        "currentSeed": current_seed,
        "currentSeedIndex": current_index,
        "seeds": [int(seed) for seed in args.seeds],
        "mergedRows": merged_rows,
        "mergedOutput": str(merged_output.resolve()) if merged_output is not None else str(args.output_merged.resolve()),
        "latestRun": latest_run,
        "runs": run_metrics,
    }


def render_status_markdown(status: dict) -> str:
    lines: list[str] = []
    lines.append("# Headless Teacher Campaign Status")
    lines.append("")
    lines.append(f"- Phase: `{status['phase']}`")
    lines.append(f"- Mode: `{status['mode']}`")
    lines.append(f"- Headless: `{status['headless']}`")
    lines.append(f"- Max rounds: `{status['maxRounds']}`")
    lines.append(f"- Progress: `{status['completedSeeds']}/{status['totalSeeds']}` (`{status['progressPercent']}%`)")
    lines.append(f"- Current seed: `{status['currentSeed']}`")
    lines.append(f"- Elapsed: `{status['elapsed']}`")
    lines.append(f"- Avg per seed: `{status['avgPerSeed']}`")
    lines.append(f"- ETA: `{status['eta']}`")
    lines.append(f"- Rows completed so far: `{status['rowsCompleted']}`")
    if status.get("mergedRows") is not None:
        lines.append(f"- Merged rows: `{status['mergedRows']}`")
    lines.append(f"- Updated (UTC): `{status['updatedAtUtc']}`")
    lines.append(f"- Output target: `{status['mergedOutput']}`")
    lines.append("")

    latest = status.get("latestRun")
    if latest:
        lines.append("## Latest Seed")
        lines.append("")
        lines.append(f"- Seed: `{latest['seed']}`")
        lines.append(f"- Rows: `{latest['rows']}`")
        lines.append(f"- Max round: `{latest['roundMax']}`")
        lines.append(f"- Final score: `{latest['scoreFinal']}`")
        lines.append(f"- Min pellets: `{latest['remainingPelletsMin']}`")
        lines.append(f"- Deaths: `{latest['deaths']}`")
        lines.append(f"- Timed out: `{latest['timedOut']}`")
        lines.append(f"- Loop likely: `{latest['tightLoopLikely']}`")
        lines.append(f"- Dataset file: `{Path(latest['datasetPath']).name}`")
        lines.append("")

    if status["runs"]:
        lines.append("## Completed Seeds")
        lines.append("")
        lines.append("| Seed | Rows | Max Round | Final Score | Min Pellets | Deaths | Timeout | Loop |")
        lines.append("| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |")
        for run in status["runs"]:
            lines.append(
                f"| {run['seed']} | {run['rows']} | {run['roundMax']} | {run['scoreFinal']} | "
                f"{run['remainingPelletsMin']} | {run['deaths']} | {run['timedOut']} | {run['tightLoopLikely']} |"
            )
        lines.append("")

    return "\n".join(lines) + "\n"


def write_status_files(args: argparse.Namespace, status: dict) -> None:
    if args.status_json:
        args.status_json.parent.mkdir(parents=True, exist_ok=True)
        args.status_json.write_text(json.dumps(status, indent=2), encoding="utf-8")

    if args.status_markdown:
        args.status_markdown.parent.mkdir(parents=True, exist_ok=True)
        args.status_markdown.write_text(render_status_markdown(status), encoding="utf-8")


def build_campaign_summary(run_metrics: list[dict], merged_output: Path, merged_rows: int) -> dict:
    total_timeouts = sum(1 for run in run_metrics if run.get("timedOut"))
    total_loop_flags = sum(1 for run in run_metrics if run.get("tightLoopLikely"))
    total_fallbacks = sum(1 for run in run_metrics if run.get("usedFallback"))
    max_round = max((int(run.get("roundMax", 1)) for run in run_metrics), default=1)
    min_pellets = min((int(run.get("remainingPelletsMin", 9999)) for run in run_metrics), default=9999)
    total_rows = sum(int(run.get("rows", 0)) for run in run_metrics)

    return {
        "mode": run_metrics[0]["mode"] if run_metrics else "ResearchPlanner",
        "headless": bool(run_metrics and run_metrics[0].get("headless", False)),
        "maxRounds": int(run_metrics[0].get("maxRounds", 0)) if run_metrics else 0,
        "seedCount": len(run_metrics),
        "seeds": [int(run["seed"]) for run in run_metrics],
        "mergedOutput": str(merged_output),
        "mergedRows": merged_rows,
        "sourceRows": total_rows,
        "timeoutRuns": total_timeouts,
        "loopFlagRuns": total_loop_flags,
        "fallbackRuns": total_fallbacks,
        "maxRoundReached": max_round,
        "lowestPelletCount": min_pellets,
        "runs": run_metrics,
    }


def print_summary(summary: dict) -> None:
    print(f"Dataset campaign mode: {summary['mode']}")
    print(f"Seeds: {' '.join(str(seed) for seed in summary['seeds'])}")
    print(f"Headless: {summary['headless']}")
    if summary.get("maxRounds", 0):
        print(f"Max rounds: {summary['maxRounds']}")
    print(f"Merged rows: {summary['mergedRows']}")
    print(f"Max round reached: {summary['maxRoundReached']}")
    print(f"Lowest pellet count: {summary['lowestPelletCount']}")
    print()

    for run in summary["runs"]:
        print(
            "seed={seed} rows={rows} maxRound={roundMax} minPellets={remainingPelletsMin} "
            "timeout={timedOut} loopLikely={tightLoopLikely} fallback={usedFallback} dataset={dataset}".format(
                seed=run["seed"],
                rows=run["rows"],
                roundMax=run["roundMax"],
                remainingPelletsMin=run["remainingPelletsMin"],
                timedOut=run["timedOut"],
                tightLoopLikely=run["tightLoopLikely"],
                usedFallback=run["usedFallback"],
                dataset=Path(run["datasetPath"]).name,
            )
        )


def main() -> int:
    args = parse_args()

    if not args.game.exists():
        raise FileNotFoundError(f"Game executable not found: {args.game}")

    started_at = time.time()
    run_metrics: list[dict] = []

    initial_status = build_status_payload(
        args=args,
        phase="starting",
        started_at=started_at,
        run_metrics=run_metrics,
        current_seed=args.seeds[0] if args.seeds else None,
        current_index=1 if args.seeds else None,
    )
    write_status_files(args, initial_status)

    for index, seed in enumerate(args.seeds, start=1):
        pre_run_status = build_status_payload(
            args=args,
            phase="running",
            started_at=started_at,
            run_metrics=run_metrics,
            current_seed=seed,
            current_index=index,
        )
        write_status_files(args, pre_run_status)
        print(f"[{index}/{len(args.seeds)}] Recording seed {seed}...")
        metrics = run_seed(args, seed)
        run_metrics.append(metrics)
        post_run_status = build_status_payload(
            args=args,
            phase="running",
            started_at=started_at,
            run_metrics=run_metrics,
            current_seed=args.seeds[index] if index < len(args.seeds) else None,
            current_index=(index + 1) if index < len(args.seeds) else None,
        )
        write_status_files(args, post_run_status)
        print(
            f"[{index}/{len(args.seeds)}] seed={seed} rows={metrics['rows']} maxRound={metrics['roundMax']} "
            f"score={metrics['scoreFinal']} minPellets={metrics['remainingPelletsMin']} timeout={metrics['timedOut']}"
        )

    merged_rows = merge_dataset_files(run_metrics, args.output_merged)
    summary = build_campaign_summary(run_metrics, args.output_merged.resolve(), merged_rows)

    final_status = build_status_payload(
        args=args,
        phase="completed",
        started_at=started_at,
        run_metrics=run_metrics,
        merged_rows=merged_rows,
        merged_output=args.output_merged.resolve(),
    )
    write_status_files(args, final_status)

    print_summary(summary)
    print()
    print(f"Wrote merged dataset -> {args.output_merged}")

    if args.output_report:
        args.output_report.parent.mkdir(parents=True, exist_ok=True)
        args.output_report.write_text(json.dumps(summary, indent=2), encoding="utf-8")
        print(f"Wrote campaign report -> {args.output_report}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
