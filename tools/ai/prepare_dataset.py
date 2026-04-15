import argparse
import json
from collections import deque
from pathlib import Path

import numpy as np


DIRECTION_TO_ACTION = {1: 0, 2: 1, 3: 2, 4: 3}
ESCAPE_TRAP_OBJECTIVE = 5
SEEK_ENERGIZER_OBJECTIVE = 2
CHASE_FRIGHTENED_OBJECTIVE = 3
FEATURES_PER_DIRECTION = 18
BASE_GLOBAL_FEATURE_COUNT = 11
ENHANCED_FEATURE_COUNT = 11
LEGACY_BASE_INPUT_SIZE = (FEATURES_PER_DIRECTION * 4) + BASE_GLOBAL_FEATURE_COUNT
TEMPORAL_FEATURE_COUNT = 8
LEGACY_FULL_INPUT_SIZE = LEGACY_BASE_INPUT_SIZE + TEMPORAL_FEATURE_COUNT
FULL_INPUT_SIZE = LEGACY_FULL_INPUT_SIZE + ENHANCED_FEATURE_COUNT
CHOSEN_PELLET_OFFSET = 3
CHOSEN_IMMEDIATE_DANGER_OFFSET = 10
CHOSEN_FUTURE_DANGER_OFFSET = 11
CHOSEN_OPPORTUNITY_OFFSET = 12
RECENT_TILE_WINDOW = 48
LOW_PELLET_THRESHOLD = 24
OPPOSITE_DIRECTIONS = {1: 2, 2: 1, 3: 4, 4: 3}
DEFAULT_FRAME_STACK = 3


def softmax(scores: np.ndarray) -> np.ndarray:
    scores = scores - np.max(scores)
    exps = np.exp(scores)
    denom = np.sum(exps)
    if denom <= 0:
        return np.full_like(scores, 1.0 / len(scores))
    return exps / denom


def iter_jsonl_entries(input_paths: list[str]):
    for input_index, input_arg in enumerate(input_paths):
        input_path = Path(input_arg)
        for line in input_path.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            yield input_index, json.loads(line)


def starts_new_run(previous_entry: dict | None, current_entry: dict) -> bool:
    if previous_entry is None:
        return True

    previous_frame = int(previous_entry.get("frameIndex", 0))
    current_frame = int(current_entry.get("frameIndex", 0))
    previous_lives = int(previous_entry.get("lives", 3))
    current_lives = int(current_entry.get("lives", 3))
    previous_score = int(previous_entry.get("score", 0))
    current_score = int(current_entry.get("score", 0))

    # Respawn transitions keep the same frame/score while lives drop. Treat
    # that as one continuous run so the death-risk target can see across it.
    if current_lives < previous_lives and current_frame == previous_frame and current_score == previous_score:
        return False

    if current_frame <= previous_frame:
        return True

    previous_round = int(previous_entry.get("round", 1))
    current_round = int(current_entry.get("round", 1))
    if current_round < previous_round:
        return True

    if current_lives > previous_lives:
        return True

    if current_score < previous_score and current_lives >= previous_lives:
        return True

    return False


def direction_to_vector(direction: int) -> tuple[float, float]:
    if direction == 1:
        return 0.0, 1.0
    if direction == 2:
        return 0.0, -1.0
    if direction == 3:
        return -1.0, 0.0
    if direction == 4:
        return 1.0, 0.0
    return 0.0, 0.0


