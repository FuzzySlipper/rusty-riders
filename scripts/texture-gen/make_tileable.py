#!/usr/bin/env python3
"""Make a generated texture tile, and derive its normal map.

    scripts/texture-gen/make_tileable.py <input.png> <out-albedo.png> <out-normal.png> --prompt "..." [--size 1024]

Tiling (needs a ComfyUI, COMFY_URL, default the 5090 desktop, with Qwen-Image 2.1 or Z-Image Turbo):
1. The image is rolled by half its size, so its seams form a cross through the middle and its borders,
   formerly its interior, wrap continuously.
2. A model repaints a soft band over that cross (img2img under a noise mask), so the seam disappears while
   the borders stay untouched. Qwen-Image 2.1 (the default) also sees the rolled image as its reference,
   so the band continues the texture's own style; Z-Image Turbo works from the prompt alone.
The albedo is saved greyscale unless --colour is given: level palettes tint it.

Normal map: a height from blurred luminance (bright is high, as the old toon textures read) and its
wrap-around gradient give a tangent-space normal in glTF's convention (x right, y up the image).

Needs Pillow and numpy.
"""
import argparse
import os
import sys
import tempfile

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
import comfy  # noqa: E402


def seam_mask(size: int, band: float) -> Image.Image:
    """White over a soft cross through the middle (the rolled image's seams), black elsewhere."""
    c = np.abs(np.arange(size) - size / 2) / (size * band)
    line = np.clip(1.5 - c, 0, 1)
    cross = np.maximum(line[None, :], line[:, None])
    return Image.fromarray((cross * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(size / 64))


def inpaint_graph(model: str, image: str, mask: str, prompt: str, seed: int, denoise: float) -> dict:
    if model == "qwen":
        return {
            "unet": {"class_type": "UNETLoader", "inputs": {"unet_name": "qwen_image_2.1_int8_convrot.safetensors", "weight_dtype": "default"}},
            "cache": {"class_type": "QwenImage21Cache", "inputs": {"model": ["unet", 0], "device": "auto", "dtype": "default"}},
            "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen3vl_8b_int8_convrot.safetensors", "type": "qwen_image", "device": "default"}},
            "vae": {"class_type": "VAELoader", "inputs": {"vae_name": "qwen_image_2.1_vae_bf16.safetensors"}},
            "image": {"class_type": "LoadImage", "inputs": {"image": image}},
            "mask": {"class_type": "LoadImageMask", "inputs": {"image": mask, "channel": "red"}},
            "text": {"class_type": "TextEncodeQwenImage21", "inputs": {"clip": ["clip", 0], "prompt":
                     "The same texture as the reference image, unchanged in style, scale and colour, continuing seamlessly "
                     "with no seam, line or border through the middle. " + prompt, "negative_prompt": "", "resolution": 1024,
                     "vae": ["vae", 0], "images.image_1": ["image", 0]}},
            "encode": {"class_type": "VAEEncode", "inputs": {"pixels": ["image", 0], "vae": ["vae", 0]}},
            "masked": {"class_type": "SetLatentNoiseMask", "inputs": {"samples": ["encode", 0], "mask": ["mask", 0]}},
            "sample": {"class_type": "KSampler", "inputs": {"model": ["cache", 0], "positive": ["text", 0], "negative": ["text", 1],
                       "latent_image": ["masked", 0], "seed": seed, "steps": 25, "cfg": 1, "sampler_name": "euler",
                       "scheduler": "simple", "denoise": denoise}},
            "decode": {"class_type": "VAEDecode", "inputs": {"samples": ["sample", 0], "vae": ["vae", 0]}},
            "save": {"class_type": "SaveImage", "inputs": {"images": ["decode", 0], "filename_prefix": "riders-tileable"}},
        }
    return {
        "unet": {"class_type": "UNETLoader", "inputs": {"unet_name": "z_image_turbo_bf16.safetensors", "weight_dtype": "default"}},
        "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen_3_4b.safetensors", "type": "lumina2", "device": "default"}},
        "vae": {"class_type": "VAELoader", "inputs": {"vae_name": "ae.safetensors"}},
        "model": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["unet", 0], "shift": 3}},
        "text": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["clip", 0], "text": prompt}},
        "zero": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["text", 0]}},
        "image": {"class_type": "LoadImage", "inputs": {"image": image}},
        "mask": {"class_type": "LoadImageMask", "inputs": {"image": mask, "channel": "red"}},
        "encode": {"class_type": "VAEEncode", "inputs": {"pixels": ["image", 0], "vae": ["vae", 0]}},
        "masked": {"class_type": "SetLatentNoiseMask", "inputs": {"samples": ["encode", 0], "mask": ["mask", 0]}},
        "sample": {"class_type": "KSampler", "inputs": {"model": ["model", 0], "positive": ["text", 0], "negative": ["zero", 0],
                   "latent_image": ["masked", 0], "seed": seed, "steps": 8, "cfg": 1, "sampler_name": "res_multistep",
                   "scheduler": "simple", "denoise": denoise}},
        "decode": {"class_type": "VAEDecode", "inputs": {"samples": ["sample", 0], "vae": ["vae", 0]}},
        "save": {"class_type": "SaveImage", "inputs": {"images": ["decode", 0], "filename_prefix": "riders-tileable"}},
    }


