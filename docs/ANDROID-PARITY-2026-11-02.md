# Android ARM64 — bibliothèque et jaquettes, 2 novembre 2026

APK DecompShelf **0.19.0-rc.3, code Android 53**, pour Android 8 minimum et processeur ARM64. Le catalogue comprend 24 ports Android, plus PartyBoard intégré, sur neuf consoles. Il reste indépendant du catalogue Windows : chaque jeu doit disposer d'un paquet Android vérifié.

## Mes jeux

Le bouton permanent dans l'en-tête ouvre les jeux installés ou ajoutés, toutes consoles confondues. L'accueil des consoles, F2 et View/Select à la manette ouvrent la même bibliothèque. « Catalogue » et Retour/B rendent le catalogue de la console ; les téléchargements et l'inspection des boîtes restent accessibles.

La bibliothèque utilise les paquets réellement installés, les APK téléchargés et vérifiés, et les données préparées des moteurs intégrés. La seule présence du moteur Super Mario 64 embarqué ne compte pas comme un jeu ajouté sans ROM préparée. Les changements de disponibilité des téléchargements actualisent la liste sans interroger tous les paquets à chaque progression réseau.

Le téléphone conserve l'heure, la batterie et la connexion en haut à droite. Les commandes se répartissent sur deux lignes sur les écrans étroits. Pendant une préparation de ROM ou un lancement, le changement de bibliothèque reste bloqué ; le bouton d'annulation demeure à l'écran en paysage.

## Jaquettes et rendu

L'APK compile les contrôles de boîtes et de carrousel partagés avec la version PC, y compris les corrections récentes de proportions et de cadrage. Les ressources Android déjà présentes sont conservées, avec leurs dos, tranches et supports. La jaquette Switch de Monster Hunter Generations Ultimate est copiée à l'identique depuis le scan GameTDB vérifié du catalogue PC. Son nom inclut une empreinte pour éviter de réutiliser une ancienne image en cache.

## Monster Hunter Generations Ultimate

[MHGURecomp 0.13.1](https://github.com/morerokk/MHGURecomp/releases/tag/0.13.1) est ajouté comme **port Switch natif Android expérimental**. Le [README amont](https://github.com/morerokk/MHGURecomp) décrit une recompilation statique vers du code natif et limite les essais Android annoncés à un émulateur, sans essai sur téléphone réel.

- APK : `Android-MHGURecomp.0.13.1.apk`, 136 684 642 octets.
- SHA-256 : `4d3f425c269eed69de6ba8311088e7bac5d0c476ea6b524e17f6eb678b729a94`.
- Paquet : `io.github.morerokk.mhgurecomp`, version 346, minSdk 29, targetSdk 35.
- Trois bibliothèques natives vérifiées ELF64 AArch64, ABI `arm64-v8a` ; signature APK vérifiée.
- Empreinte SHA-256 du certificat : `c23343270c827bf90b22096bd4779bc25b2d199a1ded28d58bc7fe5e86674834`.
- Android 10 et Vulkan 1.2 requis. Manette nécessaire, aucune commande tactile. L'auteur recommande 6 Go de RAM et environ 13 Go de stockage pour les fichiers personnels, 26 Go pendant l'import.

Le catalogue épingle URL, taille et SHA-256. Les contrôles existants d'architecture, SDK et Vulkan s'appliquent avant téléchargement. La visibilité du paquet sous Android 11 et versions suivantes est déclarée dans le manifeste. L'installation et le lancement utilisent le flux existant des ports externes, avec les confirmations Android requises. Aucun APK de ce jeu ni fichier de jeu n'est inclus dans DecompShelf.

## Validation

- Suite Mobile complète : intégrité des téléchargements, 24 paquets distincts, neuf plateformes, bibliothèque vide, jeux de plusieurs consoles, APK vérifié ajouté après téléchargement, moteur sans ROM exclu, retours et accès depuis les téléchargements.
- Affichages contrôlés à 320 × 700, 390 × 844 et 844 × 390 ; vérification de la position des informations système et des actions, captures inspectées en portrait et paysage.
- Compilation Release ARM64 avec AOT réussie ; signature APK et certificat vérifiés, même certificat local que le précédent APK contrôlé.
- Paquet final : version 53, minSdk 26, targetSdk 34, label DecompShelf ; 198 bibliothèques ELF64 AArch64, aucune ABI supplémentaire.

L'utilisateur a demandé de préparer seulement l'APK : aucune installation sur son téléphone et aucun nouvel essai de partie. La validation des binaires et de l'interface simulée ne garantit pas la jouabilité de tous les jeux.
