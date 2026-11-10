# Ports natifs ajoutés le 5 novembre 2026

Le catalogue PC contient 663 références de projets ; les variantes d'un même jeu restent regroupées dans une seule fiche. Android contient 28 ports distincts, tous disposant d'un APK ARM64 épinglé. Aucun fork supplémentaire n'a été créé.

| Jeu | Console d'origine | Windows | Android | Source primaire |
| --- | --- | --- | --- | --- |
| The Legend of Zelda: The Minish Cap | GBA | v0.9.6 | v0.9.6 | [Project Picori](https://github.com/999sian/tmc) |
| Kingdom Hearts: Chain of Memories | GBA | v2.8 | v2.8, bêta | [ReKHCoM](https://github.com/MatyMeatBoy/ReKHCoM) |
| Super Smash Bros. | Nintendo 64 | v1.6, version US | v1.6 | [BattleShip](https://github.com/JRickey/BattleShip) |
| Conker: Live & Reloaded | Xbox | v1.4 | — | [Conker Recomped and Reloaded](https://github.com/Mabec-games/Conker-Recomped-and-Reloaded) |
| Zone of the Enders | PlayStation 2 | v1.0.1, version précoce | — | [ZOE Recompiled](https://github.com/Yaz-Myst/ZOE-Recompiled) |
| AeroGauge | Nintendo 64 | Déjà présent | v0.7.0, Vulkan 1.1 | [AeroGauge Recompiled](https://github.com/alondero/aerogauge-recomp) |

Les jaquettes d'origine proviennent des dépôts libretro-thumbnails ; les images déjà disponibles pour AeroGauge sont réutilisées. Les nouveaux visuels gardent leurs proportions : GBA carrée, Nintendo 64 horizontale, PS2 et Xbox verticales. Les fichiers originaux, dimensions, URL et SHA-256 sont enregistrés dans `Catalogs/covers.json`. Les cinq nouveaux logos transparents proviennent de LaunchBox Games Database. Les nouvelles jaquettes ne prétendent pas fournir une reproduction des faces arrière et tranches manquantes.

L'audit `src/CubeShelf.Desktop/Catalogs/native-additions-2026-11-05.json` relie chaque entrée à son README épinglé par commit et garde `GameplayTested=false`. Pour Android, les quatre APK ont été téléchargés et comparés aux tailles et empreintes GitHub ; manifestes, identités, versions et bibliothèques ELF64 AArch64 ont été contrôlés. Ils sont hébergés par des adaptateurs privés de DecompShelf, sans installation d'une application de jeu externe. Ces contrôles ne remplacent pas un essai de partie avec les fichiers personnels du joueur.

Cette passe a étudié 170 dépôts primaires repérés dans les listes de ports. Elle ne certifie pas tous les projets de ces listes. Resident Evil Gaiden a été écarté de ce lot car son runtime lie un interpréteur SM83. WarioWare: Twisted! nécessite une revue supplémentaire de sa couverture native et de son chemin interprété. Les variantes, émulateurs, projets source seuls et intégrations non vérifiées ne sont pas promus automatiquement en jeux prêts à jouer.

Les tests NativePorts, Mobile et ShelfUi vérifient les catalogues, les illustrations et leur navigation. Les adaptations Android sont compilées et les paquets finaux sont contrôlés séparément. Les versions précoces gardent leur statut explicite ; une partie complète de chaque nouveau port reste à tester.
