from pathlib import Path
import subprocess
import sys

REPO = Path.cwd()
AUDIO = REPO / "src" / "port" / "audio.c"
WORKFLOW = REPO / ".github" / "workflows" / "cubeshelf-windows.yml"

def die(message):
    print(f"\nERREUR: {message}")
    sys.exit(1)

if not (REPO / "CMakeLists.txt").exists() or not AUDIO.exists():
    die("Lance ce script depuis la racine du dépôt Marioparty4.")

try:
    branch = subprocess.check_output(
        ["git", "branch", "--show-current"],
        text=True
    ).strip()
except Exception:
    branch = ""

if branch and branch != "audio-local":
    die(f"Branche actuelle: {branch}. Passe d'abord sur audio-local.")

audio = AUDIO.read_text(encoding="utf-8")

def replace_once(old, new, label):
    global audio
    if new in audio:
        print(f"OK déjà appliqué: {label}")
        return
    if old not in audio:
        die(f"Bloc introuvable pour: {label}. Le fichier a probablement changé.")
    audio = audio.replace(old, new, 1)
    print(f"OK: {label}")

replace_once(
'''#include "game/gamework_data.h"

static int HuSePlay''',
'''#include "game/gamework_data.h"

#ifdef TARGET_PC
#include "port/rollback_audio_bridge.h"
#endif

static int HuSePlay''',
"include rollback_audio_bridge"
)

replace_once(
'''int HuAudFXPlayVolPan(int seId, s16 vol, s16 pan)
{
    MSM_SEPARAM seParam;

    if (omSysExitReq != 0) {
        return 0;
    }
    seParam.flag = MSM_SEPARAM_VOL|MSM_SEPARAM_PAN;
    seParam.vol = vol;
    seParam.pan = pan;
    return HuSePlay(seId, &seParam);
}''',
'''int HuAudFXPlayVolPan(int seId, s16 vol, s16 pan)
{
    MSM_SEPARAM seParam;

    if (omSysExitReq != 0) {
        return 0;
    }
#ifdef TARGET_PC
    if (PartyBoard_RollbackAudioBridgeActive()) {
        return PartyBoard_RollbackAudioBridgePlay2D(seId, vol, pan);
    }
#endif
    seParam.flag = MSM_SEPARAM_VOL|MSM_SEPARAM_PAN;
    seParam.vol = vol;
    seParam.pan = pan;
    return HuSePlay(seId, &seParam);
}''',
"HuAudFXPlayVolPan rollback"
)

replace_once(
'''void HuAudFXStop(int seNo) {
    // msmSeStop(seNo, 0);
}''',
'''void HuAudFXStop(int seNo) {
#ifdef TARGET_PC
    if (PartyBoard_RollbackAudioBridgeIsVirtual(seNo)) {
        PartyBoard_RollbackAudioBridgeStopFX(seNo, 0);
        return;
    }
#endif
    msmSeStop(seNo, 0);
}''',
"HuAudFXStop rollback"
)

replace_once(
'''void HuAudFXFadeOut(int seNo, s32 speed) {
    // msmSeStop(seNo, speed);
}''',
'''void HuAudFXFadeOut(int seNo, s32 speed) {
#ifdef TARGET_PC
    if (PartyBoard_RollbackAudioBridgeIsVirtual(seNo)) {
        PartyBoard_RollbackAudioBridgeStopFX(seNo, speed);
        return;
    }
#endif
    msmSeStop(seNo, speed);
}''',
"HuAudFXFadeOut rollback"
)

replace_once(
'''void HuAudFXPanning(int seNo, s16 pan) {
    MSM_SEPARAM seParam;

    if (omSysExitReq == 0) {
        seParam.flag = MSM_SEPARAM_PAN;
        seParam.pan = pan;
        // msmSeSetParam(seNo, &seParam);
    }
}''',
'''void HuAudFXPanning(int seNo, s16 pan) {
    MSM_SEPARAM seParam;

    if (omSysExitReq == 0) {
#ifdef TARGET_PC
        if (PartyBoard_RollbackAudioBridgeIsVirtual(seNo)) {
            PartyBoard_RollbackAudioBridgeParameter(
                seNo, PARTYBOARD_ROLLBACK_FX_PAN, pan);
            return;
        }
#endif
        seParam.flag = MSM_SEPARAM_PAN;
        seParam.pan = pan;
        msmSeSetParam(seNo, &seParam);
    }
}''',
"HuAudFXPanning rollback"
)

replace_once(
'''s32 HuAudFXStatusGet(int seNo) {
    // return msmSeGetStatus(seNo);
    return 12;
}''',
'''s32 HuAudFXStatusGet(int seNo) {
#ifdef TARGET_PC
    if (PartyBoard_RollbackAudioBridgeIsVirtual(seNo)) {
        return PartyBoard_RollbackAudioBridgeStatus(seNo);
    }
#endif
    return msmSeGetStatus(seNo);
}''',
"HuAudFXStatusGet rollback"
)

