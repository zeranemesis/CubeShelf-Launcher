# Second thème HOME : direction iiSU

Les logos ont ensuite été complétés pour tout le catalogue : [import LaunchBox du 4 novembre](GAME-LOGOS-2026-11-04.md), 550 associations vérifiées. Les mentions de quatre logos seulement dans l'historique ci-dessous décrivent la version précédente.

## Aperçu du jeu en 3D — 4 novembre 2026

L'utilisateur a choisi de réutiliser les boîtiers 3D existants uniquement pour le grand aperçu du jeu sélectionné. Le composant `GameCaseControl` est maintenant utilisé dans cette zone avec le format de la console et les faces disponibles du catalogue. La grille conserve ses icônes carrées. Les variantes claire et sombre restent sobres et se choisissent dans **Personnaliser → Affichage**.

Une seule instance 3D est conservée, libérée au changement de jeu, au masquage et au détachement. Les faces secondaires Android sont extraites seulement pour le jeu sélectionné ; les faces absentes restent facultatives. Une image de recto manquante ou corrompue conserve le repli de console. Les formats courts en paysage privilégient toujours la grille et les commandes.

Après un premier rendu trop petit, la hauteur de l'aperçu et la place du titre ont été ajustées. La revue indépendante des captures du 4 novembre confirme une boîte entière et agrandie à 320, 390 et 1280 pixels, ainsi que le titre complet d'Ocarina of Time. Elle confirme le format vertical du boîtier GameCube et le carton plus large Nintendo 64. Cette appréciation porte sur ces corrections et les captures examinées, sans validation matérielle mobile.

Les tests d'interface vérifient une seule boîte 3D, le changement de format à la sélection, la réutilisation des vignettes, le repli sur image corrompue, les actions dans le viewport, le retour aux consoles/Classique, le focus des menus et l'arrêt de musique. Les captures se trouvent hors du dépôt dans `../android-embedded-engines/home3d-ui-2026-11-04/`.

Les suites Mobile et ShelfUi ont réussi après le dernier ajustement. Windows x64 autonome a été publié dans `../decompshelf-home3d-windows-2026-11-04/` puis démarré sur le profil existant : fenêtre DecompShelf répondante. Le contrôle souris final a rencontré la session verrouillée ; le dernier aperçu 3D est donc confirmé par les captures de test, sans prétendre à un parcours manuel complet de cette version.

L'APK ARM64 **versionCode 61** est publié dans `../decompshelf-home3d-android-2026-11-04/`, avec empreinte SHA-256 adjacente. Signature locale inchangée et vérifications de paquets réussies (23 hôtes, 59 composants privés, alignement 16 Ko, aucun stub/ROM/APK imbriqué). Aucun téléphone n'a été installé ni testé pour cette révision. Pas de commit, push ou release GitHub réalisé.

## Révision sobre claire et sombre

Après le premier rendu, l'utilisateur a demandé plusieurs agents pour retravailler la finition, puis deux variantes sobres, claire et sombre. La révision conserve deux thèmes de présentation (Classique et HOME) ; HOME possède ses deux apparences dans **Personnaliser → Affichage**, mémorisées séparément du thème Classique. La variante claire est le défaut d'un profil sans préférence HOME.

Travail réparti entre composition native, composant d'illustration et revue indépendante des captures. Police Nunito embarquée sous OFL ; navigation graphique avec libellés accessibles et cibles de 48 pixels, commande de recherche repliée, état/action du jeu réels conservés. Les vignettes sont recadrées pour remplir leur tuile ; l'aperçu sélectionné conserve la couverture complète ou utilise un logo existant. Les logos Nintendo 64 trouvés dans une recherche représentaient les éditions 3D : ils ont été écartés pour préserver la fidélité des éditions. Aucun nouveau logo de jeu n'est annoncé.

Le décor illustré initialement envisagé a été abandonné après la consigne de sobriété. Aucun bitmap généré n'est livré. Les deux fonds sont natifs, neutres et statiques. Le déplacement dans une même page réutilise les vignettes ; les menus de personnalisation gèrent leur focus et le retour avant les actions de jeu. Une image corrompue est rejetée explicitement par le cache commun et affiche un repli sans planter l'interface. Les écritures de préférences sont sérialisées pendant l'import audio.

