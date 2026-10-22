# Jouer depuis la bibliothèque

Le bouton principal de chaque jaquette donne accès directement au jeu :

- **Configurer et jouer** : choisir son image de disque. CubeShelf mémorise ce choix,
  installe les composants manquants et lance le jeu lorsqu’il est prêt.
- **Installer et jouer** : le disque est déjà connu ; seule l’installation manque.
- **Préparer et jouer** : le moteur est installé ; CubeShelf prépare les données
  nécessaires, puis démarre le jeu sans autre clic.
- **Jouer** : tout est prêt, le jeu démarre directement sans attendre une recherche
  de mise à jour. **Arrêter** remplace ce bouton pendant une partie.

La même action est disponible sur la fiche du jeu. Les sélecteurs manuels de
disque et d’exécutable, réparation, préparation manuelle et sources sont regroupés
dans **Options avancées**, repliées par défaut.

Une opération d’installation/préparation déjà dans Téléchargements est réutilisée.
Deux clics ne créent pas deux installations ni deux lancements. On peut consulter
un autre jeu pendant l’attente : la demande conserve le jeu d’origine. Le bouton
**Annuler le lancement** supprime ce démarrage automatique ; les téléchargements
restent gérables dans leur onglet. Une annulation ou un échec dans la file empêche
également le démarrage automatique.

Les moteurs qui lisent ou préparent eux-mêmes leur disque gardent ce fonctionnement :
CubeShelf ne leur ajoute pas d’extraction. Pour un RVZ nécessitant une conversion,
DolphinTool est récupéré automatiquement sur les plateformes où cette acquisition
est disponible. Ailleurs, un message indique ce qu’il faut installer. Une variante
qui nécessite le patch d’un mod doit toujours disposer de ce mod dans l’onglet Mods.
Un jeu personnalisé sans source d’installation demande une fois son programme.

Seuls les fichiers de jeu fournis par l’utilisateur sont utilisés ; le launcher
ne récupère pas de disque de jeu.

Tests du parcours :

```powershell
$env:CUBESHELF_TEST_FILTER = 'quick play:'
dotnet run --project tests/CubeShelf.Core.Tests -c Release
```