replace_once(
'''s32 HuAudFXPitchSet(int seNo, s16 pitch)
{
    MSM_SEPARAM param;
    if(omSysExitReq) {
        return 0;
    }
    param.flag = MSM_SEPARAM_PITCH;
    param.pitch = pitch;
    // return msmSeSetParam(seNo, &param);
    return 12;
}''',
'''s32 HuAudFXPitchSet(int seNo, s16 pitch)
{
    MSM_SEPARAM param;
    if(omSysExitReq) {
        return 0;
    }
#ifdef TARGET_PC
    if (PartyBoard_RollbackAudioBridgeIsVirtual(seNo)) {
        return PartyBoard_RollbackAudioBridgeParameter(
            seNo, PARTYBOARD_ROLLBACK_FX_PITCH, pitch) ? 0 : MSM_ERR_INVALIDSE;
    }
#endif
    param.flag = MSM_SEPARAM_PITCH;
    param.pitch = pitch;
    return msmSeSetParam(seNo, &param);
}''',
"HuAudFXPitchSet rollback"
)

replace_once(
'''s32 HuAudFXVolSet(int seNo, s16 vol)
{
    MSM_SEPARAM param;

    if(omSysExitReq) {
        return 0;
    }
    param.flag = MSM_SEPARAM_VOL;
    param.vol = vol;
    // return msmSeSetParam(seNo, &param);
    return 12;
}''',
'''s32 HuAudFXVolSet(int seNo, s16 vol)
{
    MSM_SEPARAM param;

    if(omSysExitReq) {
        return 0;
    }
#ifdef TARGET_PC
    if (PartyBoard_RollbackAudioBridgeIsVirtual(seNo)) {
        return PartyBoard_RollbackAudioBridgeParameter(
            seNo, PARTYBOARD_ROLLBACK_FX_VOLUME, vol) ? 0 : MSM_ERR_INVALIDSE;
    }
#endif
    param.flag = MSM_SEPARAM_VOL;
    param.vol = vol;
    return msmSeSetParam(seNo, &param);
}''',
"HuAudFXVolSet rollback"
)

portable_stubs = r'''
/*
 * Minimal sound-effect voice shim for the default desktop backend.
 *
 * rollback_audio.cpp talks to the msmSe API after an effect is confirmed.
 * The normal desktop build does not compile src/msm/msmse.c, so these three
 * symbols live here. They only model lifecycle/status; this backend is silent.
 */
#define PORTABLE_SE_VOICE_COUNT 64

typedef struct PortableSeVoice_s {
    s32 handle;
    s32 status;
} PortableSeVoice;

static PortableSeVoice sPortableSeVoices[PORTABLE_SE_VOICE_COUNT];
static s32 sPortableSeNextHandle = 0x1000;

static PortableSeVoice *PortableSeFind(s32 handle)
{
    s32 i;
    for (i = 0; i < PORTABLE_SE_VOICE_COUNT; i++) {
        if (sPortableSeVoices[i].status != MSM_SE_DONE
            && sPortableSeVoices[i].handle == handle) {
            return &sPortableSeVoices[i];
        }
    }
    return NULL;
}

int msmSePlay(int seId, MSM_SEPARAM *param)
{
    s32 i;
    (void)param;

    if (seId < 0 || seId > 0xFFFF) {
        return MSM_ERR_INVALIDID;
    }

    for (i = 0; i < PORTABLE_SE_VOICE_COUNT; i++) {
        if (sPortableSeVoices[i].status == MSM_SE_DONE) {
            if (++sPortableSeNextHandle <= 0) {
                sPortableSeNextHandle = 0x1000;
            }
            sPortableSeVoices[i].handle = sPortableSeNextHandle;
            sPortableSeVoices[i].status = MSM_SE_PLAY;
            return sPortableSeVoices[i].handle;
        }
    }

    return MSM_ERR_CHANLIMIT;
}

s32 msmSeStop(int seNo, s32 speed)
{
    PortableSeVoice *voice;
    (void)speed;

    voice = PortableSeFind(seNo);
    if (voice == NULL) {
        return MSM_ERR_INVALIDSE;
    }

    voice->status = MSM_SE_DONE;
    return 0;
}

s32 msmSeGetStatus(int seNo)
{
    PortableSeVoice *voice = PortableSeFind(seNo);
    return voice != NULL ? voice->status : MSM_SE_DONE;
}

'''

anchor = '''/*
 * PartyBoard's REL modules import these symbols directly from dol.dll.  Keep
 * the silent PC backend self-contained until the MusyX data path is ready.
 */
s32 msmMusGetStatus'''

