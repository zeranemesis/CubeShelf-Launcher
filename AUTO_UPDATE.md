# CubeShelf automatic updates

CubeShelf is hard-linked by default to:

`zeranemesis/CubeShelf-Launcher`

At application startup:

1. CubeShelf calls GitHub's `releases/latest` endpoint.
2. It compares the latest release tag (`vX.Y.Z`) with its own assembly version.
3. If `AutoInstallLauncherUpdates=true`, the update is downloaded automatically.
4. CubeShelf downloads:
   - `CubeShelf-win-x64.zip`
   - `checksums.txt`
5. SHA-256 is verified.
6. `CubeShelf.Updater.exe` is started.
7. CubeShelf exits.
8. The updater replaces the application files.
9. The new CubeShelf.exe is launched.

Configuration:

```json
{
  "Launcher": {
    "GitHubOwner": "zeranemesis",
    "GitHubRepo": "CubeShelf-Launcher",
    "ReleaseAssetName": "CubeShelf-win-x64.zip",
    "ChecksumAssetName": "checksums.txt",
    "AutoCheckLauncherUpdates": true,
    "AutoInstallLauncherUpdates": true
  }
}
```

GitHub Actions creates the Windows ZIP and checksum when a `v*` tag is pushed.
