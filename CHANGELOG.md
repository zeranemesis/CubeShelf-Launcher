# Changelog

## 0.16.0 — 22 octobre 2026

- Gestionnaire de téléchargements illustré : file, pause/reprise, priorités et limite de débit.
- Lancement direct depuis les jaquettes, préparation automatique et options avancées dépliées.
- Big Picture avec étagère 3D par défaut et modèle GLB fourni, tranche visible, rendu mis en cache et animations à la demande.
- Accueil Reprendre, profils de mods, clavier à l’écran et parcours de salons avec vérification avant connexion.
- Premier aperçu Android natif : mêmes jaquettes 3D, navigation tactile/manette sans boutons fléchés, lancement de PartyBoard installé séparément. Les autres jeux, amis et téléchargements PC ne sont pas encore portés ; jeu croisé Android/PC non disponible.
- Génération d’archives PC Windows/Linux/macOS x64 et ARM64.
- Correction d’un délai NAT-PMP devenu négatif à expiration, susceptible d’interrompre le repli UPnP.

Les binaires PC ne sont pas signés avec un certificat de distribution. Android reste un aperçu signé avec une clé de développement ; aucun essai physique de ce launcher mobile ni partie réelle Android n’a été effectué ici.

## Non publié — consolidation du launcher

- Le disque sélectionné est transmis selon le runtime : variables PartyBoard, argument Ring Out, `STRIKERS_DATA` pour Strikers. Les chemins contenant des espaces restent un seul argument.
- Le catalogue déclare les formats proposés par jeu : Strikers accepte CISO/GCZ et ne propose plus RVZ. Les champs de lancement sont migrés sans effacer les chemins utilisateur, favoris ou statistiques.
- Les ISO/GCM tronqués ou dépourvus de signature GameCube sont refusés. Les images compressées sont explicitement signalées comme non vérifiées par le launcher.
- Les tâches de mods sont enregistrées avant téléchargement et analyse des dépendances. Après redémarrage, elles peuvent être reprises depuis l’historique, avec reconstitution et confirmation du plan avant activation.
- Un serveur répondant HTTP 416 à une reprise d’archive complète déclenche un nouveau téléchargement. Une opération terminée n’affiche plus de reprise possible.
- Le diagnostic ne réclame plus une extraction CubeShelf ou DolphinTool pour les runtimes qui ouvrent eux-mêmes leur disque.
- README et roadmap actualisés ; guide de contribution ajouté.

Validation locale sous Windows : compilation de la solution sans erreur ni avertissement, 168 tests Core et 6 tests archives/disques réussis. Les cinq nouveaux scénarios couvrent les contrats du catalogue, la migration de profils, la reconnaissance des disques, la reprise des tâches de mods et les réponses HTTP Range/416. Le rendu graphique et les lancements réels des jeux restent à vérifier avec des disques utilisateur ; les essais entre deux box, Steam Deck et macOS restent à réaliser.