if portable_stubs.strip() not in audio:
    if anchor not in audio:
        die("Point d'insertion des stubs msmSe introuvable.")
    audio = audio.replace(anchor, portable_stubs + anchor, 1)
    print("OK: backend msmSe portable")
else:
    print("OK déjà appliqué: backend msmSe portable")

AUDIO.write_text(audio, encoding="utf-8")

WORKFLOW.parent.mkdir(parents=True, exist_ok=True)

workflow = r'''name: CubeShelf Windows Release

on:
  push:
    branches:
      - audio-local
    paths-ignore:
      - '*.md'
      - 'docs/**'
  workflow_dispatch:

permissions:
  contents: write

concurrency:
  group: cubeshelf-windows-${{ github.ref }}
  cancel-in-progress: true

jobs:
  windows:
    name: Build and publish PartyBoard Windows
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v6
        with:
          fetch-depth: 0
          submodules: recursive

      - name: Enable Visual Studio environment
        uses: ilammy/msvc-dev-cmd@v1
        with:
          arch: amd64

      - name: Override VCPKG_ROOT
        shell: pwsh
        run: |
          "VCPKG_ROOT=C:\vcpkg" | Out-File -FilePath $env:GITHUB_ENV -Append

      - name: Setup sccache
        uses: mozilla-actions/sccache-action@v0.0.10

      - name: Install dependencies
        shell: pwsh
        run: |
          choco install ninja -y
          vcpkg install freetype:x64-windows-static zstd:x64-windows-static

      - name: Configure CMake
        shell: pwsh
        run: cmake --preset x-windows-ci-msvc

      - name: Build
        shell: pwsh
        run: cmake --build --preset x-windows-ci-msvc

      - name: Validate runtime
        shell: pwsh
        run: |
          $exe = Get-ChildItem -Path "build/install" -Filter "partyboard.exe" -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1

          if (-not $exe) {
            throw "partyboard.exe was not produced under build/install."
          }

          Write-Host "PartyBoard executable: $($exe.FullName)"

      - name: Create CubeShelf package
        shell: pwsh
        run: |
          $ErrorActionPreference = "Stop"

          $zip = "PartyBoard-win-x64.zip"
          if (Test-Path $zip) {
            Remove-Item $zip -Force
          }

          Compress-Archive -Path "build/install/*" -DestinationPath $zip -Force

          $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
          "$hash  $zip" | Set-Content "checksums.txt" -Encoding ascii

          $shortSha = "${{ github.sha }}".Substring(0, 12)
          $changes = (git log -5 --pretty=format:"%h %s") -join "`n"

          $manifest = [ordered]@{
            schema = 1
            game = "Mario Party 4"
            gameId = "GMPE01_00"
            version = "nightly-$shortSha"
            commit = "${{ github.sha }}"
            branch = "${{ github.ref_name }}"
            platform = "win-x64"
            executable = "partyboard.exe"
            supportedDiscs = @(
              "GMPE01_00",
              "GMPE01_01"
            )
            changes = $changes
          } | ConvertTo-Json -Depth 6

          Set-Content "manifest.json" -Value $manifest -Encoding utf8

          @"
          CubeShelf automatic Windows build

          Commit: ${{ github.sha }}
          Branch: ${{ github.ref_name }}

          Recent changes:
          $changes
          "@ | Set-Content "release-notes.txt" -Encoding utf8

      - name: Upload Actions artifact
        uses: actions/upload-artifact@v7
        with:
          name: PartyBoard-win-x64-${{ github.sha }}
          path: |
            PartyBoard-win-x64.zip
            manifest.json
            checksums.txt
            release-notes.txt
          retention-days: 14

      - name: Publish CubeShelf nightly release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $ErrorActionPreference = "Continue"

          gh release view cubeshelf-nightly *> $null
          if ($LASTEXITCODE -eq 0) {
            gh release delete cubeshelf-nightly --yes --cleanup-tag
            if ($LASTEXITCODE -ne 0) {
              throw "Unable to replace existing cubeshelf-nightly release."
            }
          }

          $ErrorActionPreference = "Stop"

          gh release create cubeshelf-nightly `
            "PartyBoard-win-x64.zip" `
            "manifest.json" `
            "checksums.txt" `
            "release-notes.txt" `
            --target "${{ github.sha }}" `
            --title "CubeShelf Nightly - PartyBoard Windows" `
            --notes-file "release-notes.txt" `
            --prerelease
'''

WORKFLOW.write_text(workflow, encoding="utf-8")

print("\nPatch appliqué localement aux fichiers de travail.")
print("Fichier modifié :", AUDIO)
print("Workflow créé   :", WORKFLOW)
