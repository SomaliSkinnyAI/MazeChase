from __future__ import annotations

import argparse
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace

from generate_dataset_campaign import run_seed as record_teacher_seed
from run_benchmark import (
    DEFAULT_DATASET_DIR,
    DEFAULT_GAME_PATH,
    aggregate as aggregate_benchmark,
    run_seed as benchmark_seed,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Mine hard seeds from the current neural policy, then record "
            "teacher trajectories on those exact seeds to build a targeted "
            "curriculum dataset."
        )
    )
    parser.add_argument("--game", type=Path, default=DEFAULT_GAME_PATH, help="Path to the built MazeChase player executable.")
    parser.add_argument("--dataset-dir", type=Path, default=DEFAULT_DATASET_DIR, help="Directory where runtime dataset JSONL files are written.")
    parser.add_argument("--student-mode", default="NeuralPolicy", help="Autoplay mode used to mine hard seeds.")
    parser.add_argument("--teacher-mode", default="ResearchPlanner", help="Autoplay mode used to record the teacher dataset.")
    parser.add_argument("--candidate-seeds", type=int, nargs="+", required=True, help="Seed pool used to mine hard cases from the student.")
    parser.add_argument("--hard-seed-count", type=int, default=8, help="How many hard seeds to replay with the teacher.")
    parser.add_argument("--student-fast-forward", type=float, default=10.0, help="Time scale used while mining hard seeds.")
    parser.add_argument("--teacher-fast-forward", type=float, default=12.0, help="Time scale used while recording the teacher curriculum.")
    parser.add_argument("--timeout-seconds", type=float, default=40.0, help="Hard timeout per run before the player process is terminated.")
    parser.add_argument("--settle-seconds", type=float, default=1.0, help="Delay after each run so the dataset file is flushed to disk.")
    parser.add_argument("--headless", action=argparse.BooleanOptionalAction, default=True, help="Run the player with Unity batchmode/nographics and the in-game headless profile.")
    parser.add_argument("--max-rounds", type=int, default=0, help="Optional round cap passed through to the Unity runtime. Zero means unlimited.")
    parser.add_argument("--target-round", type=int, default=2, help="Round target used when ranking hard seeds. Higher values favor seeds that fail to reach deeper rounds.")
    parser.add_argument("--low-pellet-threshold", type=int, default=24, help="Pellet count considered endgame for summary metrics.")
    parser.add_argument("--loop-window", type=int, default=24, help="Decision window used in the loop detector.")
    parser.add_argument("--loop-unique-tiles", type=int, default=8, help="Maximum unique tiles inside the loop window for a tight loop.")
    parser.add_argument("--select-max-min-pellets", type=int, help="Optional filter: only replay student runs whose minimum remaining pellet count is at or below this value.")
    parser.add_argument("--select-min-deaths", type=int, default=0, help="Optional filter: only replay student runs with at least this many deaths.")
    parser.add_argument("--select-max-round", type=int, help="Optional filter: only replay student runs whose max round is at or below this value.")
    parser.add_argument("--select-max-round-clears", type=int, help="Optional filter: only replay student runs whose round-clear count is at or below this value.")
    parser.add_argument("--select-require-fallback", action="store_true", help="Optional filter: only replay student runs that used planner fallback/rescue at least once.")
    parser.add_argument("--select-loop-likely-only", action="store_true", help="Optional filter: only replay student runs flagged as likely tight loops.")
    parser.add_argument("--baseline-jsonl", nargs="*", default=[], help="Optional broad planner dataset(s) to append ahead of the targeted teacher data.")
    parser.add_argument("--output-merged", type=Path, required=True, help="Merged curriculum JSONL output path.")
    parser.add_argument("--output-teacher-only", type=Path, help="Optional JSONL output containing only the teacher replay rows for the selected hard seeds.")
    parser.add_argument("--output-report", type=Path, help="Optional JSON report path for the curriculum summary.")
    parser.add_argument("--status-json", type=Path, help="Optional live JSON status file updated while the curriculum campaign runs.")
    parser.add_argument("--status-markdown", type=Path, help="Optional live Markdown status file updated while the curriculum campaign runs.")
    parser.add_argument("--extra-arg", action="append", default=[], help="Additional raw argument to pass through to the player.")
    return parser.parse_args()


