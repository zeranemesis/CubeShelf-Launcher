# Mises à jour de DecompShelf

Dans l’interface PC, DecompShelf consulte la dernière release stable de `zeranemesis/CubeShelf-Launcher` à la demande depuis la page Paramètres.

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

Les préversions sont exclues des mises à jour stables automatiques ; 0.19.0-rc.1 s’installe manuellement. Android consulte aussi les releases stables et vérifie l’APK avant de demander la confirmation d’installation du système.

Le workflow stable de construction exige Authenticode, sauf dérogation manuelle explicite. La publication des paquets préparés vérifie leur provenance, leurs tailles et leurs empreintes. Aucun certificat Authenticode de distribution n’est actuellement configuré : les paquets Windows de cette candidate ne sont pas annoncés comme signés. Les APK utilisent la signature persistante historique, contrôlée avant publication.
