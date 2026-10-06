# Le réseau CubeShelf

Comment les CubeShelf se trouvent et se parlent sans serveur ni cloud, ce que chacun voit passer,
et ce qui est protégé contre qui. Le code est dans `src/CubeShelf.Core/Social/Mesh/`.

## L'idée

Chaque CubeShelf est un **nœud**. Ceux qui sont joignables depuis Internet forment l'ossature : ils
gardent chacun une petite part des données du réseau et relaient pour ceux qui ne le sont pas.
Personne n'héberge rien pour les autres en particulier. Plus il y a de nœuds, plus chaque donnée est
copiée à des endroits différents, et plus le réseau survit aux départs.

Le réseau ne transporte que des choses déjà chiffrées pour leurs destinataires : la présence de
chacun (le même document scellé qu'avant, ami par ami) et les demandes d'ami. Les nœuds qui les
gardent ne peuvent ni les lire, ni les modifier, ni savoir à qui elles appartiennent.

## Les couches, de bas en haut

### 1. L'identité d'un nœud

- Une clé P-256 neuve **à chaque lancement**. Rien ne relie la position d'un nœud dans le réseau
  d'un jour à l'autre, ni à la personne : qui tu es ne circule que dans les sessions chiffrées
  entre amis.
- L'identifiant du nœud est un hachage de cette clé et d'un nonce, et seuls comptent les nonces
  dont le **hachage de travail commence par 22 bits nuls** : environ quatre millions de hachages,
  moins d'une seconde, une fois par lancement. Obtenir un identifiant proche d'une position voulue
  coûte autant de fois plus que le réseau compte de nœuds.
- La preuve est vérifiée à chaque poignée de main : un nœud ne peut pas annoncer une position qu'il
  n'a pas payée.

### 2. Les sessions

- **Noise XX** (spécification Noise, révision 34) sur P-256, AES-256-GCM et SHA-256 : chaque côté
  prouve qu'il détient sa clé, les deux directions ont des clés neuves (confidentialité persistante
  au niveau des sessions), et le hachage de transcription final sert à lier les preuves d'amitié à
  cette session-là.
- Ensuite, chaque datagramme est chiffré, numéroté et vérifié contre une **fenêtre anti-rejeu** de
  1 024 positions. Un paquet rejoué est jeté avant d'être déchiffré.
- Les messages plus grands qu'un datagramme sont **fragmentés** (1 195 octets par fragment, sous le
  MTU minimal d'IPv6) ; les fragments perdus sont redemandés.
- Une session ne change d'adresse que sur un paquet authentifié et plus récent que tous les autres :
  un datagramme ancien ou rejoué ne peut pas l'entraîner ailleurs. C'est ce qui permet à une session
  relayée de passer en direct dès que le perçage des box réussit.

### 3. La table distribuée

- Type **Kademlia** : distance XOR sur 256 bits, seaux de 16, 3 requêtes en vol par recherche,
  chaque enregistrement copié sur les 8 nœuds les plus proches de son emplacement.
- **Seuls entrent dans la table les nœuds avec qui on a terminé une poignée de main à l'adresse
  notée.** Un contact donné dans une réponse est une piste à essayer, jamais une entrée.
- **Diversité** : au plus deux nœuds d'un même voisinage d'adresses (un /24 en IPv4, un /48 en IPv6)
  par seau, et au plus deux nœuds par adresse dans toute la table.
- **Les anciens gardent leur place** : un seau plein garde ses nœuds tant qu'ils répondent ; un
  nouveau venu attend qu'une place se libère. C'est la défense classique contre l'encerclement.
- **Jamais d'adresse privée** : ni dans la table, ni dans un code ami, ni dans une réponse. Une
  réponse ne peut pas diriger nos poignées de main vers notre propre réseau local.
- Une adresse qui n'a pas répondu n'est pas réessayée pendant dix minutes ; un nœud dont les
  recommandations mènent systématiquement nulle part est écarté pendant une heure.

### 4. Les enregistrements

- **Signés** : un enregistrement vit au hachage de sa clé de signature, donc seul le détenteur de
  cette clé peut écrire à cet endroit. Un numéro de séquence plus élevé remplace le précédent ; un
  plus ancien est refusé. Un détenteur qui sert une vieille copie est mis en minorité par les autres.
- **Boîtes aux lettres** : plusieurs auteurs, une entrée chacun, chaque entrée coûtant une petite
  preuve de travail (14 bits). Au plus 32 entrées par boîte.
- Rien ne vit plus d'**un jour**. Chaque nœud borne ce qu'il garde : 4 096 enregistrements, 32 Mio,
  et une part par voisinage d'adresses (256 enregistrements, 4 Mio), pour qu'un seul foyer ne puisse
  pas remplir un nœud.
