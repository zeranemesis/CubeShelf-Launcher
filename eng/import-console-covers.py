"""Fetch matching original box fronts from libretro-thumbnails; never substitute another game."""
import concurrent.futures
import json
from pathlib import Path
import re
import unicodedata
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
CATALOGS = ROOT / "src/CubeShelf.Desktop/Catalogs"
REPOS = {
    "nes": "Nintendo_-_Nintendo_Entertainment_System", "snes": "Nintendo_-_Super_Nintendo_Entertainment_System",
    "gb": "Nintendo_-_Game_Boy", "gbc": "Nintendo_-_Game_Boy_Color", "gba": "Nintendo_-_Game_Boy_Advance",
    "nds": "Nintendo_-_Nintendo_DS", "3ds": "Nintendo_-_Nintendo_3DS", "wiiu": "Nintendo_-_Wii_U",
    "virtualboy": "Nintendo_-_Virtual_Boy", "mastersystem": "Sega_-_Master_System_-_Mark_III",
    "megadrive": "Sega_-_Mega_Drive_-_Genesis", "gamegear": "Sega_-_Game_Gear", "32x": "Sega_-_32X",
    "segacd": "Sega_-_Mega-CD_-_Sega_CD", "saturn": "Sega_-_Saturn", "dreamcast": "Sega_-_Dreamcast",
    "ps1": "Sony_-_PlayStation", "ps2": "Sony_-_PlayStation_2", "ps3": "Sony_-_PlayStation_3",
    "ps4": "Sony_-_PlayStation_4", "psp": "Sony_-_PlayStation_Portable", "xbox": "Microsoft_-_Xbox",
    "xbox360": "Microsoft_-_Xbox_360", "arcade": "MAME", "neogeo": "SNK_-_Neo_Geo", "cdi": "Philips_-_CD-i",
}

def read(url, maximum=12_000_000):
    request = urllib.request.Request(url, headers={"User-Agent": "DecompShelf-catalog-import"})
    with urllib.request.urlopen(request, timeout=25) as response:
        data = response.read(maximum + 1)
    if len(data) > maximum:
        raise ValueError("Response exceeds limit")
    return data

def normalize(title):
    title = re.sub(r"\([^)]*\)|\[[^]]*\]", "", title)
    title = unicodedata.normalize("NFKD", title).encode("ascii", "ignore").decode().lower()
    return re.sub(r"[^a-z0-9]", "", re.sub(r"\bthe\b", "", title))

def fetch_console(item):
    console, repo = item
    file = CATALOGS / (console + ".json")
    if not file.exists():
        return [], []
    ports = json.loads(file.read_text(encoding="utf-8-sig"))
    try:
        cached = ROOT / ".tools/console-cover-index" / (console + ".json")
        if console == "nes":
            images = [f"Named_Boxarts/{port['Title']} ({region}).png" for port in ports for region in ("USA", "World", "Japan, USA", "USA, Europe")]
        elif cached.exists():
            images = json.loads(cached.read_text(encoding="utf-8"))
        else:
            tree = json.loads(read(f"https://api.github.com/repos/libretro-thumbnails/{repo}/git/trees/master?recursive=1"))
            if tree.get("truncated"):
                raise ValueError("Incomplete artwork tree")
            images = [entry["path"] for entry in tree["tree"] if entry["path"].startswith("Named_Boxarts/") and entry["path"].endswith(".png")]
    except Exception as error:
        print(f"{console}: artwork unavailable ({type(error).__name__})", flush=True)
        return [], [{"Console": console, "Title": port["Title"]} for port in ports]
    records, missing = [], []
    for title in dict.fromkeys(port["Title"] for port in ports):
        key = normalize(title)
        matches = [path for path in images if normalize(Path(path).stem) == key]
        if not matches:
            missing.append({"Console": console, "Title": title})
            continue
        matches.sort(key=lambda path: ("(USA" not in path, "(Europe" not in path, len(path)))
        path = matches[0]
        branch = "main" if console == "cdi" else "master"
        url = f"https://raw.githubusercontent.com/libretro-thumbnails/{repo}/{branch}/" + urllib.parse.quote(path)
        slug = re.sub(r"[^a-z0-9]+", "-", key)
        relative = f"Assets/Covers/{console}/{slug}.png"
        target = ROOT / "src/CubeShelf.Desktop" / relative
        try:
            if not target.exists():
                data = None
                for candidate in matches:
                    candidate_url = f"https://raw.githubusercontent.com/libretro-thumbnails/{repo}/{branch}/" + urllib.parse.quote(candidate)
                    try:
                        candidate_data = read(candidate_url, 8_000_000)
                        if candidate_data.startswith(b"\x89PNG\r\n\x1a\n"):
                            data, url = candidate_data, candidate_url
                            break
                    except Exception:
                        continue
                if data is None:
                    raise ValueError("No exact cover available")
                target.parent.mkdir(parents=True, exist_ok=True)
                temporary = target.with_suffix(".partial")
                temporary.write_bytes(data)
                temporary.replace(target)
            records.append({"Console": console, "Title": title, "GameTdbId": "", "Source": "libretro-thumbnails", "OriginUrl": url, "Front": relative, "Back": "", "Spine": ""})
        except Exception:
            missing.append({"Console": console, "Title": title})
    print(f"{console}: {len(records)} exact covers, {len(missing)} missing", flush=True)
    return records, missing

def main():
    covers_file = CATALOGS / "covers.json"
    covers = json.loads(covers_file.read_text(encoding="utf-8-sig"))
    missing = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        for records, absent in pool.map(fetch_console, REPOS.items()):
            for record in records:
                covers = [cover for cover in covers if (cover["Console"], cover["Title"]) != (record["Console"], record["Title"])]
                covers.append(record)
            missing.extend(absent)
    assert len({(cover["Console"], cover["Title"]) for cover in covers}) == len(covers)
    available = {(cover["Console"], cover["Title"]) for cover in covers if (ROOT / "src/CubeShelf.Desktop" / cover.get("Front", "")).is_file()}
    missing = [item for item in missing if (item["Console"], item["Title"]) not in available]
    covers_file.write_text(json.dumps(covers, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (CATALOGS / "missing-console-covers.json").write_text(json.dumps(missing, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Missing artwork recorded: {len(missing)}", flush=True)

if __name__ == "__main__":
    main()
