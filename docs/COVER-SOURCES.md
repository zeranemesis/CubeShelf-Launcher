# Sources des jaquettes

L'animation Big Picture dispose aussi de 47 scans GameTDB de disques et cartouches. `Media`, `MediaSource` et `MediaFileSha256` dans le registre identifient les fichiers locaux et leur provenance. Muscle March, Diablo II: Resurrected et Super Mario Bros. 35 n'ont pas de scan de support dans cette sélection : leur animation utilise une forme neutre, sans illustration de disque inventée.

Les 47 nouvelles faces sont des illustrations du jeu concerné issues de [GameTDB](https://www.gametdb.com/), avec 41 dos et tranches découpés depuis leurs scans complets. Les 4 jeux GameCube intégrés conservent leurs jaquettes existantes, dont celle de Sunshine partagée avec son autre port.

Le registre `src/CubeShelf.Desktop/Catalogs/covers.json` conserve pour chaque jeu la console, le titre, l'identifiant GameTDB, les chemins locaux, l'URL source et le SHA-256 du téléchargement. Les champs `FullSource` et `FullSourceSha256` identifient le scan complet utilisé pour le dos et la tranche, ainsi que pour une face de meilleure définition lorsque `FrontDerivedFromFull` vaut `true`. Les champs `FrontFileSha256`, `BackFileSha256` et `SpineFileSha256` identifient les images locales après découpe. Les fichiers sont fournis avec l'application pour éviter un téléchargement à l'ouverture de la bibliothèque.

Les jaquettes WiiWare et Nintendo eShop sont les représentations de ces jeux proposées par GameTDB. Les illustrations et marques appartiennent à leurs ayants droit ; les avoir téléchargées ne transfère aucun droit sur elles. Les contributions de la base sont créditées à GameTDB et à ses contributeurs.
