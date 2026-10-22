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
dans **Options avancées**, dépliées par défaut.

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

## Big Picture

Le bouton **Big Picture** de la barre latérale ou **F11** ouvre une bibliothèque
plein écran avec une étagère de boîtiers GameCube en 3D. La présentation s’inspire
de [Sly Launcher](https://github.com/ioncodes/sly) : fond clair à courbes discrètes,
jeu sélectionné au premier plan, voisins inclinés et fiche compacte à droite.
Le rendu utilise le composant natif de CubeShelf, avec ses jaquettes avant,
arrière et tranche ; aucun code ni visuel de Sly n’est embarqué.
Le jeu choisi utilise le même lancement
automatique que la bibliothèque classique. Les options avancées restent dépliées
par défaut sur sa fiche.

- **Gauche / droite** : changer de jeu ; **haut / bas** : choisir une action.
- **Entrée** : activer l’action sélectionnée ; **Échap / F11** : revenir au bureau.
- **F** : retourner le boîtier ; **V** : alterner les vues **Ruban / Rangée**.
- Souris : sélectionner un voisin, retourner le boîtier sélectionné, parcourir
  l’étagère à la molette, ou utiliser les boutons de navigation.
- Sous Windows, une manette compatible **XInput** peut utiliser la croix ou le
  stick gauche, **A** pour valider, **X** pour retourner le boîtier, **Y** pour
  changer de vue et **B** pour revenir au bureau.

L’étagère garde au plus sept boîtiers chargés et libère leurs images en quittant
Big Picture. La vue vide conserve le bouton de retour au bureau.

La manette est lue uniquement lorsque Big Picture a le focus, hors des dialogues
et des parties. Une pression maintenue ne déclenche pas plusieurs lancements.
La fenêtre retrouve son état et ses dimensions précédents en quittant le mode ;
le retour après une partie conserve le plein écran. La configuration initiale
du fichier de jeu utilise toujours le sélecteur de fichiers du système.

Tests du parcours :

```powershell
$env:CUBESHELF_TEST_FILTER = 'quick play:'
dotnet run --project tests/CubeShelf.Core.Tests -c Release
```
