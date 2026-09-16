# CubeShelf Launcher

CubeShelf est un launcher et un gestionnaire de bibliothèque GameCube. La préversion 0.8 utilise une interface Avalonia commune à Windows et Linux : elle installe et met à jour les runtimes des jeux portés sur PC (PartyBoard pour Mario Party 4, Ring Out pour Soulcalibur II), prépare les images fournies légalement par l’utilisateur et gère les mods GameBanana.

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

## Jeux pris en charge

Le catalogue est piloté par les données : `src/CubeShelf.Launcher/games.json` décrit chaque jeu, son runtime, les révisions de disque acceptées et la façon dont le runtime est récupéré. Ajouter un jeu ne demande pas de code.

| Jeu | Runtime | Disques acceptés | Acquisition | Préparation du disque |
| --- | --- | --- | --- | --- |
| Mario Party 4 | PartyBoard | `GMPE01_00`, `GMPE01_01` | manifeste CubeShelf | par CubeShelf |
| Soulcalibur II | [Ring Out](https://github.com/jackpoison-prog/RingOut) | `GRSEAF`, `GRSPAF`, `GRSJAF`, `GRSEPS` | asset de release GitHub | par le runtime |
| Super Mario Strikers | [Strikers](https://github.com/new-coke/strikers) | `G4QE01`, `G4QP01`, `G4QJ01` | asset de release GitHub | par le runtime |

Une entrée de `SupportedDiscIds` est soit un identifiant de révision complet (`GMPE01_00`, cette révision uniquement), soit un identifiant de disque sur six caractères (`GRSEAF`, toutes les révisions).

### Deux modes d’acquisition du runtime

`RuntimeSource` vaut `Manifest` ou `GitHubReleaseAsset`.

En `Manifest`, l’éditeur publie un `manifest.json` à côté de ses assets, qui nomme l’artefact par plateforme avec sa taille et son SHA-256. C’est ce que fait PartyBoard, et c’est le mode à privilégier : le téléchargement est comparé à une valeur publiée par l’éditeur.

En `GitHubReleaseAsset`, l’éditeur ne publie que des archives ordinaires. CubeShelf interroge l’API GitHub pour la dernière release et sélectionne l’asset via `RuntimeAssets`, dont le motif accepte un joker parce que la version figure dans le nom du fichier. `RuntimeLaunchPaths` indique l’exécutable à lancer dans l’archive, par plateforme, avec le jeton `{version}`.

### Vérification du téléchargement

CubeShelf cherche de quoi contrôler l’archive, dans cet ordre :

1. **Un fichier de checksums publié dans la même release**, nommé par `RuntimeChecksumAsset` et lu au format `sha256sum`. C’est la meilleure preuve disponible : elle vient de l’éditeur et suit chaque version. Strikers publie `SHA256SUMS` — ses installations sont donc vérifiées.
2. **Un SHA-256 épinglé** dans `RuntimeSha256`, qui fige une version précise et devient obsolète à la release suivante.
3. **Rien.** Ring Out ne publie ni manifeste ni checksum : l’archive reste plafonnée en taille et hachée pendant le transfert, mais aucune valeur de l’éditeur ne permet de confirmer le résultat.

Dans ce dernier cas l’installation n’est pas bloquée, elle est **enregistrée comme non vérifiée** : le runtime porte la mention « installé sans vérification » dans la fiche du jeu, et l’état est conservé dans `runtime-state.json` — l’information survit à l’installation qui l’a produite.

### Préparation du disque

`DataPreparation` vaut `CubeShelf` ou `Runtime`.

PartyBoard attend que CubeShelf extraie le disque (conversion RVZ via DolphinTool, puis l’arborescence). Ring Out fait l’inverse : il extrait et **recompile** le jeu depuis le disque à son premier lancement, ce que CubeShelf ne peut ni ne doit refaire. Pour ces jeux, le bouton « Préparer les données » n’est pas proposé et Jouer est disponible dès que le runtime est installé et le disque compatible.

Ring Out cible Windows x64 et Linux x86-64. Prévois plusieurs minutes et environ 1,5 Go d’espace libre au premier lancement.

### Jaquettes

Les jaquettes vivent dans `src/CubeShelf.Launcher/Assets/Covers/<Jeu>/`, en trois fichiers : `pal_front.png`, `pal_back.png` et `pal_spine.png`. Pour en remplacer une, écrase ces fichiers sans toucher au catalogue. Une jaquette absente n’est pas une erreur, la fiche s’affiche sans image. Les jaquettes complètes (dos + tranche + face) se découpent aux proportions GameCube standard : dos 900, tranche 120, face 900 sur 1920.

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
- installation issue d’un éditeur sans manifeste ni checksum marquée comme non vérifiée et tracée dans l’état du runtime ;
- releases stables signées obligatoirement, sauf dérogation manuelle explicite.
- téléchargement de l’outil AppImage épinglé et vérifié par SHA-256.

## Séparation des responsabilités

- `CubeShelf-Launcher` : interface, bibliothèque, installation, mises à jour et mods ;
- `Marioparty4` / PartyBoard : runtime du jeu et artefact Windows `PartyBoard-win-x64.zip`.

Compatibilité : Windows 10/11 x64 avec installateur, Linux x64/Steam Deck via AppImage, macOS x64/arm64 expérimental sans support PartyBoard garanti. Version actuelle : voir le fichier `VERSION` à la racine du dépôt, qui pilote l’assembly, l’installateur, l’AppImage et les workflows.
