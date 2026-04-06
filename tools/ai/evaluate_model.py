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
    return exps / np.clip(np.sum(exps, axis=1, keepdims=True), 1e-8, None)


def forward(model, inputs, legal_mask):
    z1 = inputs @ model["W1"].T + model["b1"]
    h1 = relu(z1)
    z2 = h1 @ model["W2"].T + model["b2"]
    h2 = relu(z2)
    logits = h2 @ model["Wp"].T + model["bp"]
    logits = np.where(legal_mask > 0, logits, -1e9)
    values = np.tanh(np.sum(h2 * model["Wv"][None, :], axis=1, keepdims=True) + model["bv"])
    if "Wr" in model.files and "br" in model.files:
        risk = sigmoid(np.sum(h2 * model["Wr"][None, :], axis=1, keepdims=True) + model["br"])
    else:
        risk = np.full((inputs.shape[0], 1), 0.5, dtype=np.float32)
    return logits, values[:, 0], risk[:, 0]


def main() -> None:
    parser = argparse.ArgumentParser(description="Evaluate a trained autoplay model against a prepared dataset.")
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--model", required=True)
    args = parser.parse_args()

    dataset = np.load(Path(args.dataset))
    model = np.load(Path(args.model))

    logits, values, risk = forward(model, dataset["inputs"], dataset["legal_mask"])
    probs = softmax(logits)
    teacher_labels = np.argmax(dataset["teacher"], axis=1)
    predictions = np.argmax(probs, axis=1)
    top2 = np.argsort(probs, axis=1)[:, -2:]
    top2_hits = np.mean(np.any(top2 == teacher_labels[:, None], axis=1))
    value_mse = np.mean((values - dataset["values"]) ** 2)
    accuracy = np.mean(predictions == teacher_labels)

    if "death_risk" in dataset.files:
        death_risk = dataset["death_risk"].astype(np.float32)
        risk_probs = np.clip(risk, 1e-6, 1.0 - 1e-6)
        risk_bce = np.mean(-(death_risk * np.log(risk_probs) + (1.0 - death_risk) * np.log(1.0 - risk_probs)))
        risk_accuracy = np.mean((risk_probs >= 0.5) == (death_risk >= 0.5))
        print(f"death_risk_bce={risk_bce:.6f}")
        print(f"death_risk_acc={risk_accuracy:.4f}")

    print(f"accuracy={accuracy:.4f}")
    print(f"top2_accuracy={top2_hits:.4f}")
    print(f"value_mse={value_mse:.6f}")


if __name__ == "__main__":
    main()
