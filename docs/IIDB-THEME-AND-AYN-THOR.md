# Thème Double écran et préparation AYN Thor

**Mise à jour du 5 novembre 2026 :** le thème HOME est partagé entre Windows et Android, avec palettes claire/sombre, sons embarqués, ambiance et pistes locales par jeu. Voir [le thème HOME](HOME-THEME-2026-11-03.md). Le branchement d'un second écran Android est maintenant implémenté ; les essais sur une AYN Thor réelle restent à faire.

Étude du 3 novembre 2026. Deux thèmes, pas deux applications : Classique reste disponible ; Double écran est le second thème choisi par l'utilisateur.

Le choix se trouve dans **Paramètres → Thème de la bibliothèque** sur PC et Android. Sur mobile, l'entrée Téléchargements existante ouvre également ce panneau ; le choix du thème est sa première carte. Il est conservé localement sur chaque appareil. Les réglages clair/sombre restent distincts du choix de présentation.

Les tests d'interface contrôlent retour aux consoles et à Classique, recherche, navigation à la manette, sélection synchronisée et lancement du même jeu. Les essais supplémentaires à deux fenêtres vérifient les tailles logiques 960×540 / 620×540, 768×432 / 496×432 et 640×360 / 413×360, les actions tactiles d'au moins 48 unités, la veille et le retrait du second écran. Ce sont des tests automatisés de disposition et de comportement, pas une certification matérielle Thor.

## Référence examinée

