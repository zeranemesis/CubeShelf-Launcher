---
name: DecompShelf
description: "Deux thèmes de bibliothèque natifs, Classique et Double écran, avec les images des jeux conservées."
colors:
  classic-dark-bg: "#050811"
  classic-dark-sidebar: "#090E19"
  classic-dark-panel: "#111827"
  classic-dark-panel2: "#1A2538"
  classic-dark-border: "#3B4B69"
  classic-dark-text: "#F8FAFF"
  classic-dark-muted: "#B7C3DA"
  classic-dark-input: "#172235"
  classic-dark-hover: "#2A3852"
  classic-dark-accent: "#8A76FF"
  classic-dark-accent2: "#A895FF"
  classic-dark-danger: "#F05A78"
  classic-dark-success: "#5DE09A"
  classic-dark-warning: "#F2B45F"
  classic-light-bg: "#F2F4F9"
  classic-light-sidebar: "#FFFFFF"
  classic-light-panel: "#FFFFFF"
  classic-light-panel2: "#F8F9FC"
  classic-light-border: "#D8DEEA"
  classic-light-text: "#151927"
  classic-light-muted: "#66718B"
  classic-light-input: "#FFFFFF"
  classic-light-hover: "#E9EDFA"
  classic-light-accent: "#6347EE"
  classic-light-accent2: "#7359F0"
  classic-light-danger: "#D33C5D"
  classic-light-success: "#299E63"
  classic-light-warning: "#B87416"
  dual-light-bg: "#E9EDF0"
  dual-light-text: "#202E38"
  dual-light-panel: "#F8FAFC"
  dual-light-border: "#B9C8D1"
  dual-dark-bg: "#101820"
  dual-dark-text: "#F4F8FC"
  dual-dark-panel: "#1A2632"
  dual-dark-border: "#52687A"
  dual-selection: "#1686BC"
  dual-thumbnail: "#E2EAF0"
typography:
  dual-heading:
    fontSize: "20px"
  dual-title:
    fontSize: "24px"
    fontWeight: 700
  dual-title-compact:
    fontSize: "18px"
    fontWeight: 700
  dual-body:
    fontSize: "13px"
rounded:
  control: "10px"
  dual-thumbnail: "8px"
  dual-item: "12px"
  dual-panel: "16px"
spacing:
  dual-inline: "8px"
  dual-row: "10px"
  dual-inner: "12px"
  dual-outer: "16px"
components:
  classic-button-dark:
    backgroundColor: "{colors.classic-dark-panel2}"
    textColor: "{colors.classic-dark-text}"
    rounded: "{rounded.control}"
    padding: "9px 14px"
  classic-button-primary-dark:
    backgroundColor: "{colors.classic-dark-accent}"
    textColor: "white"
    rounded: "{rounded.control}"
    padding: "9px 14px"
  classic-input-dark:
    backgroundColor: "{colors.classic-dark-input}"
    textColor: "{colors.classic-dark-text}"
    rounded: "{rounded.control}"
    padding: "9px 12px"
  dual-action:
    typography: "{typography.dual-body}"
    rounded: "{rounded.control}"
    padding: "8px 12px"
  dual-panel-light:
    backgroundColor: "{colors.dual-light-panel}"
    textColor: "{colors.dual-light-text}"
    rounded: "{rounded.dual-panel}"
  dual-panel-dark:
    backgroundColor: "{colors.dual-dark-panel}"
    textColor: "{colors.dual-dark-text}"
    rounded: "{rounded.dual-panel}"
  dual-thumbnail:
    backgroundColor: "{colors.dual-thumbnail}"
    rounded: "{rounded.dual-thumbnail}"
    height: "68px"
    width: "68px"
---

# Design System: DecompShelf

## Overview

**Creative North Star: "Deux façons de parcourir la même collection"**

DecompShelf conserve Classique et propose un deuxième thème optionnel, Double écran, inspiré de la 3DS. Les jaquettes et les logos existants donnent leur identité aux jeux ; les commandes natives restent lisibles et familières.

Classique garde ses boîtes 3D et ses écrans existants. Double écran est une extension de bibliothèque à deux régions dans une même fenêtre, sur Windows, en Big Picture et sur smartphone. Ce choix de disposition est indépendant du clair/sombre ; il ne redéfinit pas toute l'application.

**Key Characteristics:**

- Deux thèmes explicites, Classique et Double écran.
- Jaquettes complètes, logos existants et titre lisible en repli.
- Navigation tactile, clavier et manette ; action de lancement explicite.
- Musique locale volontaire, liée au jeu et arrêtée en quittant la vue.

