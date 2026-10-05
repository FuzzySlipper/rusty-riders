#!/usr/bin/env python3
"""Caption a LoRA training set with Qwen3-VL running in ComfyUI (TextGenerate), with a trigger word.

    scripts/texture-gen/caption.py <dataset-dir> [--trigger "rrink style"]

Writes <image>.txt beside each PNG: "<trigger>, <description>". The description names what is shown
(forms, layout, colours) and avoids style words, so the trigger word carries the style.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import comfy  # noqa: E402

PROMPT = ("Describe this game texture image in one sentence of at most 40 words, for an image-training caption: "
          "what it shows (materials, forms, how they are arranged, whether it is a tiling surface, a sheet of parts "
          "or a single object) and its main colours. Do not mention art style, outlines, shading, cartoon or toon. "
          "Reply with the sentence only.")


def graph(image: str) -> dict:
    return {
        "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen3vl_8b_int8_convrot.safetensors", "type": "qwen_image", "device": "default"}},
        "image": {"class_type": "LoadImage", "inputs": {"image": image}},
        "text": {"class_type": "TextGenerate", "inputs": {"clip": ["clip", 0], "prompt": PROMPT, "max_length": 120,
                 "sampling_mode": "off", "image": ["image", 0]}},
        "show": {"class_type": "PreviewAny", "inputs": {"source": ["text", 0]}},
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("dataset")
    parser.add_argument("--trigger", default="rrink style")
    args = parser.parse_args()
    for name in sorted(os.listdir(args.dataset)):
        if not name.endswith(".png"):
            continue
        caption_path = os.path.join(args.dataset, name[:-4] + ".txt")
        if os.path.exists(caption_path):
            continue
        comfy.upload(os.path.join(args.dataset, name))
        texts = comfy.run_text(graph(name))
        description = " ".join(texts).strip().replace("\n", " ")
        open(caption_path, "w").write(f"{args.trigger}, {description}\n")
        print(f"{name}: {description}")


if __name__ == "__main__":
    main()
