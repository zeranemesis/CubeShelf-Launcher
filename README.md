# DecompShelf

DecompShelf regroupe des ports natifs de jeux de consoles dans une application Windows et Android. Il propose un accueil par console, une bibliothèque, les téléchargements, les favoris et un mode Big Picture à la manette.

Deux présentations sont disponibles : **Classique** et **Double écran · HOME**. Le choix se trouve dans les paramètres sur PC et Android (également accessibles par Téléchargements sur mobile). HOME propose un grand aperçu du jeu avec son boîtier 3D, une grille d'icônes, et deux apparences sobres dans **Personnaliser → Affichage**. Le même menu permet de couper la musique ou les effets et d'associer une piste locale au jeu. Android peut afficher la grille sur un second écran compatible : voir [l'adaptation AYN Thor et ses limites](docs/IIDB-THEME-AND-AYN-THOR.md). Voir aussi [les détails du thème](docs/HOME-THEME-2026-11-03.md).

**X** ouvre les [options du jeu sélectionné](docs/GAME-OPTIONS-CONTROLLER.md) dans Big Picture sur PC, ou avec une manette sur Android. Windows donne accès aux paramètres et aux mods existants ; la gestion des mods Android reste indisponible et est signalée dans le menu.

Le [bouton RetroAchievements](docs/RETROACHIEVEMENTS-MENU.md), sous les jeux dans Big Picture et sur Android, affiche uniquement les ports natifs dont la compatibilité est confirmée.

Les [logos des jeux](docs/GAME-LOGOS-2026-11-04.md) proviennent de LaunchBox Games Database : 555 associations console/titre disponibles hors ligne sur PC ; Android embarque seulement les logos utiles à son catalogue compatible. Un titre lisible est conservé lorsqu'aucune correspondance fiable n'est disponible.

Les [lancements et mises à jour sans forks](docs/NATIVE-UPDATES-WITHOUT-FORKS.md) utilisent les paquets officiels vérifiés, conservent les anciens moteurs Windows et préparent les révisions Android compatibles avec les adaptateurs de l'application. Après les [ajouts du 5 novembre](docs/NATIVE-PORTS-2026-11-05.md) et l'intégration de [Power Moon et Recharged](docs/USER-PROJECTS-2026-11-05.md), les catalogues comptent **664 références PC et 28 ports Android**, sans ajouter d'émulateur.

Sur PC, Big Picture conserve un bandeau **Consoles · Mes jeux · Menu · Quitter Big Picture** dans les deux thèmes et sur les pages de gestion. **Menu (F10 / START)** regroupe bibliothèque, options du jeu sélectionné et gestion ; les réglages d'affichage/audio et de manette se trouvent dans **Paramètres**. **B / Échap** remonte d'un niveau. Le bouton de sortie et **F11** restaurent la fenêtre de bureau et ses dimensions, sans fermer DecompShelf.

**Version candidate : `0.19.0-rc.3`.** Le numéro utilisé par les compilations est défini dans [VERSION](VERSION). Cette candidate corrige le plantage constaté au retour d’un jeu. Une compilation réussie ne valide pas la jouabilité de tous les projets tiers. Les essais matériels encore nécessaires sont consignés dans [le plan de finition](docs/FINISHING-PLAN.md).

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

**Mes jeux** rassemble les jeux installés ou déjà ajoutés, toutes consoles confondues, avec leurs jaquettes et le logo de leur plateforme. L'accès se trouve dans le menu latéral et sur l'accueil des consoles. En Big Picture : bouton « Mes jeux », X ou F2 depuis l'accueil, ou menu principal. Le **Catalogue des ports** conserve les projets disponibles à installer. Un jeu externe doit être ajouté ou associé une fois ; les fichiers du PC ne sont pas parcourus automatiquement.

L'application PC possède 30 catalogues de plateformes, dont Nintendo, Sega, PlayStation, Xbox et Arcade. L'inventaire est documenté mais ne garantit pas l'exhaustivité de tous les ports existants ni leur fonctionnement sur Windows.

[Catalogues et provenance](docs/CONSOLE-CATALOGS.md) · [Inventaire d'origine](docs/NATIVE-PORT-INVENTORY.md) · [Sources des jaquettes](docs/COVER-SOURCES.md).

[Actualisation Windows du 1er novembre 2026](docs/WINDOWS-CATALOG-2026-11-01.md) : 218 références supplémentaires et 138 jaquettes ; aucun port natif Windows Switch 2 vérifié dans cette passe. La présence d'un paquet ne vaut pas validation de sa jouabilité.

Les photos et logos des consoles sont inclus dans les paquets et fonctionnent hors connexion. Leurs crédits sont livrés dans `Assets/Consoles/` et `Assets/Brand/Consoles/`. Les jaquettes absentes sont indiquées ; aucune illustration d'un autre jeu ne leur est substituée. Les modèles 3D de consoles restent stylisés et certaines silhouettes simplifiées.

## Android

La gestion locale du stockage et des sauvegardes est disponible dans **Réglages → Stockage et sauvegardes** sur PC et **Téléchargements → Stockage et sauvegardes** sur Android. Elle propose les tailles par jeu, des copies locales, l'export/import ZIP et un nettoyage protégé. [Fonctionnement et limites](docs/STORAGE-AND-SAVES-2026-11-03.md).

Le catalogue mobile est distinct du PC : 25 références sur neuf consoles, uniquement pour les moteurs Android identifiés.

Mario Party 4 / PartyBoard et Super Mario 64 disposent de moteurs intégrés sur ARM64. Les 23 autres moteurs se téléchargent à la demande et se préparent dans les fichiers privés de DecompShelf, sans installer d'application de jeu séparée. Cet hébergement est expérimental et nécessite Android 9 minimum, ainsi que les prérequis propres au port. Le sélecteur de fichiers Android reste nécessaire pour importer sa propre copie du jeu.

Les paquets des moteurs et leurs adaptateurs sont contrôlés par identité, taille et SHA-256 ; leur disponibilité ne constitue pas une validation en partie sur téléphone. Les prérequis de version Android, d'architecture et de GPU dépendent du port. The Wind Waker HD exige notamment Android 13 et Vulkan 1.3. Les anciennes applications et leurs sauvegardes sont conservées ; elles ne sont pas migrées automatiquement.

[Installation, signatures, versions et limites Android](packaging/ANDROID.md) · [Moteurs hébergés](docs/ANDROID-NATIVE-HOST.md) · [PartyBoard Android](docs/ANDROID-PARTYBOARD.md).

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
