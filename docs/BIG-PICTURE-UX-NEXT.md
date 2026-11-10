# Prochaine évolution de Big Picture

## But

Choisir un jeu, comprendre s’il est prêt et accéder à l’action principale sans
faire défiler la fiche. Conserver l’étagère et les boîtiers 3D, au tactile comme
à la manette. Ce document propose la prochaine interface ; il ne décrit pas des
fonctionnalités Android déjà intégrées.

## Premier lot mobile — preview.17

- Action Jouer/Installer séparée de la zone défilante et fixée en bas de la fiche.
- En-tête compact, logo et état d’installation plus courts ; aucune action de
  lancement pour un moteur absent sur Android.
- Retour utilisateur intégré : boutons Retourner/Informations supprimés.
  Toucher pour retourner, maintenir pour agrandir la jaquette 3D ; R3 ouvre
  également l’agrandissement à la manette. B ferme, X retourne.
- Boutons avec dégradé violet pour l’action principale, contours et fond sombre
  pour les actions secondaires, focus blanc et états pressé/survol cohérents.
- Aides tactile/manette exclusives selon la dernière méthode utilisée ; contour
  de focus explicite, B ferme le panneau ouvert avant de revenir à l’action principale.
- Les invitations privées restent regroupées dans leur panneau pour PartyBoard
  installé. Depuis preview.18, le téléchargement et la préparation des ressources
  sont intégrés à CubeShelf ; voir [PartyBoard Android](ANDROID-PARTYBOARD.md).

Le zoom ne redessine pas l’étagère derrière le boîtier agrandi ; son modèle et
ses images sont conservés, avec libération de la vue à sa fermeture.

Ce premier lot concerne l’application mobile. Les sections ci-dessous décrivent
également les évolutions ultérieures ; recherche, filtres et nouvelles sections
ne font pas partie de ce lot. Les propositions Informations/Retourner ci-dessous
sont remplacées sur mobile par les gestes demandés par l’utilisateur.

## Contraintes à conserver

- Android ouvre uniquement Big Picture ; le choix de démarrage reste disponible sur PC.
- Aucun bouton Home ni flèches à l’écran. Navigation par geste, stick ou croix.
- Boîtiers 3D, tranche visible, jaquettes recto/verso et logos des jeux conservés.
- Lancement du moteur depuis CubeShelf avec transition et plein écran.
- Les options avancées existantes restent dépliées par défaut.
- Sur Android, seulement des moteurs natifs compatibles. Aucun lancement via Dolphin.
- Ne pas afficher une action de jeu, de mods ou de jeu en ligne que le moteur ne permet pas.

## Priorité 1 : hiérarchie de la fiche

### Portrait

1. En-tête compact : marque, recherche/filtres et accès aux paramètres.
2. Étagère 3D occupant environ la moitié de l’espace disponible, ajustée aux insets.
3. Logo du jeu, puis une ligne courte : genre, joueurs et état d’installation.
4. Barre d’action persistante dans le bas de l’écran : **Jouer**, **Reprendre**
   si réellement disponible, ou **Installer** si un moteur compatible est publié.
5. Les informations détaillées défilent entre l’étagère et cette barre.

Pour un jeu sans port Android disponible, afficher un état explicite et les
informations du jeu. Éviter un bouton Jouer inactif sans explication ou une
installation proposée pour un moteur incompatible.

### Paysage et PC

Conserver deux colonnes : étagère à gauche, fiche et action principale à droite.
La fiche peut défiler, mais l’action principale et le nom du jeu restent visibles.
Les dimensions doivent suivre la taille réellement disponible, sans hauteur fixe
qui masque les commandes sur un téléphone étroit.

## Priorité 2 : informations utiles au jeu sélectionné

- Remplacer le long texte permanent sur PartyBoard par un accès **Informations**
  pour Mario Party 4. Ne pas afficher ce guide sur les autres jeux.
- Montrer clairement **Prêt**, **À installer**, **Téléchargement en cours** ou
  **Port indisponible** avec un libellé, pas seulement une couleur.
- Regrouper les actions secondaires : Retourner, Informations, Mods et En ligne
  lorsque ces fonctions sont compatibles.
- Garder le tap de retournement déjà utilisé ; ajouter une aide courte à la première
  utilisation pour le rendre découvrable. Ne pas détourner ce geste pour lancer un jeu.
- Logos transparents et proportions conservées, avec une hauteur cohérente qui
  laisse de la place aux commandes.

## Priorité 3 : tactile et manette

- Zones tactiles d’au moins 48 DIP ; boutons secondaires moins envahissants que Jouer.
- Focus visible, un seul élément ciblé, ordre prévisible ; focus principal restauré
  après fermeture d’une fiche secondaire.
- A active l’élément ciblé ; B ferme d’abord le panneau courant ; X retourne le boîtier.
- Afficher les aides de manette quand elle est utilisée et les aides tactiles après
  un geste. Éviter de consacrer un bloc permanent aux deux méthodes simultanément.
- Ajouter une haptique discrète seulement comme option, sans la rendre nécessaire.

## Priorité 4 : accès aux sections

Préparer trois accès simples : **Bibliothèque**, **Téléchargements**, **Paramètres**.
La section Téléchargements doit s’appuyer sur des opérations Android réelles ;
ne pas présenter la file PC comme une fonction mobile déjà disponible.

Sur PC, conserver l’accès direct aux onglets **Mes mods** et **GameBanana** déjà
développés. Sur Android, prévoir ce même parcours seulement après validation des
formats et des capacités du moteur concerné. Les états non compatibles doivent
expliquer ce qui manque.

## Budget graphique

- Les mouvements, retournements et transitions suivent le signal de l’écran.
- Aucun mouvement permanent du fond ou des boîtiers au repos.
- Privilégier transformations du compositeur et ressources réutilisées.
- Éviter flous plein écran animés, nombreuses couches transparentes et chargements
  d’images dans les callbacks d’animation.
- Contrôler séparément défilement, retournement, ouverture d’un panneau et lancement.
  Une moyenne proche de 120 ne constitue pas une validation de 120 images/s constantes.

## Premier lot à réaliser après la cadence

1. Rendre l’action principale persistante et compacter le bas de la fiche.
2. Déplacer les explications techniques vers Informations, propres à chaque jeu.
3. Harmoniser états, focus et aides selon le périphérique utilisé.
4. Mesurer l’interface obtenue avant d’ajouter des effets décoratifs.

Critères : action principale visible sans défilement en portrait et paysage,
aucun piège de focus, geste de retournement préservé, pas d’action incompatible,
et absence de rendu continu au repos.