def tileable(source: Image.Image, model: str, prompt: str, seed: int, denoise: float, band: float) -> Image.Image:
    size = source.width
    rolled = Image.fromarray(np.roll(np.asarray(source), (size // 2, size // 2), axis=(0, 1)))
    mask = seam_mask(size, band)
    with tempfile.TemporaryDirectory() as work:
        image_path, mask_path = os.path.join(work, f"rolled-{seed}.png"), os.path.join(work, f"seam-mask-{size}.png")
        rolled.save(image_path)
        mask.save(mask_path)
        comfy.upload(image_path)
        comfy.upload(mask_path)
        out = comfy.run(inpaint_graph(model, os.path.basename(image_path), os.path.basename(mask_path), prompt, seed, denoise),
                        os.path.join(work, "out"))
        painted = Image.open(out[0]).convert("RGB").resize((size, size), Image.LANCZOS)
    # Keep the untouched pixels exactly (the VAE round trip softens them), so the borders still wrap.
    weight = np.asarray(mask, dtype=np.float32)[..., None] / 255
    blended = np.asarray(painted, dtype=np.float32) * weight + np.asarray(rolled, dtype=np.float32) * (1 - weight)
    return Image.fromarray(blended.round().clip(0, 255).astype(np.uint8), "RGB")


def normal_map(albedo: Image.Image, strength: float) -> Image.Image:
    height = np.asarray(albedo.convert("L").filter(ImageFilter.GaussianBlur(1.5)), dtype=np.float32) / 255
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * strength
    # Normals tilt downhill: a height rising rightward tilts x negative; rising down the image tilts y up it (+y).
    normal = np.stack([-dx, dy, np.ones_like(height)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return Image.fromarray(((normal * .5 + .5) * 255).round().astype(np.uint8), "RGB")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("input")
    parser.add_argument("albedo")
    parser.add_argument("normal")
    parser.add_argument("--prompt", required=True, help="what the texture shows, for repainting the seams")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--strength", type=float, default=3, help="normal map tilt per unit of luminance change")
    parser.add_argument("--seed", type=int, default=9373)
    parser.add_argument("--denoise", type=float, default=.75)
    parser.add_argument("--band", type=float, default=.08, help="seam band half-width as a share of the size")
    parser.add_argument("--model", choices=["qwen", "zimage"], default="qwen", help="what repaints the seams")
    parser.add_argument("--colour", action="store_true", help="keep colour instead of saving a greyscale albedo")
    args = parser.parse_args()
    source = Image.open(args.input).convert("RGB").resize((args.size, args.size), Image.LANCZOS)
    albedo = tileable(source, args.model, args.prompt, args.seed, args.denoise, args.band)
    if not args.colour:
        albedo = albedo.convert("L").convert("RGB")
    # Engine content textures are 8-bit RGBA PNGs.
    albedo.convert("RGBA").save(args.albedo, optimize=True)
    normal_map(albedo, args.strength).convert("RGBA").save(args.normal, optimize=True)


if __name__ == "__main__":
    main()
