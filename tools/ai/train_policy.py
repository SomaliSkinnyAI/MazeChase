import argparse
from pathlib import Path

import numpy as np


def relu(x: np.ndarray) -> np.ndarray:
    return np.maximum(x, 0.0)


def sigmoid(x: np.ndarray) -> np.ndarray:
    return 1.0 / (1.0 + np.exp(-x))


def softmax(logits: np.ndarray) -> np.ndarray:
    shifted = logits - np.max(logits, axis=1, keepdims=True)
    exps = np.exp(shifted)
    denom = np.sum(exps, axis=1, keepdims=True)
    return exps / np.clip(denom, 1e-8, None)


def load_split(dataset_path: Path):
    dataset = np.load(dataset_path)
    inputs = dataset["inputs"].astype(np.float32)
    legal_mask = dataset["legal_mask"].astype(np.float32)
    labels = dataset["labels"].astype(np.int64)
    values = dataset["values"].astype(np.float32)
    if "death_risk" in dataset.files:
        death_risk = dataset["death_risk"].astype(np.float32)
    else:
        death_risk = np.zeros((inputs.shape[0],), dtype=np.float32)
    teacher = dataset["teacher"].astype(np.float32)
    if "sample_weights" in dataset.files:
        sample_weights = dataset["sample_weights"].astype(np.float32)
    else:
        sample_weights = np.ones((inputs.shape[0],), dtype=np.float32)
    train_indices = dataset["train_indices"].astype(np.int64)
    val_indices = dataset["val_indices"].astype(np.int64)
    return inputs, legal_mask, labels, values, death_risk, teacher, sample_weights, train_indices, val_indices


def forward(params, x, legal_mask):
    z1 = x @ params["W1"].T + params["b1"]
    h1 = relu(z1)
    z2 = h1 @ params["W2"].T + params["b2"]
    h2 = relu(z2)
    logits = h2 @ params["Wp"].T + params["bp"]
    logits = np.where(legal_mask > 0, logits, -1e9)
    value_raw = np.sum(h2 * params["Wv"][None, :], axis=1, keepdims=True) + params["bv"]
    value = np.tanh(value_raw)
    risk_raw = np.sum(h2 * params["Wr"][None, :], axis=1, keepdims=True) + params["br"]
    risk = sigmoid(risk_raw)
    cache = (x, legal_mask, z1, h1, z2, h2, logits, value_raw, value, risk_raw, risk)
    return logits, value, risk, cache


def backward(
    params,
    cache,
    teacher_targets,
    value_targets,
    death_risk_targets,
    sample_weights,
    value_weight,
    risk_weight,
    risk_positive_weight,
):
    x, legal_mask, z1, h1, z2, h2, logits, value_raw, value, risk_raw, risk = cache
    normalized_weights = sample_weights.astype(np.float32)
    weight_sum = float(np.sum(normalized_weights))
    if weight_sum <= 0:
        normalized_weights = np.ones_like(normalized_weights, dtype=np.float32)
        weight_sum = float(len(normalized_weights))

    normalized_weights = normalized_weights / weight_sum

    probs = softmax(logits)
    dlogits = (probs - teacher_targets) * normalized_weights[:, None]
    dlogits = np.where(legal_mask > 0, dlogits, 0.0)

    dvalue = (2.0 * (value - value_targets[:, None])) * normalized_weights[:, None] * value_weight
    dvalue_raw = dvalue * (1.0 - np.tanh(value_raw) ** 2)

    risk_targets = death_risk_targets[:, None]
    positive_scale = 1.0 + (risk_targets * (risk_positive_weight - 1.0))
    drisk_raw = (risk - risk_targets) * normalized_weights[:, None] * positive_scale * risk_weight

    grads = {}
    grads["Wp"] = dlogits.T @ h2
    grads["bp"] = np.sum(dlogits, axis=0)
    grads["Wv"] = np.sum(dvalue_raw * h2, axis=0)
    grads["bv"] = np.sum(dvalue_raw, axis=0)
    grads["Wr"] = np.sum(drisk_raw * h2, axis=0)
    grads["br"] = np.sum(drisk_raw, axis=0)

    dh2 = dlogits @ params["Wp"] + dvalue_raw @ params["Wv"][None, :] + drisk_raw @ params["Wr"][None, :]
    dz2 = dh2 * (z2 > 0)
    grads["W2"] = dz2.T @ h1
    grads["b2"] = np.sum(dz2, axis=0)

    dh1 = dz2 @ params["W2"]
    dz1 = dh1 * (z1 > 0)
    grads["W1"] = dz1.T @ x
    grads["b1"] = np.sum(dz1, axis=0)
    return grads