[iiDB, plateformes](https://iidb.iisu.network/assets?sort=platforms) et [fiche GameCube](https://iidb.iisu.network/platforms/36), examinées dans le navigateur aux formats mobile et PC. La grille met les logos de plateformes au premier plan, avec filtres par constructeur, recherche accessible et détail latéral sur PC. La fiche sépare icônes, cadres, logos et bannières : il s'agit d'une bibliothèque d'illustrations, pas d'une liste de ports natifs jouables. Aucun compteur de jeux ni fichier graphique d'iiDB n'est importé dans DecompShelf.

L'utilisateur a précisé la disposition voulue : type 3DS, grille en bas et jeu sélectionné en haut, logo et musique liés au jeu. Cette direction remplace la proposition initiale de simple grille iiDB.

## Adaptation dans DecompShelf

- **Windows :** conserver les menus de gauche ; le second thème occupe la zone de contenu. Big Picture utilise les mêmes deux zones en plein écran.
- **Smartphone :** deux zones dans la même fenêtre, sous l'heure, le réseau et la batterie. Le retour Android et les commandes existantes restent disponibles.
- **Choix :** Paramètres ; exactement deux thèmes mémorisés. Clair/sombre reste distinct du choix de disposition.
- **Aperçu :** jaquette complète, titre graphique déjà disponible ou nom lisible, état réel du port, action explicite Jouer/Préparer et accès aux options existantes.
- **Musique :** piste MP3/WAV locale associée volontairement, copiée dans le stockage privé de l'application, faible volume et arrêt au lancement du jeu ou à la sortie. Aucun morceau commercial n'est redistribué. Le changement rapide de sélection temporise la lecture ; la grille charge au maximum 24 miniatures par page.

Les logos disponibles dans le catalogue local sont réutilisés. Pour les jeux sans logo, le titre reste affiché ; aucune prétention de bibliothèque complète de logos.

## AYN Thor : faisabilité et limites

La [fiche officielle AYN](https://www.ayntec.com/products/ayn-thor) annonce Android 13, un écran AMOLED principal de 6 pouces, un écran tactile secondaire de 3,92 pouces, des sticks Hall, un Snapdragon 865 pour Lite et un Snapdragon 8 Gen 2 pour Base/Pro/Max. L'utilisateur ne possède pas encore de Thor pour des essais.

Une ABI ARM64 ne prouve ni la compatibilité graphique ni la jouabilité. La logique `AndroidNativePorts.CompatibilityError` vérifie version Android, ABI, OpenGL ES et niveau Vulkan déclaré par l'appareil : ce contrôle est conservé plutôt que déclarer tous les modèles compatibles par leur nom.

| Groupe du catalogue local | Vérification matérielle à faire sur Thor |
|---|---|
| SM64, Ocarina of Time, Majora's Mask, Mario Kart 64, Star Fox 64, Perfect Dark | OpenGL ES 3 et rendu réel ; commandes et retour au lanceur |
| Donkey Kong 64, Harvest Moon 64 | Vulkan 1.1 effectivement annoncé et démarrage en jeu |
| Pokémon Snap, Monster Hunter Generations Ultimate | Vulkan 1.2 ; MHGU exige aussi les commandes physiques |
| Wind Waker HD | Android 13 et Vulkan 1.3 effectivement annoncé ; pilote et performances à tester |
| Autres ports | Android/ABI compatibles sur le papier ; dépendances, commandes, import et parties à vérifier |

Les versions Base/Pro/Max et Lite doivent être testées séparément. Ni le processeur ni une release GitHub ne suffisent à annoncer un résultat en jeu.

## Double écran Android : fonctionnement intégré

Le réglage **Second écran automatique · AYN Thor / écran compatible** est activé par défaut pour HOME. Sans écran secondaire admissible, HOME conserve ses deux zones dans une seule fenêtre. Classique reste inchangé.

`AndroidHomeDisplays` suit les écrans de présentation proposés par Android, sans supposer un identifiant fixe. Lorsqu'un écran devient disponible, une `Presentation` et une vue Avalonia distincte y affichent la grille tactile. L'activité principale conserve l'aperçu, le logo et le boîtier 3D. La sélection est synchronisée et les commandes physiques passent par le contrôleur existant. Une seule vue rend le boîtier 3D et un seul lecteur joue la musique ; l'aperçu ne décode pas une deuxième grille masquée.

Le lancement d'un jeu, les paramètres et la mise en arrière-plan libèrent la présentation. Le retrait d'un écran ou un refus du système restaure la disposition complète sur l'écran principal. L'option et le thème sont conservés localement. Ce mécanisme concerne le lanceur ; il ne modifie pas l'écran de lancement des ports et ne sépare pas les deux écrans d'un jeu DS/3DS.

## Validation restante sur Thor

1. Relever par ADB le modèle, Android, ABI, fonctionnalités graphiques, écrans, densités et contrôleurs d'une Thor réelle. Ne pas supposer que l'écran secondaire porte toujours l'identifiant 1.
2. Vérifier les événements de [DisplayManager](https://developer.android.com/reference/android/hardware/display/DisplayManager) lors de l'ajout/retrait, de la fermeture du capot et des changements d'activité.
3. Vérifier que le firmware accepte la [Presentation](https://developer.android.com/reference/android/app/Presentation) sur l'écran inférieur, ainsi que la densité, les zones tactiles et le repli sur un écran.
4. Pour le jeu sur l'autre écran, vérifier les autorisations de lancement avant [setLaunchDisplayId](https://developer.android.com/reference/android/app/ActivityOptions#setLaunchDisplayId(int)). Le support du firmware et du port doit être contrôlé ; un affichage d'aperçu sur le deuxième écran ne sépare pas automatiquement les écrans d'un jeu DS/3DS.
5. Garder un seul propriétaire de la sélection et de l'audio, transférer proprement le focus des commandes physiques, couper la musique avant le jeu et gérer reprise/rotation/veille.
6. Vérifier des parties et sauvegardes sur plusieurs familles de moteurs, puis endurance, chaleur, autonomie et fluidité. Ne pas activer deux scènes 3D et deux lecteurs audio en permanence.

**État :** thème Android et branchement logiciel des deux écrans intégrés. Compatibilité Thor réelle, performances et jeux sur cette console non validés en l'absence de matériel.

## Essai smartphone du 5 novembre

L'APK ARM64 versionCode **69** a été installé par ADB sur le Samsung SM-S936B sans désinstallation ; la date d'installation initiale est conservée. HOME a été observé en clair et en sombre. Les écrans Android simulés 1240×1080 à 240 dpi ont reçu les identifiants 16 puis 17, ce qui exerce la découverte sans identifiant fixe.

La grille secondaire a été utilisée pour sélectionner Ocarina of Time puis Majora's Mask : boîtier 3D et logo principal suivent la sélection. Les événements de boutons X/A/B injectés par ADB ouvrent les options, les détails du même port, puis le retour HOME. Le parcours accueil Android → retour rapide → retrait/reconnexion du second écran conserve le processus et restaure les deux zones sur l'écran principal. Le journal de crash de ce processus est vide à la fin. L'écran simulé, la veille forcée et les réglages temporaires de rotation ont été retirés ou restaurés à leur valeur initiale.

La correction recrée l'arbre de navigation après fermeture, libère explicitement sa racine Avalonia et attend le rappel Android de fermeture avant de libérer le dialogue natif. Les tests automatisés couvrent aussi la recréation avec conservation de la sélection. Cet essai ne remplace ni une manette physique, ni le firmware et les écrans d'une AYN Thor réelle.
