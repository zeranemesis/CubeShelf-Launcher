# PartyBoard Android host

Source: https://github.com/zeranemesis/Marioparty4/tree/v0.21.1/platforms/android/app/src/main/java
Commit: `e4c51af88cbede515591b2cd891bb4d8831bdcf0`.

SDL's original notices are retained. The engine is distributed by the upstream
project; `eng/prepare-partyboard-android.py` verifies the published APK's SHA256
before extracting the native libraries for packaging in CubeShelf. Executable
libraries are installed with CubeShelf, never loaded from runtime downloads.

`eng/import-partyboard-android.py` reproduces the Java import and marks alterations:

- Settings, saves, resources and disc preparation use `files/partyboard`.
- Game restarts/online transitions only target the `:partyboard` process.
- The phone host replaces the separate Quest implementation.
- The account-link dialog accepts the PC's CSL1 code without requiring a Google
  scanner dependency. Automatic QR scanning is not included in this host.
- Upstream APK update checks are suppressed: CubeShelf distributes engine updates.

`RuntimePaths.java` and the phone `QuestVr.java` adapter are CubeShelf additions.
Neither the proprietary game disc nor extracted disc data is distributed here.

`DualScreen.java` (bottom-screen board map and settings) is part of v0.21.1 and
imported unchanged. With an engine that lacks its native side the second screen
is left alone.
