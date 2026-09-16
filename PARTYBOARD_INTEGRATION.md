# PartyBoard integration

Target repository:

`zeranemesis/Marioparty4`

Target branch during current development:

`audio-local`

No patch is needed any more. PartyBoard reads the CubeShelf mod list natively
since `src/port/mods.cpp`; see `docs/CUBESHELF_MODS.md` in that repository.

> The `patches/partyboard_mod_overlay*.patch` files that used to live here
> hooked `src/port/dvd.c`, which is excluded from the build (`files.cmake`).
> The game uses Aurora's DVD layer, so those patches never had any effect and
> have been removed.

## Runtime contract

CubeShelf starts PartyBoard with:

```text
PARTYBOARD_MOD_LIST=<absolute path>/Mods/GMPE01_00/active-mods.txt
```

`active-mods.txt` is rewritten from the installed state at every launch
(`PortableModManager.PrepareActiveList`). Each line is an absolute content root,
sorted **highest priority first**:

```text
C:\CubeShelf\Mods\GMPE01_00\546878\files
C:\CubeShelf\Mods\GMPE01_00\407132\files
```

A content root maps onto the root of the disc image. When the game requests:

```text
/data/board.bin
```

PartyBoard tries each enabled root in order and the first match wins; otherwise
it falls back to the file stored in the disc image. Enabling, disabling and
reordering mods therefore never rewrites the original game data.

The same overlay covers every disc entry point — `DVDOpen()`,
`DVDConvertPathToEntrynum()` + `DVDFastOpen()` (used for `data/*.bin` and for
the `sound/` audio banks), and the async reads — because it is installed in the
FST itself rather than in a single open function.

`PARTYBOARD_DISC_IMAGE` names the copy of the game being started, and therefore
the one the mods were installed against. PartyBoard now boots it in preference to
its own remembered `backend.isoPath`, and skips its pre-launch picker, so a
second disc cannot quietly play unmodded. An online session's disc still wins,
and a path naming no readable file is ignored with a warning.

## Mod packaging

`PortableModManager` unpacks an archive and searches it breadth-first for the
shallowest directory that looks like the disc's file partition: a folder named
`files`, or one already holding a disc entry (`data`, `dll`, `mess`, `movie`,
`sound`, `opening.bnr`). Real packs bury it anywhere from zero to three levels
deep, sometimes beside a `sys/` folder that must not be overlaid, and only the
top-level case used to be found. Everything below the root is overlaid as-is, so
what the game sees mirrors the disc layout:

```text
files/data/board.bin
files/sound/mpgcsnd.msm
```

`cubeshelf-mod.json` (see `schemas/cubeshelf-mod-v1.schema.json`) stays launcher
metadata and is never exposed to the game.

A pack whose content root holds none of the disc's own folders (`data`, `dll`,
`mess`, `movie`, `sound`, `opening.bnr`) overlays nothing and would otherwise
install, enable and do nothing in game with no other symptom - Dolphin-style
texture packs behave exactly this way. `PortableModManager.AnalyzeLayout` reports
those mods in the mods panel; it never blocks an installation, since a mod may
legitimately add files the disc does not carry.

## Checks

```bash
dotnet run --project tests/CubeShelf.Core.Tests/CubeShelf.Core.Tests.csproj -c Release
partyboard --mods-self-test
```
