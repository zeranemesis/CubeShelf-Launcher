"""Import original-platform Clear Logos from LaunchBox's public Metadata.zip.

python eng/import-game-logos.py path/to/Metadata.zip [--match-only]
python eng/import-game-logos.py --check
Images remain unmodified; no fuzzy or cross-platform substitutions.
"""
import argparse
import concurrent.futures
import hashlib
import io
import http.client
import json
from pathlib import Path
import re
import threading
import unicodedata
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "src/CubeShelf.Desktop"
DEST = DESKTOP / "Assets/Logos/Database"
PLATFORMS = dict(gamecube="Nintendo GameCube", wii="Nintendo Wii", switch="Nintendo Switch",
    n64="Nintendo 64", nes="Nintendo Entertainment System", snes="Super Nintendo Entertainment System",
    gb="Nintendo Game Boy", gbc="Nintendo Game Boy Color", gba="Nintendo Game Boy Advance",
    nds="Nintendo DS", wiiu="Nintendo Wii U", virtualboy="Nintendo Virtual Boy",
    mastersystem="Sega Master System", megadrive="Sega Genesis", gamegear="Sega Game Gear",
    segacd="Sega CD", saturn="Sega Saturn", dreamcast="Sega Dreamcast", ps1="Sony Playstation",
    ps2="Sony Playstation 2", ps3="Sony Playstation 3", ps4="Sony Playstation 4", psp="Sony PSP",
    xbox="Microsoft Xbox", xbox360="Microsoft Xbox 360", arcade="Arcade", neogeo="SNK Neo Geo AES",
    cdi="Philips CD-i", **{"3ds": "Nintendo 3DS", "32x": "Sega 32X"})

# Reviewed abbreviated/regional catalogue names; never infer a different platform or edition.
ALIASES = {
    ("3ds", "Ocarina of Time 3D"): "12698",
    ("arcade", "Marvel vs. Capcom"): "7876",
    ("arcade", "Vampire Hunter 2"): "40411",
    ("arcade", "Vampire Savior"): "12860",
    ("arcade", "Vampire Savior 2"): "40414",
    ("n64", "Bomberman 64"): "1072",
    ("n64", "The World Is Not Enough"): "238",
    ("n64", "Virtual Pro Wrestling 2"): "31085",
    ("neogeo", "Metal Slug X"): "973",
    ("ps1", "Bloody Roar 2"): "2506",
    ("ps1", "Hogs of War / Frontschweine"): "9933",
    ("ps1", "Metal Gear Solid"): "196",
    ("ps1", "PSX USA MediEvil"): "10019",
    ("ps1", "Rockman Dash"): "566",
    ("ps1", "Simpsons Wrestling Recompiled"): "8282",
    ("ps1", "ToyStory2"): "10827",
    ("ps2", "DQ8 Recompiled"): "7226",
    ("ps3", "Dragon Ball Z: Budokai HD"): "11842",
    ("saturn", "Shining Force III Scenario 1"): "185",
    ("saturn", "Shining Force III Scenario 2"): "11177",
    ("saturn", "Shining Force III Scenario 3"): "11178",
    ("switch", "Pokémon Legends: Z-A"): "442130",
    ("switch", "The Legend of Zelda: Link's Awakening (2019)"): "168410",
    ("xbox", "Burnout 2: Point of Impact"): "6010",
    ("xbox", "The Nightmare Before Christmas: Oogie's Revenge"): "13506",
    ("xbox360", "Civilization Revolution"): "13096",
    ("xbox360", "Dead or Alive Extreme 2"): "7173",
    ("xbox360", "Earthworm Jim HD Recompiled"): "94365",
    ("xbox360", "Edge Of Time Recomp"): "8889",
    ("xbox360", "Forza Horizon 1"): "11977",
    ("xbox360", "GoldenEye 007 XBLA PC Recomp"): "199223",
    ("xbox360", "King Kong Recompiled"): "13804",
    ("xbox360", "Perfect Dark Remaster"): "15695",
    ("xbox360", "Resident Evil Code: Veronica"): "16329",
    ("xbox360", "Rockstar Table Tennis"): "15101",
    ("xbox360", "Skate (2007)"): "7579",
    ("xbox360", "The Simpsons Arcade"): "16544",
}


def title_key(title):
    text = unicodedata.normalize("NFKD", title).encode("ascii", "ignore").decode().lower()
    text = text.replace("+", "plus").replace("&", "and")
    text = re.sub(r"^the\s+|,\s*the$", "", text)
    return re.sub(r"[^a-z0-9]", "", text)