class EpisodeFeatureState:
    def __init__(self) -> None:
        self.recent_tiles: deque[tuple[int, int]] = deque()
        self.recent_tile_counts: dict[tuple[int, int], int] = {}
        self.recent_tile_last_seen_step: dict[tuple[int, int], int] = {}
        self.recent_tile_previous_seen_step: dict[tuple[int, int], int] = {}
        self.recent_tile_step = 0
        self.last_direction = 0
        self.same_direction_streak = 0
        self.last_direction_was_reversal = False
        self.last_round = -1
        self.last_lives = -1
        self.last_score = -1
        self.last_remaining_pellets = -1
        self.no_progress_decisions = 0
        self.low_pellet_no_progress_decisions = 0

    def observe(self, entry: dict) -> None:
        tile = (int(entry.get("playerX", 0)), int(entry.get("playerY", 0)))
        self._remember_tile(tile)
        self._update_direction(int(entry.get("playerDirection", 0)))
        self._update_progress(entry)

    def build_temporal_features(self, entry: dict) -> list[float]:
        tile = (int(entry.get("playerX", 0)), int(entry.get("playerY", 0)))
        recent_visit_count = self.recent_tile_counts.get(tile, 0)
        repeat_age = self.get_recent_repeat_age(tile)
        unique_ratio = np.clip(len(self.recent_tile_counts) / float(max(1, RECENT_TILE_WINDOW)), 0.0, 1.0)
        no_progress = np.clip(self.no_progress_decisions / 24.0, 0.0, 1.0)
        low_pellet_no_progress = np.clip(self.low_pellet_no_progress_decisions / 24.0, 0.0, 1.0)
        same_direction = np.clip(self.same_direction_streak / 8.0, 0.0, 1.0)
        revisit_pressure = np.clip(recent_visit_count / 6.0, 0.0, 1.0)
        repeat_age_affinity = self.distance_affinity(repeat_age, RECENT_TILE_WINDOW)
        tight_loop_pressure = revisit_pressure * repeat_age_affinity * np.clip((1.0 - unique_ratio) + low_pellet_no_progress, 0.0, 1.0)

        return [
            revisit_pressure,
            repeat_age_affinity,
            unique_ratio,
            no_progress,
            low_pellet_no_progress,
            same_direction,
            1.0 if self.last_direction_was_reversal else 0.0,
            float(tight_loop_pressure),
        ]

    def get_recent_repeat_age(self, tile: tuple[int, int]) -> int:
        previous_seen_step = self.recent_tile_previous_seen_step.get(tile)
        if previous_seen_step is None:
            return 999
        return self.recent_tile_step - previous_seen_step

    @staticmethod
    def distance_affinity(distance: int, cap: int) -> float:
        if distance >= 999:
            return 0.0
        return float(1.0 - np.clip(distance / float(max(1, cap)), 0.0, 1.0))

    def _remember_tile(self, tile: tuple[int, int]) -> None:
        self.recent_tile_step += 1
        self.recent_tiles.append(tile)

        previous_last_seen = self.recent_tile_last_seen_step.get(tile)
        if previous_last_seen is not None:
            self.recent_tile_previous_seen_step[tile] = previous_last_seen

        self.recent_tile_counts[tile] = self.recent_tile_counts.get(tile, 0) + 1
        self.recent_tile_last_seen_step[tile] = self.recent_tile_step

        while len(self.recent_tiles) > RECENT_TILE_WINDOW:
            expired = self.recent_tiles.popleft()
            expired_count = self.recent_tile_counts.get(expired, 0)
            if expired_count <= 1:
                self.recent_tile_counts.pop(expired, None)
            else:
                self.recent_tile_counts[expired] = expired_count - 1

    def _update_direction(self, direction: int) -> None:
        if direction == 0:
            self.last_direction_was_reversal = False
            self.same_direction_streak = 0
            return

        self.last_direction_was_reversal = (
            self.last_direction in OPPOSITE_DIRECTIONS and
            OPPOSITE_DIRECTIONS[self.last_direction] == direction
        )

        if direction == self.last_direction:
            self.same_direction_streak += 1
        else:
            self.same_direction_streak = 1

        self.last_direction = direction

    def _update_progress(self, entry: dict) -> None:
        score = int(entry.get("score", 0))
        remaining_pellets = int(entry.get("remainingPellets", 0))
        lives = int(entry.get("lives", 3))
        round_index = int(entry.get("round", 1))

        needs_reset = (
            self.last_round < 0 or
            round_index != self.last_round or
            lives != self.last_lives or
            remaining_pellets > self.last_remaining_pellets or
            score < self.last_score
        )

        if needs_reset:
            self.no_progress_decisions = 0
            self.low_pellet_no_progress_decisions = 0
        else:
            made_progress = score != self.last_score or remaining_pellets != self.last_remaining_pellets

            if made_progress:
                self.no_progress_decisions = 0
            else:
                self.no_progress_decisions += 1

            if remaining_pellets <= LOW_PELLET_THRESHOLD:
                if made_progress:
                    self.low_pellet_no_progress_decisions = 0
                else:
                    self.low_pellet_no_progress_decisions += 1
            else:
                self.low_pellet_no_progress_decisions = 0

        self.last_round = round_index
        self.last_lives = lives
        self.last_score = score
        self.last_remaining_pellets = remaining_pellets


