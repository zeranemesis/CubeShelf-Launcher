@echo off
setlocal
cd /d "%~dp0"
echo CubeShelf v0.6.7 - correctif popup mises a jour
where dotnet >nul 2>&1
if errorlevel 1 ( echo [ERREUR] .NET 8 SDK n'est pas installe. & pause & exit /b 1 )
if exist "src\CubeShelf.Launcher\bin" rmdir /s /q "src\CubeShelf.Launcher\bin"
if exist "src\CubeShelf.Launcher\obj" rmdir /s /q "src\CubeShelf.Launcher\obj"
if exist "src\CubeShelf.Updater\bin" rmdir /s /q "src\CubeShelf.Updater\bin"
if exist "src\CubeShelf.Updater\obj" rmdir /s /q "src\CubeShelf.Updater\obj"
if exist "Release" rmdir /s /q "Release"
mkdir Release
echo [1/4] Restore...
dotnet restore CubeShelfLauncher.sln
if errorlevel 1 goto :fail
echo [2/4] Build CubeShelf...
dotnet publish src\CubeShelf.Launcher\CubeShelf.Launcher.csproj -c Release -r win-x64 --self-contained true -o Release
if errorlevel 1 goto :fail
echo [3/4] Build updater...
dotnet publish src\CubeShelf.Updater\CubeShelf.Updater.csproj -c Release -r win-x64 --self-contained true -o Release\UpdaterTmp
if errorlevel 1 goto :fail
copy /y "Release\UpdaterTmp\CubeShelf.Updater.exe" "Release\CubeShelf.Updater.exe" >nul
rmdir /s /q "Release\UpdaterTmp"
echo [4/4] Verification...
if not exist "Release\CubeShelf.exe" goto :fail
echo OK : Release\CubeShelf.exe
explorer "%cd%\Release"
pause
exit /b 0
:fail
echo [ERREUR] La compilation a echoue.
echo Envoie-moi les dernieres lignes rouges.
pause
exit /b 1
