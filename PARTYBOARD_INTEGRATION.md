# PartyBoard integration

Target repository:

`zeranemesis/Marioparty4`

Target branch during current development:

`audio-local`

## Patch

Apply:

```powershell
git apply patches/partyboard_mod_overlay_v0.2.patch
```

The actual patch file lives in the CubeShelf repository, so either copy it into
the MarioParty4 checkout or run `git apply` with its full path.

## Runtime contract

CubeShelf starts PartyBoard with:

```text
PARTYBOARD_MOD_LIST=<absolute path>/Mods/GMPE01_00/active-mods.txt
PARTYBOARD_GAME_ROOT=<absolute path>/Marioparty4/GMPE01_00
```

Each line of `active-mods.txt` is an absolute content root. The list is sorted
highest priority first.

Example:

```text
C:\CubeShelf\Mods\GMPE01_00\546878\files
C:\CubeShelf\Mods\GMPE01_00\407132\files
```

When the game requests:

```text
data/texture/example.tpl
```

the patched `DVDOpen()` tries each enabled mod root first and finally falls back
to the original current working directory under `GMPE01_00/files`.

This means enable/disable and load-order changes require no rewriting of the
original game files.
