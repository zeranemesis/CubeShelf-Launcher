# DecompShelf

DecompShelf regroupe des ports natifs de jeux de consoles dans une application Windows et Android. Il propose un accueil par console, une bibliothèque, les téléchargements, les favoris et un mode Big Picture à la manette.

**Version candidate : `0.19.0-rc.1`.** Le numéro utilisé par les compilations est défini dans [VERSION](VERSION). Une compilation réussie ne valide pas la jouabilité de tous les projets tiers. Les essais matériels encore nécessaires sont consignés dans [le plan de finition](docs/FINISHING-PLAN.md).

Aucun disque, ROM, BIOS, firmware, clé ou fichier extrait d'un jeu n'est fourni. Chaque port utilise les fichiers de la copie personnelle du joueur, selon les instructions de son auteur. Les mods destinés uniquement à une ISO émulée ne sont pas ajoutés au catalogue des ports natifs.

## Installer

Les paquets disponibles sont sur [GitHub Releases](https://github.com/zeranemesis/CubeShelf-Launcher/releases). Une version candidate est identifiée comme préversion ; elle n'est pas proposée comme mise à jour stable automatique.

- **Windows x64** : installateur `CubeShelf-Setup-x64.exe` ou archive portable `CubeShelf-win-x64.zip`.
- **Windows ARM64, Linux et macOS** : consulter les architectures et limites dans [les paquets portables](packaging/DESKTOP-PORTABLE.md). Un launcher compilé pour une architecture ne rend pas tous les ports compatibles avec elle.
- **Android ARM64** : APK signé `CubeShelf-Android-arm64-preview.apk`. Le paquet x86-64 vise les appareils ou environnements compatibles ; la disponibilité des moteurs intégrés y diffère.

Les noms techniques CubeShelf, l'identifiant Android `io.github.zeranemesis.cubeshelf` et le profil Windows `%LOCALAPPDATA%\CubeShelf` sont conservés pour permettre les mises à jour et préserver les données. L'installateur Windows utilise `%LOCALAPPDATA%\Programs\CubeShelf`.

Les paquets Windows ne sont pas annoncés comme signés Authenticode lorsqu'aucun certificat de distribution n'est configuré. Android utilise une clé persistante ; une version signée avec une autre clé ne peut pas remplacer directement l'application existante. Ne pas désinstaller une ancienne application sans avoir sauvegardé ses données.

## Choisir une console et jouer

1. Au démarrage, choisir une console. Sur le bureau, des cases présentent les photos des machines et le menu de gauche reste accessible. En Big Picture, les consoles apparaissent dans un carrousel 3D stylisé.
2. Trier les consoles par nom, marque, année de première sortie ou nombre de jeux.
3. Choisir un jeu et une version du port si plusieurs projets existent pour ce titre.
4. Télécharger le paquet compatible lorsqu'il est vérifiable, ou suivre les instructions du projet et associer un programme natif déjà installé.
5. Fournir ses fichiers de jeu dans le parcours prévu par le moteur, puis jouer.

Dans Big Picture, **Retour aux consoles**, **B** ou **Échap** ramènent au carrousel depuis les jeux. **F10 / START** ouvre le menu ; **F11** revient au bureau. Le stick parcourt les jeux et plateformes, la croix les menus ; leur rôle peut être inversé dans les paramètres manette.

Un paquet sans variante compatible avec l'appareil n'est pas installé automatiquement. Les états expérimental, compilation nécessaire et non testé restent distincts de la présence d'un paquet public. Plusieurs ports d'un même jeu se choisissent sur une seule carte.

## Catalogues et illustrations

L'application PC possède 30 catalogues de plateformes, dont Nintendo, Sega, PlayStation, Xbox et Arcade. L'inventaire est documenté mais ne garantit pas l'exhaustivité de tous les ports existants ni leur fonctionnement sur Windows.

[Catalogues et provenance](docs/CONSOLE-CATALOGS.md) · [Inventaire d'origine](docs/NATIVE-PORT-INVENTORY.md) · [Sources des jaquettes](docs/COVER-SOURCES.md).

Les photos et logos des consoles sont inclus dans les paquets et fonctionnent hors connexion. Leurs crédits sont livrés dans `Assets/Consoles/` et `Assets/Brand/Consoles/`. Les jaquettes absentes sont indiquées ; aucune illustration d'un autre jeu ne leur est substituée. Les modèles 3D de consoles restent stylisés et certaines silhouettes simplifiées.

## Android

Le catalogue mobile est distinct du PC : GameCube, Nintendo 64, Game Boy, Game Boy Color, Game Boy Advance et Wii U, uniquement pour les moteurs ou APK Android identifiés.

Mario Party 4 / PartyBoard et Super Mario 64 disposent de moteurs intégrés sur ARM64. Les autres ports du catalogue s'installent comme applications Android séparées. Les confirmations d'installation et d'accès aux fichiers imposées par Android restent nécessaires.

Les APK externes sont contrôlés par identité, taille et SHA-256 ; leur disponibilité ne constitue pas une validation sur téléphone. Les prérequis de version Android, d'architecture et de GPU dépendent du port. The Wind Waker HD exige notamment Android 13 et Vulkan 1.3.

[Installation, signatures, versions et limites Android](packaging/ANDROID.md) · [PartyBoard Android](docs/ANDROID-PARTYBOARD.md).

## Fonctions existantes et limites

- Téléchargements, pause/reprise et vérification des paquets : [guide](DOWNLOADS.md).
- Préparation et lancement des moteurs historiques : [guide de jeu](QUICKPLAY.md).
- Amis et réseau du launcher PC : [fonctionnement et limites](RESEAU.md).
- Mises à jour du launcher : [documentation](AUTO_UPDATE.md).
- Bip est conservé en préversion et reste hors de cette passe de finition : [état séparé de l'assistance](docs/LOCAL-GAME-HELP.md).

Les amis, fonctions réseau et profils de mods PC ne sont pas tous disponibles sur Android. La compatibilité des mods et des parties en ligne dépend du moteur du jeu ; aucun jeu croisé PC/Android n'est annoncé comme validé pour PartyBoard.

## Compiler et vérifier

Prérequis de base : SDK .NET 8. Les étapes natives préparent leurs outils et bibliothèques ; Android nécessite aussi le SDK Android et Java 17.

```powershell
dotnet build CubeShelf.Portable.slnf -c Release
dotnet run --project tests/CubeShelf.Core.Tests -c Release
dotnet run --project tests/CubeShelf.Tests -c Release
dotnet run --project tests/CubeShelf.NativePorts.Tests -c Release
dotnet run --project tests/CubeShelf.ShelfUi.Tests -c Release
dotnet run --project tests/CubeShelf.Mobile.Tests -c Release
dotnet run --project tests/CubeShelf.AppUpdates.Tests -c Release
```

[Compilation et contribution](CONTRIBUTING.md) · [Packaging Windows et autres bureaux](packaging/DESKTOP-PORTABLE.md) · [Packaging Android](packaging/ANDROID.md).

Pour produire l'installateur Windows avec Inno Setup 6 : `./BUILD_INSTALLER.ps1`. Pour publier une candidate, les workflows manuels construisent les paquets sur une même révision ; le workflow de publication vérifie leur provenance, leurs empreintes et la signature persistante des APK avant de publier la préversion.