- Un nœud n'accepte que les enregistrements dont il fait partie des plus proches : personne ne peut
  se servir de son disque comme d'un stockage gratuit.

### 5. Ta présence

- Ton document scellé est déposé chaque jour à un **endroit nouveau**, dont la clé de signature
  dérive de ta clé d'identité (que toi seul détiens).
- Chaque ami le trouve par un **pointeur** : un petit enregistrement à un endroit calculé à partir de
  la clé que vous partagez et du jour, qui contient l'emplacement du document, chiffré pour lui. Vous
  seuls savez calculer cet endroit.
- Conséquences :
  - les nœuds qui gardent ces enregistrements ne peuvent pas savoir à qui ils appartiennent ;
  - quelqu'un qui a ton code ami sans être ton ami ne trouve **rien** : il n'a pas de pointeur ;
  - personne ne peut suivre un emplacement d'un jour à l'autre ;
  - un ami retiré perd ta trace au changement de jour.
- **Entre amis en ligne**, une session directe remplace la table. Chacun prouve son identité par un
  HMAC sous la clé de paire, **lié au hachage de la poignée de main de cette session** : une preuve
  ne se rejoue pas dans une autre. Celui qui n'est pas ton ami ne reçoit **aucune réponse**, ce qui
  ne lui apprend même pas que tu es là. Les documents sont alors poussés dès qu'ils changent.
- En **Invisible**, aucune session directe n'est proposée ni acceptée : elle trahirait ta présence.

### 6. Les demandes d'ami

- Déposées dans la boîte du destinataire, qui change chaque jour, chiffrées pour lui seul.
- Chaque demande porte une **preuve que seul le détenteur de la clé de l'expéditeur peut faire** :
  personne ne peut demander en ami au nom d'un autre.
- Renvoyées une fois par jour, deux semaines au plus, tant que le destinataire n'a pas répondu ;
  relevées chaque minute.

### 7. Être joignable

- CubeShelf demande à la box d'ouvrir le port UDP 47914 (PCP, puis NAT-PMP, puis UPnP), en ne
  parlant **qu'à la passerelle par défaut**, et en refusant toute adresse de description ou de
  contrôle qui sortirait de la box (pas de détournement vers une autre machine).
- **Rien n'est cru sur parole** : une adresse candidate (celle de la box, celle que les autres nœuds
  voient, une IPv6 globale) n'est retenue que si un autre nœud y a envoyé une sonde **depuis une
  adresse avec qui nous n'avons jamais parlé**, et qu'elle est arrivée.
- Une sonde ne part **que vers l'adresse de celui qui la demande** : on ne peut pas s'en servir pour
  viser un tiers.
- Les nœuds annoncent leurs changements d'état sur leurs sessions ouvertes : une session d'ami qui
  dure des heures n'en reste pas à ce qu'elle savait au départ.

### 8. Relais et perçage des box

- Un nœud injoignable garde une session avec **deux relais**, qui lui donnent chacun un jeton. Ses
  amis connaissent ces jetons (ils sont dans son document scellé, pas ailleurs).
- Pour le joindre, un ami demande au relais de les mettre en contact. Le relais dit à chacun où il
  voit l'autre, et **les deux envoient en même temps** : la plupart des box laissent alors passer, et
  la session devient directe. Sinon (box « symétriques », 4G), le relais porte la session, **chiffrée
  de bout en bout** : il ne voit que des tailles et des moments.
- Le relais est borné : 64 réservations, un débit de connexions par réservation, 8 Mio et 200
  paquets par seconde par circuit. Un relais malhonnête ne peut nous faire envoyer des paquets vers
  un appelant qu'une fois toutes les dix secondes par appelant, et quelques fois par minute en tout.

## Ce qui est protégé, et comment