def build_runner_args(
    args: argparse.Namespace,
    mode: str,
    fast_forward: float,
) -> SimpleNamespace:
    return SimpleNamespace(
        game=args.game,
        dataset_dir=args.dataset_dir,
        mode=mode,
        fast_forward=fast_forward,
        timeout_seconds=args.timeout_seconds,
        settle_seconds=args.settle_seconds,
        headless=args.headless,
        max_rounds=args.max_rounds,
        low_pellet_threshold=args.low_pellet_threshold,
        loop_window=args.loop_window,
        loop_unique_tiles=args.loop_unique_tiles,
        extra_arg=list(args.extra_arg),
    )


def difficulty_score(run: dict, target_round: int) -> float:
    round_max = int(run.get("roundMax", 1))
    round_clears = int(run.get("roundClears", 0))
    remaining_min = int(run.get("remainingPelletsMin", 0))
    remaining_final = int(run.get("remainingPelletsFinal", remaining_min))
    deaths = int(run.get("deaths", 0))
    low_pellet_stall = int(run.get("maxLowPelletNoProgressStall", 0))
    total_stall = int(run.get("maxNoProgressStall", 0))

    score = 0.0
    required_clears = max(0, target_round - 1)
    score += max(0, target_round - round_max) * 175.0
    score += max(0, required_clears - round_clears) * 120.0
    score += deaths * 65.0
    score += remaining_min * 0.9
    if round_clears < required_clears:
        score += remaining_final * 0.6
    score += low_pellet_stall * 0.45
    score += total_stall * 0.08
    if run.get("tightLoopLikely"):
        score += 40.0
    if run.get("usedFallback"):
        score += 30.0
    if run.get("timedOut"):
        score += 12.0
    return round(score, 3)


def count_jsonl_rows(path: Path) -> int:
    rows = 0
    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                rows += 1
    return rows


def merge_jsonl(output_path: Path, baseline_paths: list[Path], teacher_runs: list[dict]) -> tuple[int, int, int]:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    baseline_rows = 0
    teacher_rows = 0

    with output_path.open("w", encoding="utf-8", newline="\n") as destination:
        for baseline_path in baseline_paths:
            with baseline_path.open("r", encoding="utf-8") as handle:
                for line in handle:
                    if not line.strip():
                        continue
                    destination.write(line.rstrip("\n"))
                    destination.write("\n")
                    baseline_rows += 1

        for run in teacher_runs:
            dataset_path = Path(run["datasetPath"])
            with dataset_path.open("r", encoding="utf-8") as handle:
                for line in handle:
                    if not line.strip():
                        continue
                    destination.write(line.rstrip("\n"))
                    destination.write("\n")
                    teacher_rows += 1

    return baseline_rows, teacher_rows, baseline_rows + teacher_rows


def merge_teacher_only(output_path: Path, teacher_runs: list[dict]) -> int:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    teacher_rows = 0
    with output_path.open("w", encoding="utf-8", newline="\n") as destination:
        for run in teacher_runs:
            dataset_path = Path(run["datasetPath"])
            with dataset_path.open("r", encoding="utf-8") as handle:
                for line in handle:
                    if not line.strip():
                        continue
                    destination.write(line.rstrip("\n"))
                    destination.write("\n")
                    teacher_rows += 1
    return teacher_rows


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


def selection_criteria_summary(args: argparse.Namespace) -> dict:
    return {
        "maxMinPellets": int(args.select_max_min_pellets) if args.select_max_min_pellets is not None else None,
        "minDeaths": int(args.select_min_deaths),
        "maxRound": int(args.select_max_round) if args.select_max_round is not None else None,
        "maxRoundClears": int(args.select_max_round_clears) if args.select_max_round_clears is not None else None,
        "requireFallback": bool(args.select_require_fallback),
        "loopLikelyOnly": bool(args.select_loop_likely_only),
    }


def matches_selection_filters(args: argparse.Namespace, run: dict) -> bool:
    if args.select_max_min_pellets is not None and int(run.get("remainingPelletsMin", 999999)) > args.select_max_min_pellets:
        return False
    if int(run.get("deaths", 0)) < int(args.select_min_deaths):
        return False
    if args.select_max_round is not None and int(run.get("roundMax", 999999)) > args.select_max_round:
        return False
    if args.select_max_round_clears is not None and int(run.get("roundClears", 999999)) > args.select_max_round_clears:
        return False
    if args.select_require_fallback and not bool(run.get("usedFallback", False)):
        return False
    if args.select_loop_likely_only and not bool(run.get("tightLoopLikely", False)):
        return False
    return True