def build_enhanced_features(entry: dict) -> list[float]:
    player_dx, player_dy = direction_to_vector(int(entry.get("playerDirection", 0)))
    features = [player_dx, player_dy, float(entry.get("nearestJunctionProximity", 0.0))]

    ghost_directions = entry.get("ghostDirections") or []
    for ghost_index in range(4):
        direction = int(ghost_directions[ghost_index]) if ghost_index < len(ghost_directions) else 0
        ghost_dx, ghost_dy = direction_to_vector(direction)
        features.extend((ghost_dx, ghost_dy))

    return features


def build_augmented_input(raw_input: list[float], entry: dict, state: EpisodeFeatureState) -> np.ndarray:
    raw = np.asarray(raw_input, dtype=np.float32)
    if raw.shape[0] == FULL_INPUT_SIZE:
        return raw

    enhanced = np.asarray(build_enhanced_features(entry), dtype=np.float32)
    if raw.shape[0] == LEGACY_FULL_INPUT_SIZE:
        return np.concatenate((raw[:LEGACY_BASE_INPUT_SIZE], enhanced, raw[LEGACY_BASE_INPUT_SIZE:]), axis=0)
    if raw.shape[0] != LEGACY_BASE_INPUT_SIZE:
        raise SystemExit(
            f"Unexpected input size {raw.shape[0]} in dataset row; "
            f"expected {LEGACY_BASE_INPUT_SIZE}, {LEGACY_FULL_INPUT_SIZE}, or {FULL_INPUT_SIZE}."
        )

    temporal = np.asarray(state.build_temporal_features(entry), dtype=np.float32)
    return np.concatenate((raw, enhanced, temporal), axis=0)


def populate_death_risk_targets(rows: list[dict], episode_row_indices: dict[tuple[int, str], list[int]], horizon: int) -> np.ndarray:
    death_risk = np.zeros((len(rows),), dtype=np.float32)
    if horizon <= 0:
        return death_risk

    for row_indices in episode_row_indices.values():
        for local_index, row_index in enumerate(row_indices):
            current_lives = int(rows[row_index]["entry"].get("lives", 3))
            lookahead_limit = min(len(row_indices), local_index + 1 + horizon)
            for future_local_index in range(local_index + 1, lookahead_limit):
                future_row_index = row_indices[future_local_index]
                future_lives = int(rows[future_row_index]["entry"].get("lives", current_lives))
                if future_lives < current_lives:
                    death_risk[row_index] = 1.0
                    break

    return death_risk


