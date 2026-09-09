@echo off
setlocal
cd /d "%~dp0"

if not exist "Release\CubeShelf.exe" (
  echo CubeShelf.exe n'existe pas. Lance BUILD_WINDOWS.bat d'abord.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "if(Test-Path 'CubeShelf-win-x64.zip'){Remove-Item 'CubeShelf-win-x64.zip' -Force}; Compress-Archive -Path 'Release\*' -DestinationPath 'CubeShelf-win-x64.zip'; $h=(Get-FileHash 'CubeShelf-win-x64.zip' -Algorithm SHA256).Hash.ToLower(); Set-Content -Path 'checksums.txt' -Value ($h + '  CubeShelf-win-x64.zip') -Encoding Ascii"

echo.
echo Cree :
echo   CubeShelf-win-x64.zip
echo   checksums.txt
echo.
pause
