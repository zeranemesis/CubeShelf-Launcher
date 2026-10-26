# Ghostship inside DecompShelf

This is a small embedded Android host, not another installable Ghostship APK.
The original Ghostship C++ engine is unmodified; its file is named
`libghostship.so` to coexist with PartyBoard's `libmain.so`. Both games run in
separate private processes. The original ELF SONAME remains `libmain.so`;
the Ghostship process loads only `libghostship.so`, not PartyBoard's engine.

## Pinned upstreams

- Ghostship 3.0.0: https://github.com/HarbourMasters/Ghostship/tree/deb091c29caff57dd986398087b058f16b11911e
- Release `Nautilus-Alfa-Android.zip`: SHA-256 `f33904ab87c7a99c2465fc244baa43ebdf09d433ef60f63e2b1f8be77b5eafde`.
- Contained APK: SHA-256 `4560cfba9eac305c856c9f255517fdf740fb455886d69fd2f3a786b1c74f6dad`.
- SDL 2.32.10: https://github.com/libsdl-org/SDL/tree/5d249570393f7a37e037abf22cd6012a4cc56a71

`eng/prepare-ghostship-android.py` verifies both release hashes at build time,
copies only native libraries and the engine's preparation assets, and builds
SDL2 from its pinned source. SDL2 Java classes and JNI registrations are
relocated from `org.libsdl.app` to `io.decompshelf.sdl2`; PartyBoard keeps its
SDL3 classes. The generated bridge sources and build inputs remain under
`artifacts/ghostship` and `src/CubeShelf.Android/Generated/Ghostship`.
The derivative Java host here is MIT licensed under the Ghostship notice;
the SDL2 relocation retains SDL's zlib license and is marked as modified.

The extractor's original JNI class/method name is retained in `GameAssets`.
Extraction runs in `:sm64_prepare`, because upstream Torch intentionally retains
its parsed data; the process ends after the result is delivered. No engine code
is downloaded or installed at runtime. Only the user's own US/JP big-endian
`.z64` ROM is accepted by SHA-1; there is no ROM or generated `sm64.o2r` in the APK.

## Runtime directories

All are below DecompShelf's `files/native-games/sm64/`:

- `baserom.us.z64`: validated user import, replaced only after validation.
- `extracting-<attempt>`: isolated temporary recipes and extraction output.
- `current/`: committed engine assets and generated game archive.
- `saves/`: saves, outside staging/replacement directories.

The game activity points SDL's app directory at `current/` and
`GHOSTSHIP_SAVES_DIR` at `saves/`. It retains Ghostship's settings and touch
controls, adds a return button and exits only its own process on close.
Standalone Ghostship installations and their saves are not modified or imported.

Build: Android SDK 34, CMake 3.22.1, NDK 29.0.14206865, JDK 17, .NET 8 Android.
The integrated engine is ARM64 only; x86-64 reports it unavailable.
`eng/test-ghostship-package.py` checks packaged JNI hosts, ELF architecture and
16 KB alignment, native extractor, assets and absence of ROMs/nested APKs.
Managed tests cover rejection/cancellation, staging, transactional readiness,
save preservation and the in-library preparation flow. Gameplay and the native
service lifecycle still require a physical ARM64 device and a user's ROM.
