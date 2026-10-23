# CubeShelf 0.17.0-preview.2 : améliorations du launcher

Cette passe applique le plan au launcher. Les moteurs, leurs capacités réseau,
leurs ports et les fichiers des jeux restent hors périmètre.

## Accéder aux fonctions

- **Bibliothèque** : les boutons de jaquette conservent le lancement direct et
  la préparation automatique existants. Après une sortie anormale, CubeShelf
  propose une nouvelle tentative ou l’accès aux journaux.
- **Big Picture** : Retour remonte le parcours et replace le focus. Les
  manettes ne sont plus interrogées pendant une session ou quand la fenêtre est
  masquée/réduite. Les jaquettes GLB et les commandes tactile/manette sont conservées.
  Le paramètre `--big-picture` permet d’ouvrir directement cette vue.
- **Téléchargements** : pause, reprise, priorité, limite de débit et phases
  restent dans la page illustrée. Les vues masquées ne rafraîchissent plus leurs
  listes, graphiques ou lectures de stockage. Les transferts eux-mêmes continuent.
- **Mods** : « Profils et ordre des mods » prépare une sélection, montre les
  conflits et permet de régler les priorités avant Appliquer. Les profils
  enregistrent désormais les priorités ; les anciens profils restent lisibles.
  « Profil original » désactive la sélection sans désinstaller les mods.
- **Paramètres → Sauvegardes et récupération** : sauvegarde du profil,
  restauration du profil, restauration des cartes déjà sauvegardées,
  retour à une version précédente de CubeShelf et mesure CPU/mémoire.

## Ce qui est sauvegardé

Les points de restauration du profil contiennent `settings.json`,
`desktop-profile.json`, le catalogue personnel `games.json` et les JSON de
`mod-profiles`. Ils ne contiennent ni clés d’identité, ni exécutables de jeux,
ni disques. L’export chiffré d’identité existant reste disponible séparément.

La restauration est programmée puis appliquée au démarrage, avant l’ouverture
des stores. Les JSON et leurs SHA-256 sont vérifiés ; le profil courant est
copié avant remplacement. Les échecs tentent de remettre les fichiers antérieurs.
Les fichiers absents du point choisi ne sont pas supprimés.

Les cartes mémoire proposées sont les copies réellement présentes dans le
répertoire `save-backups` utilisé par le transfert téléphone. Seuls les formats
et chemins déjà reconnus par ce service sont restaurés. Il ne s’agit pas d’un
système universel de sauvegardes pour tous les moteurs. Les sessions suivies
par CubeShelf doivent être fermées avant une restauration.

## Versions précédentes et plateformes

- Windows : le nouvel updater conserve la copie complète précédente et ses
  empreintes. Paramètres prépare une archive vérifiée et réutilise l’updater
  transactionnel. Les anciennes mises à jour qui supprimaient leur copie ne
  peuvent pas fournir rétroactivement cette fonction.
- Linux AppImage : le nouveau helper conserve `CubeShelf.AppImage.previous` ;
  Paramètres permet de la réinstaller par le même helper.
- macOS et distributions Linux installées autrement : installation manuelle
  conservée. Le profil peut être sauvegardé/restauré, mais aucun remplacement
  automatique de paquet système ou de bundle macOS n’est annoncé.
- Manette PC : XInput sur Windows ; lecture SDL2 facultative sur Linux/macOS
  lorsqu’une bibliothèque SDL2 compatible est installée (`libSDL2-2.0.so.0`
  ou `libSDL2.dylib`). L’absence de cette bibliothèque laisse le clavier et
  le tactile/souris utilisables. La lecture suit l’API native de
  [SDL_GameControllerUpdate](https://wiki.libsdl.org/SDL2/SDL_GameControllerUpdate),
  sans créer de fenêtre SDL ni accumuler une file d’événements inutilisée.
  Aucun appareil Linux/macOS n’a validé cette lecture dans cette passe.
- Mobile : optimisation partagée du rendu, dernière jaquette et disposition
  mémorisées. Cet aperçu ne devient pas une copie complète des fonctions PC.

## Performances et validation

Le cache de jaquettes partage les bitmaps entre vues et borne à 24 Mio les
images inutilisées ; les images encore affichées restent en mémoire. Le modèle
réutilise les contours et maillages tant que la pose ne change pas, même quand
le reflet du pointeur change. Les animations restent pilotées par le compositeur,
à la demande, avec le plafond existant de 120 mises à jour/s.

La préversion 1 utilisait `DrawingGroup.Open()` pour enregistrer des bitmaps,
mais Avalonia 11.3.21 lève `NotImplementedException` sur ce chemin. La préversion
2 conserve plutôt les matrices et clips des triangles, puis soumet les images
au contexte de rendu normal du compositeur. Aucune exception de rendu n’est
ignorée pour masquer le défaut.

« Mesurer la consommation » mesure le processus CubeShelf pendant deux secondes,
avec CPU moyen, mémoire résidente, threads et taille estimée des bitmaps partagés.
Il ne mesure ni les FPS des jeux ni la mémoire GPU.

Les composants PC, Mobile et Updater sont compilés. Aucun test automatisé,
rollback réel d’installation, restauration de données utilisateur, essai de
manette physique ou essai sur téléphone n’a été exécuté dans cette passe.
