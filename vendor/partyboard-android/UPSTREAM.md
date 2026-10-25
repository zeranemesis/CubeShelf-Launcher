# PartyBoard Android host

Source: https://github.com/zeranemesis/Marioparty4/tree/v0.20.0/platforms/android/app/src/main/java
Commit: `10673868b69bbcdada496215656a5b7012145f5c`.

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