Sources de vérité : `src/CubeShelf.Desktop/App.axaml` pour les ressources de Classique ; `src/CubeShelf.Desktop/Controls/DualScreenLibraryView.cs` pour Double écran partagé avec le mobile ; `PRODUCT.md` et `docs/UI-DUAL-SCREEN-BRIEF.md` pour le choix confirmé. Les captures de référence sont des rendus headless : `../android-embedded-engines/dual-ui/dual-390-dark.png`, `dual-pc-light.png` et `desktop/dual-desktop-window.png`.

## Colors

La coque Classique utilise un accent violet sur des surfaces neutres ; Double écran ajoute des panneaux froids et une sélection bleue.

### Primary

- **Violet Classique** : les tokens `classic-*-accent` servent aux actions principales, au focus et aux états actifs du bureau ; `accent2` accompagne l'accent existant.
- **Bleu de sélection** : `dual-selection` entoure la miniature sélectionnée. Il ne devient pas automatiquement le fond du bouton de lancement.

### Neutral

- **Classique bureau** : `bg`, `sidebar`, `panel` et `panel2` structurent la fenêtre ; `text` et `muted` portent les deux niveaux de lecture ; `border`, `input` et `hover` distinguent les contrôles et leurs états. Choisir ensemble les tokens de la variante claire ou sombre.
- **Double écran** : `dual-*-bg`, `panel`, `text` et `border` définissent la région de bibliothèque. Le fond des petites images reste `dual-thumbnail` dans les deux variantes.
- Les boutons et la recherche de Double écran héritent des ressources Fluent et de l'hôte. Sur Windows, ils utilisent notamment les ressources de `App.axaml` ; ne pas leur attribuer une palette bleue autonome.
- Les tokens Classique extraits ici sont ceux du bureau. Le Big Picture Classique et les écrans mobiles existants conservent leurs propres fonds et styles ; cette documentation ne les unifie pas rétroactivement.

### États

Les tokens Classique `danger`, `success` et `warning` gardent leurs rôles sémantiques. Une jaquette ou une release disponible ne remplace jamais le texte d'état du port.

Les rampes du sidecar sont des échantillons de présentation synthétisés en OKLCH, non des couleurs de production supplémentaires.

## Typography

La famille de caractères reste celle héritée de Fluent et de l'hôte Avalonia. Aucune nouvelle police n'est introduite.

- **Titre de jeu Double écran** : rôle `dual-title`, gras ; rôle compact `dual-title-compact` quand la largeur du contrôle est inférieure à 480 unités. Le contrôle titre est limité en hauteur (85 unités).
- **Titre de collection** : rôle `dual-heading`.
- **État, pagination, messages et commandes Double écran** : rôle `dual-body`. L'état visible est limité à trois lignes et peut être ellipsé ; une infobulle fournit l'état complet mis à jour.
- Les boutons du bureau héritent de la graisse SemiBold. Ne pas généraliser cette graisse à toutes les surfaces mobiles.
- Les quatre logos fournis remplacent visuellement le titre pour les jeux concernés ; le titre reste nommé pour l'accessibilité. Les autres jeux ont un nom textuel gras avec retour à la ligne.

## Layout

Les mesures de la source Avalonia sont en unités indépendantes de la densité ; les valeurs `px` du frontmatter en sont la transcription portable.

Classique conserve ses compositions existantes : navigation latérale du bureau, cartes et boîtes 3D, ainsi que son mode Big Picture. Double écran remplace uniquement la région de bibliothèque et conserve la navigation latérale Windows.

Dans Double écran, la grille racine possède quatre rangées (`Auto,*,Auto,*`) : commandes, aperçu, titre/pagination et grille. L'aperçu et la région de grille partagent donc la place extensible. L'aperçu comporte deux colonnes (`2*,3*`) : image complète à gauche, logo ou nom, état et commandes à droite. Sur smartphone, ce sont deux régions du même écran, pas deux écrans physiques.

La marge externe et celle de l'aperçu utilisent `dual-outer` ; l'écart des rangées utilise `dual-row`. Le panneau inférieur utilise `dual-inner`, avec recherche, grille et commandes de pagination. Les espacements des textes et commandes utilisent `dual-inline`.

La grille remplit sa largeur avec des emplacements (84 × 88 unités) ; la miniature est carrée mais son contenu conserve son ratio. Les images horizontales ne sont ni étirées ni recadrées pour remplir le carré. Chaque page contient au plus 24 éléments. Les petites images sont décodées à une hauteur cible de 128 pixels, l'aperçu à 640 ; le cache libère les références au détachement.

Le thème est utilisable avec une bibliothèque personnelle et un catalogue de plateformes propre à l'appareil. Aucune dimension de téléphone ne vaut validation d'un écran AYN Thor ; le routage vers deux écrans physiques reste à étudier.

## Elevation & Depth

### Mouvement de sélection — 0.19.0-rc.6

