# Jaquettes — dimensions et confort, 2 novembre 2026

L'audit automatique porte sur les **474 faces présentes** : fichiers décodables, dimensions positives et contrôle des empreintes locales lorsqu'elles sont renseignées. Aucune image corrompue. Leur définition suffit au nouvel affichage à 100 %, dont l'emplacement maximal mesure 232 × 260 unités d'interface. Les images absentes restent signalées explicitement.

## Ajustements

- Ajustement uniforme dans la largeur **et** la hauteur disponibles ; illustration entière, sans déformation ni recadrage des logos et mentions imprimés.
- Ligne de base commune aux boîtes de la bibliothèque, avec espace transparent autour des formats plus courts. Les cases n'ont plus des hauteurs d'image arbitrairement imposées par leur largeur.
- Formats OriginalBox et Nintendo 64 dessinés sans la charnière générique d'un DVD. Les coques historiques GameCube, Wii et Switch sont conservées.
- Titres sur deux lignes, avec nom complet en infobulle et nom accessible sur le bouton de jaquette.
- En 3D, rapport du scan conservé pour les boîtes Nintendo 64 et OriginalBox, grand côté normalisé et projection uniformément réduite si nécessaire pour rester entièrement dans la vue. La vérification couvre plusieurs inclinaisons, pas seulement la position de repos.

## Sources améliorées

| Jeu | Avant | Après | Provenance |
| --- | --- | --- | --- |
| Crash Team Racing Nitro-Fueled, Switch | 160 × 260 | 1166 × 1888 | [GameTDB, scan USA](https://art.gametdb.com/switch/coverHQ/US/AST4A.jpg) |
| Unicorn Overlord, Switch | 160 × 260 | 1166 × 1888 | [GameTDB, scan USA](https://art.gametdb.com/switch/coverHQ/US/BACCC.jpg) |
| Kingdom Hearts 358/2 Days, DS | 255 × 226 | 512 × 459 | [libretro-thumbnails, USA anglais/français](https://raw.githubusercontent.com/libretro-thumbnails/Nintendo_-_Nintendo_DS/master/Named_Boxarts/Kingdom%20Hearts%20-%20358-2%20Days%20%28USA%29%20%28En%2CFr%29.png) |

Ces trois scans ont été inspectés visuellement et repris sans reconstruction. Les autres faces et supports existants restent conservés. `covers.json` enregistre les URL et empreintes actualisées.

## Vérification et limites

La suite ShelfUi contrôle le maintien des proportions, les limites de vignette, l'alignement des formats mélangés dans « Mes jeux », les cas portrait japonais et paysage, et les huit sommets 3D dans la vue pour neuf orientations. Les régressions existantes de bibliothèque, navigation, cache et ouverture des boîtes passent. Captures inspectées : bibliothèque mixte, fenêtre compacte, boîtes SNES horizontale et verticale dans Big Picture.

Compilation Windows x64 autonome réussie ; registre et 474 faces publiées contrôlés à l'identique des sources.

126 sources restent sous la densité idéale pour un affichage à 200 %, dont 40 Xbox et 80 Xbox 360. Elles sont affichées avec interpolation de qualité ; aucun détail n'a été inventé par agrandissement artificiel. Le contrôle automatique des dimensions ne remplace pas une identification manuelle de chaque édition régionale des 474 scans. Aucun nouvel essai sur écran 4K physique ou de jouabilité n'est déduit de ces contrôles.
