# Lancements et mises à jour sans forks

## Windows

Le premier lancement d’un port avec une unique archive compatible passe par la file de téléchargement existante. Le paquet est vérifié par taille et SHA-256, extrait dans un nouveau dossier privé, puis associé si un seul programme de jeu PE compatible est identifiable. Les installateurs, outils et paquets ambigus nécessitent toujours un choix explicite. Aucun programme téléchargé n’est exécuté pour le détecter.

Le menu Big Picture du jeu donne accès à « Moteur et mises à jour ». La recherche utilise la dernière release stable du dépôt officiel et conserve la famille de paquet et l’architecture déjà connues. Un SHA-256 GitHub est obligatoire. Les mises à jour ne sont pas installées à l’insu du joueur. La vérification au démarrage respecte le réglage existant de recherche des mises à jour des jeux.

Une mise à jour gérée garde l’ancien moteur, copie les sauvegardes reconnues et les paramètres modifiés près de l’exécutable, puis change l’association après réussite. Les installations externes ne sont pas remplacées automatiquement. « Revenir au moteur précédent » change cette association sans supprimer les deux versions. Les sauvegardes créées depuis la mise à jour restent dans le nouveau dossier : leur conversion vers un ancien format n’est pas automatique. Les chemins de sauvegarde externes et formats particuliers restent à gérer par les adaptateurs du jeu et la gestion du stockage.

Le lancement utilise les contrats déjà présents dans DecompShelf. Les options plein écran sont appliquées uniquement lorsque le port possède un contrat connu ; une option universelle ne peut pas être imposée à tous les exécutables tiers.

## Android

Les moteurs restent épinglés au catalogue et aux adaptateurs livrés avec l’APK DecompShelf. Après une mise à jour de DecompShelf, un moteur précédemment préparé dans une ancienne révision est reconnu. Le moteur correspondant au nouvel adaptateur est téléchargé automatiquement seulement sur une connexion déclarée non facturée par Android, en respectant la pause de la file. S’il est déjà en cache, sa préparation peut commencer hors connexion.

Avant préparation : taille, empreinte, nom de paquet et code de version sont vérifiés. La préparation publie une nouvelle révision dans un autre dossier, sans déplacer les données de jeu ni leurs sauvegardes. Une erreur conserve la révision précédente sur disque. Elle ne garantit pas que celle-ci reste lançable avec un nouvel adaptateur incompatible : la mise à jour doit aboutir pour utiliser le moteur actuel.

Ce mécanisme n’installe pas une autre application de jeu et ne charge pas automatiquement un APK amont inconnu. Les changements de composants Java ou natifs nécessitent de régénérer et valider les adaptateurs, puis de construire une nouvelle version de DecompShelf. Aucun nouveau fork n’est nécessaire pour cette surveillance et cette préparation.

## Surveillance des dépôts

`eng/watch-native-upstream.py` observe les commits et releases officielles des catalogues PC et Android. Il produit un rapport et des catalogues candidats séparés, sans modifier les catalogues livrés ni exécuter le code amont. Les propositions Android demandent une reconstruction des adaptateurs.

Le workflow `native-upstream-watch.yml` programme cette observation chaque jour à 07:17 UTC avec des permissions GitHub en lecture seule. Il deviendra actif après publication sur la branche par défaut ; ajouter le fichier localement ne l’active pas sur GitHub.

L’observation du 4 novembre 2026 a couvert 639 dépôts : 237 entrées modifiées, dont 127 demandant une revue d’intégration. Il ne s’agit pas de 237 mises à jour de jeux validées. Métadonnées, compilation et contrôles de paquets ne prouvent pas la jouabilité.

## Vérification

Les tests NativePorts couvrent sélection d’exécutable, migration des fichiers, maintien de l’ancien moteur, annulation, archives ambiguës, empreinte invalide et fusion des associations concurrentes. Les tests Mobile couvrent détection d’une ancienne révision et préservation des données lors d’une préparation réussie ou refusée. Le watcher dispose de `--self-test`.
