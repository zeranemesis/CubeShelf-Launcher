"""Generate DecompShelf's original, quiet UI cues; no console audio is sampled."""
import math
from pathlib import Path
import struct
import wave

RATE = 24000
OUT = Path(__file__).resolve().parents[1] / "src/CubeShelf.Desktop/Assets/Sounds"


def tone(t, frequency, length, gain, attack):
    if t < 0 or t >= length:
        return 0
    envelope = math.sin(min(1, t / attack) * math.pi / 2) ** 2 * math.cos(t / length * math.pi / 2) ** 4
    return gain * envelope * math.sin(2 * math.pi * frequency * t)


def write(name, length, sample):
    values = [sample(i / RATE) for i in range(round(length * RATE))]
    assert 0 < max(abs(v) for v in values) < .12, "Keep cues quiet and free of clipping"
    assert abs(values[0]) < 1e-9 and abs(values[-1]) < 1e-5, "Fade both boundaries to silence"
    assert max(abs(b - a) for a, b in zip(values, values[1:])) < .012, "Avoid abrupt waveform edges"
    data = b"".join(struct.pack("<h", round(v * 32767)) for v in values)
    with wave.open(str(OUT / name), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(data)
    with wave.open(str(OUT / name), "rb") as output:
        assert output.getnframes() == len(values) and output.getsampwidth() == 2


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    write("click.wav", .065, lambda t: tone(t, 235, .065, .045, .009) + tone(t, 410, .045, .008, .007))
    write("launch.wav", .42, lambda t: sum(tone(t, frequency, .42, gain, .07)
          for frequency, gain in [(196, .04), (246.94, .025), (293.66, .03)]))
    assert (OUT / "click.wav").read_bytes() != (OUT / "launch.wav").read_bytes()
    print("Generated soft tap (65 ms) and warm launch pad (420 ms), mono PCM, 24 kHz.")
