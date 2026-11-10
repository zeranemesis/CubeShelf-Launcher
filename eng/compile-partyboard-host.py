"""Compile the upstream Java 8 host against the Android API, then let D8 desugar it."""
import argparse
from pathlib import Path
import subprocess
import zipfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--java-home", required=True, type=Path)
parser.add_argument("--sdk", required=True, type=Path)
args = parser.parse_args()
output = root / "src/CubeShelf.Android/Generated/PartyBoard"
classes = output / "classes"
sources = sorted((root / "vendor/partyboard-android/java").rglob("*.java"))
jar = output / "partyboard-host.jar"
if jar.exists() and all(source.stat().st_mtime < jar.stat().st_mtime for source in sources):
    raise SystemExit(0)
classes.mkdir(parents=True, exist_ok=True)
compiler = args.java_home / "bin" / ("javac.exe" if (args.java_home / "bin/javac.exe").exists() else "javac")
subprocess.run([str(compiler), "--release", "8", "-encoding", "UTF-8", "-classpath",
                str(args.sdk / "platforms/android-34/android.jar"), "-d", str(classes),
                *map(str, sources)], check=True)
with zipfile.ZipFile(jar, "w", zipfile.ZIP_DEFLATED) as target:
    for item in classes.rglob("*.class"):
        target.write(item, item.relative_to(classes).as_posix())
