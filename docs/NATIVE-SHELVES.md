# DecompShelf — bibliothèques GameCube, Wii, Switch et Nintendo 64

DecompShelf réunit les quatre bibliothèques de ports natifs et recompilations dans l'application issue de CubeShelf. Elle ne fournit ni émulateur généraliste, ni disque, jeu, firmware ou clé.

## Utiliser l'application

- Choisir **GameCube**, **Wii**, **Switch** ou **Nintendo 64** dans le sélecteur **en haut à gauche**. Les cartes et favoris changent dans la même fenêtre. Le mode Big Picture propose aussi ce sélecteur.
- Le logo à côté de DecompShelf suit automatiquement le catalogue : GameCube, Wii, Switch ou Nintendo 64. Les marques sont incluses dans l'application et ne nécessitent aucun accès réseau.
- Une transition courte accompagne le changement de console. **Paramètres → Réduire les animations de changement de console** permet un passage immédiat. Big Picture conserve le plein écran lors du changement.
- Un jeu n'apparaît qu'une fois par console. Lorsqu'il possède plusieurs ports, choisir le projet et sa version dans le **menu déroulant de sa carte ou de sa fiche**. Le choix est mémorisé ; les favoris sont communs aux versions du même jeu. Les programmes associés et les statistiques restent propres à chaque port.
- Les jaquettes réelles sont incluses pour chaque jeu : format GameCube, boîtier Wii blanc et boîtier Switch plus étroit. Les scans de face, dos et tranche disponibles viennent de [GameTDB](https://www.gametdb.com/). Les identifiants, URLs et empreintes sont consignés dans `Catalogs/covers.json`. Les jeux dématérialisés utilisent les jaquettes correspondantes de la base ; aucun dos fictif n'est ajouté quand le scan manque.
- GameCube conserve ses intégrations existantes et affiche les ports supplémentaires dans la même bibliothèque.
- Wii et Switch utilisent les mêmes pages, cartes et réglages. Les projets Apple Silicon restent signalés comme tels et ne deviennent pas compatibles Windows.
- **Installer le port**, depuis une carte, ouvre sa fiche de gestion pour télécharger le paquet vérifié et associer son programme. Le bouton **Gérer les ports natifs** donne accès à tout le catalogue de la console courante.

La recherche porte sur les titres et les noms de projets. Le filtre **Paquets pour ce PC uniquement** montre uniquement les entrées avec un paquet dont l'architecture est déclarée compatible. Une entrée sans paquet vérifié peut nécessiter une compilation depuis les sources. Une release disponible ne signifie pas que le jeu est entièrement jouable : lire l'état et les instructions du projet.

Choisir un paquet et toucher **Installer ce paquet** le télécharge dans un nouveau dossier, vérifie sa taille et son SHA-256 puis extrait les ZIP et archives tar.gz. Les fichiers existants et sauvegardes ne sont pas remplacés. Les EXE d'installation, AppImage, DMG, PKG et DEB restent des fichiers à ouvrir ou installer selon les instructions de leur auteur. Aucun programme téléchargé n'est exécuté automatiquement.

Utiliser **Associer le programme** pour choisir l'exécutable natif, ou **Associer une application .app** sur macOS, puis **Jouer**. Le port lui-même demande et prépare les fichiers de votre copie du jeu. Le launcher n'invente pas d'arguments de démarrage et ne soumet pas les disques Wii ou Switch à une validation GameCube.

Les versions officielles et le README sont accessibles depuis chaque fiche. Le catalogue est une photographie vérifiée le 26 octobre 2026 ; il ne se met pas silencieusement à jour. L'inventaire cite les sources primaires et les exclusions. L'exhaustivité de tous les projets existants n'est pas garantie.

## Données et mises à jour

Les réglages et données restent dans le profil **CubeShelf** après le changement de nom : aucune migration ni réinstallation des jeux n'est nécessaire. Chaque entrée de port possède un identifiant incluant sa console : favoris, statistiques et programmes associés ne sont pas confondus entre bibliothèques. Le dernier choix de console est conservé au redémarrage. La variable de test `CUBESHELF_DATA_DIR` peut isoler le profil complet.

Il n'y a plus d'applications WiiShelf ou SwitchShelf à lancer séparément. Cette version de travail remplace la proposition initiale de trois exécutables et n'est pas une nouvelle release stable publiée.

## Compiler

```powershell
dotnet run --project tests/CubeShelf.NativePorts.Tests -c Release
pwsh packaging/Build-Native-Shelves.ps1 -RuntimeIdentifier win-x64 -OutputDirectory dist/native-shelves
```

Le script produit un seul dossier **DecompShelf**, avec le sélecteur des trois consoles. Sous Windows, ouvrir **DecompShelf.exe**. Ne pas déplacer l'EXE seul : conserver les DLL, catalogues et autres fichiers à côté. Le fichier CubeShelf.exe reste un lanceur de compatibilité pour les anciens contrats de mise à jour et de restauration ; il ouvre la même application et le même profil. Le paquet n'est pas signé avec un certificat de distribution.

Pour le développement :

```powershell
dotnet run --project src/CubeShelf.Desktop -- --shelf wii
dotnet run --project src/CubeShelf.Desktop -- --shelf switch
```

Les tests vérifient les catalogues, les origines des paquets, leurs empreintes, l'extraction, l'association des programmes et la séparation des profils. Aucun test ne remplace une partie réelle avec les fichiers du joueur.

## Nintendo 64

29 jeux, 34 projets natifs, regroupés sans doublons. Les autres versions du même jeu restent accessibles dans le sélecteur de port. Boîtes en carton horizontales, ou verticales pour les éditions japonaises, 29 scans de cartouches. Au lancement en Big Picture, le rabat s’ouvre et la cartouche sort puis descend devant la boîte ; elle ne tourne pas comme un disque. L’animation s’arrête une fois terminée et reste annulable avec B/Échap.

Le catalogue PC contient 29 jeux Nintendo 64. Depuis preview.11, Android propose les cinq ports compatibles décrits dans `packaging/ANDROID.md` ; les autres ne sont pas annoncés compatibles.

### Boîtes Nintendo 64 complètes

Les six faces sont prises en charge dans la bibliothèque, la fiche du jeu et le lancement Big Picture : face, dos, tranche gauche, tranche droite, dessus, dessous. Les scans de chaque boîte complète viennent de la même édition Nintendo64EVER ; des éditions régionales différentes peuvent être illustrées selon les scans disponibles. Les données de jeu demandées restent celles du port natif.

22 jeux disposent des six scans. Les côtés manquants restent en carton uni : Harvest Moon 64, Ogre Battle 64, Snowboard Kids 2 et WWF WrestleMania 2000 (tranches et dessous) ; Star Fox 64 et les deux Virtual Pro Wrestling (dessous). Les 29 boîtes ont leur face, leur dos, leur dessus et leur cartouche.

Sur une boîte Nintendo 64 sélectionnée, maintenir le clic et glisser pour la faire tourner horizontalement et verticalement. Un clic simple conserve le retournement face/dos. La navigation manette entre les jeux et consoles reste identique.

### Construction du carton

Le visuel est imprimé jusqu’aux plis, sans cadre beige ni joint de boîtier plastique. L’animation ouvre le petit côté des éditions horizontales et le haut des éditions japonaises verticales, déplie le rabat et les pattes, puis fait sortir un support intérieur en carton avec la cartouche. Le support est une représentation simplifiée ; sa forme exacte varie selon les éditions.

Références visuelles : [boîte PAL originale Ocarina of Time et son support](https://own4less.co.uk/product/386486/zelda-ocarina-of-time-nintendo-n64-game-boxed-instructions-a), [inventaire de contenus originaux Micro-64](https://www.micro-64.com/database/n64gamecontents.shtml).

### Cartes de bibliothèque

La hauteur des miniatures suit le rapport largeur/hauteur réel des jaquettes. Une boîte Nintendo 64 horizontale occupe un espace horizontal, une boîte japonaise reste verticale ; aucune bande noire ni découpe n’est ajoutée. Les boîtiers GameCube, Wii et Switch conservent leurs bordures propres à la console.
