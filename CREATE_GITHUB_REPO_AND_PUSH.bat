@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "REPO=zeranemesis/CubeShelf-Launcher"
set "VERSION=v0.6.7"

echo ============================================================
echo CubeShelf Launcher - creation du depot GitHub + premier push
echo ============================================================
echo Depot   : %REPO%
echo Version : %VERSION%
echo.

where git >nul 2>&1
if errorlevel 1 (
  echo [ERREUR] Git n'est pas installe.
  echo Installe-le avec :
  echo   winget install --id Git.Git -e
  pause
  exit /b 1
)

where gh >nul 2>&1
if errorlevel 1 (
  echo GitHub CLI n'est pas installe.
  echo Installation via winget...
  winget install --id GitHub.cli -e --accept-package-agreements --accept-source-agreements
  if errorlevel 1 (
    echo [ERREUR] Impossible d'installer GitHub CLI.
    pause
    exit /b 1
  )

  echo.
  echo IMPORTANT : ferme puis relance ce BAT si la commande gh
  echo n'est toujours pas reconnue apres l'installation.
  echo.
)

where gh >nul 2>&1
if errorlevel 1 (
  echo [ERREUR] La commande gh n'est pas encore disponible.
  echo Ferme cette fenetre et relance CREATE_GITHUB_REPO_AND_PUSH.bat.
  pause
  exit /b 1
)

gh auth status >nul 2>&1
if errorlevel 1 (
  echo.
  echo Connexion a GitHub necessaire.
  echo Choisis GitHub.com puis HTTPS et connecte-toi dans le navigateur.
  echo.
  gh auth login
  if errorlevel 1 goto :fail
)

echo.
echo Verification du compte GitHub...
gh api user --jq ".login"
if errorlevel 1 goto :fail

if exist ".git" (
  echo Un depot Git local existe deja.
) else (
  echo Initialisation Git locale...
  git init
  if errorlevel 1 goto :fail
)

git config user.name >nul 2>&1
if errorlevel 1 git config user.name "Valentin SAMSON"

git config user.email >nul 2>&1
if errorlevel 1 git config user.email "zeranemesis@users.noreply.github.com"

git branch -M main

echo.
echo Ajout des fichiers CubeShelf v0.6.7...
git add .
if errorlevel 1 goto :fail

git diff --cached --quiet
if errorlevel 1 (
  git commit -m "CubeShelf Launcher v0.6.7"
  if errorlevel 1 goto :fail
) else (
  echo Aucun nouveau fichier a committer.
)

echo.
gh repo view %REPO% >nul 2>&1
if errorlevel 1 (
  echo Creation du depot public %REPO%...
  gh repo create %REPO% ^
    --public ^
    --description "Native Windows GameCube launcher with PartyBoard integration, GameBanana mods and automatic updates" ^
    --source=. ^
    --remote=origin ^
    --push
  if errorlevel 1 goto :fail
) else (
  echo Le depot existe deja : %REPO%
  git remote get-url origin >nul 2>&1
  if errorlevel 1 (
    git remote add origin https://github.com/%REPO%.git
  )
  git push -u origin main
  if errorlevel 1 goto :fail
)

echo.
echo Creation du tag %VERSION% pour produire la Release Windows...
git tag -f %VERSION%
if errorlevel 1 goto :fail

git push origin %VERSION% --force
if errorlevel 1 goto :fail

echo.
echo ============================================================
echo TERMINE
echo ============================================================
echo.
echo Repository :
echo   https://github.com/%REPO%
echo.
echo GitHub Actions va compiler CubeShelf puis publier :
echo   CubeShelf-win-x64.zip
echo   checksums.txt
echo.
echo Release attendue :
echo   %VERSION%
echo.
start https://github.com/%REPO%
start https://github.com/%REPO%/actions
echo.
pause
exit /b 0

:fail
echo.
echo ============================================================
echo ECHEC
echo ============================================================
echo Envoie-moi les lignes d'erreur affichees au-dessus.
echo.
git status --short
pause
exit /b 1
