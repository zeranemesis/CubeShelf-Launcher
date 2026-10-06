# Essai à deux PC — invitations CubeShelf

Le système d'invitation n'a **jamais tourné entre deux machines physiques**. Tout ce qui suit a
été compilé, raisonné et testé unitairement ; rien n'a été joué. Ce document est la procédure
pour le prouver ou le démentir, et surtout pour savoir *lequel des maillons* a lâché quand ça
échoue.

Compte deux heures pour le premier essai, dont l'essentiel en attente de téléchargements.

## Ce qu'il faut avant de commencer

| Sur chaque PC | Pourquoi |
| --- | --- |
| Windows x64 | Le compagnon est WinForms, PartyBoard est win-x64 uniquement |
| Le **même fichier disque**, octet pour octet | Le salon compare un SHA-256 complet et refuse deux fichiers différents. Un ISO et un RVZ du même jeu sont deux fichiers différents |
| Un dossier synchronisé servi en HTTPS — **sauf** si les deux PC sont sur le même réseau | Dropbox, Nextcloud, OneDrive, Google Drive : c'est ainsi que la présence circule à distance. Sur le même réseau, CubeShelf s'en passe |
| CubeShelf **0.10** | Réseau local, liens convertis, statuts, messages. Une 0.9.x voit une 0.10 « en ligne » mais rien de plus |
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

## Étape 1 — la présence circule (sans jeu, sans salon)

Avant de toucher au multijoueur, prouvez que les deux CubeShelf se voient.

**Sur le même réseau** (le plus simple, à faire en premier) :

1. Sur chaque PC : **Mon profil** (Ctrl+7), créez votre identité. C'est tout pour la configuration.
2. Les deux ouvrent **Amis → « Réseau local »** en même temps. Chacun voit l'autre par son pseudo ;
   l'un clique « Ajouter », l'autre « Accepter ». Si Windows demande d'autoriser CubeShelf sur le
   réseau privé, acceptez.

✅ **Attendu** : en quelques secondes, chacun voit l'autre « En ligne » et « 📶 Sur ton réseau ».

**À distance** (chacun chez soi) :

1. Sur chaque PC : **Mon profil**, choisissez un des dossiers synchronisés détectés. CubeShelf ouvre
   le dossier et dit comment partager `cubeshelf-presence.json` avec votre service ; collez le lien
   tel quel. Il est converti et testé tout seul ; le code apparaît quand il marche. Cochez
   **« Publier ma présence »**.
2. **« Copier mon code »**, collez le message dans votre conversation. L'autre le copie, ouvre sa
   page Amis (Ctrl+6) : un bandeau propose « Ajouter Zera#4821 ? ». Vérifiez de vive voix que le
   numéro affiché est bien celui de l'autre. Juste après l'ajout, CubeShelf propose de renvoyer son
   propre code : faites-le.
3. Tant que l'autre ne vous a pas ajouté, il apparaît **« En attente »** chez vous.
4. **Attendez.** Un ami en ligne est relu toutes les 25 secondes, plus la latence de votre client de
   synchro. Notez ce délai réel : il décide si la fenêtre de fraîcheur de 15 minutes est bien réglée.

✅ **Attendu** : chacun voit l'autre « En ligne », avec son avatar et son statut.

**En passant** : écrivez-vous un message (bouton « Message ») et changez de statut (« Ne pas
déranger » en haut de la page Amis). L'autre doit voir le message arriver et votre statut changer ;
vous devez voir « remis » sous votre message une fois qu'il l'a reçu.

❌ Si l'un reste « Hors ligne » indéfiniment : le problème est dans la présence, pas dans les
invitations. Inutile de continuer.

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
   onglet Amis, pour rejoindre. »*), sinon dans CubeShelf. Elle peut prendre quelques minutes :
   la livraison se fait par sondage, pas par sonnerie.
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
| L'un voit l'autre, pas l'inverse | Un seul des deux a ajouté l'autre : il apparaît « En attente » chez celui qui l'a ajouté |
| « Réseau local » : personne n'apparaît | Les deux PC ne sont pas sur le même réseau, un pare-feu bloque CubeShelf, ou le Wi-Fi isole ses clients (réseaux invités) |
| Un bandeau dit que ton adresse sert une page, a disparu ou est en retard | Le lien de partage a été retiré ou le client de synchro est arrêté : recréez le lien et retestez |
| **« DISQUE DIFFÉRENT »** | Les deux fichiers ne sont pas identiques. Recopiez-en un sur l'autre PC |
| Les deux se voient mais le ping reste « En attente… » | Le réseau : routeur, pare-feu, NAT. **CubeShelf n'y est pour rien** — c'est le compagnon qui traverse |
| La partie se lance et chacun attend seul | Vérifiez l'empreinte de l'étape 0 |

## Ce que cet essai ne prouvera pas

- **Que ça marche à travers n'importe quelle box.** L'auteur de PartyBoard l'écrit lui-même :
  l'accès réel à travers les box reste à valider, aucun pare-feu ni routeur n'a été modifié par
  les tests. Si le ping ne s'établit pas, c'est un problème PartyBoard, pas CubeShelf.
- **Que l'invitation arrive vite à distance.** Sur le même réseau, elle arrive en une seconde ;
  à distance, au prochain sondage (25 secondes pour un ami en ligne) plus la latence du service.
- **Que ça tient plus de deux joueurs.** Le salon créé depuis CubeShelf est pour deux ; on passe à
  trois ou quatre dans la fenêtre du compagnon pendant qu'il vérifie le disque.

## Le repli, si le pilotage échoue

Rien n'est perdu : contre un compagnon trop ancien, « Rejoindre » copie le code au presse-papiers
et ouvre le compagnon en le disant. C'est l'ancien flux, plus pénible, mais il fonctionne.

## À rapporter après l'essai

1. Les deux empreintes de l'étape 0.
2. Le **délai réel** entre la publication et la réception, mesuré à l'étape 1.
3. Où ça a cassé, avec la fenêtre du compagnon en photo si le problème est de son côté.
4. « Exporter diagnostic » dans le compagnon, **sur les deux PC**.
