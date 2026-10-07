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

CubeShelf peut montrer qui de tes amis est en ligne, à quoi il joue, sa bibliothèque et ses mods —
**sans serveur, sans cloud, sans compte, sans mot de passe enregistré**.

Les CubeShelf forment eux-mêmes un **réseau maillé**. Chaque PC joignable depuis Internet en porte
une petite part et relaie pour ceux qui ne le sont pas : plus il y a de monde, plus il est solide.
Ta présence y est déposée chiffrée pour tes seuls amis ; eux la retrouvent, et quand vous êtes en
ligne tous les deux, vos CubeShelf se parlent directement. Sur le même réseau local, c'est plus
direct encore (voir *Réseau local*). Le fonctionnement et le modèle de sécurité sont détaillés dans
[RESEAU.md](RESEAU.md).

### Mise en route

Tout se passe sur la page **Mon profil** (Ctrl+7, ou la pastille en bas de la barre latérale).

1. **Crée ton identité.** Un pseudo est obligatoire pour tout ce qui touche aux amis, et pour rien d'autre : jouer seul n'en demande pas. Il n'est jamais déduit de ton compte Windows.
2. **Ton code ami est prêt aussitôt.** Rien à configurer : il porte ta clé, ton pseudo et deux ou trois points d'entrée dans le réseau.
3. **« Copier mon code »** copie un message prêt à coller dans n'importe quelle conversation. Ton ami le copie à son tour, ouvre sa page Amis (Ctrl+6), et CubeShelf le trouve tout seul dans le presse-papiers : « Ajouter Zera#4821 ? ».
4. **La demande d'ami revient toute seule.** Dès qu'il t'a ajouté, son CubeShelf te dépose une demande dans le réseau : elle apparaît en haut de ta page Amis, avec « Accepter » et « Refuser ». Personne n'a à renvoyer son code. Tant que tu n'as pas accepté, il te voit **« En attente »**.
5. **Ta box.** Si tu le laisses faire, CubeShelf lui demande d'ouvrir son port (PCP, NAT-PMP ou UPnP), et ton PC devient joignable. Sinon il passe par des relais : d'autres CubeShelf vous mettent en contact, puis vos deux PC tentent de percer leurs box pour se parler directement, et ne laissent le relais porter la conversation (chiffrée de bout en bout) que si c'est impossible.

La carte « Réseau CubeShelf » de Mon profil dit où en est ton PC : joignable directement, par
relais, ou ne connaissant encore personne. Un code ami sert alors de porte d'entrée.

**Le dossier synchronisé de la 0.9 n'est plus proposé.** Pour qui l'avait configuré, CubeShelf
continue d'y publier pour les amis qui le lisent encore par ce lien, et la carte n'apparaît que
dans ce cas.

### Le pseudo et son numéro

Tes amis te voient sous la forme `Zera#4821`. Les quatre chiffres ne sont choisis par personne : ils sont lus sur ta clé publique, donc identiques sur tous tes PC, et ils distinguent deux joueurs du même pseudo.

Ce que le numéro n'est pas, et c'est structurel :

- **Un moyen de trouver quelqu'un.** Taper `Zera#4821` ne suffit pas pour ajouter Zera : il n'existe aucun annuaire où le chercher. Discord le peut parce que ses serveurs savent qui est qui ; ici, personne ne le sait, et c'est voulu. C'est le code qui transporte la clé et l'adresse.
- **Une preuve d'identité.** Quatre chiffres, ce sont dix mille possibilités : n'importe qui peut fabriquer une clé qui donne `#4821` en quelques secondes. Le numéro sert à reconnaître, le code sert à se fier.

Les codes de la 0.10 (`CSF3-…`) n'ont plus d'adresse web : ils portent des points d'entrée dans
le réseau, jamais une adresse privée. **Une 0.9 ne sait pas les lire** : les deux PC doivent être en
0.10. Les codes plus anciens (`CSF1-…`, `CSF2-…`) restent acceptés.

### Profil, blocage, invitations

> Le profil (avatar, statut, jeu mis en avant) se règle lui aussi sur la page **Mon profil**.


> **Jamais testé entre deux machines.** Les invitations ont été compilées et testées
> unitairement, jamais jouées. [TEST_DEUX_PC.md](TEST_DEUX_PC.md) donne la procédure et, surtout,
> ce que chaque échec veut dire.

**Ton profil** est ce que tes amis voient à côté de ton nom : un avatar, une ligne de statut,
la taille de ta bibliothèque et un jeu mis en avant. Rien n'est obligatoire et tout voyage dans
le même document chiffré. L'avatar est recadré au carré puis réduit en 96×96 avant publication —
ce document est réécrit à chaque battement, une photo non réduite ne serait pas un coût unique
mais un flux permanent à travers le réseau. Le jeu mis en avant se choisit dans
ta bibliothèque et ne se tape pas : un champ libre laisserait publier un titre que personne ne
peut ouvrir.

