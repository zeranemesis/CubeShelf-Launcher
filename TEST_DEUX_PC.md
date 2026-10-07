# Essai à deux PC — le réseau CubeShelf et les invitations

Ni le réseau CubeShelf ni les invitations n'ont **encore tourné entre deux box sur Internet**.
Le réseau a été testé sur un Internet simulé (tous les types de box) et entre trois CubeShelf d'un
même PC ; les invitations, unitairement. Ce document est la procédure pour le prouver ou le
démentir, et surtout pour savoir *lequel des maillons* a lâché quand ça échoue.

Il y a deux essais, à faire dans l'ordre : **le réseau** (étape 1, un quart d'heure, sans jeu),
puis **les invitations** (étapes 0 et 2). Compte deux heures pour le tout, dont l'essentiel en
attente de téléchargements.

## Ce qu'il faut avant de commencer

| Sur chaque PC | Pourquoi |
| --- | --- |
| Windows x64 | Le compagnon est WinForms, PartyBoard est win-x64 uniquement |
| Le **même fichier disque**, octet pour octet | Le salon compare un SHA-256 complet et refuse deux fichiers différents. Un ISO et un RVZ du même jeu sont deux fichiers différents |
| CubeShelf **0.10** sur les deux PC | Le réseau CubeShelf. Une 0.9 ne lit pas les codes de la 0.10 (`CSF3-…`) |
| Chez **au moins un** des deux : une box qui accepte UPnP, ou de l'IPv6, ou le port UDP 47914 ouvert à la main | Il faut un PC joignable pour que le réseau existe. Voir *Si aucun des deux n'est joignable* |
| Chez **chacun** : chez soi, pas sur le même réseau | C'est Internet qu'on teste. Sur le même réseau, CubeShelf passe par le réseau local et l'essai ne prouve rien |
| PartyBoard ≥ **0.16.0** | Avant, le compagnon ignore `--host` et `--join` |

> **Le fichier disque est le piège le plus coûteux.** Copiez-le d'un PC à l'autre plutôt que de
> le régénérer chacun de votre côté. Deux extractions du même disque physique ne donnent pas
> forcément deux fichiers identiques.

## Étape 0 — prouver que les deux PC ont le même PartyBoard

C'est la vérification qui évite de chercher pendant une heure. **Sur chaque PC**, dans le dossier
d'installation de PartyBoard :

```bash
PartyBoardOnline.exe --build-hash
```

Les deux doivent afficher **exactement la même empreinte**. Le compagnon lui-même entre dans ce
calcul, donc deux versions différentes du compagnon donnent deux empreintes différentes — et le
salon refusera de lancer la partie, tard et sans dire pourquoi. Si elles diffèrent, arrêtez-vous
ici : réinstallez PartyBoard des deux côtés depuis la même release.

## Étape 1 — le réseau CubeShelf (sans jeu, sans salon)

Avant de toucher au multijoueur, prouvez que les deux CubeShelf se trouvent et se voient.

**1. Sur chaque PC : Mon profil (Ctrl+7).** Créez votre identité. Laissez cochés « Participer au
réseau CubeShelf » et « Ouvrir le port de ma box automatiquement ». Si Windows demande d'autoriser
CubeShelf sur le réseau, **acceptez** (réseaux privés *et* publics).

**2. Regardez la carte « Réseau CubeShelf »** et notez ce qu'elle dit, sur chaque PC. À ce stade,
seul, chacun dit *« Aucun autre CubeShelf connu pour l'instant »* : c'est normal, personne n'a encore
donné de porte d'entrée. Elle précise si **ta box a ouvert le port** : c'est ce qui compte pour la
suite.

**3. Le premier code.** Si la carte de l'un dit que **sa box a ouvert le port**, c'est lui qui
envoie son code : « Copier mon code », collez le message dans votre conversation. L'autre le copie,
ouvre sa page Amis (Ctrl+6) : un bandeau propose « Ajouter Zera#4821 ? ». Vérifiez de vive voix que
le numéro est bien celui de l'autre, puis ajoutez. Si aucune des deux cartes ne le dit, faites-le
quand même, puis dans l'autre sens : une adresse IPv6 peut suffire.

> Pourquoi celui-là : un code porte l'adresse de son auteur quand sa box en annonce une. C'est par
> le code de celui qui est joignable que l'autre entre dans le réseau.

**4. Pas de code à renvoyer.** Chez celui qui a envoyé son code, une **demande d'ami** apparaît en
haut de la page Amis, avec « Accepter ». Acceptez.

