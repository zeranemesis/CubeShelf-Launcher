"""A newly supported console must invalidate the artwork metadata cache."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import zipfile

spec = importlib.util.spec_from_file_location("logos", Path(__file__).with_name("import-game-logos.py"))
logos = importlib.util.module_from_spec(spec)
spec.loader.exec_module(logos)
with tempfile.TemporaryDirectory(prefix="decompshelf-logo-index-") as directory:
    logos.ROOT = Path(directory)
    metadata = logos.ROOT / "Metadata.zip"
    with zipfile.ZipFile(metadata, "w") as archive:
        archive.writestr("Metadata.xml", "<LaunchBox><Game><DatabaseID>1</DatabaseID><Name>Mario Kart World</Name><Platform>Nintendo Switch 2</Platform></Game></LaunchBox>")
    cache = logos.ROOT / ".tools/game-logo-index.json"
    cache.parent.mkdir()
    cache.write_text(json.dumps({"Sha256": hashlib.sha256(metadata.read_bytes()).hexdigest(), "Games": []}), encoding="utf-8")
    assert logos.read_index(metadata)[0]["Name"] == "Mario Kart World"
    assert logos.read_index(metadata)[0]["Platform"] == "Nintendo Switch 2"
    del logos.PLATFORMS["switch2"]
    assert logos.read_index(metadata) == []
print("Artwork cache tracks supported platforms, including consoles added after the cache was written.")
