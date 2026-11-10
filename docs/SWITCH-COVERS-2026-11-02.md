# Switch et jaquettes — Windows, 2 novembre 2026

Le catalogue Switch passe de 24 à 32 références, regroupées sur 25 jeux au lieu de 18. Le périmètre reste celui des ports natifs Windows. Chaque ajout possède un README épinglé à un commit ; `Catalogs/switch-refresh-audit.json` conserve la provenance et les exclusions de cette passe, sans réécrire l'audit historique de novembre.

## Ajouts

| Jeu | Projet | Distribution contrôlée |
| --- | --- | --- |
| Monster Hunter Generations Ultimate | [MHGURecomp](https://github.com/morerokk/MHGURecomp) | ZIP Windows 0.13.1, dernière release hors préversion contrôlée |
| Pokémon Sword / Pokémon Shield | [SwSh Recomp](https://github.com/raulzrrs/swsh-recomp) | ZIP Windows 0.1.0, deux jeux distincts |
| Metroid Dread | [Metroid Dread Recomp](https://github.com/yozander2009-stack/Metroid-Dread-Recomp) | ZIP Windows 0.5.0, aperçu |
| Pokémon: Let’s Go, Pikachu! | [Let's Go Recomp](https://github.com/raulzrrs/letsgo-recomp) | ZIP Windows 0.7.6, moteur déjà référencé pour Évoli |
| Pokémon Legends: Z-A | [ZA Recomp](https://github.com/Hexveil404/PokeZ-ARecomp) | ZIP Windows 0.1.14, version Switch originale |
| Super Mario Bros. Wonder | [Wonder Recomp](https://github.com/BluetufieBugado/SuperMarioWonderRecomp) | Sources uniquement, compilation manuelle |
| Astral Chain | [AstralChain-PC](https://github.com/Puddz/AstralChain-PC) | Sources uniquement, variante de la carte existante |

Les six références avec paquet disposent d'une URL de release, d'une taille et du SHA-256 publié par GitHub. Les deux autres restent explicitement des compilations manuelles. Tous les ajouts sont marqués « partie non testée » ou « expérimental » : l'existence d'un port et les essais annoncés par son auteur ne garantissent pas une partie complète. La documentation d'Astral Chain limite notamment les essais à File 01, avant son boss. Les versions personnelles et prérequis restent ceux des README, sans données de jeu fournies par DecompShelf.

Le téléchargement de `dougchansan/mk8-recomp` inclut le JIT selon son README : il n'a pas été ajouté. La documentation Windows d'Echoes of Wisdom ne prouve pas encore l'exécution du jeu. Aucun de ces constats ne modifie les références macOS déjà présentes.

## Jaquettes

39 fronts manquants ajoutés : les sept nouveaux jeux Switch, 23 Xbox 360, deux PlayStation, une NES, une Xbox, quatre PS4 et un PS3. Les sept scans Switch GameTDB font 1166 × 1888 pixels. Les visuels ont été inspectés en planches et conservés sans étirement ni reconstruction ; une illustration promotionnelle carrée de MediEvil a été écartée. Les entrées existantes avec dos, tranche, faces et support sont préservées.

`Catalogs/covers.json` enregistre la source, l'URL et le SHA-256 du fichier ajouté. Les sources sont GameTDB, libretro-thumbnails, l'archive x360db et les illustrations originales des articles du jeu exact sur Wikipédia. Les reprises Xbox 360 utilisent des correspondances de titres contrôlées, par exemple « Edge Of Time Recomp » → Spider-Man: Edge of Time ; elles ne dépendent pas d'une recherche floue automatique.

49 titres restent dans `missing-console-covers.json` : sources indisponibles, compilations portant plusieurs jeux, prototypes sans boîte commerciale et jeux sans visuel suffisamment identifié. Les jaquettes manquantes continuent d'afficher leur état prévu. Aucune illustration d'une autre console ou d'un autre épisode n'a été substituée.

## Contrôles

Tests NativePorts : chargement des 658 références, provenance et paquets valides, 25 titres Switch sans doublon de variante, séparation des compilations manuelles. Tests ShelfUi : navigation de toutes les consoles et de la bibliothèque personnelle, proportions et cache des jaquettes, clavier et manette simulée, annulation et transitions. Vérification des fichiers et empreintes avant publication Windows autonome.

Les nouveaux jeux n'ont pas été exécutés et la manette physique n'a pas été retestée dans cette passe. Android est hors de cette mise à jour.
