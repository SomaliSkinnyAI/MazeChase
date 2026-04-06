import argparse
import json
from pathlib import Path

import numpy as np


def main() -> None:
    parser = argparse.ArgumentParser(description="Export a trained NumPy model to Unity JSON weights.")
    parser.add_argument("--model", required=True, help="Path to model .npz from train_policy.py")
    parser.add_argument("--output", required=True, help="Path to output Unity JSON file")
    parser.add_argument("--source", default="offline-trainer")
    args = parser.parse_args()

    model = np.load(Path(args.model))
    payload = {
        "version": "2.0",
        "source": args.source,
        "inputSize": int(model["input_size"]),
        "hidden1Size": int(model["hidden1_size"]),
        "hidden2Size": int(model["hidden2_size"]),
        "w1": model["W1"].astype(float).reshape(-1).tolist(),
        "b1": model["b1"].astype(float).reshape(-1).tolist(),
        "w2": model["W2"].astype(float).reshape(-1).tolist(),
        "b2": model["b2"].astype(float).reshape(-1).tolist(),
        "policyW": model["Wp"].astype(float).reshape(-1).tolist(),
        "policyB": model["bp"].astype(float).reshape(-1).tolist(),
        "valueW": model["Wv"].astype(float).reshape(-1).tolist(),
        "valueB": model["bv"].astype(float).reshape(-1).tolist(),
        "riskW": model["Wr"].astype(float).reshape(-1).tolist(),
        "riskB": model["br"].astype(float).reshape(-1).tolist(),
    }

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload), encoding="utf-8")
    print(f"Exported weights -> {output_path}")


if __name__ == "__main__":
    main()
