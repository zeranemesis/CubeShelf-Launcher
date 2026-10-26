# CubeShelf Universal — architecture et UX (proposition initiale)

## Portée

Conserver **CubeShelf.Core**, le launcher Avalonia et son flux GameCube actuellement utilisé pour PartyBoard, Sunshine, Ring Out et Strikers. Ajouter une bibliothèque multi-console sans associer par défaut les plateformes à un émulateur installé. Un profil visuel ne signifie **pas** qu'un jeu est lançable.

La v0.15.0 possède déjà `GameCaseControl` (projection 3D de boîtiers avec textures), `GameCatalogService` et les systèmes existants de mods, mises à jour, profils et réseau. **Ne pas réécrire ces composants.**

## Contrat de données

- `GameCatalogEntry.PlatformId`: identifiant de console stable (ex. `gamecube`, `ds`, `ps2`), valeur `gamecube` pour les anciens profils.
- `ConsoleMediaProfiles.Resolve(platformId)`: profil de présentation (famille de média, boîtier, identifiant du template et animation).
- Formats du registre : cartouche, disque, mini-disque, carte, UMD, HuCard, cassette et numérique.
- Un `TemplateId` décrit uniquement la géométrie et ses emplacements de texture. Il ne fournit **aucune jaquette sous copyright**.
- Séparer plus tard `PlatformId` (console), `MediaVariant` (édition, région, multi-disque) et `LaunchProfile` (émulateur natif ou externe).

## UX cible

1. **Bibliothèque** : étagère par console / toutes les consoles ; filtres année, favoris, région, mods, ports natifs.
2. **Fiche du jeu** : boîte manipulable, tranche et arrière ; action distincte « ouvrir la boîte ».
3. **Vue média** : disque/cartouche au format de la console ; rotation, zoom et texture utilisateur facultative.
4. **Insertion** : animation selon `InsertAnimationId`, annulable et désactivable pour accessibilité.
5. **Jouer** : lancement uniquement si le backend est configuré et les fichiers appartiennent à l'utilisateur ; erreur explicite sinon.
6. **Mods** : réutiliser les mécanismes existants lorsque le runtime les supporte ; ne pas annoncer les mods ni le netplay pour les autres jeux sans intégration réelle.

## Etapes recommandées

### P0 — Socle (ce PR)
- Registry de profils et formats de média multi-console, sans moteur graphique additionnel.
- `PlatformId` migrable dans `GameCatalogService`, anciens jeux assimilés à GameCube.
- Tests de registre, fallback et conservation des statistiques lors d'une migration.

### P1 — Interface desktop
- Introduire `IMediaPresentation` et des rendus `Case` / `Cartridge` / `Disc` / `GameCard` / `Digital`.
- Réutiliser `GameCaseControl` pour la famille de boîtiers, avec paramètres largeur/hauteur/profondeur plutôt qu'un nouveau moteur 3D immédiatement.
- Conserver les images de jaquettes existantes ; prendre en charge les médias sans art fourni par défaut.
- Navigation manette et tactile, animations réduites configurables ; viser 60 images/s sans garantie matérielle.

### P2 — Import et lancement
- Indexer les dossiers définis par l'utilisateur (ne pas télécharger de ROMs).
- Choisir un backend par jeu et par appareil. Desktop : exécutable local avec arguments contrôlés.
- Gestion des multiples disques, des variantes PAL/NTSC et de plusieurs installations d'un même jeu.
- Cache des textures et chargement différé pour éviter de saturer la RAM.

### P3 — Android (application distincte)
- Ajouter un projet Android et réutiliser le cœur de données compatible, après audit des dépendances.
- Lancer les émulateurs externes via leurs intents/API officiellement documentées et validées individuellement.
- Sélecteur de fichiers Android (SAF), permissions minimales, compatibilité manettes et écrans pliables/doubles.
- Ne pas tenter d'exécuter les binaires Windows des ports PC sur Android. Prévoir des builds Android dédiés si disponibles.

## Garde-fous
- Le registre est ouvert à de nouveaux profils et ne limite pas le concept aux consoles listées.
- Vérifier les droits d'utilisation des textures, logos et sons avant toute redistribution.
- Pas de ROMs, BIOS, clés ni fichiers de jeux fournis.
- Ne pas casser les installations, profils, mods, versions et amis déjà enregistrés.
- Tests graphiques et de lancement sur appareils réels requis avant d'annoncer la fonction comme terminée.
