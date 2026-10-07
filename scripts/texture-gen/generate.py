#!/usr/bin/env python3
"""Generate a batch of images on the ComfyUI host, one per seed.

    scripts/texture-gen/generate.py <out-dir> --prompt "..." --model zimage-base [--lora NAME.safetensors]
        [--lora-strength 1] [--seeds 1-8] [--reference ref.png] [--size 1024] [--tag name]

Models:
- zimage-turbo: Z-Image Turbo, 8 steps, cfg 1 (fast drafts; no negative prompt)
- zimage-base: Z-Image, 25 steps, cfg 4 (what style LoRAs are trained on)
- qwen-edit: Qwen-Image 2.1 with --reference as its reference image, 25 steps, cfg 1

Writes <out-dir>/<tag>-<seed>.png and appends each run (model, LoRA, prompt, seed, seconds) to
<out-dir>/runs.jsonl, the provenance a texture's sources.json entry is copied from.
"""
import argparse
import json
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(__file__))
import comfy  # noqa: E402


def zimage(prompt: str, seed: int, size: int, steps: int, cfg: float, unet: str, lora: str | None, strength: float,
           negative: str = "") -> dict:
    g = {
        "unet": {"class_type": "UNETLoader", "inputs": {"unet_name": unet, "weight_dtype": "default"}},
        "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen_3_4b.safetensors", "type": "lumina2", "device": "default"}},
        "vae": {"class_type": "VAELoader", "inputs": {"vae_name": "ae.safetensors"}},
        "sampling": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["unet", 0], "shift": 3}},
        "text": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["clip", 0], "text": prompt}},
        "negative": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["clip", 0], "text": negative}},
        "latent": {"class_type": "EmptySD3LatentImage", "inputs": {"width": size, "height": size, "batch_size": 1}},
        "sample": {"class_type": "KSampler", "inputs": {"model": ["sampling", 0], "positive": ["text", 0], "negative": ["negative", 0],
                   "latent_image": ["latent", 0], "seed": seed, "steps": steps, "cfg": cfg, "sampler_name": "res_multistep",
                   "scheduler": "simple", "denoise": 1}},
        "decode": {"class_type": "VAEDecode", "inputs": {"samples": ["sample", 0], "vae": ["vae", 0]}},
        "save": {"class_type": "SaveImage", "inputs": {"images": ["decode", 0], "filename_prefix": "riders-batch"}},
    }
    if lora:
        g["lora"] = {"class_type": "LoraLoaderModelOnly", "inputs": {"model": ["unet", 0], "lora_name": lora, "strength_model": strength}}
        g["sampling"]["inputs"]["model"] = ["lora", 0]
    return g


def qwen_edit(prompt: str, seed: int, size: int, reference: str) -> dict:
    return {
        "unet": {"class_type": "UNETLoader", "inputs": {"unet_name": "qwen_image_2.1_int8_convrot.safetensors", "weight_dtype": "default"}},
        "cache": {"class_type": "QwenImage21Cache", "inputs": {"model": ["unet", 0], "device": "auto", "dtype": "default"}},
        "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen3vl_8b_int8_convrot.safetensors", "type": "qwen_image", "device": "default"}},
        "vae": {"class_type": "VAELoader", "inputs": {"vae_name": "qwen_image_2.1_vae_bf16.safetensors"}},
        "ref": {"class_type": "LoadImage", "inputs": {"image": reference}},
        "text": {"class_type": "TextEncodeQwenImage21", "inputs": {"clip": ["clip", 0], "prompt": prompt, "negative_prompt": "",
                 "resolution": size, "vae": ["vae", 0], "images.image_1": ["ref", 0]}},
        "latent": {"class_type": "EmptyLatentImage", "inputs": {"width": size, "height": size, "batch_size": 1}},
        "sample": {"class_type": "KSampler", "inputs": {"model": ["cache", 0], "positive": ["text", 0], "negative": ["text", 1],
                   "latent_image": ["latent", 0], "seed": seed, "steps": 25, "cfg": 1, "sampler_name": "euler",
                   "scheduler": "simple", "denoise": 1}},
        "decode": {"class_type": "VAEDecode", "inputs": {"samples": ["sample", 0], "vae": ["vae", 0]}},
        "save": {"class_type": "SaveImage", "inputs": {"images": ["decode", 0], "filename_prefix": "riders-batch"}},
    }


def seeds(text: str) -> list[int]:
    if "-" in text:
        first, last = text.split("-")
        return list(range(int(first), int(last) + 1))
    return [int(s) for s in text.split(",")]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("out_dir")
    parser.add_argument("--prompt", required=True)
    parser.add_argument("--model", choices=["zimage-turbo", "zimage-base", "qwen-edit"], default="zimage-base")
    parser.add_argument("--negative", default="", help="negative prompt (zimage-base only; turbo runs at cfg 1 and ignores it)")
    parser.add_argument("--lora")
    parser.add_argument("--lora-strength", type=float, default=1.0)
    parser.add_argument("--seeds", default="1-4")
    parser.add_argument("--reference")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--tag", default="gen")
    args = parser.parse_args()
    os.makedirs(args.out_dir, exist_ok=True)
    if args.model == "qwen-edit":
        if not args.reference:
            parser.error("qwen-edit needs --reference")
        comfy.upload(args.reference)
    for seed in seeds(args.seeds):
        if args.model == "qwen-edit":
            g = qwen_edit(args.prompt, seed, args.size, os.path.basename(args.reference))
        elif args.model == "zimage-turbo":
            g = zimage(args.prompt, seed, args.size, 8, 1, "z_image_turbo_bf16.safetensors", args.lora, args.lora_strength)
        else:
            g = zimage(args.prompt, seed, args.size, 25, 4, "z_image_bf16.safetensors", args.lora, args.lora_strength, args.negative)
        with tempfile.TemporaryDirectory() as work:
            out = comfy.run(g, os.path.join(work, "out"))
            path = os.path.join(args.out_dir, f"{args.tag}-{seed}.png")
            shutil.copy(out[0], path)
        record = {"image": path, "model": args.model, "lora": args.lora, "lora_strength": args.lora_strength if args.lora else None,
                  "reference": args.reference, "prompt": args.prompt, "negative": args.negative, "seed": seed, "size": args.size}
        with open(os.path.join(args.out_dir, "runs.jsonl"), "a") as runs:
            runs.write(json.dumps(record) + "\n")


if __name__ == "__main__":
    main()
