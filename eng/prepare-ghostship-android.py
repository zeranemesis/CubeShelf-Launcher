"""Package the verified Ghostship engine and rebuild its isolated SDL2 Android bridge."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
APK_HASH = "4560cfba9eac305c856c9f255517fdf740fb455886d69fd2f3a786b1c74f6dad"
RELEASE_HASH = "f33904ab87c7a99c2465fc244baa43ebdf09d433ef60f63e2b1f8be77b5eafde"
SDL_COMMIT = "5d249570393f7a37e037abf22cd6012a4cc56a71"
SDL_HASH = "8f71d910b4fd8eb9b40e2f33752cf17d50070e87d2985c54c6cd3e1da4b35f32"


def verified_download(url, target, expected):
    target.parent.mkdir(parents=True, exist_ok=True)
    if not target.exists():
        temporary = target.with_suffix(".download")
        with urllib.request.urlopen(url, timeout=60) as source, temporary.open("wb") as output:
            shutil.copyfileobj(source, output)
        temporary.replace(target)
    with target.open("rb") as source:
        if hashlib.file_digest(source, "sha256").hexdigest() != expected:
            raise ValueError(f"SHA-256 mismatch: {target.name}")
    return target


def prepare(sdk, java):
    cache = ROOT / "artifacts/ghostship"
    output = ROOT / "src/CubeShelf.Android/Generated/Ghostship"
    native = output / "arm64-v8a"
    assets = output / "assets/ghostship"
    native.mkdir(parents=True, exist_ok=True)
    assets.mkdir(parents=True, exist_ok=True)
    release = verified_download(
        "https://github.com/HarbourMasters/Ghostship/releases/download/3.0.0/Nautilus-Alfa-Android.zip",
        cache / "Nautilus-Alfa-Android.zip", RELEASE_HASH)
    with zipfile.ZipFile(release) as archive:
        apks = [n for n in archive.namelist() if n.endswith(".apk")]
        if len(apks) != 1:
            raise ValueError("Expected one Ghostship APK")
        apk = cache / "ghostship.apk"
        data = archive.read(apks[0])
        if hashlib.sha256(data).hexdigest() != APK_HASH:
            raise ValueError("Ghostship APK SHA-256 mismatch")
        if not apk.exists() or apk.read_bytes() != data:
            apk.write_bytes(data)
    with zipfile.ZipFile(apk) as archive:
        for name in ("libmain.so", "libc++_shared.so"):
            target = native / ("libghostship.so" if name == "libmain.so" else name)
            data = archive.read("lib/arm64-v8a/" + name)
            if not target.exists() or target.read_bytes() != data:
                target.write_bytes(data)
        for name in ("gamedata.zip", "gamedata.version", "gamecontrollerdb.txt"):
            target = assets / name
            data = archive.read("assets/" + name)
            if not target.exists() or target.read_bytes() != data:
                target.write_bytes(data)

    sdl_zip = verified_download(f"https://codeload.github.com/libsdl-org/SDL/zip/{SDL_COMMIT}",
                                cache / "SDL-5d249570.zip", SDL_HASH)
    source = cache / ("SDL-" + SDL_COMMIT)
    if not source.exists():
        with zipfile.ZipFile(sdl_zip) as archive:
            for entry in archive.infolist():
                target = (cache / entry.filename).resolve()
                if not target.is_relative_to(cache.resolve()):
                    raise ValueError("Unsafe SDL source path")
            archive.extractall(cache)
    # PartyBoard owns org.libsdl.app (SDL3). SDL2 must register different JNI classes.
    for relative in ("src/core/android/SDL_android.c", "src/hidapi/android/hid.cpp"):
        file = source / relative
        text = file.read_text(encoding="utf-8")
        patched = text.replace("org_libsdl_app", "io_decompshelf_sdl2").replace("org/libsdl/app", "io/decompshelf/sdl2")
        if text != patched:
            file.write_text(patched, encoding="utf-8")
    cmake_root = max((sdk / "cmake").iterdir(), key=lambda p: tuple(int(n) for n in p.name.split(".")))
    ndk_root = max((sdk / "ndk").iterdir(), key=lambda p: tuple(int(n) for n in p.name.split(".")))
    executable = ".exe" if os.name == "nt" else ""
    host = "windows-x86_64" if os.name == "nt" else "linux-x86_64"
    cmake = str(cmake_root / "bin" / ("cmake" + executable))
    ninja = str(cmake_root / "bin" / ("ninja" + executable))
    build = cache / "sdl-build-arm64"
    stamp = output / "sdl-build.json"
    record = dict(source=SDL_HASH, namespace="io.decompshelf.sdl2", ndk=ndk_root.name, api=26)
    if not (stamp.exists() and json.loads(stamp.read_text()) == record and (native / "libSDL2.so").exists()):
        subprocess.run([cmake, "-S", str(source), "-B", str(build), "-G", "Ninja",
                        "-DCMAKE_MAKE_PROGRAM=" + ninja,
                        "-DCMAKE_TOOLCHAIN_FILE=" + str(ndk_root / "build/cmake/android.toolchain.cmake"),
                        "-DANDROID_ABI=arm64-v8a", "-DANDROID_PLATFORM=android-26", "-DCMAKE_BUILD_TYPE=Release",
                        "-DSDL_SHARED=ON", "-DSDL_STATIC=OFF", "-DSDL_TEST=OFF"], check=True)
        subprocess.run([cmake, "--build", str(build), "--target", "SDL2", "--parallel", "4"], check=True)
        shutil.copyfile(build / "libSDL2.so", native / "libSDL2.so")
        stamp.write_text(json.dumps(record))
    subprocess.run([str(ndk_root / "toolchains/llvm/prebuilt" / host / "bin" / ("llvm-strip" + executable)),
                    "--strip-unneeded", str(native / "libSDL2.so")], check=True)
    shutil.copyfile(ndk_root / "NOTICE.toolchain", assets / "LLVM-NOTICE.txt")

    java_sources = output / "java/io/decompshelf/sdl2"
    java_sources.mkdir(parents=True, exist_ok=True)
    for file in (source / "android-project/app/src/main/java/org/libsdl/app").glob("*.java"):
        text = file.read_text(encoding="utf-8").replace("org.libsdl.app", "io.decompshelf.sdl2")
        (java_sources / file.name).write_text("// DecompShelf: relocated SDL2 host; original notice retained.\n" + text, encoding="utf-8")
    classes = output / "classes"
    classes.mkdir(exist_ok=True)
    sources = list(java_sources.glob("*.java")) + list((ROOT / "vendor/ghostship-android/java").rglob("*.java"))
    subprocess.run([str(java / "bin" / ("javac" + executable)), "--release", "8", "-encoding", "UTF-8",
                    "-classpath", str(sdk / "platforms/android-34/android.jar"), "-d", str(classes),
                    *map(str, sources)], check=True)
    with zipfile.ZipFile(output / "ghostship-host.jar", "w", zipfile.ZIP_DEFLATED) as archive:
        for file in classes.rglob("*.class"):
            archive.write(file, file.relative_to(classes).as_posix())
    print("Ghostship 3.0.0: verified ARM64 engine, assets and isolated SDL2 host ready")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--sdk", type=Path, required=True)
    parser.add_argument("--java-home", type=Path, required=True)
    args = parser.parse_args()
    prepare(args.sdk, args.java_home)
