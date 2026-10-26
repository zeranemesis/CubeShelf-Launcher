"""Check the built APK contract without executing a game or requiring a ROM."""
import argparse
import io
import struct
import zipfile


def verify(path, abi):
    with zipfile.ZipFile(path) as apk:
        names = set(apk.namelist())
        assert apk.testzip() is None
        assert not any(n.lower().endswith((".z64", ".n64", ".v64", ".apk")) for n in names)
        dex = b"".join(apk.read(n) for n in names if n.endswith(".dex"))
        for name in (b"Ldev/net64/ghostship/ExtractionService;", b"Ldev/net64/ghostship/MainActivity;",
                     b"Lio/decompshelf/sdl2/SDLActivity;", b"Lorg/libsdl/app/SDLActivity;"):
            assert name in dex, name
        if abi != "arm64-v8a":
            assert not any(n.endswith("/libghostship.so") for n in names)
            print("Verified: SM64 host included, ARM64-only engine unavailable on x86-64.")
            return
        prefix = "lib/arm64-v8a/"
        assert {prefix + n for n in ("libmain.so", "libdol.so", "libghostship.so", "libSDL2.so", "libc++_shared.so")} <= names
        sdl = apk.read(prefix + "libSDL2.so")
        assert b"io/decompshelf/sdl2/SDLActivity" in sdl
        assert b"org/libsdl/app/SDLActivity" not in sdl
        engine = apk.read(prefix + "libghostship.so")
        assert b"Java_dev_net64_ghostship_GameAssets_nativeGenerateGameArchive" in engine
        assert b"SDL_main" in engine
        for name in ("libghostship.so", "libSDL2.so", "libc++_shared.so"):
            data = apk.read(prefix + name)
            assert data[:6] == b"\x7fELF\x02\x01" and struct.unpack_from("<H", data, 18)[0] == 183
            start = struct.unpack_from("<Q", data, 32)[0]
            size, count = struct.unpack_from("<HH", data, 54)
            for i in range(count):
                kind = struct.unpack_from("<I", data, start + size * i)[0]
                if kind == 1:
                    assert struct.unpack_from("<Q", data, start + size * i + 48)[0] >= 16384, (name, "16 KB pages")
        with zipfile.ZipFile(io.BytesIO(apk.read("assets/ghostship/gamedata.zip"))) as assets:
            assert {"config.yml", "ghostship.o2r"} <= set(assets.namelist())
            assert "sm64.o2r" not in assets.namelist()
        assert "assets/ghostship/gamecontrollerdb.txt" in names
        assert "assets/ghostship/licenses/Ghostship-MIT.txt" in names
        assert "assets/ghostship/licenses/SDL2-zlib.txt" in names
    print("Verified: isolated SDL2/SDL3 hosts, ARM64 native libraries, 16 KB alignment, extractor, data assets and notices; no ROM or nested APK.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("apk")
    parser.add_argument("--abi", required=True, choices=("arm64-v8a", "x86_64"))
    args = parser.parse_args()
    verify(args.apk, args.abi)
