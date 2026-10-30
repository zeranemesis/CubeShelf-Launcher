# Validation de DecompShelf 0.19.0-rc.2

Candidate corrective du 30 octobre 2026, Android code **51**. Les catalogues, illustrations et fonctions restent ceux de la [candidate précédente](VALIDATION-0.19.0-rc.1.md). Bip n’a pas été modifié.

## Problème reproduit

L’essai de Mario Party 4 / PartyBoard sur Windows a atteint l’écran titre depuis Big Picture, puis ouvert le panneau de configuration. L’arrêt depuis DecompShelf a fermé le jeu et enregistré le temps de session, mais a ensuite provoqué un plantage du launcher : `InvalidOperationException: No process is associated with this object` dans `GameSessionTracker.GameIdForProcess`.

La lecture de `Process.HasExited` peut déclencher l’événement de fin de processus de façon synchrone, supprimer la session et libérer l’objet pendant la recherche. La correction prend un instantané des sessions avant de parcourir leur état et protège la lecture complète de l’état/PID contre cette libération. Elle concerne le suivi commun des jeux, sans évolution de l’assistance.

## Régression automatisée

Un vrai processus enfant attend l’ordre de terminer sur son entrée standard. Le test désactive ses notifications automatiques pour forcer la détection et la finalisation synchrones pendant la recherche.

- Avant correction : échec avec la même exception que le test réel.
- Après correction : recherche par PID et liste des jeux en cours réussies ; une seule notification de fin ; aucune attribution à un processus fermé.
- Suites complètes et contrôle réel fermeture/relance : résultats à consigner après exécution.

## Essai réel et limites

Le test utilise une copie locale du runtime PartyBoard au commit `c79f0d4551a1ff3c771225dafefecfae6fdbfdcb` (version affichée `v0.20.0-9-dirty`), le disque personnel Mario Party 4 USA Rev 1 et des dossiers de profil distincts. Aucun fichier du disque ou de carte mémoire n’est publié.

L’écran titre est rendu et la carte mémoire du profil de test est créée (16 744 448 octets). Son empreinte est conservée après le premier arrêt. Le clavier neuf n’a pas de touches attribuées ; les tentatives d’affectation par l’outil d’automatisation n’ont pas été enregistrées. Cela ne permet pas de valider les commandes physiques, une partie complète ou la sauvegarde d’une progression.

Les essais sur téléphone, manette physique et autres architectures restent en attente. Les 54 jaquettes manquantes et les modèles de consoles stylisés restent signalés. Windows reste sans signature Authenticode de distribution ; Android conserve la signature persistante historique.