| Menace | Défense |
| --- | --- |
| Lire la présence de quelqu'un | Documents scellés ami par ami (AES-256-GCM sous la clé de paire). Les nœuds ne gardent que du chiffré. |
| Se faire passer pour quelqu'un | Clés d'identité, preuves HMAC liées à la session, enregistrements signés, demandes prouvées. |
| Rejouer un ancien document | Séquences strictement croissantes à chaque niveau ; fenêtre anti-rejeu dans les sessions. |
| Écouter le réseau (FAI, Wi-Fi) | Tout est chiffré entre nœuds ; même les recherches et les emplacements sont illisibles. |
| Usurper une position dans la table | Identifiant = hachage de la clé et d'une preuve de travail, vérifié à chaque poignée de main. |
| Empoisonner une table | Seuls les nœuds contactés à leur adresse y entrent ; diversité par voisinage ; les anciens restent. |
| Viser une personne pour la faire taire | Ses emplacements sont imprévisibles sans être son ami, et changent chaque jour. |
| Amplifier une attaque contre un tiers | Une réponse de poignée de main n'est jamais plus grande que la demande ; au-delà d'un seuil, il faut un cookie, qu'on ne reçoit qu'à sa vraie adresse ; les sondes ne vont qu'au demandeur ; les perçages sont limités. |
| Épuiser un nœud | Plafonds partout : sessions (1 024, 32 par adresse, 4 par nœud), poignées de main (4 par seconde et par adresse), messages en cours (1 Mio par session, 32 Mio en tout), requêtes simultanées, dépôts par session et en tout, vérifications de signature par réponse, preuves d'amitié (3 par session), stockage par voisinage. |
| Remplir le disque d'un nœud | Il ne garde que ce dont il fait partie des plus proches, un jour au plus, dans des quotas. |
| Inonder une boîte de demandes | Chaque entrée coûte une preuve de travail ; une boîte garde 32 entrées. |
| Une clé « hors courbe » dans un code | Toute clé est vérifiée sur la courbe, par calcul, là où elle entre. |

## Ce qui n'est pas protégé

Ce sont des limites de conception, pas des oublis :

- **L'adresse IP de ton PC** est vue par les nœuds avec qui il échange, comme dans tout réseau
  pair-à-pair. Ils ne savent pas qui tu es. Ton code ami contient cette adresse quand ton PC est
  joignable : donne-le en privé plutôt que sur un salon public.
- **Un relais voit qui parle à qui et quand**, pas ce qui se dit.
- **Un adversaire disposant de beaucoup d'adresses et de beaucoup de calcul** peut gêner, c'est-à-dire
  taire des enregistrements ; il ne peut ni les lire, ni les falsifier, ni viser une personne précise.
- **Un réseau naissant a besoin d'au moins un PC joignable.** Si personne autour de toi ne l'est,
  vous ne vous verrez que sur le même réseau local.
- **Pas de confidentialité persistante au niveau des documents** : qui obtient ta clé d'identité
  déchiffre rétroactivement les documents qu'il aurait gardés. Les sessions, elles, en ont une.
- **Un adversaire qui voit tout Internet** peut corréler les flux. Le réseau n'est pas un réseau
  d'anonymat (ce n'est pas Tor) : il protège le contenu et l'identité, pas le fait de communiquer.

## Comment c'est vérifié

- **Tests automatiques sur un Internet simulé** (`tests/CubeShelf.Core.Tests/Mesh*.cs`), dans un seul
  processus : box à cône complet, restreintes, à ports restreints et symétriques, pertes, doublons,
  latence, et jusqu'à 30 nœuds. Ils couvrent :
  - poignée de main altérée, rejeu, 3 000 datagrammes hostiles, amplification, cookies sous charge,
    messages à moitié envoyés ;
  - recherches qui convergent, enregistrements qui survivent au départ de leurs détenteurs, un nœud
    menteur qui ne pollue rien et ne fait pas viser le réseau local ;
  - joignabilité prouvée et non déclarée, perçage entre deux box fermées, repli sur le relais entre
    deux box symétriques, sondes impossibles à détourner ;
  - amis qui se trouvent et étranger qui ne trouve rien, preuves impossibles à forger ou à rejouer,
    demandes impossibles à faire au nom d'un autre.
- **Les traducteurs de port** (PCP, NAT-PMP, UPnP) sont testés contre de faux routeurs, y compris
  hostiles : DTD, bombes XML, URL de contrôle qui sortent de la box, réponses tronquées ou forgées.
- **Essais réels** entre trois CubeShelf sur un même PC, réseau local coupé :
  - deux profils se trouvent et se voient en ligne en 5 secondes, en session directe ;
  - un troisième, neuf, rejoint le réseau avec le seul code d'un ami : sa demande arrive en
    10 secondes, et il voit l'autre 5 secondes après l'acceptation.

**Jamais essayé :** entre deux vraies box, sur Internet. C'est le prochain test à faire.

## Réglages

Sur la page Mon profil, carte « Réseau CubeShelf » :

- **Participer au réseau CubeShelf** (activé par défaut). Désactivé, seuls les amis du même réseau
  local te voient.
- **Ouvrir le port de ma box automatiquement** (activé par défaut). Désactivé, CubeShelf passe par
  des relais.

Le port est l'UDP 47914 ; un autre est pris s'il est occupé. Windows peut demander d'autoriser
CubeShelf sur le réseau : il faut accepter.
