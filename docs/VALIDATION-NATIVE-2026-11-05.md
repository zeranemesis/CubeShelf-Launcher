# Validation du 5 novembre 2026

## Contrôles automatisés

- Core : 198 tests réussis.
- NativePorts : catalogues des 30 consoles, 663 références, nouveaux paquets et provenance, détection PE compatible, installation sans exécution de l'archive, migration des paramètres/sauvegardes, annulation, échec, ambiguïté, retour à l'ancien moteur et fusion des associations concurrents réussis.
- Mobile : 28 ports Android, identités distinctes, téléchargements, intégrité, préparation d'une nouvelle révision et préservation des sauvegardes, navigation et présentations réussis. Le test du bouton principal inclut huit lignes de message dans des fenêtres de 320×700, 390×844, 844×390 et 832×360.
- ShelfUi : navigation des catalogues et bibliothèque personnelle, jaquettes, logos, thèmes, sons, menu Big Picture, sortie et retour bureau, blocage de l'installation d'un moteur utilisé réussis.
- Logos : 555 associations, 549 fichiers PNG distincts, 32 ressources utiles à Android et 17 replis explicites vérifiés.
- Watcher GitHub : auto-tests réussis, essai réel sur deux dépôts sans erreur après séparation des budgets REST et GraphQL. La passe complète précédente a observé 639 dépôts ; le workflow local n'est pas encore publié sur GitHub.

## Paquets

Windows : publication autonome x64 dans `../decompshelf-ports-windows-2026-11-05/`.

Android : publication ARM64 Release, version `0.19.0-rc.3`, code de version **65**, 173 656 973 octets. Le paquet contient 27 hôtes épinglés et 68 composants privés. Vérifications des adaptations, absence des stubs de compilation, bibliothèques ARM64, alignement 16 Ko, notices et absence de ROM ou d'APK imbriqué réussies.

SHA-256 APK : `3ba033672fe3151deeccbfee9b11a2a7a6987dec6836cd30b74690657a922dc7`.

Certificat SHA-256 : `3c5e62f4713b437cee47fca52246e1edff3b52252cf2472d8ad7695f184b8344` (même signature locale que les APK précédents).

## Téléphone réel

Samsung SM-S936B connecté par ADB, pages mémoire de 4 Ko. Mise à jour depuis le code 57 vers 63, puis 64 et 65 par `adb install -r`, sans effacement. La date de première installation du 25 octobre est conservée. Démarrage et navigation par consoles, sélection Nintendo 64 et GBA, présence des nouveaux jeux et de leurs illustrations contrôlés.

AeroGauge : téléchargement du paquet officiel de 7 Mo, vérification et préparation dans DecompShelf réussis. Le moteur préparé dans le code 64 reste prêt après le passage au code 65. Deux lancements successifs de son écran de configuration fonctionnent dans le composant privé `io.decompshelf.enginebridge.P_n64_aerogauge_0`, avec des processus de jeu distincts, puis retour à la bibliothèque. Aucune application AeroGauge séparée n'a été installée. Aucun fichier du jeu AeroGauge n'a été fourni : le rendu d'une course, les performances et les sauvegardes de ce nouveau jeu n'ont pas été testés.

Un contrôle initial de régression sur Mario Party 4 déjà préparé a atteint l'écran titre et la sélection de carte mémoire avec les commandes tactiles. Aucune action de suppression, de formatage ou d'écrasement de sauvegarde n'a été demandée. Le test s'est ensuite concentré sur les nouveaux ports.

Le contrôle a révélé un message de téléchargement repoussant l'action principale hors écran en paysage. Le message est maintenant dans la zone de détails défilante ; les actions restent dans leur zone fixe. APK 65 installé après correction et tests de géométrie réussis.

La veille temporairement désactivée pendant les essais a été rétablie à sa valeur initiale (0). Les deux fichiers de capture temporaires créés dans le stockage partagé du téléphone ont été retirés. Les captures et journaux sont conservés dans `../android-embedded-engines/native-updates-2026-11-05/`.

## Limites

Aucune partie complète des cinq nouveaux ports Windows ou des quatre ports Android n'a été validée. Les autres nouveaux moteurs Android n'ont pas été démarrés sur ce téléphone. Un paquet vérifié et un écran de préparation fonctionnel ne constituent pas une certification de jouabilité. Les sauvegardes au format propre à un moteur et les mises à jour de ses adaptateurs demandent toujours des essais avec les fichiers personnels du joueur. Aucun commit, push ou release GitHub effectué dans cette passe.
