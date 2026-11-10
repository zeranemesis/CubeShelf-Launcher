---
version: 1
slug: "desktop-controls-dualscreenlibraryview-cs-2a161bec"
primary_target: "src/CubeShelf.Desktop/Controls/DualScreenLibraryView.cs"
related_targets: ["src/CubeShelf.Desktop/MainWindow.Presentation.cs","src/CubeShelf.Mobile/MobileLibraryView.Presentation.cs"]
---

# Deuxième thème : Double écran

Mode : Operate. Direction explicitement choisie par l'utilisateur : disposition de type 3DS, conservée comme thème optionnel à côté du thème Classique. Pas de remplacement de l'identité existante.

THESIS : parcourir une grille tactile ou à la manette sans perdre l'aperçu du jeu sélectionné.

OWN-WORLD : surfaces claires ou sombres sobres, séparation horizontale des deux zones, petits emplacements carrés, sélection bleue nette. Logos et jaquettes du catalogue existant ; aucun asset extrait d'iiDB ni bande-son redistribuée.

FIRST VIEWPORT : zone supérieure avec la jaquette complète, le logo disponible ou le nom, l'état réel et l'action du jeu ; zone inférieure avec recherche et grille de sélection. Consoles et bibliothèque personnelle restent accessibles. Les commandes tactiles ont une cible d'au moins 48 pixels.

INTERACTION : un clic sélectionne et met à jour l'aperçu ; Jouer/Préparer reste une action explicite. Clavier et manette déplacent la sélection dans deux dimensions. Les pistes locales MP3/WAV sont associées volontairement, à faible volume, et s'arrêtent à la sortie de la vue ou au lancement d'un jeu. Le choix et les associations sont mémorisés séparément sur chaque appareil.

SCOPE : Windows, Big Picture et smartphone. Sur un seul écran, deux zones dans une même fenêtre. Préparation documentaire de la Thor uniquement : aucune prétention de double écran physique validé, aucun second parent pour une même vue Avalonia.

FORM : extension optionnelle aux contrôles natifs existants avec une composition explicitement imposée par la réponse utilisateur (« grille sur le bas et le jeu sélectionné en haut logo et musique »). Aucune autre direction visuelle n'est mise en compétition ; le thème Classique demeure la référence des écrans hors de cette extension. Construction par code et ressources existantes, sans comp généré ni nouveau raster produit.

VERIFY : rendus PC et téléphone, formats horizontaux et verticaux conservés, absence de résultat, retour au thème Classique, persistance, navigation et arrêt de musique. Revue de finition indépendante avant livraison ; distinguer headless, téléphone réel et matériel Thor non disponible.

VALIDATION — 3 novembre 2026 : suites Mobile et ShelfUi réussies, avec navigation clavier/manette, recherche synchronisée avec le jeu lancé, retour aux consoles et au thème Classique, persistance et état vide. Captures headless à 320, 390 et 1280 pixels, variantes claire/sombre et fenêtre Windows avec menus latéraux. Lecture/arrêt d'un WAV silencieux vérifiés avec le lecteur Windows sur thread STA. Revue indépendante : disposition finale « ship » après correction de la sélection filtrée, du focus de grille et des commandes sans résultat. Portée : code, tests et captures ; aucun essai de ce thème sur téléphone réel ou Thor.

LIVRABLES : publication Windows x64 et APK Android ARM64 versionCode 58 préparés localement, sans release GitHub. Vérifications de l'APK : signature locale existante, 23 hôtes épinglés, 59 composants privés, alignement 16 Ko ; absence de ROM, APK imbriqué et stubs de compilation. Empreinte SHA-256 jointe à l'APK.