La sélection reprend le geste d’une boîte présentée au joueur : la nouvelle jaquette pivote brièvement de la tranche vers la face. La miniature sélectionnée monte de 2 unités et atteint une échelle de 1,045 ; l’appui descend à 0,975. Le contenu de l’aperçu reste visible (opacité minimale 0,86), avec un déplacement horizontal de 8 unités sur 220 ms, dans le sens de navigation. Pas de boucle, pas de nouvelle ombre animée, ni de temporisation imposée avant l’action. Les animations suivent le compositeur, se coupent au masquage et s’interrompent au profit de la sélection courante. Le réglage de réduction des animations annule ces mouvements ; Android le reprend du système au retour dans l’application.

Classique conserve la profondeur de ses boîtes 3D. Double écran est plat : panneaux différenciés par leur tonalité et un trait de bordure, sans nouvelle ombre ni nouvelle animation décorative. Les transitions natives de boutons du bureau restent héritées ; elles ne définissent pas un nouveau langage d'animation du thème.

## Shapes

Les deux panneaux de Double écran ont les coins `dual-panel` et un contour (1 unité). Les cases de sélection utilisent `dual-item`, une marge (2 unités), un padding (4 unités) et un contour (2 unités). La petite image utilise `dual-thumbnail`. Les boutons reprennent `control`.

Ces formes appartiennent aux composants concernés ; elles ne remplacent pas les formes des boîtiers de jeux ni les cartes des autres écrans.

## Components

### Buttons

Les boutons Double écran ont une hauteur minimale (48 unités), le padding `dual-action` et une taille de texte `dual-body`. Leur fond, leurs états et leur focus restent natifs et hérités. L'action principale est nommée d'après l'état réel : ouvrir, préparer, jouer ou configurer. Une simple sélection ne lance pas le jeu.

Sur le bureau Classique, les boutons ont une hauteur minimale (40 unités), une largeur minimale (44 unités), les coins `control`, un contour (1 unité) et une marge (4 unités). Les variantes principale et danger conservent leurs couleurs sémantiques. Survol : fond hover, contour accent et échelle (1.035) ; pression : fond accent, texte blanc et échelle (0.985) ; focus visible : contour accent (2 unités). Les transitions existantes durent 140 ms. Il s'agit du style bureau existant, pas d'une nouvelle règle tactile.

### Inputs / Fields

La recherche Double écran a une hauteur minimale (48 unités), un nom accessible et un style hérité. La recherche filtre la grille et remet la pagination au début ; si elle change le jeu sélectionné, l'aperçu et l'identifiant transmis à l'hôte suivent ce même jeu.

Le champ Classique bureau utilise les ressources input, text et border, avec focus accent. Ne pas coder les couleurs de l'hôte dans le composant partagé.

### Cards / Containers

Les panneaux Double écran groupent aperçu et grille. Ils n'ajoutent aucune ombre. Une miniature est un emplacement de sélection, pas une nouvelle jaquette carrée ; le dessin conserve son ratio avec `Stretch.Uniform`. Une marque de console remplace l'image absente.

### Navigation

Consoles, Mes jeux et Paramètres restent accessibles au-dessus de Double écran. Les boutons peuvent revenir à la ligne. Le bureau conserve aussi son menu gauche. Les flèches et la manette déplacent la sélection sur deux axes ; ce déplacement donne le focus à la grille. La confirmation respecte le contrôle actif et l'action explicite.

Précédente et Suivante indiquent la disponibilité des pages. Sans résultat, le lancement, les options et la musique sont désactivés ; un message propose une autre recherche ou le catalogue.

### Aperçu musical

Le menu Musique permet d'associer volontairement un MP3 ou WAV local au jeu et d'activer ou couper la lecture. Les associations et le choix du thème sont mémorisés par appareil ; aucune piste de jeu n'est livrée.

La sélection attend 350 ms avant de demander la piste, pour éviter les relances pendant un défilement rapide. La lecture s'arrête quand la vue est masquée ou détachée, quand l'hôte la désactive et au départ d'un jeu. Cette temporisation est un comportement audio, pas une animation visuelle.

## Do's and Don'ts

### Do:

- **Do** conserver Classique et proposer Double écran comme deuxième choix distinct du clair/sombre.
- **Do** conserver les proportions des images, y compris les boîtes horizontales et verticales.
- **Do** afficher le titre du jeu si aucun logo n'est fourni et conserver son nom accessible.
- **Do** conserver la navigation latérale sur PC et les accès Consoles, Mes jeux et Paramètres.
- **Do** garder une action de lancement explicite et afficher l'état réel du port pour l'appareil.

### Don't:

- **Don't** étendre la composition Double écran aux autres écrans de gestion sans décision produit.
- **Don't** redistribuer de bande-son de jeu ni importer des illustrations d'iiDB pour ce thème.
- **Don't** présenter les rendus headless comme un essai sur téléphone ou sur AYN Thor.
- **Don't** transformer les anciennes icônes Unicode de Classique en règle pour les nouveaux composants.

