"""Fetch the pinned official engine; package executable libraries at build time only."""
import hashlib
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = "v0.20.0"
SHA256 = "7b11534b8e68d4386d03738c6f3fd97324ecf5a71dc212929128d0d8201c2374"
NAME = "PartyBoard-v0.20.0-phone-arm64-v8a.apk"
URL = f"https://github.com/zeranemesis/Marioparty4/releases/download/{VERSION}/{NAME}"


def prepare():
    cache = ROOT / "artifacts" / "partyboard"
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / NAME
    if not archive.exists():
        temporary = archive.with_suffix(".download")
        with urllib.request.urlopen(URL, timeout=60) as source, temporary.open("wb") as output:
            while chunk := source.read(256 * 1024):
                output.write(chunk)
        temporary.replace(archive)
    with archive.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    if digest != SHA256:
        raise ValueError("PartyBoard archive SHA256 mismatch; refusing to package executable code")
    destination = ROOT / "src" / "CubeShelf.Android" / "Generated" / "PartyBoard" / "arm64-v8a"
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as package:
        for entry in package.infolist():
            if not entry.filename.startswith("lib/arm64-v8a/") or not entry.filename.endswith(".so"):
                continue
            target = destination / Path(entry.filename).name
            data = package.read(entry)
            if not target.exists() or target.read_bytes() != data:
                target.write_bytes(data)
    print(f"PartyBoard {VERSION}: verified native ARM64 libraries ready")


if __name__ == "__main__":
    prepare()
