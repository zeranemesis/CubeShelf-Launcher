# CubeShelf Launcher

CubeShelf is a native Windows launcher and GameCube library manager.

This repository is intentionally separate from PartyBoard / Mario Party 4.

## Responsibilities

**CubeShelf-Launcher**
- native WPF GameCube library;
- game installation/update UI;
- PartyBoard release download and installation;
- ISO/RVZ selection and local preparation;
- GameBanana mod management;
- download queue;
- launcher self-update.

**Marioparty4 / PartyBoard**
- game port/runtime;
- Windows PartyBoard build;
- `PartyBoard-win-x64.zip` release.

CubeShelf does not contain Nintendo game assets. Users provide their own legally obtained game image when required.

## Windows

The GitHub Actions workflow builds a self-contained Windows x64 release:

- `CubeShelf-win-x64.zip`
- `checksums.txt`

## Current development version

`v0.6.7`
