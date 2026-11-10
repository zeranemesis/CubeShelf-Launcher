"""Import exact-title Xbox 360 box art with the archived marketplace title ID."""
import json
from pathlib import Path
import re
import unicodedata
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
CATALOGS = ROOT / "src/CubeShelf.Desktop/Catalogs"
BASE = "https://raw.githubusercontent.com/xenia-manager/x360db/main/"

def read(url, limit=8_000_000):
    request = urllib.request.Request(url, headers={"User-Agent": "DecompShelf/1.0"})
    with urllib.request.urlopen(request, timeout=20) as response:
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError("Oversized response")
    return data

def key(title):
    title = unicodedata.normalize("NFKD", title).encode("ascii", "ignore").decode().lower()
    return re.sub(r"[^a-z0-9]", "", re.sub(r"\bthe\b", "", title))

def main():
    games = json.loads(read(BASE + "games.json"))
    covers_path = CATALOGS / "covers.json"
    covers = json.loads(covers_path.read_text(encoding="utf-8-sig"))
    existing = {(item["Console"], item["Title"]) for item in covers if item.get("Front")}
    ports = json.loads((CATALOGS / "xbox360.json").read_text(encoding="utf-8"))
    missing_path = CATALOGS / "missing-console-covers.json"
    missing = json.loads(missing_path.read_text(encoding="utf-8"))
    added = []
    for title in dict.fromkeys(port["Title"] for port in ports):
        if ("xbox360", title) in existing:
            continue
        matches = [item for item in games if key(item.get("title", "")) == key(title) and re.fullmatch(r"[0-9A-F]{8}", item.get("id", ""))]
        for item in matches:
            url = BASE + f"titles/{item['id']}/artwork/boxart.jpg"
            try:
                data = read(url)
                if not data.startswith(b"\xff\xd8\xff"):
                    continue
                relative = f"Assets/Covers/xbox360/{item['id']}.jpg"
                target = ROOT / "src/CubeShelf.Desktop" / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                covers.append({"Console": "xbox360", "Title": title, "GameTdbId": item["id"], "Source": "x360db", "OriginUrl": url, "Front": relative, "Back": "", "Spine": ""})
                added.append(title)
                break
            except (OSError, ValueError):
                continue
    assert len({(item["Console"], item["Title"]) for item in covers}) == len(covers)
    covers_path.write_text(json.dumps(covers, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    missing = [item for item in missing if item["Console"] != "xbox360" or item["Title"] not in added]
    missing_path.write_text(json.dumps(missing, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Xbox 360: {len(added)} exact title matches added; {len(missing)} covers remain missing overall")

if __name__ == "__main__":
    main()
