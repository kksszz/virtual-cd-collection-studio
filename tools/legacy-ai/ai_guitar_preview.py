"""Experimental guitar-remix helper used by ZipMp3Player.

The AI portion is Demucs htdemucs_6s source separation. The isolated guitar is
then modernized with deterministic audio processing and blended back into the
remaining stems. The source audio is never modified.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
import wave
from pathlib import Path

import numpy as np
import torch
import torchaudio.functional as audiofx
from demucs.api import Separator


def report(stage: str, message: str, percent: int) -> None:
    print(json.dumps({"stage": stage, "message": message, "percent": percent}, ensure_ascii=False), flush=True)


def modernize_guitar(guitar: torch.Tensor, sample_rate: int, style: str) -> torch.Tensor:
    presets = {
        "modern_metal": dict(drive=2.20, low=-2.0, body=-3.5, presence=6.5, air=3.5, width=1.24),
        "hard_rock": dict(drive=1.75, low=1.5, body=-1.5, presence=4.5, air=2.5, width=1.16),
        "tight_thrash": dict(drive=2.35, low=-3.5, body=-4.5, presence=7.5, air=4.0, width=1.20),
        "dramatic_remake": dict(drive=3.10, low=-3.0, body=-5.0, presence=9.0, air=5.5, width=1.32),
    }
    p = presets.get(style, presets["modern_metal"])
    result = audiofx.highpass_biquad(guitar, sample_rate, 70.0, 0.707)
    result = audiofx.lowpass_biquad(result, sample_rate, 15500.0, 0.707)
    result = audiofx.equalizer_biquad(result, sample_rate, 110.0, p["low"], 0.8)
    result = audiofx.equalizer_biquad(result, sample_rate, 420.0, p["body"], 0.9)
    result = audiofx.equalizer_biquad(result, sample_rate, 3200.0, p["presence"], 0.8)
    result = audiofx.equalizer_biquad(result, sample_rate, 9000.0, p["air"], 0.7)

    # A conservative neural-amp-like saturation stage. A user-selectable NAM
    # capture can replace this stage later without changing the separation UI.
    drive = p["drive"]
    result = torch.tanh(result * drive) / math.tanh(drive)

    if result.shape[0] == 2:
        mid = (result[0] + result[1]) * 0.5
        side = (result[0] - result[1]) * 0.5 * p["width"]
        result = torch.stack((mid + side, mid - side))
    return result


def match_rms(audio: torch.Tensor, reference: torch.Tensor, limit: float = 2.5) -> torch.Tensor:
    """Keep the comparison honest while retaining the new tone and dynamics."""
    audio_rms = torch.sqrt(torch.mean(audio * audio) + 1.0e-9)
    reference_rms = torch.sqrt(torch.mean(reference * reference) + 1.0e-9)
    gain = torch.clamp(reference_rms / audio_rms, 1.0 / limit, limit)
    return audio * gain


def saturate(audio: torch.Tensor, drive: float) -> torch.Tensor:
    return torch.tanh(audio * drive) / math.tanh(drive)


def modernize_full_mix(original: torch.Tensor, stems: dict[str, torch.Tensor],
                       sample_rate: int, style: str) -> torch.Tensor:
    """Rebuild the mix stem-by-stem so this is audibly beyond ordinary EQ/HDR."""
    guitar_source = stems["guitar"]
    guitar = match_rms(modernize_guitar(guitar_source, sample_rate, style), guitar_source)

    drums_source = stems.get("drums", torch.zeros_like(original))
    drums = audiofx.highpass_biquad(drums_source, sample_rate, 28.0, 0.707)
    drums = audiofx.equalizer_biquad(drums, sample_rate, 68.0, 4.0, 0.8)
    drums = audiofx.equalizer_biquad(drums, sample_rate, 280.0, -2.5, 0.9)
    drums = audiofx.equalizer_biquad(drums, sample_rate, 4800.0, 5.0, 0.7)
    drums = match_rms(saturate(drums, 1.55), drums_source)

    bass_source = stems.get("bass", torch.zeros_like(original))
    bass = audiofx.highpass_biquad(bass_source, sample_rate, 30.0, 0.707)
    bass = audiofx.lowpass_biquad(bass, sample_rate, 7500.0, 0.707)
    bass = audiofx.equalizer_biquad(bass, sample_rate, 75.0, 3.5, 0.8)
    bass = audiofx.equalizer_biquad(bass, sample_rate, 260.0, -3.0, 0.9)
    bass = audiofx.equalizer_biquad(bass, sample_rate, 1350.0, 3.5, 0.8)
    bass = match_rms(saturate(bass, 1.85), bass_source)

    vocals_source = stems.get("vocals", torch.zeros_like(original))
    vocals = audiofx.highpass_biquad(vocals_source, sample_rate, 75.0, 0.707)
    vocals = audiofx.equalizer_biquad(vocals, sample_rate, 350.0, -1.5, 0.9)
    vocals = audiofx.equalizer_biquad(vocals, sample_rate, 3600.0, 2.5, 0.8)
    vocals = match_rms(saturate(vocals, 1.12), vocals_source)

    known = guitar_source + drums_source + bass_source + vocals_source
    remainder = original - known
    gains = {
        "modern_metal": (1.16, 1.15, 1.10, 1.00, 0.94, 1.12),
        "hard_rock": (1.12, 1.08, 1.08, 1.02, 1.00, 1.07),
        "tight_thrash": (1.20, 1.18, 1.08, 0.98, 0.91, 1.13),
        "dramatic_remake": (1.32, 1.23, 1.15, 1.02, 0.88, 1.18),
    }
    guitar_gain, drum_gain, bass_gain, vocal_gain, remainder_gain, glue = gains.get(
        style, gains["modern_metal"])
    rebuilt = (guitar * guitar_gain + drums * drum_gain + bass * bass_gain
               + vocals * vocal_gain + remainder * remainder_gain)
    rebuilt = saturate(rebuilt, glue)
    return match_rms(rebuilt, original, 1.6)


def save_pcm16(path: Path, audio: torch.Tensor, sample_rate: int) -> None:
    data = audio.detach().cpu().clamp(-1.0, 1.0).transpose(0, 1).numpy()
    pcm = np.round(data * 32767.0).astype("<i2")
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(pcm.shape[1])
        output.setsampwidth(2)
        output.setframerate(sample_rate)
        output.writeframes(pcm.tobytes())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--style", default="modern_metal",
                        choices=["modern_metal", "hard_rock", "tight_thrash", "dramatic_remake"])
    parser.add_argument("--mix", default=65.0, type=float)
    parser.add_argument("--mode", default="remix", choices=["remix", "karaoke"])
    args = parser.parse_args()

    report("model", "AIギター分離モデルを準備しています", 5)
    separator = Separator(model="htdemucs_6s", device="cpu", shifts=0, split=True, overlap=0.15, jobs=0, progress=False)
    report("separate", "楽器とボーカルをAIで分離しています", 15)
    original, stems = separator.separate_audio_file(args.input)
    if "guitar" not in stems:
        raise RuntimeError("ギターステムを取得できませんでした")

    wet = max(0.0, min(1.0, args.mix / 100.0))
    if args.mode == "karaoke":
        if "vocals" not in stems:
            raise RuntimeError("ボーカル成分を取得できませんでした")
        report("tone", "ボーカルを除去して伴奏を再構成しています", 78)
        processed = original - stems["vocals"]
    else:
        report("tone", "各楽器を現代的な音へ再構築しています", 78)
        processed = modernize_full_mix(original, stems, separator.samplerate, args.style)
    remixed = original * (1.0 - wet) + processed * wet

    # Avoid clipping without needlessly normalizing quiet previews.
    peak = float(remixed.abs().max())
    if peak > 0.98:
        remixed = remixed * (0.98 / peak)
    report("save", "AIプレビューを保存しています", 94)
    save_pcm16(args.output, remixed, separator.samplerate)
    report("done", "AIカラオケが完成しました" if args.mode == "karaoke" else "AIリメイクが完成しました", 100)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(json.dumps({"stage": "error", "message": str(error), "percent": 0}, ensure_ascii=False), flush=True)
        raise