✅ **Attendu, en moins d'une minute :**

- la carte « Réseau CubeShelf » passe à *« Connecté et joignable directement »* ou *« Connecté,
  joignable par N relais »* sur les deux PC ;
- la demande d'ami arrive (étape 4), et une fois acceptée, chacun voit l'autre **« ● En ligne »**,
  avec son avatar et son statut ;
- sous son nom : **« 🔗 En direct »** (vos PC se parlent sans intermédiaire) ou **« 🔗 En direct, par
  un relais »** (vos box n'ont pas laissé passer, un relais porte la conversation, chiffrée).

**En passant** : écrivez-vous un message (bouton « Message ») et changez de statut (« Ne pas
déranger » en haut de la page Amis). En direct, l'autre voit le message et le statut en une ou deux
secondes ; vous voyez « remis » sous votre message une fois qu'il l'a reçu.

❌ Si l'un reste « ○ Hors ligne » indéfiniment : le problème est dans le réseau, pas dans les
invitations. Inutile de continuer — regardez le tableau en bas.

### Si aucun des deux n'est joignable

Si les deux cartes disent encore *« Aucun autre CubeShelf connu »* une minute après l'échange des
codes, aucune de vos deux box n'a ouvert de porte (UPnP désactivé, IPv6 absent ou filtré). Il suffit
qu'**une** des deux le fasse :

1. Dans l'interface de la box (Livebox, Freebox, Bbox, box SFR…), activez **UPnP**, ou créez une
   **redirection de port** : UDP 47914 vers l'adresse locale du PC.
2. Redémarrez CubeShelf sur ce PC, puis **recopiez son code** : il porte maintenant une adresse.

Sans aucun PC joignable, deux CubeShelf ne se voient que sur le même réseau local : c'est la limite
de conception d'un réseau sans serveur.

## Étape 2 — le tour complet

**PC A (hôte)**

1. Lancez Mario Party 4 **depuis CubeShelf**, puis **F1 → onglet Amis**. Sans lancer le jeu :
   page Amis de CubeShelf (Ctrl+6).
2. Si le haut de l'onglet dit *« trop ancien pour les invitations »*, le PartyBoard installé ne
   déclare pas qu'il peut être piloté : réinstallez-le depuis CubeShelf, puis retour à l'étape 0.
3. **« Inviter »** à côté de l'ami (ou « Créer un salon et inviter tout le monde »). Confirmez :
   Mario Party 4 se ferme.

✅ Le compagnon s'ouvre, le pseudo est déjà rempli, le disque est déjà sélectionné et en cours de
vérification.

✅ Une fois le disque haché, CubeShelf publie l'invitation tout seul. Personne n'a rien copié.

**PC B (invité)**

4. Une notification apparaît : dans le jeu si vous y êtes (*« Zera#4821 t'invite à jouer. F1,
   onglet Amis, pour rejoindre. »*), sinon dans CubeShelf. En direct (étape 1), elle arrive en
   quelques secondes ; sinon au prochain passage, 25 secondes au plus pour un ami en ligne.
5. F1 → onglet Amis, **« Rejoindre »** à côté de l'ami. Ou page Amis de CubeShelf.
6. Confirmez : le jeu se ferme.

✅ Le compagnon s'ouvre déjà en train de rejoindre. Rien à coller, rien à choisir.

**Les deux**

7. Les deux joueurs apparaissent dans la liste, avec *« Identique — SHA-256 vérifié »* et un ping.
8. Le PC A lance **« Lancer pour tout le monde »**.

## Ce que chaque échec veut dire

| Symptôme | Maillon en cause |
| --- | --- |
| Pas d'onglet « Amis » dans F1 | Le jeu n'a pas été lancé par CubeShelf, ou ce PartyBoard n'a pas encore l'onglet |
| « CubeShelf n'est pas ouvert » dans l'onglet | CubeShelf a été fermé : l'onglet passe par lui |
| « trop ancien pour les invitations » | Le paquet PartyBoard ne contient pas de `manifest.json` déclarant `launcher-invites`. Ceux publiés avant le 2026-09-24 au soir n'en avaient pas : réinstallez PartyBoard |
| Le compagnon s'ouvre **vide** au lieu de créer le salon | Les flags n'ont pas été reçus : vérifiez que c'est bien le compagnon 0.16.0 qui a démarré |
| CubeShelf attend puis dit *« Le compagnon n'a pas créé de salon »* | Le salon n'a pas abouti côté compagnon — regardez sa fenêtre, pas CubeShelf |
| L'invité ne voit jamais l'invitation | Problème de présence (étape 1), ou l'ami est en pause ou bloqué |
| Le bandeau « Code ami trouvé » n'apparaît pas | Le presse-papiers ne contient pas le message entier, ou cet ami est déjà dans la liste. Collez le message dans « Ajouter un ami » à la place |
| « Aucun autre CubeShelf connu » sur les deux PC, même après l'échange des codes | Aucune des deux box n'est ouverte : voir *Si aucun des deux n'est joignable* |
| « Aucun autre CubeShelf connu » sur un seul PC | Celui-là n'a pas collé le code de l'autre, ou son pare-feu bloque CubeShelf : autorisez-le (Paramètres Windows → Pare-feu → Autoriser une application) |
| « Connecté, joignable par 0 relais » | Il a trouvé le réseau mais aucun relais ne l'a accepté encore. Attendre une minute ; si ça dure, l'autre PC n'est pas joignable non plus |
| La demande d'ami n'arrive jamais | Celui qui a collé le code n'est pas entré dans le réseau (sa carte le dit), ou la demande a été refusée une fois : elle n'est plus montrée jusqu'au prochain lancement. Collez alors aussi le code dans l'autre sens |
| L'un voit l'autre, pas l'inverse | Un seul des deux a ajouté l'autre : il apparaît « En attente » chez celui qui l'a ajouté, et une demande attend chez l'autre |
| « 🔗 En direct, par un relais » | Pas un échec : vos deux box refusent le perçage (box « symétriques », 4G). Tout marche, un peu plus lentement |
| En ligne, mais jamais « 🔗 En direct » | Le réseau marche par relevés (25 s) ; la session directe n'a pas pu s'établir. Notez-le, avec vos modèles de box |
| « Réseau local » : personne n'apparaît | Les deux PC ne sont pas sur le même réseau, un pare-feu bloque CubeShelf, ou le Wi-Fi isole ses clients (réseaux invités) |
| **« DISQUE DIFFÉRENT »** | Les deux fichiers ne sont pas identiques. Recopiez-en un sur l'autre PC |
| Les deux se voient mais le ping reste « En attente… » | Le réseau : routeur, pare-feu, NAT. **CubeShelf n'y est pour rien** — c'est le compagnon qui traverse |
| La partie se lance et chacun attend seul | Vérifiez l'empreinte de l'étape 0 |

## Ce que cet essai ne prouvera pas

- **Que ça marche à travers n'importe quelle box.** L'auteur de PartyBoard l'écrit lui-même :
  l'accès réel à travers les box reste à valider, aucun pare-feu ni routeur n'a été modifié par
  les tests. Si le ping ne s'établit pas, c'est un problème PartyBoard, pas CubeShelf.
- **Que le réseau tient à grande échelle.** À deux, chaque PC garde toutes les données de l'autre ;
  la répartition, la réplication et les défenses contre les nœuds malveillants ne se voient qu'avec
  beaucoup de monde. Elles sont couvertes par les tests sur Internet simulé, pas par cet essai.
- **Que ça marche avec toutes les box.** Un essai ne vaut que pour vos deux box : notez leurs modèles.
- **Que ça tient plus de deux joueurs.** Le salon créé depuis CubeShelf est pour deux ; on passe à
  trois ou quatre dans la fenêtre du compagnon pendant qu'il vérifie le disque.

## Le repli, si le pilotage échoue

Rien n'est perdu : contre un compagnon trop ancien, « Rejoindre » copie le code au presse-papiers
et ouvre le compagnon en le disant. C'est l'ancien flux, plus pénible, mais il fonctionne.

## À rapporter après l'essai

1. **Pour chaque PC** : le modèle de la box, et ce que disait la carte « Réseau CubeShelf » au
   début et une fois connectés (joignable directement, par relais, port ouvert sur ta box ou non).
2. Ce qui s'affichait sous le nom de l'ami : « 🔗 En direct », « 🔗 En direct, par un relais », ou
   rien.
3. Combien de temps la demande d'ami a mis à arriver, et combien de temps avant de voir l'autre en
   ligne.
4. Les deux empreintes de l'étape 0.
5. Où ça a cassé, avec la fenêtre du compagnon en photo si le problème est de son côté.
6. « Exporter diagnostic » dans le compagnon, **sur les deux PC**.
