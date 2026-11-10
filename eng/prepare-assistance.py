"""Build the pinned, offline-only llama.cpp bridge (Windows and Android). No model in packages."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
COMMIT = "8ca4060b7e30e76baaa5670ee1ec1b760b7f8552"
SHA256 = "86b048943e2ce79d93d310bcff38bf2b1298cf3129e66ba11a129a6ec9a5e189"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--android-sdk", type=Path)
    parser.add_argument("--abi", default="arm64-v8a", choices=["arm64-v8a", "x86_64"])
    parser.add_argument("--arch", default="x64", choices=["x64", "ARM64"])
    args = parser.parse_args()
    cache = ROOT / ".tools/assistant"
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / "llama.zip"
    if not archive.exists() or hashlib.sha256(archive.read_bytes()).hexdigest() != SHA256:
        urllib.request.urlretrieve(f"https://codeload.github.com/ggml-org/llama.cpp/zip/{COMMIT}", archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != SHA256:
        raise ValueError("llama.cpp source hash mismatch")
    source = cache / ("llama.cpp-" + COMMIT)
    if not source.exists():
        with zipfile.ZipFile(archive) as package:
            for entry in package.infolist():
                if not (cache / entry.filename).resolve().is_relative_to(cache.resolve()):
                    raise ValueError("Unsafe source archive")
            package.extractall(cache)
    if args.android_sdk:
        sdk = args.android_sdk.resolve()
        cmake_home = max((sdk / "cmake").iterdir(), key=lambda p: tuple(map(int, p.name.split("."))))
        ndk = max((sdk / "ndk").iterdir(), key=lambda p: tuple(map(int, p.name.split("."))))
        suffix = ".exe" if os.name == "nt" else ""
        cmake = str(cmake_home / "bin" / ("cmake" + suffix))
        build = cache / ("android-" + args.abi)
        output = ROOT / "src/CubeShelf.Android/Generated/Assistance" / args.abi
        options = ["-G", "Ninja", "-DCMAKE_MAKE_PROGRAM=" + str(cmake_home / "bin" / ("ninja" + suffix)),
                   "-DCMAKE_TOOLCHAIN_FILE=" + str(ndk / "build/cmake/android.toolchain.cmake"),
                   "-DANDROID_ABI=" + args.abi, "-DANDROID_PLATFORM=android-26", "-DANDROID_STL=c++_static"]
    else:
        cmake = shutil.which("cmake") or "cmake"
        build = cache / ("windows-" + args.arch)
        output = ROOT / "src/CubeShelf.Desktop/Generated/Assistance" / args.arch
        options = ["-A", args.arch]
        if args.arch == "ARM64":
            # MSVC requires host probing; clang-cl supports a portable ARM64 cross-build.
            options += ["-T", "ClangCL", "-DGGML_CPU_ARM_ARCH=armv8-a"]
    subprocess.run([cmake, "-S", str(ROOT / "native/assistance"), "-B", str(build),
                    "-DLLAMA_SOURCE=" + str(source), "-DCMAKE_BUILD_TYPE=Release", *options], check=True)
    subprocess.run([cmake, "--build", str(build), "--config", "Release", "--target", "decompshelf_ai", "-j", "4"], check=True)
    subprocess.run([cmake, "--install", str(build), "--config", "Release", "--prefix", str(output), "--component", "DecompShelf"], check=True)
    output.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source / "LICENSE", output / "llama.cpp-LICENSE.txt")


if __name__ == "__main__":
    main()