def targets():
    result = {(c["Console"], c["Title"]) for c in json.loads((DESKTOP / "Catalogs/covers.json").read_text("utf-8-sig"))}
    for console in PLATFORMS:
        file = DESKTOP / f"Catalogs/{console}.json"
        if file.exists():
            result.update((console, p["Title"]) for p in json.loads(file.read_text("utf-8-sig")))
    result.update((g.get("ShelfConsole", "gamecube"), g["Title"]) for g in json.loads((DESKTOP / "games.json").read_text("utf-8-sig")))
    for p in json.loads((ROOT / "src/CubeShelf.Mobile/Catalogs/android-ports.json").read_text("utf-8-sig")):
        result.add((p.get("ShelfConsole", "n64"), p["Title"]))
    return sorted(result)


def mobile_targets():
    result = {(g.get("ShelfConsole", "gamecube"), g["Title"]) for g in json.loads((DESKTOP / "games.json").read_text("utf-8-sig"))}
    result.update((p.get("ShelfConsole", "n64"), p["Title"]) for p in json.loads((ROOT / "src/CubeShelf.Mobile/Catalogs/android-ports.json").read_text("utf-8-sig")))
    return result


def mobile_resources(records):
    wanted = mobile_targets()
    project = ET.Element("Project")
    group = ET.SubElement(project, "ItemGroup")
    for file in sorted({r["File"] for r in records if (r["Console"], r["Title"]) in wanted}):
        ET.SubElement(group, "AvaloniaResource", Include="../CubeShelf.Desktop/Assets/Logos/" + file, Link="Assets/Logos/" + file)
    ET.indent(project, space="  ")
    (ROOT / "src/CubeShelf.Mobile/LogoAssets.props").write_text(ET.tostring(project, encoding="unicode") + "\n", "utf-8")


def read_index(metadata):
    cache = ROOT / ".tools/game-logo-index.json"
    stamp = hashlib.sha256(metadata.read_bytes()).hexdigest()
    if cache.exists():
        saved = json.loads(cache.read_text("utf-8"))
        if saved["Sha256"] == stamp:
            return saved["Games"]
    games = {}
    with zipfile.ZipFile(metadata) as archive, archive.open("Metadata.xml") as stream:
        depth = 0
        for event, e in ET.iterparse(stream, events=("start", "end")):
            if event == "start":
                depth += 1
                if depth == 1:
                    root = e
                continue
            if depth == 2:
                key = e.findtext("DatabaseID")
                if e.tag == "Game" and e.findtext("Platform") in PLATFORMS.values():
                    games[key] = {"Id": key, "Name": e.findtext("Name"), "Platform": e.findtext("Platform"), "Aliases": [], "Images": []}
                elif e.tag == "GameAlternateName" and key in games:
                    games[key]["Aliases"].append(e.findtext("AlternateName") or "")
                elif e.tag == "GameImage" and key in games and e.findtext("Type") == "Clear Logo":
                    games[key]["Images"].append({"FileName": e.findtext("FileName"), "Region": e.findtext("Region") or ""})
                root.clear()
            depth -= 1
    cache.parent.mkdir(parents=True, exist_ok=True)
    cache.write_text(json.dumps({"Sha256": stamp, "Games": list(games.values())}), "utf-8")
    return list(games.values())


def validate(payload):
    if not payload.startswith(b"\x89PNG\r\n\x1a\n") or len(payload) > 8_000_000:
        raise ValueError("Expected PNG under 8 MB")
    with Image.open(io.BytesIO(payload)) as image:
        w, h = image.size
        if min(w, h) < 32 or w * h > 32_000_000:
            raise ValueError("Invalid logo dimensions")
        image.load()
        minimum, maximum = image.convert("RGBA").getchannel("A").getextrema()
        if minimum == 255 or maximum == 0:
            raise ValueError("Logo must contain visible artwork and transparency")
        return w, h


