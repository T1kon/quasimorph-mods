"""Build game-ready Moon textures from NASA LRO and extracted Quasimorph maps."""

from argparse import ArgumentParser, Namespace
import hashlib
from pathlib import Path

import numpy as np
from PIL import Image
import tifffile


OUTPUT_SIZE = (3600, 1800)
QUANTILES = np.linspace(0.0, 1.0, 257)
EMISSION_RESIDUAL_THRESHOLD = 12.0
MAXIMUM_SEAM_RATIO = 1.5
EXPECTED_SOURCE_SHA256 = "9731fa8af425b6c2f88f277ecca82bf8c603f3743894f64ed7b25c5bfefa22ff"
EXPECTED_GAME_DIFFUSE_PIXEL_SHA256 = (
    "709a2a6d5cadd863e65f4607ed4771a0d557bba5f8cd8b26c74843a0bf569477"
)
EXPECTED_GAME_NIGHT_PIXEL_SHA256 = (
    "2cae25ae8e70aa130f1ecab3371aa7c5624c850c1e0cfd8051f85c6d91e6332a"
)
EXPECTED_OUTPUT_PIXEL_SHA256 = {
    "moon-lroc.png": "b94bf6095aac5b9ac79061b44cd0b378dad1a4cec9e78119b74c64c2c2f977f9",
    "moon-lroc-night.png": "2478cfb29c658815587ebc55a3da48fd95ef3c45b0636784979e0fdb2eabc52b",
}


