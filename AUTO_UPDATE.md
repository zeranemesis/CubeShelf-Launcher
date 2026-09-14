# Mises à jour de CubeShelf

Dans l’interface Avalonia 0.8, CubeShelf consulte la dernière release stable de `zeranemesis/CubeShelf-Launcher` à la demande depuis la page Paramètres.

Une mise à jour téléchargée suit ce parcours : lecture du manifeste v2, sélection stricte de la plateforme, contrôle de taille et SHA-256 du ZIP, lancement de `CubeShelf.Updater.exe`, remplacement transactionnel des fichiers puis redémarrage. Sous Linux, le bouton ouvre la release contenant la nouvelle AppImage.

L’installation automatique est désactivée : l’utilisateur confirme toujours l’application de la mise à jour.

```json
{
  "Launcher": {
    "GitHubOwner": "zeranemesis",
    "GitHubRepo": "CubeShelf-Launcher",
    "ReleaseAssetName": "CubeShelf-win-x64.zip",
    "ChecksumAssetName": "checksums.txt",
    "AutoCheckLauncherUpdates": true,
    "AutoInstallLauncherUpdates": false
  }
}
```

Le workflow de release ne publie une version stable que pour un tag `v*` ou un déclenchement manuel. La signature Authenticode est obligatoire pour une release stable ; une compilation non signée exige une dérogation manuelle explicite.
