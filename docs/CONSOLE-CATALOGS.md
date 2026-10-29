# Catalogues PC — extension du 29 octobre 2026

Les quatre catalogues existants restent à leurs positions : GameCube, Wii, Switch, Nintendo 64. Le sélecteur commun au bureau et au Big Picture ajoute 26 plateformes : NES, Super Nintendo, Game Boy, Game Boy Color, Game Boy Advance, Nintendo DS, Nintendo 3DS, Wii U, Virtual Boy, Master System, Mega Drive, Game Gear, Sega 32X, Sega CD, Saturn, Dreamcast, PlayStation, PlayStation 2, PlayStation 3, PlayStation 4, PSP, Xbox, Xbox 360, Arcade, Neo Geo et Philips CD-i.

L’extension contient 346 références de ports regroupées en 313 jeux. Les éditions, variantes de projets et noms de dépôts entre parenthèses ne créent pas de cartes supplémentaires : les versions se choisissent sur la carte du jeu. Les titres distincts pris en charge par un même projet, comme Oracle of Ages / Seasons ou les jeux CPS-2, ont chacun leur carte.

## Provenance et limites

Les projets ont été repérés à partir de l’index public de Carter54git/Greates-Games-Decompilations-List, puis leur README amont a été consulté via GitHub. Chaque référence conserve le dépôt, le commit, un lien vers son README épinglé et la date du contrôle. `Catalogs/platform-expansion-audit.json` conserve les éléments documentaires retenus. Cet inventaire n’est pas une garantie d’exhaustivité de tous les ports existants.

Le contrôle porte sur la documentation, pas sur une partie exécutée. Les prototypes et travaux en cours identifiés sont marqués **expérimental** sur la carte. Les annonces de fonctionnement restent celles des auteurs. Certains projets nécessitent une compilation ou ne prennent en charge que macOS/Linux : consulter les plateformes et instructions dans Détails.

Les projets seulement destinés à reconstruire une ROM, les wrappers d’émulateurs identifiés et les projets dont la documentation mentionne un recours au JIT/interpréteur ont été écartés conservativement. Cette sélection peut donc omettre des ports natifs dont les limites ou la méthode restent ambiguës. Aucun port PS5 natif correspondant aux critères n’a été ajouté.

L’installation automatique conserve la règle existante : URL de release du dépôt, taille et SHA-256 publiés par GitHub, contrôle avant extraction. Sans paquet vérifiable compatible, l’utilisateur suit les instructions du projet et associe son programme. Aucun moteur de jeu, ROM, BIOS, firmware, clé ou sauvegarde n’a été ajouté par cet import.

## Jaquettes et présentation

259 jaquettes exactes de l’extension ont été récupérées dans les dépôts publics `libretro-thumbnails` et l’archive Xbox 360 `xenia-manager/x360db`. Les URL de provenance sont enregistrées dans `covers.json`. `eng/import-console-covers.py`, `eng/import-verified-cover-aliases.py` et `eng/import-xbox360-covers.py` utilisent des correspondances de titres exactes ; ils ne remplacent pas une image manquante par celle d’un autre jeu. Les 54 fronts absents sont listés dans `missing-console-covers.json` et affichent **Jaquette indisponible**.

Les nouvelles boîtes suivent le rapport largeur/hauteur de l’illustration disponible. Les faces arrière, tranches et supports physiques ne sont pas inventés. Les animations détaillées historiques restent réservées aux quatre formats déjà implémentés ; les nouvelles consoles conservent leur jaquette pendant le lancement. Les sélecteurs et l’en-tête emploient les logos de consoles intégrés, avec provenance et licences ; Arcade reste une catégorie sans logo de constructeur. Le bureau affiche des photos statiques des consoles et conserve le menu de gauche ; Big Picture conserve son carrousel 3D stylisé et un retour direct au choix des consoles.

## Validation

- `dotnet run --project tests/CubeShelf.NativePorts.Tests -c Release` : chargement des 30 catalogues, provenance, identités, origines et empreintes des paquets, validation des archives et préservation des installations existantes.
- `dotnet run --project tests/CubeShelf.ShelfUi.Tests -c Release` : tous les nouveaux catalogues dans une seule fenêtre, synchronisation bureau/Big Picture, absence de cartes dupliquées, contrôles manette et régressions des quatre catalogues historiques.
- Captures de contrôle : NES, GBA, PlayStation et Xbox 360, réalisées après la fin de transition.

Sur Android, le catalogue est distinct : il affiche uniquement les jeux avec un moteur ou un APK Android identifié, sur six plateformes. Quatre ports ARM64 supplémentaires sont proposés : Wario Land, Oracle of Ages / Seasons (une application commune), Final Fantasy Tactics Advance et The Wind Waker HD. Leurs APK ont été contrôlés par empreinte et architecture ; leur jouabilité sur téléphone n’a pas été testée. Les ports externes nécessitent la confirmation de l’installation Android et les fichiers de la copie personnelle du jeu. Aucun test de jouabilité de l’ensemble des ports PC n’a été effectué.