def build_status_payload(
    *,
    args: argparse.Namespace,
    phase: str,
    started_at: float,
    student_runs: list[dict],
    teacher_runs: list[dict],
    current_seed: int | None = None,
    current_index: int | None = None,
    current_stage: str | None = None,
    selected_seeds: list[int] | None = None,
    merged_rows: int | None = None,
    filtered_match_count: int | None = None,
) -> dict:
    elapsed_seconds = max(0.0, time.time() - started_at)

    if phase in {"mining-student", "ranking"}:
        completed = len(student_runs)
        total = len(args.candidate_seeds)
    elif phase in {"replaying-teacher", "merging"}:
        completed = len(teacher_runs)
        total = len(selected_seeds or [])
    else:
        completed = len(teacher_runs) if teacher_runs else len(student_runs)
        total = len(selected_seeds or args.candidate_seeds)

    avg_seconds_per_item = (elapsed_seconds / completed) if completed > 0 else None
    remaining = max(0, total - completed)
    eta_seconds = (avg_seconds_per_item * remaining) if avg_seconds_per_item is not None else None

    return {
        "phase": phase,
        "currentStage": current_stage,
        "studentMode": args.student_mode,
        "teacherMode": args.teacher_mode,
        "headless": bool(args.headless),
        "maxRounds": int(args.max_rounds),
        "targetRound": int(args.target_round),
        "startedAtUtc": datetime.fromtimestamp(started_at, timezone.utc).isoformat(),
        "updatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "elapsedSeconds": round(elapsed_seconds, 3),
        "elapsed": format_duration(elapsed_seconds),
        "avgSecondsPerItem": round(avg_seconds_per_item, 3) if avg_seconds_per_item is not None else None,
        "avgPerItem": format_duration(avg_seconds_per_item),
        "etaSeconds": round(eta_seconds, 3) if eta_seconds is not None else None,
        "eta": format_duration(eta_seconds),
        "completed": completed,
        "total": total,
        "progressPercent": round((completed / total) * 100.0, 2) if total > 0 else 0.0,
        "currentSeed": current_seed,
        "currentIndex": current_index,
        "candidateSeeds": [int(seed) for seed in args.candidate_seeds],
        "selectedSeeds": [int(seed) for seed in (selected_seeds or [])],
        "selectionCriteria": selection_criteria_summary(args),
        "filteredMatchCount": filtered_match_count,
        "studentRuns": student_runs,
        "teacherRuns": teacher_runs,
        "mergedRows": merged_rows,
        "mergedOutput": str(args.output_merged.resolve()),
    }


