# PartyBoard intégré à CubeShelf Android

## Utilisation

Ouvrir **Téléchargements**, puis **Télécharger PartyBoard**. La préparation affiche
les octets reçus, la vitesse, la vérification et l'installation des ressources.
Pause conserve le fichier partiel ; Reprendre demande la suite au serveur.
Si le serveur ignore la demande de reprise, le transfert repart du début.
Annuler retire le téléchargement partiel et sa préparation temporaire.

Une fois prêt, **Jouer** ouvre le moteur dans CubeShelf. Le premier lancement
demande le disque personnel de Mario Party 4 ; aucun disque ni données extraites
du disque ne sont inclus. L'intégration concerne le moteur Android ARM64 de
PartyBoard v0.20.0. Les autres jeux du catalogue n'acquièrent pas de port Android.

Le panneau est utilisable au tactile et avec le parcours de commandes existant :
haut/bas pour choisir une action, A pour l'activer, B pour fermer.

### Gestionnaire depuis preview.19

La présentation reprend le gestionnaire PC : débit réseau, pic de la session,
temps restant estimé, graphique, jaquette, transfert actif, file d'attente et
historique. Les taux sont exprimés en Mio/s (1 Mio = 1 048 576 octets).
Le temps restant n'est affiché que lorsque le débit est connu ; la vérification
et la préparation ont leur propre état, sans estimation inventée.

`RuntimeDownloadQueue` sérialise les transferts des moteurs enregistrés.
Les commandes **Tout mettre en pause** et **Reprendre la file** contrôlent
l'ordonnancement ; une préparation déjà en cours se termine avant la pause
de la file. Les éléments en attente peuvent monter avec **Priorité +**.
PartyBoard est actuellement l'unique moteur Android enregistré : le panneau
indique une file vide plutôt que d'inventer des téléchargements d'autres jeux.

L'ordre et les 32 dernières préparations réussies sont conservés dans
`files/download-queue.json`, par remplacement atomique. Une file interrompue
est restaurée en pause. L'installation précédente de PartyBoard est reprise
dans l'historique avec la date de son marqueur, seulement au premier démarrage
du gestionnaire. **Effacer l'historique** ne supprime aucun fichier installé.

Le graphique conserve au plus 240 points réels en mémoire et affiche les
60 secondes précédant le dernier relevé. Il n'ajoute pas de points au repos.
Les miniatures sont décodées à 256 pixels de large et libérées à la fermeture
du panneau ; la bibliothèque et les téléchargements continuent indépendamment.

## Architecture

- Les bibliothèques natives exécutables sont empaquetées et installées avec
  l'APK CubeShelf. Le gestionnaire ne charge aucun code téléchargé à l'exécution.
- La distribution officielle épinglée est téléchargée comme conteneur de
  ressources (52 716 096 octets). Sa somme SHA256 complète est vérifiée avant
  extraction. Seuls `assets/res/*` et `assets/initial_pipeline_cache.db` sont
  conservés ; les classes Java et bibliothèques natives du transfert sont ignorées.
- Les ressources, réglages, sauvegardes et disques préparés par le moteur
  utilisent le dossier privé `files/partyboard`. La préparation ne remplace que
  les ressources du moteur ; elle ne supprime pas les sauvegardes ou les disques.
- `files/partyboard-downloads` conserve le transfert partiel et la préparation
  temporaire. Un marqueur écrit en dernier identifie l'installation prête.
- Un service Android de premier plan accompagne le transfert et sa préparation.
  Une interruption du processus conserve le fichier partiel ; la reprise est
  manuelle au prochain lancement. Une notification permet de rouvrir le panneau,
  selon les autorisations de notification du téléphone.
- L'activité de jeu utilise le processus privé `:partyboard` ; le salon et son
  service utilisent `:partyboard_online`. Les activités internes ne sont pas
  exportées. Les redémarrages du jeu ne ciblent jamais le processus du lanceur.
- Les mises à jour du moteur passent par l'APK CubeShelf. L'ancien mécanisme
  d'installation d'une application PartyBoard séparée est désactivé.
- Android recrée l'activité du lanceur lors d'un changement d'orientation ;
  Avalonia conserve sa bibliothèque et remplace la vue native et sa surface
  Vulkan. Cela évite de présenter vers l'ancienne fenêtre après le jeu.

## Construire

Prérequis : .NET 8 avec le workload Android, JDK 17, Android SDK API 34 et
Python 3.11 ou plus récent. La publication ARM64 exécute automatiquement :

1. `eng/prepare-partyboard-android.py` : télécharge/cache l'APK officiel épinglé,
   vérifie son SHA256 et extrait les bibliothèques pour la compilation.
2. `eng/compile-partyboard-host.py` : compile les sources Java adaptées avec
   `javac --release 8`, puis crée le JAR utilisé sans génération de bindings C#.

Les sources Java suivies proviennent du commit
`10673868b69bbcdada496215656a5b7012145f5c` du dépôt Marioparty4.
`eng/import-partyboard-android.py` reproduit leur import ; les adaptations et
licences sont documentées dans `vendor/partyboard-android/UPSTREAM.md`.
Les sorties `Generated` et le cache `artifacts` ne sont pas suivis par Git.

## Contrôles et limites

Sur Galaxy S20 Android 13, le téléchargement réel, la vérification, la préparation
des ressources et l'ouverture de l'écran natif de sélection du disque ont été
observés. Aucune application PartyBoard séparée n'est nécessaire.
Le retour aux jaquettes 3D et un second lancement/retour depuis le paysage ont
également été observés sans redémarrage du processus principal CubeShelf.
La pause/reprise réseau, une partie complète, une session en ligne et une manette
physique ne sont pas encore validées dans cette passe. Aucun test automatisé
n'a été ajouté ou exécuté. Cette intégration ne garantit pas 120 images/s dans
le jeu ; la cadence du moteur est distincte de celle de la bibliothèque CubeShelf.
