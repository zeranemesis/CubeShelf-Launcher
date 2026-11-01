# Catalogue Windows — contrôle du 1er novembre 2026

Périmètre demandé : Windows uniquement. Le catalogue Android et les APK ne sont pas modifiés. Les 24 références exclusivement macOS/Linux du catalogue commun sont conservées à l'identique.

## Résultat

- 408 références existantes déclarant Windows contrôlées ; 126 versions de release et 162 listes de paquets actualisées.
- 218 références supplémentaires avec documentation amont épinglée et paquets Windows publiés avec taille et SHA-256 GitHub. Les variantes d'un même jeu partagent une carte ; les deux jeux Rampage pris en charge par le même moteur ont chacun leur boîte.
- 650 références au total, dont 626 déclarent Windows, regroupées avec les moteurs intégrés en 557 cartes sur 30 plateformes. Ce total n'est pas un nombre de jeux testés ni une garantie d'exhaustivité.
- 138 jaquettes supplémentaires provenant de libretro-thumbnails, GameTDB et xenia-manager/x360db, sur correspondances exactes ou alias régionaux contrôlés. Les faces et supports physiques existants sont conservés. 81 fronts restent absents et affichent « Jaquette indisponible ».
- Le dépôt `sonsegajp/HWDER` renvoie 404 : sa référence est conservée pour les profils existants, mais ses paquets sont retirés et l'installation automatique suspendue.

`src/CubeShelf.Desktop/Catalogs/windows-refresh-audit.json` conserve les références actualisées et ajoutées, les exclusions et le contrôle Switch 2. Chaque référence ajoutée contient un lien README épinglé au commit contrôlé. Les paquets sont vérifiés par taille et empreinte avant extraction ; aucune ROM, clé, BIOS ni donnée de jeu n'est fournie.

## Switch 2

Aucun port natif Windows public correspondant aux critères n'a été vérifié pendant cette passe ; aucun faux catalogue Switch 2 n'a été ajouté.

- [Donkey Kong Bananza dans nxnative](https://github.com/seedds/nxnative/blob/main/dm/games/040074A01BF12000/README.md) : le projet rapporte des essais sur Mac Apple Silicon dans les premières zones. Aucune version Windows vérifiée.
- [Eglantine](https://github.com/Djihads80/Eglantine) et [monkey](https://github.com/Mars-Hermetto/monkey) : émulateurs, exclus du périmètre natif demandé.
- `rickdodge/mariokartworldrecomp` : dépôt public inaccessible au contrôle (404).

Les wrappers d'émulateurs identifiés, projets à interpréteur/JIT ambigu, builders destinés à une autre console et projets sans instructions de données personnelles établies ont été écartés. F-ZERO GLINUX et Melee-3D restent exclus conformément aux décisions précédentes.

## Validation et limites

- `dotnet run --project tests/CubeShelf.NativePorts.Tests -c Release` : réussi ; provenance, identités, paquets, annulation, archives invalides et préservation des installations.
- `dotnet run --project tests/CubeShelf.ShelfUi.Tests -c Release` : réussi ; sélection des 30 consoles, variantes sans doublon, jaquettes des quatre catalogues historiques, thèmes, transitions, manette simulée et animations d'ouverture.
- `dotnet publish src/CubeShelf.Desktop/CubeShelf.Desktop.csproj -c Release -r win-x64 --self-contained true` : réussi ; jaquettes copiées et contrôlées par la cible de publication existante. Le compilateur natif conserve son avertissement MSB8027 préexistant dans llama.cpp.
- Captures contrôlées : bibliothèque Nintendo 64 et Xbox 360 ; proportions des boîtes conservées, image absente signalée explicitement.

La présence d'un paquet Windows et les déclarations du README ne prouvent pas une partie complète. Les nouveaux ports sont signalés « partie non testée » ; leurs prérequis et limites restent ceux des projets amont. Aucun nouveau port n'a été exécuté avec des données de jeu pendant cette passe. Le fonctionnement Windows ARM64 et la navigation sur manette physique ne sont pas qualifiés par ces contrôles.
