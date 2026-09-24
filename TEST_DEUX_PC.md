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
| Un dossier synchronisé servi en HTTPS | Nextcloud, Dropbox… c'est ainsi que la présence circule |
| CubeShelf **0.9.1**, installé avec `CubeShelf-Setup-x64.exe` | La 0.9.0 se fige à la fermeture quand la présence est activée, et sa mise à jour intégrée peut détruire l'installation. Ses codes amis ne sont pas lisibles par la 0.9.1 non plus |
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

1. Sur chaque PC : **Mon profil** (Ctrl+7). Créez votre identité, puis dossier de publication,
   adresse publique, **« Tester l'adresse »**, et cochez **« Publier ma présence »**. La liste en
   haut de la page coche chaque étape ; le code n'apparaît que quand elles le sont toutes.
2. S'il n'apparaît pas, l'adresse ne sert pas le fichier : un partage Nextcloud demande
   `/download` à la fin.
3. **« Copier mon code »**, collez le message dans votre conversation. L'autre le copie, ouvre sa
   page Amis (Ctrl+6) : un bandeau propose « Ajouter Zera#4821 ? ». Vérifiez de vive voix que le
   numéro affiché est bien celui de l'autre.
4. **Faites-le dans les deux sens.** L'amitié va dans un sens à la fois : A qui ajoute B peut
   lire B, pas l'inverse. Tant que B n'a pas ajouté A, A reste invisible pour B.
5. **Attendez.** La lecture se fait toutes les 2 minutes, plus la latence de votre client de
   synchro. C'est le bon moment pour mesurer ce délai réel — notez-le, il décide si la fenêtre de
   fraîcheur de 15 minutes est bien réglée.

✅ **Attendu** : chacun voit l'autre « En ligne », avec son avatar et son statut.

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
| L'un voit l'autre, pas l'inverse | Un seul des deux a ajouté l'autre (étape 1.4) |
| **« DISQUE DIFFÉRENT »** | Les deux fichiers ne sont pas identiques. Recopiez-en un sur l'autre PC |
| Les deux se voient mais le ping reste « En attente… » | Le réseau : routeur, pare-feu, NAT. **CubeShelf n'y est pour rien** — c'est le compagnon qui traverse |
| La partie se lance et chacun attend seul | Vérifiez l'empreinte de l'étape 0 |

## Ce que cet essai ne prouvera pas

- **Que ça marche à travers n'importe quelle box.** L'auteur de PartyBoard l'écrit lui-même :
  l'accès réel à travers les box reste à valider, aucun pare-feu ni routeur n'a été modifié par
  les tests. Si le ping ne s'établit pas, c'est un problème PartyBoard, pas CubeShelf.
- **Que l'invitation arrive vite.** Elle arrive au prochain sondage. C'est une invitation posée
  sur la table, pas une sonnerie, et ça ne changera pas sans serveur.
- **Que ça tient plus de deux joueurs.** Le salon est prévu pour deux.

## Le repli, si le pilotage échoue

Rien n'est perdu : contre un compagnon trop ancien, « Rejoindre » copie le code au presse-papiers
et ouvre le compagnon en le disant. C'est l'ancien flux, plus pénible, mais il fonctionne.

## À rapporter après l'essai

1. Les deux empreintes de l'étape 0.
2. Le **délai réel** entre la publication et la réception, mesuré à l'étape 1.
3. Où ça a cassé, avec la fenêtre du compagnon en photo si le problème est de son côté.
4. « Exporter diagnostic » dans le compagnon, **sur les deux PC**.
