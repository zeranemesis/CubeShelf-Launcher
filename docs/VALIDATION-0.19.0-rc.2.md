# Validation de DecompShelf 0.19.0-rc.2

Candidate corrective du 30 octobre 2026, Android code **51**. Elle complète la [candidate précédente](VALIDATION-0.19.0-rc.1.md) avec les ports Android ARM64 et les corrections ci-dessous. Bip n’a pas été modifié.

## Problème reproduit

L’essai de Mario Party 4 / PartyBoard sur Windows a atteint l’écran titre depuis Big Picture, puis ouvert le panneau de configuration. L’arrêt depuis DecompShelf a fermé le jeu et enregistré le temps de session, mais a ensuite provoqué un plantage du launcher : `InvalidOperationException: No process is associated with this object` dans `GameSessionTracker.GameIdForProcess`.

La lecture de `Process.HasExited` peut déclencher l’événement de fin de processus de façon synchrone, supprimer la session et libérer l’objet pendant la recherche. La correction prend un instantané des sessions avant de parcourir leur état et protège la lecture complète de l’état/PID contre cette libération. Elle concerne le suivi commun des jeux, sans évolution de l’assistance.

## Régression automatisée

Un vrai processus enfant attend l’ordre de terminer sur son entrée standard. Le test désactive ses notifications automatiques pour forcer la détection et la finalisation synchrones pendant la recherche.

- Avant correction : échec avec la même exception que le test réel.
- Après correction : recherche par PID et liste des jeux en cours réussies ; une seule notification de fin ; aucune attribution à un processus fermé.
- Suite Core locale complète : 197 tests réussis. Suite ShelfUi réussie. Le contrôle réel fermeture/relance après correction reste à effectuer.

## Android

- [14 ports ARM64 supplémentaires](ANDROID-ARM64-CATALOG.md), 23 entrées de ports plus PartyBoard intégré, sur 8 plateformes : GameCube, Wii, Nintendo 64, Super Nintendo, Game Boy, Game Boy Color, Game Boy Advance et Wii U. Aucune plateforme PC sans port Android n’est exposée.
- Vérification des tailles, SHA-256, signatures APK, manifestes et bibliothèques ELF64 AArch64 des 14 nouveaux paquets. Versions, prérequis, avertissements et illustrations exactes épinglés dans le catalogue. Aucun jeu ni BIOS distribué.
- Correction du bouton Jouer qui ne lançait que les ports Nintendo 64 ; tous les ports du catalogue utilisent maintenant le service Android approprié.
- Retour aux consoles par le retour Android ou B, annulation prioritaire des fenêtres et préparations, et maintien de la console/du jeu au retour d’une activité.
- Mario Party 4 utilise l’ouverture du boîtier et son vrai disque avant de démarrer. L’ouverture s’adapte aux petits écrans portrait et paysage ; annuler empêche le lancement.
- Suite Mobile : chargement des 23 ports, visibilité des paquets Android 11, présence des illustrations, lancement des 23 ports avec pause/reprise simulée, reprise après erreur et relance de PartyBoard. Cadrage et bouton Retour vérifiés à 320×700, 390×844 et 844×390 ; captures examinées. Ces tests utilisent un hôte simulé et ne valident pas le gameplay sur téléphone.

## Essai réel et limites

## Paquets et intégration continue

Révision applicative : `6f48c985dc51336472a37636367935eb4fb30749`.

- [CI Windows et Linux](https://github.com/zeranemesis/CubeShelf-Launcher/actions/runs/41024203357) réussie : construction et suites Core, archives/disques, NativePorts, ShelfUi, Mobile, AppUpdates et assistance existante.
- [Deux APK Android](https://github.com/zeranemesis/CubeShelf-Launcher/actions/runs/41024194414) construits et signés, vérification des moteurs Ghostship et PartyBoard incluse. Contrôle local supplémentaire du paquet ARM64 final : identité, version/code 51, SHA-256, certificat historique et présence des 23 identités de ports.
- APK ARM64 SHA-256 : `e379d8cc45348d0ae282ff6ceea8f3d794216cf6693f684b9d5edd6605073bd1`.
- [Six cibles bureau](https://github.com/zeranemesis/CubeShelf-Launcher/actions/runs/41024199038) construites. Installateur Windows x64 : mise à niveau de 0.18.0, contrôle des fichiers installés, désinstallation et conservation du profil réussis sur un runner jetable.

## Essai réel et limites

Le test utilise une copie locale du runtime PartyBoard au commit `c79f0d4551a1ff3c771225dafefecfae6fdbfdcb` (version affichée `v0.20.0-9-dirty`), le disque personnel Mario Party 4 USA Rev 1 et des dossiers de profil distincts. Aucun fichier du disque ou de carte mémoire n’est publié.

L’écran titre est rendu et la carte mémoire du profil de test est créée (16 744 448 octets). Son empreinte est conservée après le premier arrêt. Le clavier neuf n’a pas de touches attribuées ; les tentatives d’affectation par l’outil d’automatisation n’ont pas été enregistrées. Cela ne permet pas de valider les commandes physiques, une partie complète ou la sauvegarde d’une progression.

Les essais sur téléphone, manette physique et autres architectures restent en attente. Les 54 jaquettes manquantes et les modèles de consoles stylisés restent signalés. Windows reste sans signature Authenticode de distribution ; Android conserve la signature persistante historique.
