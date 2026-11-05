# Validation HOME, options X et double écran — 5 novembre 2026

## Résultat

Les deux présentations restent disponibles et mémorisées. HOME réutilise le boîtier 3D sélectionné, les logos et l'audio existants sur Windows et Android. Android peut afficher sa grille sur une seconde Presentation compatible, avec repli sur une seule fenêtre. X ouvre le menu du jeu dans Big Picture Windows et via une manette Android ; voir [les commandes et limites](GAME-OPTIONS-CONTROLLER.md).

## Vérifications exécutées

- `dotnet run --project tests/CubeShelf.Mobile.Tests -c Release` : réussi, dont recréation des deux fenêtres, sélection, une seule scène 3D et musique, tailles tactiles, X/A/B en HOME et Classique et absence de lancement pendant la navigation.
- `dotnet run --project tests/CubeShelf.ShelfUi.Tests -c Release` : réussi, dont menu X en Classique/HOME, paramètres/mods existants, gardes de saisie, Ctrl+X, dialogues et sortie Big Picture.
- Publication Windows x64 autonome et Android ARM64 Release : réussies. Les contrôles des [deux projets fournis](USER-PROJECTS-2026-11-05.md), de GitLab et des chemins d'exécutables avaient également réussi.
- Paquet final : signature locale conservée, 27 hôtes épinglés, 68 composants privés, ABI ARM64, alignement 16 Ko, absence de ROM, APK imbriqué et stubs.
- Téléphone SM-S936B : mise à jour par `adb install -r`, versionCode 69, date d'installation initiale conservée. Sélection tactile synchronisée sur deux écrans, options X/A/B injectées par ADB, ouverture des détails du bon jeu, accueil/reprise et retrait/reconnexion vérifiés. Processus 988 conservé et journal de crash vide après le dernier parcours. Réglages temporaires restaurés.

Les captures et journaux sont dans `../user-ports-2026-11-05/`, notamment `phone-options-69.png`, `phone-dual-final-69.png`, `phone-restored-69.png`, `mobile-tests-final.log`, `shelf-tests-final.log` et `android-69-crash-check.log`.

## Artifacts locaux

- Windows : `../decompshelf-home-thor-windows-2026-11-05/CubeShelf.exe` avec ses fichiers adjacents.
- Android : `../decompshelf-home-thor-android-2026-11-05/io.github.zeranemesis.cubeshelf-Signed.apk`, versionCode 70, version affichée 0.19.0-rc.3.
- SHA-256 APK : `b60a641654bbb3ba1047b7715cc2d607cba35ba8aa3104b4720f673dd755189a` ; fichier `.sha256` adjacent.

La révision 70 aligne le contour de sélection sur les limites et les arrondis de la tuile. Le contour est une surcouche du boîtier de vignette, indépendante du cadrage de l'image. Les tests Mobile contrôlent ses dimensions à 64, 71, 76 et 104 unités, avec images verticales, horizontales et repli sans image ; rendus PC et mobile capturés dans `../tile-selection-2026-11-05/`. Windows a été reconstruit. L'APK 70 a été signé, vérifié puis installé par mise à jour sur le SM-S936B : sélection tactile et événements de manette injectés par ADB vérifiés, captures `tile-after-70.png` et `tile-gamepad-70.png`. La veille forcée temporaire a été désactivée après l'essai. La fenêtre PC active reste une version antérieure ; le nouveau rendu PC est vérifié automatiquement, sans parcours manuel de cette révision.

Aucune release, aucun commit ni push GitHub effectué pour cette passe.

## Limites

AYN Thor et manette physique non disponibles pour ces essais. Le second écran est simulé sur un smartphone ; le comportement du firmware, le focus d'une vraie manette, les performances, la chauffe et l'autonomie restent à valider. La gestion des mods Android reste indisponible. Les réglages graphiques propres à chaque moteur ne sont pas unifiés par ce menu. Aucun essai en jeu de Power Moon ou Recharged n'a été effectué ; taille, empreinte, archive et publication amont ne prouvent pas la jouabilité.
