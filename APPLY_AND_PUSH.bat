@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo PartyBoard / CubeShelf - Windows CI + Release
echo ============================================================
echo.

if "%~1"=="" (
    echo Utilisation:
    echo APPLY_AND_PUSH.bat "C:\CHEMIN\VERS\Marioparty4"
    echo.
    pause
    exit /b 1
)

set "REPO=%~1"

if not exist "%REPO%\CMakeLists.txt" (
    echo ERREUR: %REPO% ne semble pas etre le depot Marioparty4.
    pause
    exit /b 1
)

copy /Y "%~dp0apply_cubeshelf_partyboard.py" "%REPO%\apply_cubeshelf_partyboard.py" >nul

pushd "%REPO%"

git checkout audio-local
if errorlevel 1 goto :error

git pull --ff-only origin audio-local
if errorlevel 1 goto :error

python apply_cubeshelf_partyboard.py
if errorlevel 1 goto :error

del /Q apply_cubeshelf_partyboard.py >nul 2>&1

git diff --check
if errorlevel 1 goto :error

echo.
echo ----- MODIFICATIONS -----
git status --short
echo.

git add src/port/audio.c .github/workflows/cubeshelf-windows.yml
git commit -m "Publish Windows build for CubeShelf launcher"
if errorlevel 1 (
    echo Aucun nouveau commit cree, verification de l'etat...
    git status --short
)

git push origin audio-local
if errorlevel 1 goto :error

echo.
echo ============================================================
echo PUSH TERMINE.
echo GitHub Actions doit maintenant creer la release:
echo   cubeshelf-nightly
echo avec:
echo   PartyBoard-win-x64.zip
echo   manifest.json
echo   checksums.txt
echo ============================================================
popd
pause
exit /b 0

:error
echo.
echo ERREUR pendant l'application ou le push.
git status --short
popd
pause
exit /b 1
