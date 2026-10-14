# Changelog

## Non publié — consolidation du launcher

- Le disque sélectionné est transmis selon le runtime : variables PartyBoard, argument Ring Out, `STRIKERS_DATA` pour Strikers. Les chemins contenant des espaces restent un seul argument.
- Le catalogue déclare les formats proposés par jeu : Strikers accepte CISO/GCZ et ne propose plus RVZ. Les champs de lancement sont migrés sans effacer les chemins utilisateur, favoris ou statistiques.
- Les ISO/GCM tronqués ou dépourvus de signature GameCube sont refusés. Les images compressées sont explicitement signalées comme non vérifiées par le launcher.
- Les tâches de mods sont enregistrées avant téléchargement et analyse des dépendances. Après redémarrage, elles peuvent être reprises depuis l’historique, avec reconstitution et confirmation du plan avant activation.
- Un serveur répondant HTTP 416 à une reprise d’archive complète déclenche un nouveau téléchargement. Une opération terminée n’affiche plus de reprise possible.
- Le diagnostic ne réclame plus une extraction CubeShelf ou DolphinTool pour les runtimes qui ouvrent eux-mêmes leur disque.
- README et roadmap actualisés ; guide de contribution ajouté.

Validation locale sous Windows : compilation de la solution sans erreur ni avertissement, 168 tests Core et 6 tests archives/disques réussis. Les cinq nouveaux scénarios couvrent les contrats du catalogue, la migration de profils, la reconnaissance des disques, la reprise des tâches de mods et les réponses HTTP Range/416. Le rendu graphique et les lancements réels des jeux restent à vérifier avec des disques utilisateur ; les essais entre deux box, Steam Deck et macOS restent à réaliser.
