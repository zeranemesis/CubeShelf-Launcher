# CubeShelf Launcher

CubeShelf est un launcher et un gestionnaire de bibliothèque GameCube. La préversion 0.8 utilise une interface Avalonia commune à Windows et Linux : elle installe et met à jour PartyBoard, prépare les images fournies légalement par l’utilisateur et gère les mods GameBanana.

CubeShelf ne contient aucun fichier de jeu Nintendo.

## Installation Windows

L’artefact recommandé est `CubeShelf-Setup-x64.exe`. L’installation est effectuée sans droits administrateur dans `%LOCALAPPDATA%\Programs\CubeShelf`, crée une entrée de désinstallation et peut ajouter un raccourci Bureau.

Le profil utilisateur reste dans `%LOCALAPPDATA%\CubeShelf` et est conservé lors d’une mise à jour ou d’une désinstallation.

## Compiler et créer l’installateur

Prérequis : SDK .NET 8 et Inno Setup 6.

```powershell
.\BUILD_INSTALLER.ps1
```

Le résultat est créé dans `dist\CubeShelf-Setup-x64.exe`.

## Aperçu Linux et Steam Deck

Le dépôt contient désormais un noyau portable `CubeShelf.Core`, une interface Avalonia `CubeShelf.Desktop` et un pipeline AppImage `linux-x64`. Cet aperçu affiche la bibliothèque avec des chemins conformes à XDG, permet de sélectionner et valider une image ISO/GCM/RVZ, puis télécharge le runtime PartyBoard correspondant à la plateforme. La taille et le SHA-256 du paquet sont contrôlés avant son activation. L’interface sait ensuite mettre à jour, réparer ou désinstaller ce runtime sans supprimer l’image originale.

La page Mods de l’aperçu charge le catalogue GameBanana, installe les archives dans le profil portable avec l’extracteur sécurisé, gère l’activation et la priorité, puis transmet à PartyBoard la liste ordonnée des mods actifs. Les collisions de fichiers entre mods sont signalées avant le lancement.

La page Téléchargements conserve l’historique et la progression des installations de runtimes et de mods dans le profil utilisateur. Une opération arrêtée par la fermeture de l’application est marquée comme interrompue au prochain démarrage. Le bouton Reprendre réutilise le fichier partiel avec HTTP Range ; si le serveur ne prend pas cette fonction en charge, CubeShelf recommence proprement le paquet concerné.

Avalonia est désormais l’exécutable `CubeShelf.exe` livré par l’installateur Windows. Il comprend l’installation/réparation de PartyBoard, la préparation ISO/GCM/RVZ, la reprise des téléchargements, les paramètres, la mise à jour intégrée Windows, ainsi que la migration non destructive des données de l’ancien launcher WPF.

Les mods peuvent fournir un manifeste `cubeshelf-mod.json` conforme à `schemas/cubeshelf-mod-v1.schema.json`. CubeShelf résout au plus 32 mods et 8 niveaux, refuse les cycles, affiche le plan avant confirmation, prépare tous les paquets puis les active dans une transaction avec rollback.

Publication locale de l’aperçu :

```powershell
dotnet publish src/CubeShelf.Desktop/CubeShelf.Desktop.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64
```

## Sécurité

- vérification SHA-256 des mises à jour CubeShelf et du paquet Dolphin épinglé ;
- limites de taille avant et pendant les téléchargements ;
- extraction d’archives confinée, avec refus des chemins absolus, traversées et doublons ;
- mises à jour automatiques du launcher désactivées par défaut ;
- releases stables signées obligatoirement, sauf dérogation manuelle explicite.
- téléchargement de l’outil AppImage épinglé et vérifié par SHA-256.

## Séparation des responsabilités

- `CubeShelf-Launcher` : interface, bibliothèque, installation, mises à jour et mods ;
- `Marioparty4` / PartyBoard : runtime du jeu et artefact Windows `PartyBoard-win-x64.zip`.

Compatibilité : Windows 10/11 x64 avec installateur, Linux x64/Steam Deck via AppImage, macOS x64/arm64 expérimental sans support PartyBoard garanti. Version actuelle : voir le fichier `VERSION` à la racine du dépôt, qui pilote l’assembly, l’installateur, l’AppImage et les workflows.
