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

`active-mods.json` sits next to it, same order, carrying the identity the game
cannot get from a path - id, name, priority, content root. The game names mods on
screen from it, and the plain text file remains the contract for anything that
only needs the roots.

`player-disabled.json` goes the other way: PartyBoard writes the ids the player
switched off from inside the game, and the launcher only reads them. **A mod runs
when the launcher has it enabled and the player has not switched it off.**
Enabling a mod in the launcher clears its in-game switch, so the panel can never
claim a mod is on while the game ignores it.

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

The online companion (`PartyBoardOnline.exe`) receives the same
`PARTYBOARD_MOD_LIST` when CubeShelf opens it for an invitation, exactly as it
inherits it when the game opens it. It reads `installed.json` beside the list to
offer every installed mod in its *Mods…* panel, for that salon only, and never
writes back.

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
install, enable and do nothing in game with no other symptom.
`PortableModManager.AnalyzeLayout` reports those mods in the mods panel and says
which of three situations it is, because each needs a different answer:

- **Dolphin texture pack** (`<GameId>/tex1_*.png`) - replaced at render time, not
  on the disc. No overlay can carry it.
- **Loose files** - PartyBoard places them itself when the disc carries exactly
  one file of that name; see `docs/CUBESHELF_MODS.md` in the game repository.
- **Several variants side by side** - the player keeps the one they want.

Measured over the fourteen packs GameBanana lists for Mario Party 4: eight are
laid out like the disc, three are Dolphin texture packs, two ship loose files and
one offers variants. Nothing is ever blocked from installing, since a mod may
legitimately add files the disc does not carry.

## In-game friends bridge

When CubeShelf starts the game it sets `CUBESHELF_INGAME_DIR` (UTF-8 path). Three things live
there, all plain files, no network:

| File | Writer | Content |
| --- | --- | --- |
| `state.json` | CubeShelf, every 3 s while the game runs | Friends, their status line (availability and what their game says included), invitations, the F1 tab's strings |
| `requests/<id>.json`, `<id>.done` | the game, then CubeShelf | Host, invite, join, cancel; a request older than 30 s is dropped unanswered |
| `activity.json` | the game | `{"schema":1,"text":"…","updatedAt":<unix seconds>}` -- one line about what is happening |

`activity.json` is what friends see beside "Mario Party 4": the RetroAchievements rich presence
while a set is played, otherwise the board and turn, a minigame, or the menus. The game rewrites
it every few seconds while it changes and every 30 s otherwise; CubeShelf ignores one older than
90 s, cleans it to one line of at most 120 characters, and publishes it only with the current game
(same sharing switch). See `src/port/ui/cubeshelf.cpp` and `src/port/game_activity.cpp` in the
game repository.

## Checks

```bash
dotnet run --project tests/CubeShelf.Core.Tests/CubeShelf.Core.Tests.csproj -c Release
partyboard --mods-self-test
```
