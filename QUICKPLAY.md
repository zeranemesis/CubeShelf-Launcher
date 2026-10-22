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
Les boîtiers de l’étagère utilisent le modèle fourni
`Assets/Models/Jaquette_GameCube_3D_vierge.glb`, embarqué dans l’application.
Sa coque, ses cadres, ses charnières, ses butées et ses bandes de film sont
conservés. Les trois inserts reçoivent les jaquettes avant, arrière et tranche
du jeu ; les textures vierges du fichier sont remplacées au rendu. L’orientation
et les proportions du modèle sont adaptées à la caméra de CubeShelf.
Le modèle est chargé une fois et partagé entre les boîtiers. Son rendu reste
natif, avec éclairage simplifié et ombres de contact ; il ne nécessite pas de
navigateur. Le lecteur prend en charge cet export précis, pas l’import de GLB
arbitraires. Le boîtier précédent sert de secours si la ressource manque.
La tranche du GLB étant orientée vers l’intérieur, sa visibilité utilise sa
normale physique extérieure. Les poses et le retournement gardent le côté portant
la tranche vers la caméra, y compris au survol.
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

### Fluidité et optimisation

Les animations utilisent les images du compositeur, avec une cible maximale de
120 mises à jour par seconde. L’interpolation dépend du temps écoulé : sa durée
reste cohérente sur les écrans 60, 120 ou 144 Hz. Les boucles s’arrêtent au repos,
hors de la vue ou pendant la minimisation. Les projections des modèles stables
sont mises en cache ; le maillage des textures des boîtiers voisins est allégé.
La cible ne garantit pas 120 images affichées : l’écran, son réglage Windows,
le moteur graphique et les performances de la machine déterminent le résultat.
Aucune mesure de débit réel à 120 Hz n’a été réalisée sur la machine utilisateur.

### Mods et Mario Party 4 en ligne

**Choisir les mods** affiche les mods installés du jeu sélectionné. Les boutons
activent/désactivent les mêmes mods que la bibliothèque classique, y compris les
mods coupés dans PartyBoard. Les formats incompatibles ne peuvent pas être
activés ; un mod incompatible déjà actif peut être désactivé. Les conflits sont
signalés et l’accès à l’installation passe par le catalogue existant.

**Jouer en ligne**, sur Mario Party 4, prépare le disque, le moteur et les données
manquantes puis ouvre la page des salons en plein écran, sans lancer une partie
locale. **Créer une room** permet de choisir les mods de cette room ; les salons
publics peuvent être rejoints depuis la liste avec le contrôle existant des mods.
La barre de retour, Échap ou B ramène à la collection. Les actions des sous-pages
sont accessibles à la manette ; les champs texte peuvent nécessiter un clavier.

Le compagnon Windows PartyBoard doit déclarer les capacités nécessaires. Les
messages existants indiquent un moteur trop ancien, un disque absent ou un relais
indisponible. Une room privée avec invitations utilise les réglages d’identité,
de présence et d’amis existants ; une room publique ne nécessite pas d’ajouter un
ami. Les participants doivent disposer de disques et versions compatibles.
Aucune partie entre deux machines n’a été validée dans cette modification.

Tests du parcours :

```powershell
$env:CUBESHELF_TEST_FILTER = 'quick play:'
dotnet run --project tests/CubeShelf.Core.Tests -c Release
```
