"""Generate DecompShelf's original, quiet UI cues; no console audio is sampled."""
import math
from pathlib import Path
import struct
import wave

RATE = 24000
OUT = Path(__file__).resolve().parents[1] / "src/CubeShelf.Desktop/Assets/Sounds"


def tone(t, frequency, length, gain):
    if t < 0 or t >= length:
        return 0
    envelope = min(1, t / .004) * (1 - t / length) ** 2
    return gain * envelope * (math.sin(2 * math.pi * frequency * t)
                              + .12 * math.sin(4 * math.pi * frequency * t))


def write(name, length, sample):
    values = [sample(i / RATE) for i in range(round(length * RATE))]
    assert 0 < max(abs(v) for v in values) < .3, "Keep cues quiet and free of clipping"
    data = b"".join(struct.pack("<h", round(v * 32767)) for v in values)
    with wave.open(str(OUT / name), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(data)
    with wave.open(str(OUT / name), "rb") as output:
        assert output.getnframes() == len(values) and output.getsampwidth() == 2


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    write("click.wav", .055, lambda t: tone(t, 880, .055, .12) + tone(t, 440, .035, .04))
    write("launch.wav", .42, lambda t: sum(tone(t - start, frequency, duration, .11)
          for start, frequency, duration in [(0, 523.25, .22), (.06, 783.99, .25), (.13, 1046.5, .29)]))
    assert (OUT / "click.wav").read_bytes() != (OUT / "launch.wav").read_bytes()
    print("Generated click (55 ms) and launch (420 ms), mono PCM, 24 kHz.")