def main() -> None:
    parser = argparse.ArgumentParser(description="Prepare Unity autoplay JSONL into a compact NumPy dataset.")
    parser.add_argument("--input", nargs="+", required=True, help="One or more JSONL datasets recorded by AIDatasetRecorder.")
    parser.add_argument("--output", required=True, help="Path to the output .npz dataset.")
    parser.add_argument("--input-weight", nargs="*", type=float, default=None, help="Optional per-input multipliers matching the order of --input paths.")
    parser.add_argument("--seed", type=int, default=1337)
    parser.add_argument("--train-split", type=float, default=0.9)
    parser.add_argument("--endgame-threshold", type=int, default=24, help="Pellet count below which samples receive extra training weight.")
    parser.add_argument("--endgame-weight-scale", type=float, default=1.5, help="Additional weight spread across endgame samples.")
    parser.add_argument("--critical-threshold", type=int, default=8, help="Very low pellet states that receive an extra bonus weight.")
    parser.add_argument("--critical-weight-bonus", type=float, default=0.75, help="Extra additive weight for critical endgame samples.")
    parser.add_argument("--junction-weight-bonus", type=float, default=0.55, help="Extra additive weight for 3-way or 4-way decisions.")
    parser.add_argument("--corridor-weight-multiplier", type=float, default=0.78, help="Multiplier applied to simple 2-way corridor choices.")
    parser.add_argument("--escape-weight-bonus", type=float, default=0.9, help="Additive weight for trap-escape planner decisions.")
    parser.add_argument("--energizer-weight-bonus", type=float, default=0.55, help="Additive weight for seek-energizer decisions.")
    parser.add_argument("--frightened-weight-bonus", type=float, default=0.25, help="Additive weight for frightened-ghost chase decisions.")
    parser.add_argument("--danger-threshold", type=float, default=0.28, help="Chosen-direction danger level above which samples get extra weight.")
    parser.add_argument("--danger-weight-scale", type=float, default=1.35, help="Scale for weighting dangerous ghost-pressure samples.")
    parser.add_argument("--low-lives-threshold", type=int, default=2, help="Rows at or below this life count receive extra weight.")
    parser.add_argument("--low-lives-weight-bonus", type=float, default=0.4, help="Additive weight per life below the threshold.")
    parser.add_argument("--finish-weight-bonus", type=float, default=0.45, help="Extra weight for critical low-pellet cleanup choices.")
    parser.add_argument("--death-risk-horizon", type=int, default=12, help="Number of future decisions used to label the death-risk target.")
    parser.add_argument("--frame-stack", type=int, default=1, help="Number of consecutive frames to concatenate (1=no stacking, 3=current+2 previous).")
    args = parser.parse_args()

    input_weights = args.input_weight if args.input_weight is not None and len(args.input_weight) > 0 else None
    if input_weights is not None and len(input_weights) != len(args.input):
        raise SystemExit("--input-weight must be omitted or provide exactly one multiplier per --input path.")

    frame_stack = max(1, args.frame_stack)
    rng = np.random.default_rng(args.seed)
    rows = []
    episode_states: dict[tuple[int, str], EpisodeFeatureState] = {}
    episode_frame_buffers: dict[tuple[int, str], deque] = {}
    previous_entries: dict[int, dict] = {}
    run_counters: dict[int, int] = {}

    for input_index, entry in iter_jsonl_entries(args.input):
        action = DIRECTION_TO_ACTION.get(entry["chosenDirection"])
        if action is None:
            continue
        base_weight = input_weights[input_index] if input_weights is not None else 1.0
        episode_id = str(entry.get("episodeId", f"{input_index}-default"))
        state_key = (input_index, episode_id)
        if state_key not in episode_states:
            episode_states[state_key] = EpisodeFeatureState()
            episode_frame_buffers[state_key] = deque(maxlen=frame_stack)

        previous_entry = previous_entries.get(input_index)
        if starts_new_run(previous_entry, entry):
            run_counters[input_index] = run_counters.get(input_index, -1) + 1
            # Reset frame buffer on new run to avoid leaking across episodes.
            episode_frame_buffers[state_key] = deque(maxlen=frame_stack)
        previous_entries[input_index] = entry

        state = episode_states[state_key]
        state.observe(entry)
        augmented_input = build_augmented_input(entry["input"], entry, state)

        # Frame-stacking: buffer single-frame vectors and concatenate.
        frame_buf = episode_frame_buffers[state_key]
        frame_buf.append(augmented_input)
        if frame_stack > 1:
            single_size = augmented_input.shape[0]
            stacked = np.zeros(single_size * frame_stack, dtype=np.float32)
            # Most recent frame first, then older frames.
            for fi, frame in enumerate(reversed(list(frame_buf))):
                stacked[fi * single_size:(fi + 1) * single_size] = frame
            final_input = stacked
        else:
            final_input = augmented_input

        rows.append({
            "entry": entry,
            "action": action,
            "base_weight": float(base_weight),
            "input": final_input,
            "episode_key": state_key,
            "run_key": (input_index, run_counters[input_index]),
        })

    if not rows:
        raise SystemExit("No usable rows found in dataset.")

    feature_size = len(rows[0]["input"])
    count = len(rows)
    inputs = np.zeros((count, feature_size), dtype=np.float32)
    legal_mask = np.zeros((count, 4), dtype=np.float32)
    labels = np.zeros((count,), dtype=np.int64)
    values = np.zeros((count,), dtype=np.float32)
    death_risk = np.zeros((count,), dtype=np.float32)
    teacher = np.zeros((count, 4), dtype=np.float32)
    sample_weights = np.ones((count,), dtype=np.float32)
    endgame_count = 0
    boosted_escape_count = 0
    boosted_low_lives_count = 0
    boosted_danger_count = 0
    boosted_junction_count = 0

    input_weighted_rows = 0
    episode_row_indices: dict[tuple[int, str], list[int]] = {}

    for index, row in enumerate(rows):
        entry = row["entry"]
        action = row["action"]
        base_weight = row["base_weight"]
        augmented_input = row["input"]
        inputs[index] = augmented_input
        labels[index] = action
        values[index] = np.float32(entry.get("valueEstimate", 0.0))
        remaining_pellets = int(entry.get("remainingPellets", 0))
        objective = int(entry.get("objective", 0))
        lives = int(entry.get("lives", 3))
        sample_weights[index] = np.float32(base_weight)
        episode_row_indices.setdefault(row["run_key"], []).append(index)
        if abs(base_weight - 1.0) > 1e-6:
            input_weighted_rows += 1

        mask = entry.get("legalMask") or [False, True, True, True, True]
        legal_mask[index, 0] = 1.0 if mask[1] else 0.0
        legal_mask[index, 1] = 1.0 if mask[2] else 0.0
        legal_mask[index, 2] = 1.0 if mask[3] else 0.0
        legal_mask[index, 3] = 1.0 if mask[4] else 0.0

        direction_scores = entry.get("directionScores") or [0, 0, 0, 0, 0]
        action_scores = np.asarray([
            direction_scores[1],
            direction_scores[2],
            direction_scores[3],
            direction_scores[4],
        ], dtype=np.float32)
        action_scores = np.where(legal_mask[index] > 0, action_scores, -1e9)
        teacher[index] = softmax(action_scores)

        legal_count = int(np.sum(legal_mask[index]))
        chosen_offset = action * FEATURES_PER_DIRECTION
        chosen_has_pellet = inputs[index, chosen_offset + CHOSEN_PELLET_OFFSET] > 0.5
        chosen_immediate_danger = float(inputs[index, chosen_offset + CHOSEN_IMMEDIATE_DANGER_OFFSET])
        chosen_future_danger = float(inputs[index, chosen_offset + CHOSEN_FUTURE_DANGER_OFFSET])
        chosen_opportunity = float(inputs[index, chosen_offset + CHOSEN_OPPORTUNITY_OFFSET])
        chosen_danger = max(chosen_immediate_danger, chosen_future_danger)

        if args.endgame_threshold > 0 and remaining_pellets <= args.endgame_threshold:
            endgame_count += 1
            normalized_pressure = (args.endgame_threshold - remaining_pellets) / max(args.endgame_threshold, 1)
            sample_weights[index] += np.float32(normalized_pressure * args.endgame_weight_scale)

        if args.critical_threshold > 0 and remaining_pellets <= args.critical_threshold:
            sample_weights[index] += np.float32(args.critical_weight_bonus)

        if legal_count <= 2:
            sample_weights[index] *= np.float32(args.corridor_weight_multiplier)

        if legal_count >= 3:
            boosted_junction_count += 1
            sample_weights[index] += np.float32(args.junction_weight_bonus * (legal_count - 2))

        if objective == ESCAPE_TRAP_OBJECTIVE:
            boosted_escape_count += 1
            sample_weights[index] += np.float32(args.escape_weight_bonus)
        elif objective == SEEK_ENERGIZER_OBJECTIVE:
            sample_weights[index] += np.float32(args.energizer_weight_bonus)
        elif objective == CHASE_FRIGHTENED_OBJECTIVE and chosen_opportunity > 0.2:
            sample_weights[index] += np.float32(args.frightened_weight_bonus)

        if lives <= args.low_lives_threshold:
            boosted_low_lives_count += 1
            sample_weights[index] += np.float32((args.low_lives_threshold - lives + 1) * args.low_lives_weight_bonus)

        if chosen_danger >= args.danger_threshold:
            boosted_danger_count += 1
            danger_pressure = (chosen_danger - args.danger_threshold) / max(1e-6, 1.0 - args.danger_threshold)
            sample_weights[index] += np.float32(danger_pressure * args.danger_weight_scale)

        if remaining_pellets <= args.critical_threshold and (chosen_has_pellet or objective in (ESCAPE_TRAP_OBJECTIVE, SEEK_ENERGIZER_OBJECTIVE)):
            sample_weights[index] += np.float32(args.finish_weight_bonus)

        sample_weights[index] = max(sample_weights[index], np.float32(0.05))

    death_risk = populate_death_risk_targets(rows, episode_row_indices, args.death_risk_horizon)

    indices = np.arange(count)
    rng.shuffle(indices)
    split = int(count * args.train_split)
    train_indices = indices[:split]
    val_indices = indices[split:] if split < count else indices[-max(1, count // 10):]

    np.savez_compressed(
        args.output,
        inputs=inputs,
        legal_mask=legal_mask,
        labels=labels,
        values=values,
        death_risk=death_risk,
        teacher=teacher,
        sample_weights=sample_weights,
        train_indices=train_indices,
        val_indices=val_indices,
    )

    print(
        f"Prepared {count} samples -> {args.output} "
        f"(features={feature_size}, frame_stack={frame_stack}, "
        f"endgame-weighted rows: {endgame_count}, "
        f"death-risk positives: {int(np.sum(death_risk > 0.5))}, "
        f"junction rows: {boosted_junction_count}, "
        f"escape rows: {boosted_escape_count}, "
        f"low-lives rows: {boosted_low_lives_count}, "
        f"danger rows: {boosted_danger_count}, "
        f"input-weighted rows: {input_weighted_rows})"
    )


if __name__ == "__main__":
    main()
