CubeShelf 0.8.0-preview.2

Pour créer l’installateur Windows :
  .\BUILD_INSTALLER.ps1 -Version 0.8.0-preview.2

Résultat :
  dist\CubeShelf-Setup-x64.exe

L’installation ne demande pas les droits administrateur. Le profil reste dans
%LOCALAPPDATA%\CubeShelf et n’est pas supprimé par le désinstalleur.

Consulte README.md, SECURITY.md et SIGNING.md pour les détails.
