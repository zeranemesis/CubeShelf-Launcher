"""Original quiet menu score and cues. No samples or console music."""
import math
import pathlib
import struct
import wave

RATE = 24000
ROOT = pathlib.Path(__file__).resolve().parents[1] / "src/CubeShelf.Desktop/Assets/Sounds"


def save(name, duration, sample):
    count = int(duration * RATE)
    values = [sample(i / RATE) for i in range(count)]
    assert max(abs(value) for value in values) < 0.85
    assert abs(values[0]) < 0.001 and abs(values[-1]) < 0.001
    with wave.open(str(ROOT / f"home-{name}.wav"), "wb") as audio:
        audio.setparams((1, 2, RATE, count, "NONE", "not compressed"))
        audio.writeframes(b"".join(struct.pack("<h", round(value * 32767)) for value in values))


def note(t, start, length, hz, volume):
    age = t - start
    if not 0 <= age < length:
        return 0
    envelope = math.sin(math.pi * age / length) ** 2
    return volume * envelope * (math.sin(math.tau * hz * age) + .12 * math.sin(math.tau * hz * 2 * age))


def ambience(t):
    # An airy suspended chord under a sparse original pentatonic phrase; 16-second loop.
    fade = min(1, t / .8, (16 - t) / .8)
    pad = sum(math.sin(math.tau * hz * t) for hz in (130.8128, 196, 293.6648, 329.6276)) * .022
    phrase = (392, 493.8833, 587.3295, 659.2551, 587.3295, 493.8833, 440, 392)
    bells = sum(note(t, i * 2, 1.9, hz, .065) for i, hz in enumerate(phrase))
    return fade * (pad + bells)


save("ambience", 16, ambience)
save("move", .12, lambda t: note(t, 0, .12, 440, .10))
save("open", .30, lambda t: note(t, 0, .30, 392, .09) + note(t, .06, .24, 587.3295, .07))
save("launch", .70, lambda t: sum(note(t, i * .075, .70 - i * .075, hz, .065) for i, hz in enumerate((261.6256, 392, 523.2511))))
print("Original HOME audio generated; PCM bounds and silent endpoints verified.")
