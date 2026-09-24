# CubeShelf Launcher

CubeShelf est un launcher et un gestionnaire de bibliothèque GameCube. La préversion 0.9 utilise une interface Avalonia commune à Windows, macOS et Linux : elle installe et met à jour les runtimes des jeux portés sur PC (PartyBoard pour Mario Party 4, Ring Out pour Soulcalibur II, Strikers pour Super Mario Strikers), prépare les images fournies légalement par l’utilisateur, gère les mods GameBanana, et propose un système d’amis pair-à-pair sans serveur.

CubeShelf ne contient aucun fichier de jeu Nintendo.

## Installation Windows

L’artefact recommandé est `CubeShelf-Setup-x64.exe`. L’installation est effectuée sans droits administrateur dans `%LOCALAPPDATA%\Programs\CubeShelf`, crée une entrée de désinstallation et peut ajouter un raccourci Bureau.

Le profil utilisateur reste dans `%LOCALAPPDATA%\CubeShelf` et est conservé lors d’une mise à jour ou d’une désinstallation.


## Artefacts publiés

Chaque release stable publie :

| Plateforme | Artefact |
| --- | --- |
| Windows x64 | `CubeShelf-Setup-x64.exe` (installateur) et `CubeShelf-win-x64.zip` (portable) |
| Linux x64 | `CubeShelf-linux-x64.AppImage` |
| macOS x64 | `CubeShelf-osx-x64.tar.gz` |
| macOS arm64 | `CubeShelf-osx-arm64.tar.gz` |

Plus `checksums.txt` et `release-manifest-v2.json`, que CubeShelf utilise pour ses propres mises à jour.

Les binaires ne sont **pas encore signés** : aucun certificat Authenticode n'est configuré, et les archives macOS ne sont ni signées ni notarisées. Windows affichera un avertissement SmartScreen, et macOS bloquera l'application tant que la quarantaine n'est pas levée (`xattr -d com.apple.quarantine CubeShelf`). C'est suivi dans la ROADMAP.
## Compiler et créer l’installateur

Prérequis : SDK .NET 8 et Inno Setup 6.

```powershell
.\BUILD_INSTALLER.ps1
```

Le résultat est créé dans `dist\CubeShelf-Setup-x64.exe`.

## Jeux pris en charge

Le catalogue est piloté par les données : `src/CubeShelf.Desktop/games.json` décrit chaque jeu, son runtime, les révisions de disque acceptées et la façon dont le runtime est récupéré. Ajouter un jeu ne demande pas de code.