def render_status_markdown(status: dict) -> str:
    lines: list[str] = []
    lines.append("# Curriculum Campaign Status")
    lines.append("")
    lines.append(f"- Phase: `{status['phase']}`")
    lines.append(f"- Stage: `{status['currentStage']}`")
    lines.append(f"- Student mode: `{status['studentMode']}`")
    lines.append(f"- Teacher mode: `{status['teacherMode']}`")
    lines.append(f"- Target round: `{status['targetRound']}`")
    lines.append(f"- Headless: `{status['headless']}`")
    lines.append(f"- Max rounds: `{status['maxRounds']}`")
    lines.append(f"- Progress: `{status['completed']}/{status['total']}` (`{status['progressPercent']}%`)")
    lines.append(f"- Current seed: `{status['currentSeed']}`")
    lines.append(f"- Elapsed: `{status['elapsed']}`")
    lines.append(f"- Avg per item: `{status['avgPerItem']}`")
    lines.append(f"- ETA: `{status['eta']}`")
    criteria = status.get("selectionCriteria") or {}
    if any(value is not None and value != 0 for value in criteria.values()):
        lines.append("- Selection filters:")
        if criteria.get("maxMinPellets") is not None:
            lines.append(f"  - `remainingPelletsMin <= {criteria['maxMinPellets']}`")
        if criteria.get("minDeaths"):
            lines.append(f"  - `deaths >= {criteria['minDeaths']}`")
        if criteria.get("maxRound") is not None:
            lines.append(f"  - `roundMax <= {criteria['maxRound']}`")
        if criteria.get("maxRoundClears") is not None:
            lines.append(f"  - `roundClears <= {criteria['maxRoundClears']}`")
        if criteria.get("requireFallback"):
            lines.append("  - `usedFallback == true`")
        if criteria.get("loopLikelyOnly"):
            lines.append("  - `tightLoopLikely == true`")
    if status.get("filteredMatchCount") is not None:
        lines.append(f"- Filter matches: `{status['filteredMatchCount']}`")
    if status.get("mergedRows") is not None:
        lines.append(f"- Merged rows: `{status['mergedRows']}`")
    lines.append(f"- Output target: `{status['mergedOutput']}`")
    lines.append(f"- Updated (UTC): `{status['updatedAtUtc']}`")
    lines.append("")

    if status["selectedSeeds"]:
        lines.append("## Selected Hard Seeds")
        lines.append("")
        lines.append(", ".join(str(seed) for seed in status["selectedSeeds"]))
        lines.append("")

    if status["studentRuns"]:
        lines.append("## Student Runs")
        lines.append("")
        lines.append("| Seed | Difficulty | Score | Max Round | Min Pellets | Deaths | Timeout | Loop |")
        lines.append("| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |")
        for run in status["studentRuns"]:
            lines.append(
                f"| {run['seed']} | {run.get('difficultyScore', 'n/a')} | {run['scoreFinal']} | {run['roundMax']} | "
                f"{run['remainingPelletsMin']} | {run['deaths']} | {run['timedOut']} | {run['tightLoopLikely']} |"
            )
        lines.append("")

    if status["teacherRuns"]:
        lines.append("## Teacher Replays")
        lines.append("")
        lines.append("| Seed | Rows | Max Round | Score | Min Pellets | Timeout |")
        lines.append("| --- | ---: | ---: | ---: | ---: | --- |")
        for run in status["teacherRuns"]:
            lines.append(
                f"| {run['seed']} | {run['rows']} | {run['roundMax']} | {run['scoreFinal']} | "
                f"{run['remainingPelletsMin']} | {run['timedOut']} |"
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


def main() -> int:
    args = parse_args()

    if not args.game.exists():
        raise FileNotFoundError(f"Game executable not found: {args.game}")

    baseline_paths = [Path(path).resolve() for path in args.baseline_jsonl]
    for baseline_path in baseline_paths:
        if not baseline_path.exists():
            raise FileNotFoundError(f"Baseline dataset not found: {baseline_path}")

    started_at = time.time()
    student_args = build_runner_args(args, args.student_mode, args.student_fast_forward)
    student_runs: list[dict] = []
    teacher_runs: list[dict] = []

    write_status_files(
        args,
        build_status_payload(
            args=args,
            phase="starting",
            started_at=started_at,
            student_runs=student_runs,
            teacher_runs=teacher_runs,
            current_seed=args.candidate_seeds[0] if args.candidate_seeds else None,
            current_index=1 if args.candidate_seeds else None,
            current_stage="Preparing student mining pass",
            filtered_match_count=0,
        ),
    )

    for index, seed in enumerate(args.candidate_seeds, start=1):
        write_status_files(
            args,
            build_status_payload(
                args=args,
                phase="mining-student",
                started_at=started_at,
                student_runs=student_runs,
                teacher_runs=teacher_runs,
                current_seed=seed,
                current_index=index,
                current_stage="Benchmarking student model on candidate seeds",
                filtered_match_count=sum(1 for run in student_runs if matches_selection_filters(args, run)),
            ),
        )
        print(f"[student {index}/{len(args.candidate_seeds)}] Benchmarking seed {seed}...")
        run = benchmark_seed(student_args, seed)
        run["difficultyScore"] = difficulty_score(run, args.target_round)
        student_runs.append(run)
        print(
            f"[student {index}/{len(args.candidate_seeds)}] seed={seed} difficulty={run['difficultyScore']} "
            f"score={run['scoreFinal']} maxRound={run['roundMax']} minPellets={run['remainingPelletsMin']} "
            f"timeout={run['timedOut']}"
        )

    write_status_files(
        args,
        build_status_payload(
            args=args,
            phase="ranking",
            started_at=started_at,
            student_runs=student_runs,
            teacher_runs=teacher_runs,
            current_stage="Ranking hardest student seeds",
            filtered_match_count=sum(1 for run in student_runs if matches_selection_filters(args, run)),
        ),
    )

    ranked_runs = sorted(student_runs, key=lambda run: (-run["difficultyScore"], int(run["seed"])))
    filtered_ranked_runs = [run for run in ranked_runs if matches_selection_filters(args, run)]
    selected_runs = filtered_ranked_runs[:max(1, args.hard_seed_count)]
    if not selected_runs:
        raise RuntimeError(
            "No student runs matched the requested selection filters. "
            "Widen the candidate seed pool or relax the filter thresholds."
        )
    selected_seeds = [int(run["seed"]) for run in selected_runs]

    teacher_args = build_runner_args(args, args.teacher_mode, args.teacher_fast_forward)
    for index, seed in enumerate(selected_seeds, start=1):
        write_status_files(
            args,
            build_status_payload(
                args=args,
                phase="replaying-teacher",
                started_at=started_at,
                student_runs=ranked_runs,
                teacher_runs=teacher_runs,
                current_seed=seed,
                current_index=index,
                current_stage="Recording planner teacher replays on the selected hard seeds",
                selected_seeds=selected_seeds,
                filtered_match_count=len(filtered_ranked_runs),
            ),
        )
        print(f"[teacher {index}/{len(selected_seeds)}] Replaying seed {seed}...")
        run = record_teacher_seed(teacher_args, seed)
        teacher_runs.append(run)
        print(
            f"[teacher {index}/{len(selected_seeds)}] seed={seed} rows={run['rows']} "
            f"score={run['scoreFinal']} maxRound={run['roundMax']} minPellets={run['remainingPelletsMin']} "
            f"timeout={run['timedOut']}"
        )

    baseline_rows, teacher_rows, merged_rows = merge_jsonl(args.output_merged.resolve(), baseline_paths, teacher_runs)
    curriculum_report = {
        "studentMode": args.student_mode,
        "teacherMode": args.teacher_mode,
        "targetRound": args.target_round,
        "candidateSeeds": args.candidate_seeds,
        "hardSeedCount": len(selected_seeds),
        "selectedSeeds": selected_seeds,
        "selectionCriteria": selection_criteria_summary(args),
        "filteredMatchCount": len(filtered_ranked_runs),
        "baselineDatasets": [str(path) for path in baseline_paths],
        "baselineRows": baseline_rows,
        "teacherRows": teacher_rows,
        "mergedRows": merged_rows,
        "mergedOutput": str(args.output_merged.resolve()),
        "studentBenchmark": {
            "runs": ranked_runs,
            "aggregate": aggregate_benchmark(student_runs),
        },
        "teacherCampaign": {
            "runs": teacher_runs,
            "aggregate": aggregate_benchmark(teacher_runs),
        },
    }

    write_status_files(
        args,
        build_status_payload(
            args=args,
            phase="completed",
            started_at=started_at,
            student_runs=ranked_runs,
            teacher_runs=teacher_runs,
            selected_seeds=selected_seeds,
            merged_rows=merged_rows,
            current_stage="Curriculum campaign complete",
            filtered_match_count=len(filtered_ranked_runs),
        ),
    )

    print(f"Curriculum student mode: {args.student_mode}")
    print(f"Candidate seeds: {' '.join(str(seed) for seed in args.candidate_seeds)}")
    print(f"Target round: {args.target_round}")
    print(f"Filtered matches: {len(filtered_ranked_runs)}")
    print()
    print("Selected hard seeds:")
    for run in selected_runs:
        print(
            "seed={seed} difficulty={difficultyScore} score={scoreFinal} roundMax={roundMax} "
            "minPellets={remainingPelletsMin} finalPellets={remainingPelletsFinal} deaths={deaths} "
            "fallback={usedFallback} loopLikely={tightLoopLikely}".format(**run)
        )

    print()
    print(f"Teacher replay seeds: {' '.join(str(seed) for seed in selected_seeds)}")
    print(f"Baseline rows: {baseline_rows}")
    print(f"Teacher rows: {teacher_rows}")
    print(f"Merged rows: {merged_rows}")
    print(f"Wrote merged curriculum dataset -> {args.output_merged}")

    if args.output_teacher_only:
        teacher_only_rows = merge_teacher_only(args.output_teacher_only.resolve(), teacher_runs)
        print(f"Wrote teacher-only curriculum dataset -> {args.output_teacher_only} ({teacher_only_rows} rows)")

    if args.output_report:
        args.output_report.parent.mkdir(parents=True, exist_ok=True)
        args.output_report.write_text(json.dumps(curriculum_report, indent=2), encoding="utf-8")
        print(f"Wrote curriculum report -> {args.output_report}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