def apply_adam(params, grads, state, learning_rate, beta1=0.9, beta2=0.999, eps=1e-8):
    state["step"] += 1
    t = state["step"]
    for key in params.keys():
        state["m"][key] = beta1 * state["m"][key] + (1 - beta1) * grads[key]
        state["v"][key] = beta2 * state["v"][key] + (1 - beta2) * (grads[key] ** 2)
        m_hat = state["m"][key] / (1 - beta1 ** t)
        v_hat = state["v"][key] / (1 - beta2 ** t)
        params[key] -= learning_rate * m_hat / (np.sqrt(v_hat) + eps)


def compute_metrics(params, inputs, legal_mask, teacher, values, death_risk, sample_weights, risk_positive_weight):
    logits, value_pred, risk_pred, _ = forward(params, inputs, legal_mask)
    probs = softmax(logits)
    weights = sample_weights.astype(np.float32)
    weight_sum = float(np.sum(weights))
    if weight_sum <= 0:
        weights = np.ones_like(weights, dtype=np.float32)
        weight_sum = float(len(weights))

    policy_terms = -np.sum(teacher * np.log(np.clip(probs, 1e-8, None)), axis=1)
    value_terms = (value_pred[:, 0] - values) ** 2
    risk_probs = np.clip(risk_pred[:, 0], 1e-6, 1.0 - 1e-6)
    risk_scale = 1.0 + (death_risk * (risk_positive_weight - 1.0))
    risk_terms = -(death_risk * np.log(risk_probs) + (1.0 - death_risk) * np.log(1.0 - risk_probs)) * risk_scale
    accuracy_terms = (np.argmax(probs, axis=1) == np.argmax(teacher, axis=1)).astype(np.float32)
    risk_accuracy_terms = ((risk_probs >= 0.5).astype(np.float32) == death_risk).astype(np.float32)

    policy_loss = float(np.sum(policy_terms * weights) / weight_sum)
    value_loss = float(np.sum(value_terms * weights) / weight_sum)
    risk_loss = float(np.sum(risk_terms * weights) / weight_sum)
    accuracy = float(np.sum(accuracy_terms * weights) / weight_sum)
    risk_accuracy = float(np.sum(risk_accuracy_terms * weights) / weight_sum)
    return policy_loss, value_loss, risk_loss, accuracy, risk_accuracy


