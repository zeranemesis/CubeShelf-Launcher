"""Shrink bundled artwork to the size the launcher can actually display.

Game logos are drawn at most 430x130 (decoded to <= 640x260); covers feed the 3D cases and the
zoom view, which never need more than ~1200 px. Oversized sources only cost package size and
decode time (a 13720x3822 logo took 250 ms to decode). Run after importing new artwork:

    python eng/optimize-artwork.py            # rewrite in place, update covers.json file hashes
    python eng/optimize-artwork.py --check    # exit 1 if any file is still oversized (CI)

Source provenance hashes (Sha256 / FullSourceSha256 of the downloaded payload) are left as-is;
only the "*FileSha256" fields that describe the bundled file are refreshed.
"""
import argparse, hashlib, json, pathlib, sys
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "src/CubeShelf.Desktop"
LOGO_BOX = (640, 260)
COVER_MAX = 1200
TARGETS = [(DESKTOP / "Assets/Logos/Database", LOGO_BOX), (DESKTOP / "Assets/Covers", (COVER_MAX, COVER_MAX)),
           (ROOT / "src/CubeShelf.Mobile/Assets/Covers", (COVER_MAX, COVER_MAX))]


def oversized(size, box):
    return size[0] > box[0] or size[1] > box[1]


def shrink(path, box):
    with Image.open(path) as image:
        if not oversized(image.size, box):
            return False
        image.load()
        fmt = image.format
        image.thumbnail(box, Image.LANCZOS)
        options = {"PNG": {"optimize": True}, "JPEG": {"quality": 90, "optimize": True}, "WEBP": {"quality": 90, "method": 6}}.get(fmt, {})
        if fmt == "JPEG" and image.mode not in ("RGB", "L"):
            image = image.convert("RGB")
        temporary = path.with_name(path.name + ".tmp")
        image.save(temporary, format=fmt, **options)
    temporary.replace(path)
    return True


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def refresh_cover_hashes(changed):
    catalog = DESKTOP / "Catalogs/covers.json"
    entries = json.loads(catalog.read_text(encoding="utf-8"))
    updated = 0
    for entry in entries:
        for key, value in list(entry.items()):
            field = key + "FileSha256"
            if isinstance(value, str) and field in entry:
                file = (DESKTOP / value).resolve()
                if file in changed:
                    entry[field] = sha256(file); updated += 1
    if updated:
        catalog.write_text(json.dumps(entries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return updated


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    changed, before, after, late = set(), 0, 0, []
    for folder, box in TARGETS:
        for path in sorted(folder.rglob("*")):
            if path.suffix.lower() not in (".png", ".jpg", ".jpeg", ".webp") or not path.is_file():
                continue
            if args.check:
                with Image.open(path) as image:
                    if oversized(image.size, box): late.append(path)
                continue
            size = path.stat().st_size
            if shrink(path, box):
                changed.add(path.resolve()); before += size; after += path.stat().st_size
    if args.check:
        for path in late: print("oversized:", path.relative_to(ROOT))
        sys.exit(1 if late else 0)
    print(f"{len(changed)} files resized: {before / 1048576:.0f} MB -> {after / 1048576:.0f} MB")
    print(f"{refresh_cover_hashes(changed)} covers.json file hashes refreshed")


if __name__ == "__main__":
    main()