Les sections suivantes retracent le premier essai, remplacé visuellement par cette révision.

L'utilisateur a rejeté les grands panneaux clairs du premier essai et demandé une interface très similaire à iiSU, avec sons et musique. Il a ensuite explicitement demandé de poursuivre sans Impeccable. Cette révision est réalisée directement dans les contrôles Avalonia partagés ; aucune nouvelle exécution du plugin ni revue déléguée n'est utilisée.

## Référence et adaptation

Les deux démonstrations officielles de [iiSU](https://github.com/iisu-network/iiSU#showcase) ont été examinées : aperçu central dans une fenêtre translucide, fond nocturne visible, tuiles illustrées et navigation compacte. Les images de référence sont conservées hors du dépôt ; aucun code, wallpaper ou son d'iiSU n'est redistribué.

Le thème **Double écran · HOME** reprend cette composition avec un fond vectoriel original, un panneau indigo quadrillé et des bords clairs, une sélection cyan et un aperçu animé pendant 220 ms. Les logos existants sont privilégiés ; les autres jeux utilisent leur jaquette complète et leur titre. Le thème Classique est conservé. Les menus latéraux restent visibles sur le bureau ; Big Picture supprime sa barre supérieure redondante sur cette vue.

La grille adapte ses rangées à la place disponible. Sur un téléphone bas en paysage, elle prend la place de l'aperçu pour conserver les actions. Les changements de page, la recherche, le retour aux consoles et la bibliothèque personnelle restent accessibles. Depuis le 5 novembre, Android peut séparer l'aperçu principal et la grille sur un écran de présentation compatible : voir [l'adaptation AYN Thor](IIDB-THEME-AND-AYN-THOR.md). Le firmware et le matériel Thor restent à tester.

## Audio

Quatre fichiers originaux PCM 24 kHz sont générés par `eng/generate-home-audio.py`. L'ambiance de 16 secondes fonctionne sans association préalable ; une piste MP3/WAV personnelle associée au jeu la remplace. Navigation, ouverture et lancement ont chacun un effet. Le bouton **Audio** propose deux coupures indépendantes, musique et sons, persistées dans le profil.

L'ambiance s'arrête à la perte d'activité, au changement de vue et au lancement d'un jeu. Les effets sont espacés d'au moins 85 ms. Sur Windows, les effets réutilisent WinMM et la musique utilise le lecteur d'aperçu existant ; Android utilise un lecteur distinct pour les effets afin de conserver l'ambiance.

## Limites de l'observation

La préparation d'un essai d'iiSU dans un AVD a été refusée par le contrôle automatique. Le comportement d'iiSU n'est donc pas annoncé comme testé directement : seules les démonstrations officielles et sa documentation ont servi de référence. Le résultat DecompShelf est vérifié par les suites locales et les captures du nouveau thème, sans affirmer une reproduction exacte d'iiSU.

## Vérification et fichiers préparés

Suites `CubeShelf.Mobile.Tests` et `CubeShelf.ShelfUi.Tests` réussies : recherche et lancement du même jeu, navigation clavier/manette, pagination adaptative, retour aux consoles et au thème Classique, musique associée puis ambiance par défaut, coupure et persistance séparées des effets, arrêt à la perte d'activité. Les rendus 320×740, 390×844, 1280×800, 1920×1080, 844×390 et 700×320 ont été capturés ; les tests contrôlent aussi des actions de 48 pixels et leur présence dans le viewport. Le fond est statique, l'animation est limitée à la sélection et les scènes 3D Classique restent cachées dans ce thème.

Publication Windows x64 autonome terminée et application relancée sur le profil existant, fenêtre `DecompShelf · NES`, processus répondant. L'écran Windows était verrouillé lors de la capture native : la vérification visuelle finale reste fondée sur les rendus headless, sans annoncer de parcours souris réel validé.

APK Android ARM64 versionCode **59**, signé avec le certificat local existant, préparé dans `../decompshelf-home-android-2026-11-03/`. Vérifications réussies : 23 hôtes épinglés, 59 composants privés, alignement 16 Ko, absence de stubs/ROM/APK imbriqué. L'empreinte SHA-256 est jointe. Aucun téléphone connecté ; aucune installation ou partie réelle annoncée pour cette révision.