def main() -> None:
    parser = argparse.ArgumentParser(description="Train the lightweight autoplay policy network.")
    parser.add_argument("--dataset", required=True, help="Prepared dataset .npz from prepare_dataset.py")
    parser.add_argument("--output", required=True, help="Output .npz model path")
    parser.add_argument("--epochs", type=int, default=40)
    parser.add_argument("--batch-size", type=int, default=256)
    parser.add_argument("--hidden1", type=int, default=128)
    parser.add_argument("--hidden2", type=int, default=64)
    parser.add_argument("--learning-rate", type=float, default=0.001)
    parser.add_argument("--value-weight", type=float, default=0.5)
    parser.add_argument("--risk-weight", type=float, default=0.35)
    parser.add_argument("--risk-positive-weight", type=float, default=3.0)
    parser.add_argument("--seed", type=int, default=1337)
    args = parser.parse_args()

    rng = np.random.default_rng(args.seed)
    inputs, legal_mask, labels, values, death_risk, teacher, sample_weights, train_indices, val_indices = load_split(Path(args.dataset))
    input_size = inputs.shape[1]

    params = {
        "W1": (rng.standard_normal((args.hidden1, input_size)) * 0.05).astype(np.float32),
        "b1": np.zeros((args.hidden1,), dtype=np.float32),
        "W2": (rng.standard_normal((args.hidden2, args.hidden1)) * 0.05).astype(np.float32),
        "b2": np.zeros((args.hidden2,), dtype=np.float32),
        "Wp": (rng.standard_normal((4, args.hidden2)) * 0.05).astype(np.float32),
        "bp": np.zeros((4,), dtype=np.float32),
        "Wv": (rng.standard_normal((args.hidden2,)) * 0.05).astype(np.float32),
        "bv": np.zeros((1,), dtype=np.float32),
        "Wr": (rng.standard_normal((args.hidden2,)) * 0.05).astype(np.float32),
        "br": np.zeros((1,), dtype=np.float32),
    }

    state = {
        "step": 0,
        "m": {key: np.zeros_like(value) for key, value in params.items()},
        "v": {key: np.zeros_like(value) for key, value in params.items()},
    }

    best_snapshot = None
    best_val_loss = float("inf")

    for epoch in range(args.epochs):
        rng.shuffle(train_indices)
        for start in range(0, len(train_indices), args.batch_size):
            batch_indices = train_indices[start:start + args.batch_size]
            batch_inputs = inputs[batch_indices]
            batch_mask = legal_mask[batch_indices]
            batch_teacher = teacher[batch_indices]
            batch_values = values[batch_indices]
            batch_death_risk = death_risk[batch_indices]
            batch_weights = sample_weights[batch_indices]

            _, _, _, cache = forward(params, batch_inputs, batch_mask)
            grads = backward(
                params,
                cache,
                batch_teacher,
                batch_values,
                batch_death_risk,
                batch_weights,
                args.value_weight,
                args.risk_weight,
                args.risk_positive_weight,
            )
            apply_adam(params, grads, state, args.learning_rate)

        train_policy_loss, train_value_loss, train_risk_loss, train_accuracy, train_risk_accuracy = compute_metrics(
            params,
            inputs[train_indices],
            legal_mask[train_indices],
            teacher[train_indices],
            values[train_indices],
            death_risk[train_indices],
            sample_weights[train_indices],
            args.risk_positive_weight,
        )
        val_policy_loss, val_value_loss, val_risk_loss, val_accuracy, val_risk_accuracy = compute_metrics(
            params,
            inputs[val_indices],
            legal_mask[val_indices],
            teacher[val_indices],
            values[val_indices],
            death_risk[val_indices],
            sample_weights[val_indices],
            args.risk_positive_weight,
        )

        total_val_loss = val_policy_loss + (args.value_weight * val_value_loss) + (args.risk_weight * val_risk_loss)
        if total_val_loss < best_val_loss:
            best_val_loss = total_val_loss
            best_snapshot = {key: value.copy() for key, value in params.items()}

        print(
            f"epoch={epoch + 1:03d} "
            f"train_policy={train_policy_loss:.4f} train_value={train_value_loss:.4f} train_risk={train_risk_loss:.4f} "
            f"train_acc={train_accuracy:.3f} train_risk_acc={train_risk_accuracy:.3f} "
            f"val_policy={val_policy_loss:.4f} val_value={val_value_loss:.4f} val_risk={val_risk_loss:.4f} "
            f"val_acc={val_accuracy:.3f} val_risk_acc={val_risk_accuracy:.3f}"
        )

    if best_snapshot is None:
        best_snapshot = params

    np.savez_compressed(
        args.output,
        input_size=input_size,
        hidden1_size=args.hidden1,
        hidden2_size=args.hidden2,
        W1=best_snapshot["W1"],
        b1=best_snapshot["b1"],
        W2=best_snapshot["W2"],
        b2=best_snapshot["b2"],
        Wp=best_snapshot["Wp"],
        bp=best_snapshot["bp"],
        Wv=best_snapshot["Wv"],
        bv=best_snapshot["bv"],
        Wr=best_snapshot["Wr"],
        br=best_snapshot["br"],
    )
    print(f"Saved model -> {args.output}")


if __name__ == "__main__":
    main()
