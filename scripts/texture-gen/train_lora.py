#!/usr/bin/env python3
"""Train a style LoRA in ComfyUI (native TrainLoraNode) on an uploaded image+caption folder.

    scripts/texture-gen/train_lora.py --dataset rrink_ink_toon --name rrink_zimage_v1 [--steps 1500] [--submit]

Without --submit it prints the graph and does nothing: training occupies the desktop GPU for a long time,
so submit only when the GPU's owner has agreed to that run. The dataset folder is a ComfyUI input subfolder
of PNGs with same-named .txt captions (scripts/texture-gen/lora_dataset.py and caption.py make one; upload
it with comfy.upload_folder). The LoRA is saved as models/loras/<name>_<steps>_steps.safetensors on the
ComfyUI host.

Defaults suit a ~40-image style set on Z-Image base: rank 16, AdamW at 1e-4, batch 1, the model's own dtype,
gradient checkpointing, about 35 passes over the set. On the 5090 (32 GB, 64 GB RAM):
- At 1024 px, training dtype bf16 and no offloading, it asked for 58 GB and ran out of memory.
- At 768 px with --offload it fitted, but streamed weights through system RAM. That took 83 s per step (32 h
  for 1500 steps) and left the desktop with 2 GB of RAM, so it was stopped.
Probe a configuration with a few --steps before a full run.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import comfy  # noqa: E402

MODELS = {
    # base diffusion model, text encoder (and its loader type), VAE
    "zimage": ("z_image_bf16.safetensors", "qwen_3_4b.safetensors", "lumina2", "ae.safetensors"),
}


def graph(dataset: str, name: str, model: str, steps: int, rank: int, learning_rate: float, seed: int,
          training_dtype: str, offload: bool) -> dict:
    unet, clip, clip_type, vae = MODELS[model]
    return {
        "unet": {"class_type": "UNETLoader", "inputs": {"unet_name": unet, "weight_dtype": "default"}},
        "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": clip, "type": clip_type, "device": "default"}},
        "vae": {"class_type": "VAELoader", "inputs": {"vae_name": vae}},
        "data": {"class_type": "LoadImageTextDataSetFromFolder", "inputs": {"folder": dataset}},
        "dataset": {"class_type": "MakeTrainingDataset", "inputs": {"images": ["data", 0], "texts": ["data", 1],
                    "vae": ["vae", 0], "clip": ["clip", 0]}},
        "train": {"class_type": "TrainLoraNode", "inputs": {
            "model": ["unet", 0], "latents": ["dataset", 0], "positive": ["dataset", 1],
            "batch_size": 1, "grad_accumulation_steps": 1, "steps": steps, "learning_rate": learning_rate,
            "rank": rank, "optimizer": "AdamW", "loss_function": "MSE", "seed": seed,
            "training_dtype": training_dtype, "lora_dtype": "bf16", "quantized_backward": False, "algorithm": "LoRA",
            "gradient_checkpointing": True, "checkpoint_depth": 1, "offloading": offload, "existing_lora": "[None]",
            "bucket_mode": False, "bypass_mode": False}},
        "save": {"class_type": "SaveLoRA", "inputs": {"lora": ["train", 0], "prefix": f"loras/{name}", "steps": ["train", 2]}},
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--dataset", required=True, help="ComfyUI input subfolder with images and .txt captions")
    parser.add_argument("--name", required=True, help="LoRA file name prefix")
    parser.add_argument("--model", choices=sorted(MODELS), default="zimage")
    parser.add_argument("--steps", type=int, default=1500)
    parser.add_argument("--rank", type=int, default=16)
    parser.add_argument("--learning-rate", type=float, default=1e-4)
    parser.add_argument("--seed", type=int, default=9420)
    parser.add_argument("--training-dtype", choices=["none", "bf16", "fp32"], default="none",
                        help="none keeps the model's own dtype; bf16 at 1024 px ran out of the 5090's 32 GB")
    parser.add_argument("--offload", action="store_true",
                        help="offload model weights to system RAM while training (very slow on the 5090: 83 s per step)")
    parser.add_argument("--submit", action="store_true", help="queue the run (only with the GPU owner's agreement)")
    args = parser.parse_args()
    g = graph(args.dataset, args.name, args.model, args.steps, args.rank, args.learning_rate, args.seed,
              args.training_dtype, args.offload)
    if not args.submit:
        print(json.dumps(g, indent=1))
        return
    pid = comfy.post("/prompt", {"prompt": g, "client_id": "riders-lora"})["prompt_id"]
    print(json.dumps({"prompt_id": pid, "queued": True}))


if __name__ == "__main__":
    main()
