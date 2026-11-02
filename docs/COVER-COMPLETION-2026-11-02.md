# Recherche des 49 visuels manquants — 2 novembre 2026

37 entrées disposent désormais d'un visuel identifié, soit 550 fronts dans le registre. Les 12 autres ont chacune une justification et un lien vers la documentation primaire dans [`cover-completion-audit.json`](../src/CubeShelf.Desktop/Catalogs/cover-completion-audit.json). Les sources, URL, dimensions et SHA-256 de chaque ajout y sont enregistrés et repris dans `covers.json`.

## Visuels intégrés

| Catalogue | Entrées complétées |
| --- | ---: |
| PlayStation 4 | 12 |
| PlayStation 3 | 6 |
| PlayStation 2 | 1 |
| PSP | 1 |
| Xbox | 2 |
| Xbox 360 | 5 |
| Arcade | 8 |
| Nintendo DS | 1 |
| Virtual Boy | 1 |

Les jaquettes PlayStation correspondent au titre, à la console et à l'édition indiqués : par exemple [MediEvil PS4](https://gamesdb.launchbox-app.com/games/images/123150-medievil), [Dragon's Crown Pro](https://gamesdb.launchbox-app.com/games/images/100875-dragons-crown-pro) et [God of War III Remastered](https://gamesdb.launchbox-app.com/games/images/30720-god-of-war-iii-remastered). Les résultats sont sélectionnés par identité et plateforme, puis inspectés ; fanarts, reconstructions et titres voisins sont écartés. Des éditions limitées authentiques du même jeu peuvent être utilisées lorsque leur scan est disponible.

Les images d'arcade sont des flyers d'époque plutôt que des boîtes de console inventées. La variante Vampire Savior Definitive reprend explicitement le flyer de son jeu de base, en partageant le même fichier. God of War II HD, Case West et Case Zero conservent leurs visuels d'édition numérique. Budokai 3 HD utilise la jaquette Xbox 360 de HD Collection, édition dont il est issu, avec cette précision dans le registre.

Deux cas sont identifiés comme tels dans la source visible du visuel :

- **Zero Racers** : image de titre officielle publiée par [Nintendo dans sa liste Nintendo Classics](https://www.nintendo.com/en-gb/Nintendo-Switch-Online/Classic-games/Classic-games-Nintendo-Switch-Online-2719182.html). Nintendo annonce sa [diffusion numérique du 4 août 2026](https://www.nintendo.com/jp/topics/article/99f2d359-8ea3-4276-92b7-45f4af3dd9ef). Il ne s'agit pas d'une boîte commerciale de 1996.
- **StarCraft: Ghost** : flyer promotionnel Blizzard complet, [archivé dans la fiche Xbox de LaunchBox](https://gamesdb.launchbox-app.com/games/images/155050-starcraft-ghost), sans le présenter comme une boîte sortie dans le commerce.

Les fichiers sont repris sans reconstruction graphique, recadrage ou agrandissement artificiel. Les dos, tranches, autres faces et supports existants restent inchangés.

## Correction d'identité

L'entrée DS intitulée « Pokémon HeartGold and SoulSilver » est renommée **Pokémon SoulSilver**. Le [README du port](https://github.com/ClickingMaster/pokeheartgold-single-screen) précise que la recompilation native et la ROM requise sont SoulSilver USA, même si le dépôt de décompilation englobe les deux jeux. La vraie jaquette SoulSilver remplace le manque ; un montage HeartGold/SoulSilver trouvé dans l'archive a été écarté. L'identifiant du port, ses assets, ses empreintes et ses autres champs sont conservés. Le catalogue utilisateur rapproche les métadonnées par identifiant, en conservant chemins et historique.

## 12 entrées restant sans jaquette unique vérifiée

| Entrée | Résultat de la recherche |
| --- | --- |
| Pokemon RBY / GSC / FRLF / RSE | Moteur couvrant de nombreuses éditions sur plusieurs consoles ; aucune boîte originale commune. |
| Zelda Oracle of Ages/Seasons | Deux jeux et deux boîtes ; les montages réunissant les deux ne sont pas leur jaquette originale. |
| Pokemon Gold / Silver / Crystal | Trois éditions regroupées. |
| Sonic 3 & Knuckles | Combinaison des cartouches Sonic 3 et Sonic & Knuckles ; aucune boîte Mega Drive dédiée vérifiée. |
| Vigilante 8 / 2nd Offense | Deux jeux originaux distincts réunis dans le port. |
| Yakumo | Nom du port de Monster Hunter Portable 3rd HD Ver. (NPJB-40001) ; classement PSP à clarifier avant une image de l'édition HD PS3. |
| GoldenEye 007 | Le README ne précise pas suffisamment la version d'origine ; association Xbox 360 à confirmer. |
| GoldenEye 007 XBLA PC Recomp | Prototype XBLA ; un visuel x360db a été trouvé, sans provenance établie comme jaquette officielle. |
| James Bond games | Quantum of Solace, Blood Stone et 007 Legends regroupés. |
| Marvel: Ultimate Alliance 1 and 2 | Deux jeux regroupés ; ne pas utiliser le bundle Xbox One pour une entrée Xbox 360. |
| Call of Duty: Future Warfare | Prototype NX1 annulé, aucune jaquette originale vérifiée. |
| Xbox 360 native ports collection | Collection de lanceurs, sans boîte commerciale commune. |

Les boîtes individuelles de plusieurs de ces jeux existent. Les intégrer honnêtement suppose de distinguer les titres ou éditions dans ces entrées de catalogue, plutôt que d'attribuer la boîte d'un seul jeu à tout le groupe. Aucun titre de remplacement n'a été ajouté et aucune classification existante n'a été déplacée dans cette passe.

## Vérification

Décodage et empreintes de tous les fichiers du registre ; unicité console/titre ; conservation des anciennes faces ; correspondance des 49 cas entre 37 ajouts et 12 résultats documentés. Les contrôles de proportions couvrent aussi les nouveaux scans PS4, la jaquette DS, l'image carrée Nintendo et les flyers paysage, en vignettes et dans les boîtes Big Picture.

Mise à jour Windows uniquement. Aucun changement de runtime, de modèle 3D ou de commande, et aucun nouvel essai de partie déduit de ces contrôles.
