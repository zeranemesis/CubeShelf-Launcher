# Moteurs Android dans DecompShelf

Le build Android 54 conserve PartyBoard et Ghostship intégrés et ajoute un hôte expérimental pour les 23 autres paquets ARM64 du catalogue. Il ne demande plus à Android d'installer ou de lancer les applications de ces jeux séparément. Les jeux et leurs fichiers ne sont pas fournis.

## Parcours

1. Télécharger le moteur depuis sa fiche ou la file de téléchargements.
2. Toucher **Préparer dans DecompShelf**. Le paquet épinglé est vérifié, puis ses bibliothèques et son code sont préparés dans le stockage privé de l'application.
3. Démarrer le moteur et choisir sa propre copie du jeu dans son écran d'importation. Pour MHGU, DecompShelf demande directement le dossier contenant `exefs` et `romfs` avant d'activer Jouer. Il faut la version 1.4.0 ; le moteur vérifie les fichiers du jeu.
4. Utiliser **Retour à DecompShelf** pour revenir à la bibliothèque.

Les sélecteurs de documents et de dossiers Android restent utilisés. La confirmation système d'une mise à jour de DecompShelf lui-même reste également nécessaire. Les anciennes applications de jeu, leurs fichiers et leurs sauvegardes ne sont ni supprimés ni importés automatiquement. Une ancienne application détectée reste dans Mes jeux, mais le moteur doit être préparé dans DecompShelf pour être lancé par cette nouvelle version.

## Stockage et processus

- Code : `files/native-modules/<id>/<révision>/` dans les données privées de DecompShelf.
- Fichiers du jeu : `files/native-games/<id>/` ; les révisions des adaptateurs ne déplacent pas ce dossier.
- Cache : `cache/native-games/<id>/`.
- Préférences : préfixe `engine_<id>_`.
- Processus : `io.github.zeranemesis.cubeshelf:engine_<id>` ; les services de préparation peuvent avoir leur propre sous-processus.

Les processus évitent les collisions de bibliothèques SDL et les arrêts simultanés avec le lanceur. Ils partagent l'UID Android de DecompShelf : il ne s'agit pas d'un bac à sable de sécurité entre moteurs. Seuls les paquets de confiance épinglés dans le catalogue sont acceptés.

## Adaptation

`eng/prepare-native-host.py` vérifie les APK d'origine et génère les composants privés du manifeste, un DEX de ponts et des deltas VCDIFF exacts. Le chargeur enfant contient les classes du moteur ; les faux types utilisés uniquement par javac ne sont pas inclus dans l'APK. Les ressources proviennent de l'APK d'origine, sans réécrire leurs identifiants.

Les adaptations retirent les contraintes `final` des composants hôtes, redirigent les classes de composants explicites, les requêtes de métadonnées, les autorités FileProvider, le stockage externe historique vers le dossier privé du jeu, et les notifications des services vers une icône disponible dans l'application installée. AndroidX Startup suit son initialisation normale via son provider, y compris les initialiseurs qui exigent une création immédiate.

Les versions et empreintes des outils Java sont dans `vendor/native-host/tools.lock.json`. Windows utilise une archive xdelta3 3.2.2 dont le SHA-256 est épinglé ; Linux utilise xdelta3 installé par le workflow. Les deltas n'utilisent pas de compression secondaire et sont appliqués par le décodeur VCDIFF déjà présent dans CubeShelf.Core.

La préparation vérifie le paquet complet, chaque DEX adapté, le pont et chaque bibliothèque ELF ARM64, puis publie une révision complète par renommage. Le code est rendu non modifiable avant publication et revérifié par Java avant chargement. Une annulation ne publie pas de révision partielle et ne remplace pas les sauvegardes.

## Vérification et limites

- Les tests mobiles couvrent les empreintes, l'annulation, la publication des modules, les chemins, les révisions et le parcours de téléchargement/préparation dans la bibliothèque.
- `python eng/test-native-host.py <apk>` vérifie les hôtes, les composants et les empreintes des deltas réellement inclus dans l'APK, ainsi que l'absence des classes de compilation dans le chargeur principal.
- Les 23 APK d'origine ont également été préparés par le service C# avec leurs vrais deltas, et les empreintes des sorties ont été contrôlées.
- Les essais de démarrage utilisent un AVD local Android 11 x86-64 avec traduction ARM64. Ce test n'équivaut pas à un téléphone ARM64. Les moteurs exigeant Android 13 ou Vulkan récent nécessitent un appareil adapté ; la traduction ARM et les pilotes graphiques de l'AVD peuvent aussi échouer.
- Aucune partie complète sur les 23 nouveaux hôtes n'est annoncée comme validée. Les imports de gros jeux et les services nécessitent encore des essais sur téléphone. Aucun APK n'a été installé sur le téléphone de l'utilisateur pour cette livraison.

L'hôte nécessite Android 9 ou plus et l'APK ARM64 de DecompShelf. Les exigences plus strictes de chaque moteur restent appliquées. Les branches originales, licences et versions des paquets restent celles référencées par `src/CubeShelf.Mobile/Catalogs/android-ports.json` ; les archives d'origine et leurs licences sont conservées dans chaque module préparé.