def parse_args() -> Namespace:
    parser = ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--game-diffuse", required=True, type=Path)
    parser.add_argument("--game-night", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    return parser.parse_args()


def resize_float_rgb(image: np.ndarray, size: tuple[int, int]) -> np.ndarray:
    channels = []
    for channel in range(3):
        plane = Image.fromarray(image[:, :, channel].astype(np.float32))
        resized = plane.resize(size, Image.Resampling.LANCZOS)
        channels.append(np.asarray(resized, dtype=np.float32))
    return np.stack(channels, axis=2)


def read_source(path: Path) -> np.ndarray:
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != EXPECTED_SOURCE_SHA256:
        raise ValueError(f"Unexpected NASA source SHA-256: {digest}")
    image = tifffile.imread(path)
    if image.dtype != np.uint16 or image.ndim != 3 or image.shape[2] != 3:
        raise ValueError("NASA source must be a 16-bit RGB TIFF.")
    return image.astype(np.float32) / np.iinfo(np.uint16).max


def read_game_texture(path: Path, expected_pixel_sha256: str) -> np.ndarray:
    with Image.open(path) as source:
        image = source.convert("RGB")
    digest = hashlib.sha256(image.tobytes()).hexdigest()
    if digest != expected_pixel_sha256:
        raise ValueError(f"Unexpected pixel SHA-256 for extracted game texture '{path}': {digest}")
    return np.asarray(image, dtype=np.float32) / 255.0


def match_histogram(
    image: np.ndarray, source_reference: np.ndarray, target: np.ndarray
) -> np.ndarray:
    result = np.empty_like(image)
    for channel in range(3):
        source_values = np.quantile(source_reference[:, :, channel], QUANTILES)
        target_values = np.quantile(target[:, :, channel], QUANTILES)
        source_values, unique_indices = np.unique(source_values, return_index=True)
        target_values = target_values[unique_indices]
        result[:, :, channel] = np.interp(
            image[:, :, channel], source_values, target_values
        )
    return np.clip(result, 0.0, 1.0)


def fit_night_transform(diffuse: np.ndarray, night: np.ndarray) -> np.ndarray:
    diffuse_pixels = diffuse.reshape(-1, 3) * 255.0
    night_pixels = night.reshape(-1, 3) * 255.0
    inputs = np.column_stack((diffuse_pixels, np.ones(len(diffuse_pixels))))
    mask = np.ones(len(inputs), dtype=bool)
    transform = np.zeros((4, 3), dtype=np.float64)
    for _ in range(5):
        transform = np.linalg.lstsq(inputs[mask], night_pixels[mask], rcond=None)[0]
        residual = np.linalg.norm(inputs @ transform - night_pixels, axis=1)
        mask = residual <= np.quantile(residual, 0.95)
    return transform


def preserve_emissive_details(
    diffuse: np.ndarray, night: np.ndarray, transform: np.ndarray
) -> np.ndarray:
    diffuse_pixels = diffuse.reshape(-1, 3) * 255.0
    inputs = np.column_stack((diffuse_pixels, np.ones(len(diffuse_pixels))))
    predicted = (inputs @ transform).reshape(night.shape)
    residual = night * 255.0 - predicted
    residual_norm = np.linalg.norm(residual, axis=2)
    emission_mask = residual_norm > EMISSION_RESIDUAL_THRESHOLD
    emission = np.maximum(residual, 0.0) * emission_mask[:, :, np.newaxis]

    channels = []
    for channel in range(3):
        plane = Image.fromarray(emission[:, :, channel].astype(np.float32))
        resized = plane.resize(OUTPUT_SIZE, Image.Resampling.NEAREST)
        channels.append(np.asarray(resized, dtype=np.float32))
    return np.stack(channels, axis=2)


def build_night_texture(
    diffuse: np.ndarray,
    game_diffuse: np.ndarray,
    game_night: np.ndarray,
) -> np.ndarray:
    transform = fit_night_transform(game_diffuse, game_night)
    diffuse_pixels = diffuse.reshape(-1, 3) * 255.0
    inputs = np.column_stack((diffuse_pixels, np.ones(len(diffuse_pixels))))
    base_night = (inputs @ transform).reshape(diffuse.shape)
    emission = preserve_emissive_details(game_diffuse, game_night, transform)
    return np.clip((base_night + emission) / 255.0, 0.0, 1.0)


def save_rgb(path: Path, image: np.ndarray) -> None:
    pixels = np.rint(np.clip(image, 0.0, 1.0) * 255.0).astype(np.uint8)
    Image.fromarray(pixels).save(path, optimize=True)


def validate_output(path: Path) -> None:
    with Image.open(path) as image:
        if image.mode != "RGB" or image.size != OUTPUT_SIZE:
            raise ValueError(f"Unexpected output format for {path}: {image.mode} {image.size}")
        rgb_pixels = np.asarray(image, dtype=np.uint8)

    pixel_digest = hashlib.sha256(rgb_pixels.tobytes()).hexdigest()
    expected_pixel_digest = EXPECTED_OUTPUT_PIXEL_SHA256[path.name]
    if pixel_digest != expected_pixel_digest:
        raise ValueError(
            f"Unexpected output pixel SHA-256 for {path}: {pixel_digest}"
        )

    pixels = rgb_pixels.astype(np.int16)

    seam_mae = float(np.mean(np.abs(pixels[:, 0] - pixels[:, -1])))
    adjacent_mae = float(np.mean(np.abs(pixels[:, 1:] - pixels[:, :-1])))
    seam_ratio = seam_mae / adjacent_mae if adjacent_mae else 0.0
    if seam_ratio > MAXIMUM_SEAM_RATIO:
        raise ValueError(
            f"Horizontal seam discontinuity in {path}: ratio {seam_ratio:.3f} "
            f"exceeds {MAXIMUM_SEAM_RATIO:.3f}"
        )

    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    print(
        f"Validated {path}: seam ratio {seam_ratio:.3f}, "
        f"SHA-256 {digest}, pixel SHA-256 {pixel_digest}"
    )


def main() -> None:
    args = parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    source = read_source(args.source)
    game_diffuse = read_game_texture(
        args.game_diffuse,
        EXPECTED_GAME_DIFFUSE_PIXEL_SHA256,
    )
    game_night = read_game_texture(
        args.game_night,
        EXPECTED_GAME_NIGHT_PIXEL_SHA256,
    )
    if game_diffuse.shape != game_night.shape:
        raise ValueError("Extracted game diffuse and night textures must have matching dimensions.")

    resized_source = resize_float_rgb(source, OUTPUT_SIZE)
    source_for_grading = resize_float_rgb(source, (game_diffuse.shape[1], game_diffuse.shape[0]))
    diffuse = match_histogram(resized_source, source_for_grading, game_diffuse)
    # Histogram knots are calculated from the aligned low-resolution source. Applying them to the
    # full output retains the NASA detail while matching Quasimorph's brightness and palette.
    night = build_night_texture(diffuse, game_diffuse, game_night)

    diffuse_path = args.output_dir / "moon-lroc.png"
    night_path = args.output_dir / "moon-lroc-night.png"
    save_rgb(diffuse_path, diffuse)
    save_rgb(night_path, night)
    validate_output(diffuse_path)
    validate_output(night_path)
    print(f"Wrote {diffuse_path}")
    print(f"Wrote {night_path}")


if __name__ == "__main__":
    main()
