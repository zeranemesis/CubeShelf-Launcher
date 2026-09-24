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
| CubeShelf ≥ le commit `459813b` | Avant, « Rejoindre » ne faisait que copier un code |
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

1. Sur chaque PC : **Paramètres → Amis et présence**. Nom affiché, dossier de publication,
   adresse publique, puis **« Tester l'adresse »**.
2. Le code ami n'apparaît **qu'après** un test réussi. S'il n'apparaît pas, l'adresse ne sert pas
   le fichier : un partage Nextcloud demande `/download` à la fin.
3. Échangez les deux codes (Ctrl+6, « Ajouter un ami »).
4. **Attendez.** La lecture se fait toutes les 2 minutes, plus la latence de votre client de
   synchro. C'est le bon moment pour mesurer ce délai réel — notez-le, il décide si la fenêtre de
   fraîcheur de 15 minutes est bien réglée.

✅ **Attendu** : chacun voit l'autre « En ligne », avec son avatar et son statut.

❌ Si l'un reste « Hors ligne » indéfiniment : le problème est dans la présence, pas dans les
invitations. Inutile de continuer.

## Étape 2 — le tour complet

**PC A (hôte)**

1. Bibliothèque → Mario Party 4. La carte **« Jouer ensemble »** doit être là.
2. Lisez la ligne d'état. Si elle dit *« trop ancien pour être piloté par CubeShelf »*, le PC A
   a un PartyBoard < 0.16.0 : retour à l'étape 0.
3. Cliquez **« Créer un salon et inviter mes amis »**.

✅ Le compagnon s'ouvre, le pseudo est déjà rempli, le disque est déjà sélectionné et en cours de
vérification. CubeShelf affiche *« Le compagnon prépare le salon »*.

✅ Une fois le disque haché, CubeShelf bascule tout seul sur *« Salon prêt, invitation envoyée »*.
Personne n'a rien copié.

**PC B (invité)**

4. Une notification apparaît : *« <nom> t'invite — Mario Party 4 »*. Elle peut prendre quelques
   minutes : la livraison se fait par sondage, pas par sonnerie.
5. Ctrl+6, le bouton **« Rejoindre »** est sur la ligne de l'ami.
6. Cliquez.

✅ Le compagnon s'ouvre déjà en train de rejoindre. Rien à coller, rien à choisir.

**Les deux**

7. Les deux joueurs apparaissent dans la liste, avec *« Identique — SHA-256 vérifié »* et un ping.
8. Le PC A lance **« Lancer pour tout le monde »**.

## Ce que chaque échec veut dire

| Symptôme | Maillon en cause |
| --- | --- |
| La carte « Jouer ensemble » n'apparaît pas | `OnlineCompanion` absent du catalogue, ou jeu autre que Mario Party 4 |
| « trop ancien pour être piloté » | `manifest.json` sans `capabilities` — PartyBoard < 0.16.0 |
| Le compagnon s'ouvre **vide** au lieu de créer le salon | Les flags n'ont pas été reçus : vérifiez que c'est bien le compagnon 0.16.0 qui a démarré |
| CubeShelf attend puis dit *« Le compagnon n'a pas créé de salon »* | Le salon n'a pas abouti côté compagnon — regardez sa fenêtre, pas CubeShelf |
| L'invité ne voit jamais l'invitation | Problème de présence (étape 1), ou l'ami est en pause ou bloqué |
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

Rien n'est perdu : la boîte **« Déjà un salon ouvert ? Colle son code ici »** reste sous la carte,
et le bouton « Rejoindre » d'un compagnon trop ancien copie le code au presse-papiers en le
disant. C'est l'ancien flux, plus pénible, mais il fonctionne — et c'est utile de tester les deux
chemins pendant que vous y êtes.

## À rapporter après l'essai

1. Les deux empreintes de l'étape 0.
2. Le **délai réel** entre la publication et la réception, mesuré à l'étape 1.
3. Où ça a cassé, avec la fenêtre du compagnon en photo si le problème est de son côté.
4. « Exporter diagnostic » dans le compagnon, **sur les deux PC**.