**Bloquer est plus fort que retirer.** Retirer arrête la relation ; recoller le code la rétablit.
Bloquer laisse une pierre tombale dans `friends.json` précisément pour que le code ne puisse plus
défaire la décision en silence — c'est toute la différence entre les deux. Un bloqué cesse de
recevoir ta présence *et* d'être lu. Ce que ça ne fait pas est dit dans la boîte de dialogue
plutôt que découvert : jusqu'à minuit (UTC), il peut encore voir que ta présence change.

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

### Réseau local

Deux CubeShelf sur le même réseau se trouvent seuls, et **un ami trouvé là se lit directement** :
présence, invitations et messages arrivent en une seconde, sans même passer par Internet.
Mesuré entre deux CubeShelf : 86 ms de la publication à l'écran de l'ami.

Ce que le réseau voit passer, et c'est voulu :

- **Pour tes amis**, quelques marqueurs par minute qu'eux seuls reconnaissent : un HMAC de la clé que
  vous partagez déjà sur une fenêtre de dix minutes, rembourré et mélangé comme les coffres. Un
  inconnu voit du bruit qui change toutes les dix minutes — ni pseudo, ni clé, rien à suivre d'un
  café à l'autre. L'ami qui te reconnaît vient chercher **le même document chiffré** que celui du
  dossier, vérifié de la même façon (il s'ouvre sous la clé de paire, sa séquence est nouvelle).
- **Pour ajouter quelqu'un**, comme on appaire un appareil Bluetooth : chacun ouvre « Réseau local »
  sur la page Amis, et tant que la fenêtre est ouverte, son pseudo et sa clé sont visibles. Une
  demande et son acceptation prouvent chacune que l'autre détient bien la clé qu'il annonce ; un
  imposteur sur le réseau n'obtient rien. Les adresses ne circulent qu'après cette preuve.

Un ami rencontré ainsi, sans adresse publique, devient lisible de partout le jour où il en publie
une : chaque document dit où il est publié, et ses amis l'apprennent en le lisant. Windows demande
d'autoriser CubeShelf sur le réseau privé la première fois. Le réglage « Me signaler à mes amis sur
le réseau local » est sur la page Mon profil.

### Rester joignable, statuts, messages

- **Fermer la fenêtre ne quitte pas.** Une fois les amis configurés, CubeShelf se range près de
  l'horloge : tes amis te voient toujours, les invitations arrivent. Clic droit sur l'icône →
  Quitter pour fermer vraiment. Une mise à jour ou l'arrêt de Windows ferment pour de bon.
- **Notifications dans le coin de l'écran** quand CubeShelf est hors de vue : une invitation, un
  message, un ami qui se connecte ou lance un jeu (au plus toutes les dix minutes par ami). Jamais
  pendant une partie — le jeu montre les siennes — et jamais en « Ne pas déranger ».
- **Statut** : Disponible, Absent (aussi automatiquement après dix minutes d'inactivité),
  Ne pas déranger, Invisible. Invisible publie « hors ligne » mais continue de lire tes amis et de
  faire passer les messages. Un CubeShelf plus ancien voit simplement « en ligne ».
- **Ce que dit le jeu** : en partie de Mario Party 4, tes amis voient « Toad's Midway Madness —
  tour 12/20 », ou la rich presence RetroAchievements quand elle tourne. Sous le même interrupteur
  que le jeu en cours.
- **Messages courts**, chiffrés une seconde fois pour leur seul destinataire et gardés dans ton
  document jusqu'à ce qu'il en accuse réception (une semaine au plus). Une boîte aux lettres, pas
  une messagerie : quelques secondes sur le réseau local, le rythme du dossier sinon.
- **Répondre à une invitation** : « Rejoindre » ou « Décliner » ; l'hôte voit « rejoint ton salon »
  ou « a décliné ».

### RetroAchievements : un compte pour tous tes jeux

Sur la page **Mon profil**, carte « RetroAchievements » : connecte-toi **une fois**, et chaque jeu
qui gère RetroAchievements (Mario Party 4 avec PartyBoard) démarre connecté, sans rien taper.

- **Ton mot de passe** part une fois vers retroachievements.org et n'est jamais gardé. CubeShelf
  garde le jeton que le site renvoie, chiffré pour ton compte Windows, et ne le donne qu'aux jeux
  qui déclarent savoir s'en servir, au moment où il les lance.
- **Dans l'autre sens aussi** : si tu te connectes ou te déconnectes depuis le jeu, CubeShelf suit,
  et si RetroAchievements refuse un jour ta session, la page te redemande le mot de passe.
- **Tes amis voient ta progression** (« 🏆 Mario Party 4 : 31/58 · 280 pts »), et dans le jeu, F1 :
  le nombre de succès de chacun, quels amis ont chaque succès, et une notification quand l'un d'eux
  en débloque un. Ça passe par ta présence, chiffrée comme le reste ; la case « Mes succès
  RetroAchievements » l'arrête. C'est ce que le jeu de ton ami déclare : son nom de compte est
  affiché, le site fait foi.

### Jouer sur téléphone

PartyBoard sur Android ne fait pas tourner CubeShelf, mais il peut devenir *toi* : il ouvre ce que
tes amis scellent pour toi, voit leurs invitations, et récupère tes sauvegardes de Mario Party 4.
C'est le PC qui continue à publier ta présence.

- **Par QR code** (même Wi-Fi) : Mon profil → « Afficher le QR code », puis dans PartyBoard : onglet
  Amis → « Compte et sauvegardes (QR code) » → Scanner. Le code porte l'adresse du PC sur le réseau
  local, un jeton et une clé AES-256 : tout ce qui passe est chiffré sous cette clé, et seul celui qui
  voit l'écran peut la lire. Le lien ne vit que le temps de la fenêtre, dix minutes au plus. Le
  téléphone peut aussi renvoyer ses sauvegardes au PC ; elles ne sont jamais écrites sous un
  Mario Party 4 ouvert, et ce qu'elles remplacent est gardé dans `save-backups`.
- **Par fichier**, sans Wi-Fi commun : « Exporter (sauvegarde ou téléphone)… » avec un mot de passe.

### Ton identité, et sa sauvegarde

`identity.key` est la seule chose qui ne se recrée pas : la perdre oblige chaque ami à t'ajouter
de nouveau. Sous Windows, elle est **chiffrée pour ton compte Windows** (DPAPI) : une copie du
profil sur un autre PC ou un autre compte ne contient pas de clé utilisable, et ne peut donc pas
publier à ta place. La contrepartie : elle ne survit plus seule à une réinstallation de Windows.

D'où la **sauvegarde** : l'export protégé par mot de passe est aussi la sauvegarde de ton identité,
et la page Mon profil la réclame tant qu'elle n'existe pas. « Restaurer une sauvegarde… » te rend ton
identité sur un nouveau PC ; tes amis présents sont gardés, ceux du fichier ajoutés, et l'ancienne
clé reste à côté, renommée. Une clé qui ne s'ouvre pas ici n'est jamais effacée ni remplacée.

### Changer d'adresse (dossier synchronisé)

Pour qui publie encore dans un dossier synchronisé. Change de dossier ou de lien quand tu veux : dès que la nouvelle adresse est vérifiée, **l'ancien
fichier reçoit un dernier document qui indique la nouvelle**, chiffré pour tes amis actuels
seulement. Leurs CubeShelf suivent tout seuls, sans nouveau code. Un ami retiré entre-temps ne peut
pas le lire et l'ancien fichier ne bouge plus : il perd ta trace, et cesse de voir quand tu joues.
Garde l'ancien fichier quelques semaines, pour les amis qui ne lancent pas CubeShelf souvent.

### Comment c'est construit

| Élément | Choix |
| --- | --- |
| Identité | Une paire de clés P-256 générée au premier lancement. La clé publique **est** l'identité : rien n'est délivré par personne. |
| Code ami | Clé publique + pseudo + points d'entrée dans le réseau + somme de contrôle. |
| Chiffrement | Le document est chiffré une fois sous une clé de contenu aléatoire, elle-même emballée par ami. Retirer un ami = ne plus inclure son coffre. |
| Authenticité | AES-GCM sous la clé de paire, que vous seuls connaissez : un document qui s'ouvre vient de cet ami. Dans le réseau, chaque enregistrement est en plus signé par une clé que son seul auteur sait dériver. |
| Anti-rejeu | Un numéro de séquence strictement croissant. Une copie conservée d'un ancien document est inerte. |
| Fraîcheur | Un document périmé se lit **hors ligne**, plutôt que de figer un ami sur son dernier jeu. |
| Réseau | Une table distribuée de type Kademlia ; des sessions Noise XX (P-256, AES-256-GCM) entre nœuds ; un identifiant de nœud qui coûte une preuve de travail. Détail : [RESEAU.md](RESEAU.md). |
| Où vit ta présence | À un endroit qui change chaque jour et que toi seul peux écrire ; chaque ami le trouve par un pointeur que vous seuls savez calculer. |

Le nombre de coffres est rembourré à un multiple de huit : le document ne publie pas combien tu as d'amis. Le contenu chiffré l'est à un multiple de 4 Kio : sa taille ne dit pas combien de jeux, de mods ou de messages il porte.

Aucune dépendance n'a été ajoutée — ECDH, ECDSA, HKDF et AES-GCM viennent de .NET 8.

### Ce que ça ne protège pas

Ces limites sont structurelles, pas des oublis :

- **Un ami retiré te perd de vue à minuit (UTC).** Jusque-là il peut encore voir *que* ta présence change, sans pouvoir la lire ; ensuite ton document change de place et il n'a plus de pointeur pour le retrouver. Il garde la clé de paire, qui dérive des deux identités et **ne peut pas être changée sans changer d'identité**. (Avec l'ancien dossier synchronisé, il voit quand ton fichier change tant que tu gardes la même adresse.)
- **Ton adresse IP n'est pas secrète.** Comme dans tout réseau pair-à-pair, les nœuds avec qui ton PC échange la voient — sans savoir qui tu es. Ton code ami contient l'adresse de ton PC quand il est joignable : donne-le plutôt en privé.
- **Un relais voit qui parle à qui, et quand** — pas ce qui se dit : la session entre amis est chiffrée de bout en bout.
- **Un réseau naissant a besoin d'un PC joignable.** Si ni toi ni aucun de tes amis n'est joignable (box fermée, UPnP coupé, pas d'IPv6), vous ne vous verrez que sur le même réseau local.
- **Un adversaire avec beaucoup d'adresses et de calcul** peut gêner le réseau, c'est-à-dire taire des enregistrements ; il ne peut ni les lire ni les falsifier. Les places de ta présence sont imprévisibles pour qui n'est pas ton ami, ce qui empêche de te viser.
- **Pas de confidentialité persistante.** Qui obtient ta clé d'identité déchiffre rétroactivement tout document que tu as publié.
- **Ta clé privée est chiffrée pour ton compte Windows**, pas par un mot de passe : quelqu'un qui ouvre une session sous ton compte peut s'en servir. Hors Windows, elle n'est protégée que par les permissions du fichier.
- **L'hébergeur peut taire, pas falsifier.** AES-GCM bloque la forgerie ; geler ton document te laisse « en jeu » jusqu'à expiration. La fenêtre de fraîcheur borne cette attaque.
- **Une identité par installation.** Restaurer ta sauvegarde sur une seconde machine fait publier deux CubeShelf sous la même identité, et celui qui prend du retard finit rejeté par tes amis ; CubeShelf le détecte et le dit. La seconde machine doit générer sa propre identité, et vous vous ajoutez mutuellement. Un téléphone, lui, ne publie jamais.
- **Le partage est décidé globalement**, pas ami par ami : les quatre cases s'appliquent à tout le monde.
- **Entre amis en ligne, tout arrive en direct**, poussé sur leur session. Sinon, un ami en ligne est relu toutes les 25 secondes, les autres toutes les deux minutes. Sur le réseau local, c'est une seconde.
- **Le téléphone ne lit pas encore le réseau.** PartyBoard sur Android sait lire un ami qui publie dans un dossier, pas encore un ami du réseau CubeShelf.
- **Le réseau local s'annonce.** Même sans nom, un CubeShelf qui diffuse ses marqueurs révèle qu'un CubeShelf tourne sur ce PC. Le réglage se décoche.
- **CubeShelf ne valide pas qu'un salon est joignable.** Il transporte le code du compagnon ; c'est le compagnon qui traverse — ou non — les box et les pare-feu.

### Où vivent les fichiers

Dans le profil utilisateur (`%LOCALAPPDATA%\CubeShelf` sous Windows) :

| Fichier | Contenu |
| --- | --- |
| `identity.key` | Ta clé privée, chiffrée pour ton compte Windows. La perdre oblige tous tes amis à te rajouter : sauvegarde-la (Mon profil). |
| `friends.json` | Tes amis, leur adresse, et l'état de lecture de chacun. |
| `presence-state.json` | Le compteur de séquence. **Ne le supprime pas** sans raison. |
| `avatar.png` | Ton avatar, déjà réduit en 96×96. Le supprimer, c'est ne plus en publier. |
| `messages.json` | Tes conversations, une semaine de messages en attente au plus. |
| `mesh-nodes.json` | Quelques nœuds publics du réseau, pour le rejoindre au prochain lancement sans code. |

`CUBESHELF_DATA_DIR`, s'il désigne un dossier absolu, y place tout le profil : deux identités sur un
même PC — pour essayer le réseau local seul — ou un test qui ne touche pas au vrai profil.
`CUBESHELF_MESH_ALLOW_PRIVATE=1` fait compter les adresses privées comme Internet, pour essayer le
réseau CubeShelf entre plusieurs profils d'un même PC ou d'une même maison. Pour les essais seulement.
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