def download(item):
    console, title, game = item
    errors = []
    for image in sorted(game["Images"], key=lambda i: ({"North America": 0, "Europe": 1, "World": 2, "": 3}.get(i["Region"], 4), i["FileName"])):
        name = image["FileName"]
        if not re.fullmatch(r"(?:r2_)?[a-fA-F0-9-]+\.png", name or ""):
            continue
        url = ("https://gamesdb-images.launchbox.gg/" if name.startswith("r2_") else "https://images.launchbox-app.com/") + name
        file = DEST / name
        try:
            if file.exists():
                payload = file.read_bytes()
            else:
                with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "DecompShelf-artwork-import/1.0"}), timeout=30) as response:
                    if response.headers.get_content_type() != "image/png":
                        raise ValueError("Unexpected content type")
                    payload = response.read(8_000_001)
            width, height = validate(payload)
            if not file.exists():
                temporary = file.with_suffix(f".{threading.get_ident()}.partial")
                temporary.write_bytes(payload)
                temporary.replace(file)
            return {"Console": console, "Title": title, "File": "Database/" + name, "DatabaseID": game["Id"],
                "MatchedTitle": game["Name"], "Platform": game["Platform"], "Region": image["Region"],
                "Source": "LaunchBox Games Database", "ImageUrl": url, "Sha256": hashlib.sha256(payload).hexdigest(),
                "Width": width, "Height": height}, None
        except (OSError, ValueError, http.client.IncompleteRead, Image.DecompressionBombError) as error:
            errors.append(str(error))
    return None, {"Console": console, "Title": title, "Reason": "No valid transparent logo", "Errors": errors}


def write(file, data):
    file.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", "utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("metadata", nargs="?", type=Path)
    parser.add_argument("--match-only", action="store_true")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    assert title_key("The Legend of Zelda: Ocarina of Time") != title_key("The Legend of Zelda: Ocarina of Time 3D")
    assert title_key("Pokémon Stadium") == title_key("Pokemon Stadium")
    assert title_key("Sonic CD++") != title_key("Sonic CD")
    manifest = DEST.parent / "catalog.json"
    report = ROOT / "docs/missing-game-logos.json"
    if args.check:
        records = json.loads(manifest.read_text("utf-8"))
        assert len({(r["Console"], r["Title"]) for r in records}) == len(records)
        for r in records:
            payload = (DEST.parent / r["File"]).read_bytes()
            assert validate(payload) == (r["Width"], r["Height"])
            assert hashlib.sha256(payload).hexdigest() == r["Sha256"]
            assert r["Platform"] == PLATFORMS[r["Console"]]
        missing = json.loads(report.read_text("utf-8"))
        assert {(r["Console"], r["Title"]) for r in records + missing} == set(targets())
        included = {e.attrib["Link"] for e in ET.parse(ROOT / "src/CubeShelf.Mobile/LogoAssets.props").iter("AvaloniaResource")}
        assert included == {"Assets/Logos/" + r["File"] for r in records if (r["Console"], r["Title"]) in mobile_targets()}
        print(f"Verified {len(records)} logos, {len(missing)} explicit fallbacks")
        return
    if not args.metadata:
        parser.error("Metadata.zip is required unless --check is used")
    index = {}
    games = read_index(args.metadata)
    by_id = {g["Id"]: g for g in games}
    for game in games:
        for title in [game["Name"]] + game["Aliases"]:
            index.setdefault((game["Platform"], title_key(title)), {})[game["Id"]] = game
    pending, missing = [], []
    for console, title in targets():
        matches = list(index.get((PLATFORMS.get(console), title_key(title)), {}).values())
        if (console, title) in ALIASES:
            game = by_id.get(ALIASES[console, title])
            matches = [game] if game and game["Platform"] == PLATFORMS[console] else []
        if len(matches) == 1 and matches[0]["Images"]:
            pending.append((console, title, matches[0]))
        else:
            missing.append({"Console": console, "Title": title, "Reason": "Ambiguous match" if len(matches) > 1 else "No clear logo" if matches else "No exact platform/title match", "Candidates": [{"Id": g["Id"], "Name": g["Name"]} for g in matches]})
    print(f"{len(pending)} matched games, {len(missing)} fallbacks", flush=True)
    DEST.mkdir(parents=True, exist_ok=True)
    if args.match_only:
        write(ROOT / ".tools/unmatched-game-logos.json", missing)
        return
    records = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for count, (record, failure) in enumerate(pool.map(download, pending), 1):
            if record:
                records.append(record)
            else:
                missing.append(failure)
            if count % 50 == 0:
                print(f"Processed {count}/{len(pending)}", flush=True)
    write(manifest, records)
    mobile_resources(records)
    write(report, sorted(missing, key=lambda r: (r["Console"], r["Title"])))
    print(f"Imported {len(records)} logos; {len(missing)} titles retain text", flush=True)


if __name__ == "__main__":
    main()