| Jeu | Runtime | Disques acceptés | Acquisition | Préparation du disque |
| --- | --- | --- | --- | --- |
| Mario Party 4 | PartyBoard | `GMPE01_00`, `GMPE01_01` | manifeste CubeShelf | par CubeShelf |
| Soulcalibur II | [Ring Out](https://github.com/jackpoison-prog/RingOut) | `GRSEAF`, `GRSPAF`, `GRSJAF`, `GRSEPS` | asset de release GitHub | par le runtime |
| Super Mario Strikers | [Strikers](https://github.com/new-coke/strikers) | `G4QE01`, `G4QP01`, `G4QJ01` | asset de release GitHub | par le runtime |

Une entrée de `SupportedDiscIds` est soit un identifiant de révision complet (`GMPE01_00`, cette révision uniquement), soit un identifiant de disque sur six caractères (`GRSEAF`, toutes les révisions).

### Deux modes d’acquisition du runtime

`RuntimeSource` vaut `Manifest` ou `GitHubReleaseAsset`.

En `Manifest`, l’éditeur publie un `manifest.json` à côté de ses assets, qui nomme l’artefact par plateforme avec sa taille et son SHA-256. C’est ce que fait PartyBoard, et c’est le mode à privilégier : le téléchargement est comparé à une valeur publiée par l’éditeur.

En `GitHubReleaseAsset`, l’éditeur ne publie que des archives ordinaires. CubeShelf interroge l’API GitHub pour la dernière release et sélectionne l’asset via `RuntimeAssets`, dont le motif accepte un joker parce que la version figure dans le nom du fichier. `RuntimeLaunchPaths` indique l’exécutable à lancer dans l’archive, par plateforme, avec le jeton `{version}`.

### Vérification du téléchargement

CubeShelf cherche de quoi contrôler l’archive, dans cet ordre :

1. **Un fichier de checksums publié dans la même release**, nommé par `RuntimeChecksumAsset` et lu au format `sha256sum`. C’est la meilleure preuve disponible : elle vient de l’éditeur et suit chaque version. Strikers publie `SHA256SUMS` — ses installations sont donc vérifiées.
2. **Un SHA-256 épinglé** dans `RuntimeSha256`, qui fige une version précise et devient obsolète à la release suivante.
3. **Rien.** Ring Out ne publie ni manifeste ni checksum : l’archive reste plafonnée en taille et hachée pendant le transfert, mais aucune valeur de l’éditeur ne permet de confirmer le résultat.

Dans ce dernier cas l’installation n’est pas bloquée, elle est **enregistrée comme non vérifiée** : le runtime porte la mention « installé sans vérification » dans la fiche du jeu, et l’état est conservé dans `runtime-state.json` — l’information survit à l’installation qui l’a produite.

### Préparation du disque

`DataPreparation` vaut `CubeShelf` ou `Runtime`.

PartyBoard attend que CubeShelf extraie le disque (conversion RVZ via DolphinTool, puis l’arborescence). Ring Out fait l’inverse : il extrait et **recompile** le jeu depuis le disque à son premier lancement, ce que CubeShelf ne peut ni ne doit refaire. Pour ces jeux, le bouton « Préparer les données » n’est pas proposé et Jouer est disponible dès que le runtime est installé et le disque compatible.

Ring Out cible Windows x64 et Linux x86-64. Prévois plusieurs minutes et environ 1,5 Go d’espace libre au premier lancement.

### Jaquettes

Les jaquettes vivent dans `src/CubeShelf.Desktop/Assets/Covers/<Jeu>/`, en trois fichiers : `pal_front.png`, `pal_back.png` et `pal_spine.png`. Pour en remplacer une, écrase ces fichiers sans toucher au catalogue. Une jaquette absente n’est pas une erreur, la fiche s’affiche sans image. Les jaquettes complètes (dos + tranche + face) se découpent aux proportions GameCube standard : dos 900, tranche 120, face 900 sur 1920.

## Amis décentralisés

CubeShelf peut montrer qui de tes amis est en ligne, à quoi il joue, sa bibliothèque et ses mods — **sans service central, sans compte, sans mot de passe enregistré**.

Le principe tient en une phrase : chacun publie un petit document chiffré à un endroit qu'il contrôle, et les amis le relisent en HTTPS.

### Mise en route

Tout se passe sur la page **Mon profil** (Ctrl+7, ou la pastille en bas de la barre latérale).

1. **Crée ton identité.** Un pseudo est obligatoire pour tout ce qui touche aux amis, et pour rien d'autre : jouer seul n'en demande pas. Il n'est jamais déduit de ton compte Windows.
2. **Choisis un dossier déjà synchronisé** par Nextcloud, Dropbox ou équivalent, et colle l'adresse publique de ce dossier.
3. **Teste l'adresse.** CubeShelf publie un document, le relit depuis cette adresse et le déchiffre avec ta clé. **Ton code ami n'apparaît qu'après**, et il reste là d'un lancement à l'autre.
4. Coche « Publier ma présence ».
5. **« Copier mon code »** copie un message prêt à coller dans n'importe quelle conversation. Ton ami le copie à son tour, ouvre sa page Amis (Ctrl+6), et CubeShelf le trouve tout seul dans le presse-papiers : « Ajouter Zera#4821 ? ».
6. **Il doit t'ajouter lui aussi.** L'amitié va dans un sens à la fois : l'ajouter te permet de le lire, pas à lui de te lire.

### Le pseudo et son numéro

Tes amis te voient sous la forme `Zera#4821`. Les quatre chiffres ne sont choisis par personne : ils sont lus sur ta clé publique, donc identiques sur tous tes PC, et ils distinguent deux joueurs du même pseudo.

Ce que le numéro n'est pas, et c'est structurel :

- **Un moyen de trouver quelqu'un.** Taper `Zera#4821` ne suffit pas pour ajouter Zera : il n'existe aucun annuaire où le chercher. Discord le peut parce que ses serveurs savent qui est qui ; ici, personne ne le sait, et c'est voulu. C'est le code qui transporte la clé et l'adresse.
- **Une preuve d'identité.** Quatre chiffres, ce sont dix mille possibilités : n'importe qui peut fabriquer une clé qui donne `#4821` en quelques secondes. Le numéro sert à reconnaître, le code sert à se fier.

Les codes de la 0.9.0 (`CSF1-…`) restent acceptés ; ils arrivent simplement sans pseudo. Ceux de la 0.9.1 (`CSF2-…`) portent le pseudo, et **une 0.9.0 ne sait pas les lire** : les deux PC doivent être en 0.9.1.

Le test n'est pas une formalité. Écrire le fichier réussit presque toujours ; c'est la **lecture** qui casse, silencieusement, et du côté où personne ne peut diagnostiquer. Le cas le plus fréquent : **un lien de partage Nextcloud sert une page HTML et non le fichier tant qu'on n'ajoute pas `/download` à la fin.** Sans le test, tu distribuerais un code ami inerte et tes amis ne te verraient jamais, sans savoir pourquoi.

> Syncthing ne convient pas : il n'expose aucune adresse HTTP, donc tes amis n'ont rien à interroger.

### Profil, blocage, invitations

> Le profil (avatar, statut, jeu mis en avant) se règle lui aussi sur la page **Mon profil**.


> **Jamais testé entre deux machines.** Les invitations ont été compilées et testées
> unitairement, jamais jouées. [TEST_DEUX_PC.md](TEST_DEUX_PC.md) donne la procédure et, surtout,
> ce que chaque échec veut dire.

**Ton profil** est ce que tes amis voient à côté de ton nom : un avatar, une ligne de statut,
la taille de ta bibliothèque et un jeu mis en avant. Rien n'est obligatoire et tout voyage dans
le même document chiffré. L'avatar est recadré au carré puis réduit en 96×96 avant publication —
ce document est réécrit à chaque battement, une photo non réduite ne serait pas un coût unique
mais un flux permanent à travers ton dossier synchronisé. Le jeu mis en avant se choisit dans
ta bibliothèque et ne se tape pas : un champ libre laisserait publier un titre que personne ne
peut ouvrir.

**Bloquer est plus fort que retirer.** Retirer arrête la relation ; recoller le code la rétablit.
Bloquer laisse une pierre tombale dans `friends.json` précisément pour que le code ne puisse plus
défaire la décision en silence — c'est toute la différence entre les deux. Un bloqué cesse de
recevoir ta présence *et* d'être lu. Ce que ça ne fait pas est dit dans la boîte de dialogue
plutôt que découvert : il garde ton adresse et voit encore quand ton fichier change.

**Les invitations n'existent que pour Mario Party 4**, et la raison est structurelle : un launcher
n'ajoute pas du multijoueur à un jeu qu'il se contente de démarrer. Le champ `OnlineCompanion` du
catalogue porte toute la portée — PartyBoard livre `PartyBoardOnline.exe`, Ring Out et Strikers ne
livrent aucun netplay, et pour eux rien n'est proposé.

**Où inviter, comme sur Steam :**

- **Dans le jeu, F1 → onglet Amis.** Qui est connecté ou en jeu, « Inviter » à côté de chacun,
  « Créer un salon », « Rejoindre » à côté d'un ami qui t'invite. Et une notification dans le jeu,
  quel que soit l'écran, la première fois qu'un ami t'invite.
- **Dans CubeShelf, page Amis**, les mêmes « Inviter » et « Rejoindre » à côté de chaque ami.

La page du jeu, elle, ne propose plus rien : c'était peu pratique.

**Créer ou rejoindre un salon ferme la partie en cours**, et une confirmation le dit avant. Le jeu
en ligne de PartyBoard démarre les deux jeux ensemble depuis le salon ; c'est déjà ce que fait son
onglet « Play Online ». Sur Steam, on rejoint en cours de partie parce que ces jeux sont faits
pour ; ce portage ne l'est pas.

Le compagnon crée le salon et écrit son invitation là où CubeShelf la guette. CubeShelf la
transporte sans la lire : chiffrée aux seuls amis visés, elle expire seule au bout d'un quart
d'heure. Côté invité, le compagnon s'ouvre déjà en train de rejoindre, pseudo et disque remplis.
La livraison est par sondage : ton ami voit l'invitation à sa prochaine lecture, pas à l'instant.
C'est une invitation posée sur la table, pas une sonnerie.

Le jeu et CubeShelf se parlent par deux fichiers dans `%LOCALAPPDATA%\CubeShelf\ingame` : CubeShelf
y écrit l'état des amis toutes les trois secondes tant que le jeu tourne, le jeu y dépose ses
demandes. Ni réseau, ni port. Une demande de plus de trente secondes est jetée sans être exécutée :
cliquée pendant que CubeShelf était fermé, elle n'ouvrira pas un salon une heure plus tard.

Deux amis qui liraient pareil — même pseudo, même numéro, une chance sur dix mille — apparaissent
avec six chiffres au lieu de quatre, les quatre premiers inchangés.

### Comment c'est construit

| Élément | Choix |
| --- | --- |
| Identité | Une paire de clés P-256 générée au premier lancement. La clé publique **est** l'identité : rien n'est délivré par personne. |
| Code ami | Clé publique + adresse de publication + somme de contrôle. L'adresse doit y figurer : sans annuaire, une clé seule ne dit pas où lire. |
| Chiffrement | Le document est chiffré une fois sous une clé de contenu aléatoire, elle-même emballée par ami. Retirer un ami = ne plus inclure son coffre. |
| Authenticité | Aucune signature. AES-GCM est authentifié et la clé de paire n'est connue que de vous deux : un document qui s'ouvre vient de cet ami. |
| Anti-rejeu | Un numéro de séquence strictement croissant. Une copie conservée d'un ancien document est inerte. |
| Fraîcheur | Un document périmé se lit **hors ligne**, plutôt que de figer un ami sur son dernier jeu. |

Le nombre de coffres est rembourré à un multiple de huit : le fichier ne publie pas combien tu as d'amis.

Aucune dépendance n'a été ajoutée — ECDH, HKDF et AES-GCM viennent de .NET 8.

### Ce que ça ne protège pas

Ces limites sont structurelles, pas des oublis :

- **Retirer un ami ne l'empêche pas de t'observer.** Il garde ton adresse et voit *quand* ton fichier change, donc quand tu joues. Il garde aussi la clé de paire, qui dérive des deux identités et **ne peut pas être changée sans changer d'identité**. Seule une rotation de l'adresse y met fin — et elle invalide tous les codes amis distribués.
- **Changer d'adresse casse silencieusement les amis existants** : ils continuent d'interroger l'ancienne, sans jamais l'apprendre.
- **Pas de confidentialité persistante.** Qui obtient ta clé d'identité déchiffre rétroactivement tout document que tu as publié.
- **Ta clé privée est un fichier en clair** dans ton profil, protégé par les permissions du système de fichiers. C'est le même niveau que le reste du profil, mais autant le dire.
- **L'hébergeur peut taire, pas falsifier.** AES-GCM bloque la forgerie ; geler ton document te laisse « en jeu » jusqu'à expiration. La fenêtre de fraîcheur borne cette attaque.
- **Une identité par installation.** Copier ton profil sur une seconde machine fait publier deux CubeShelf à la même adresse sous la même identité, et celui qui prend du retard finit rejeté par tes amis. La seconde machine doit générer sa propre identité, et vous vous ajoutez mutuellement.
- **Le partage est décidé globalement**, pas ami par ami : les quatre cases s'appliquent à tout le monde.
- **Une invitation est une offre, pas une notification.** Le transport est en sondage : compte jusqu'à quelques minutes, plus ce que ton client de synchro ajoute.
- **CubeShelf ne valide pas qu'un salon est joignable.** Il transporte le code du compagnon ; c'est le compagnon qui traverse — ou non — les box et les pare-feu.

### Où vivent les fichiers

Dans le profil utilisateur (`%LOCALAPPDATA%\CubeShelf` sous Windows) :

| Fichier | Contenu |
| --- | --- |
| `identity.key` | Ta clé privée. La perdre oblige tous tes amis à te rajouter. |
| `friends.json` | Tes amis, leur adresse, et l'état de lecture de chacun. |
| `presence-state.json` | Le compteur de séquence. **Ne le supprime pas** sans raison. |
| `avatar.png` | Ton avatar, déjà réduit en 96×96. Le supprimer, c'est ne plus en publier. |
## Aperçu Linux et Steam Deck

Le dépôt contient désormais un noyau portable `CubeShelf.Core`, une interface Avalonia `CubeShelf.Desktop` et un pipeline AppImage `linux-x64`. Cet aperçu affiche la bibliothèque avec des chemins conformes à XDG, permet de sélectionner et valider une image ISO/GCM/RVZ, puis télécharge le runtime PartyBoard correspondant à la plateforme. La taille et le SHA-256 du paquet sont contrôlés avant son activation. L’interface sait ensuite mettre à jour, réparer ou désinstaller ce runtime sans supprimer l’image originale.

La page Mods de l’aperçu charge le catalogue GameBanana, installe les archives dans le profil portable avec l’extracteur sécurisé, gère l’activation et la priorité, puis transmet à PartyBoard la liste ordonnée des mods actifs. Les collisions de fichiers entre mods sont signalées avant le lancement.

La page Téléchargements conserve l’historique et la progression des installations de runtimes et de mods dans le profil utilisateur. Une opération arrêtée par la fermeture de l’application est marquée comme interrompue au prochain démarrage. Le bouton Reprendre réutilise le fichier partiel avec HTTP Range ; si le serveur ne prend pas cette fonction en charge, CubeShelf recommence proprement le paquet concerné.

Avalonia est désormais l’exécutable `CubeShelf.exe` livré par l’installateur Windows. Il comprend l’installation/réparation de PartyBoard, la préparation ISO/GCM/RVZ, la reprise des téléchargements, les paramètres, la mise à jour intégrée Windows, ainsi que la migration non destructive des données de l’ancien launcher WPF.

Les mods peuvent fournir un manifeste `cubeshelf-mod.json` conforme à `schemas/cubeshelf-mod-v1.schema.json`. CubeShelf résout au plus 32 mods et 8 niveaux, refuse les cycles, affiche le plan avant confirmation, prépare tous les paquets puis les active dans une transaction avec rollback.

Publication locale de l’aperçu :

```powershell
dotnet publish src/CubeShelf.Desktop/CubeShelf.Desktop.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64
```

### Compiler hors Windows

L’ancien launcher WPF a été supprimé. Il ne servait plus qu’à héberger `games.json` et les
jaquettes, mais il ciblait `net8.0-windows` avec `UseWPF` : il restait compilé à chaque CI
Windows sans jamais être publié, et il rendait la solution incompilable ailleurs.

Il reste un projet Windows par nécessité — `CubeShelf.Updater` est la mise à jour intégrée
Windows et utilise WPF pour sa fenêtre de progression. Un filtre de solution met tout le reste
de côté :

```bash
dotnet build CubeShelf.Portable.slnf -c Release
dotnet run --project tests/CubeShelf.Core.Tests/CubeShelf.Core.Tests.csproj -c Release
dotnet run --project tests/CubeShelf.Tests/CubeShelf.Tests.csproj -c Release
```

Les deux suites de tests tournent désormais sur les trois plateformes. `CubeShelf.Tests` était
épinglée à `net8.0-windows` et `win-x64` uniquement à cause de sa référence à l’ancien projet ;
l’extraction d’archives et la reconnaissance de disque n’étaient donc vérifiées que sous Windows.

Sous Windows, `CubeShelfLauncher.sln` construit toujours l’ensemble, updater compris.

## Sécurité

- vérification SHA-256 des mises à jour CubeShelf et du paquet Dolphin épinglé ;
- limites de taille avant et pendant les téléchargements ;
- extraction d’archives confinée, avec refus des chemins absolus, traversées et doublons ;
- mises à jour automatiques du launcher désactivées par défaut ;
- installation issue d’un éditeur sans manifeste ni checksum marquée comme non vérifiée et tracée dans l’état du runtime ;
- releases stables signées obligatoirement, sauf dérogation manuelle explicite ;
- téléchargement de l’outil AppImage épinglé et vérifié par SHA-256 ;
- présence des amis chiffrée de bout en bout par paire, sans service central ni secret stocké ;
- adresse de publication vérifiée par un aller-retour réel avant qu’un code ami ne soit délivré.

## Séparation des responsabilités

- `CubeShelf-Launcher` : interface, bibliothèque, installation, mises à jour et mods ;
- `Marioparty4` / PartyBoard : runtime du jeu et artefact Windows `PartyBoard-win-x64.zip`.

Compatibilité : Windows 10/11 x64 avec installateur, Linux x64/Steam Deck via AppImage, macOS x64/arm64 expérimental sans support PartyBoard garanti. Version actuelle : voir le fichier `VERSION` à la racine du dépôt, qui pilote l’assembly, l’installateur, l’AppImage et les workflows.
