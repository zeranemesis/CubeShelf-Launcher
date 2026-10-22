# Compatibilité PC et Android — audit du 22 octobre 2026

**Suite de l’audit :** la branche de travail contient maintenant un premier
projet `CubeShelf.Android` et une bibliothèque visuelle `CubeShelf.Mobile`.
Ils réutilisent le catalogue et le renderer 3D, détectent PartyBoard installé,
ouvrent son activité locale ou son salon et proposent le téléchargement officiel
sur ARM64. Les constats d’absence ci-dessous décrivent l’état initial audité.
Voir `packaging/ANDROID.md` pour les limites de l’aperçu et sa compilation.

Audit des sources GitHub, workflows, releases et du catalogue CubeShelf. Aucun jeu,
APK ni appareil Android n’a été exécuté. La présence d’une décompilation, d’un
projet Android ou même d’un APK compilé ne prouve pas qu’un jeu est jouable.

## CubeShelf

Le launcher actuel est un projet Avalonia.Desktop `net8.0`, avec une fenêtre
principale de bureau. Aucun projet Android n’est présent dans l’arbre `main`
audité au commit `c0a21b07688262171aa31228a47dd4e688aebebc`, ni dans notre branche
de travail au début de cet audit. Windows, Linux et macOS ont déjà des workflows.

Un workflow manuel supplémentaire prépare les archives Windows/Linux/macOS en
x64 et ARM64 : voir `packaging/DESKTOP-PORTABLE.md`. Ce workflow n’a pas été
déclenché ; ses six cibles ne sont pas validées sur appareil. Il ne publie pas de
release stable et ne porte pas les moteurs de jeu. Les PC 32 bits et Linux musl
ne sont pas couverts par cette matrice.

Sources : [projet Desktop](https://github.com/zeranemesis/CubeShelf-Launcher/blob/c0a21b07688262171aa31228a47dd4e688aebebc/src/CubeShelf.Desktop/CubeShelf.Desktop.csproj),
[catalogue](https://github.com/zeranemesis/CubeShelf-Launcher/blob/c0a21b07688262171aa31228a47dd4e688aebebc/src/CubeShelf.Desktop/games.json).

## Moteurs et autres dépôts

| Dépôt / jeu | PC constaté | Android natif constaté |
| --- | --- | --- |
| Mario Party 4 / PartyBoard | Binaires Windows, Linux x64, macOS Intel/ARM64 dans v0.19.0 | APK téléphone ARM64 publié ; intégration Android de CubeShelf à créer |
| Sunshine / Eclipse (`sms-port`) | Archives Windows dans `cubeshelf-v0.3.1`, construites depuis `native-pal-menu` | Aucun APK publié trouvé ; branche par défaut `port` distincte |
| Super Mario Strikers | v1.3.0 : Windows x64, Linux x64/ARM64, macOS ARM64 | Aucun APK publié ; fichiers Android dans Aurora ne constituent pas un port Strikers |
| Soulcalibur II / RingOut | v1.6.10 Windows/Linux x64 | Aucun APK ; le README décrit un runtime Dolphin avec recompilation statique, pas un port répondant à une exigence strictement sans Dolphin |
| Melee (`smashmelee`) | Bootstrap HSD/rendu, non annoncé jouable | Chaîne Android réelle et artifact compilé ; pas un jeu complet confirmé |
| Mario Party 5 | Port incomplet ; README dit non jouable | Projet NDK/Gradle réel ; dernier run examiné échoue avant production APK |
| Mario Party 6, Mario Kart Double Dash, TTYD, F-Zero GX, `sms` | Décompilations GameCube, pas de port PC jouable identifié | Aucun port Android prêt trouvé |
| Super Paper Mario (`spm-decomp`) | Décompilation partielle ; port hors objectif annoncé | Aucun port Android prêt trouvé |

Liens : [PartyBoard v0.19.0](https://github.com/zeranemesis/Marioparty4/releases/tag/v0.19.0),
[PartyBoard v0.20.0](https://github.com/zeranemesis/Marioparty4/releases/tag/v0.20.0),
[Sunshine/Eclipse](https://github.com/zeranemesis/sms-port/releases/tag/cubeshelf-v0.3.1),
[Strikers](https://github.com/new-coke/strikers/releases/tag/v1.3.0),
[RingOut](https://github.com/jackpoison-prog/RingOut),
[build Melee](https://github.com/zeranemesis/smashmelee/actions/runs/34841986363),
[Melee README](https://github.com/zeranemesis/smashmelee/tree/melee-port),
[MP5 workflow](https://github.com/zeranemesis/marioparty5/blob/main/.github/workflows/port.yml),
[MP5 run](https://github.com/zeranemesis/marioparty5/actions/runs/39618569440),
[MP6](https://github.com/zeranemesis/marioparty6), [MKDD](https://github.com/zeranemesis/mkdd),
[TTYD](https://github.com/zeranemesis/ttyd), [F-Zero GX](https://github.com/zeranemesis/fzgx),
[SMS](https://github.com/zeranemesis/sms), [SPM](https://github.com/zeranemesis/spm-decomp).

L’artifact Melee examiné vient d’une branche différente de la branche par défaut,
au commit `30fd805a`. Il n’a pas été téléchargé ni exécuté. Les sources, documents
et releases peuvent évoluer après cet audit.

## Mario Party 4 Android : intégration possible sans émulateur

Le port natif Android existe déjà. L’APK téléphone v0.20.0 fait 52 716 096 octets ;
le digest SHA256 fourni par l’API GitHub est
`7b11534b8e68d4386d03738c6f3fd97324ecf5a71dc212929128d0d8201c2374`.
Ce digest n’a pas été recalculé sur un téléchargement local.

Package : `com.mariopartyrd.partyboard`, minimum Android 8/API26 dans Gradle.
Lancement local : activité exportée `PartyBoardActivity`, extra `partyboard_argv`
en tableau de chaînes. Le champ `partyboard_disc` mentionné par une documentation
n’est pas lu par `getArguments()`. Salon : activité exportée `online.LobbyActivity`,
extras `nickname`, `disc`, `language`, `invitation`. CubeShelf devra gérer les
documents Android et transmettre leurs droits de lecture au jeu.

Sources au commit audité : [activité locale](https://github.com/zeranemesis/Marioparty4/blob/5ee7005440bc642f2121965f3763a9d02bd8cf4d/platforms/android/app/src/main/java/com/mariopartyrd/partyboard/PartyBoardActivity.java#L394),
[salon](https://github.com/zeranemesis/Marioparty4/blob/5ee7005440bc642f2121965f3763a9d02bd8cf4d/platforms/android/app/src/main/java/com/mariopartyrd/partyboard/online/LobbyActivity.java#L206),
[manifest](https://github.com/zeranemesis/Marioparty4/blob/5ee7005440bc642f2121965f3763a9d02bd8cf4d/platforms/android/app/src/main/AndroidManifest.xml),
[Gradle](https://github.com/zeranemesis/Marioparty4/blob/5ee7005440bc642f2121965f3763a9d02bd8cf4d/platforms/android/app/build.gradle).

### Jeu croisé téléphone / PC : contradiction documentaire

Le README racine annonce téléphone et PC ensemble. Cependant `Wire.java`, vérifié
à la branche par défaut et au tag v0.20.0, calcule un build spécifique à l’APK avec
le préfixe `partyboard-android` et refuse explicitement la connexion au PC.
Il ne faut donc pas annoncer le jeu croisé comme opérationnel sur cette base.
Ne pas supprimer ce contrôle : un protocole commun et la déterminisme des deux
versions doivent être établis avant d’autoriser des parties croisées.

Source v0.20.0 : [calcul du build](https://github.com/zeranemesis/Marioparty4/blob/10673868b69bbcdada496215656a5b7012145f5c/platforms/android/app/src/main/java/com/mariopartyrd/partyboard/online/Wire.java#L121),
[refus explicite](https://github.com/zeranemesis/Marioparty4/blob/10673868b69bbcdada496215656a5b7012145f5c/platforms/android/app/src/main/java/com/mariopartyrd/partyboard/online/Wire.java#L267).

## Ordre d’implémentation

1. Compiler et valider le launcher PC sur les six cibles préparées.
2. Créer le projet CubeShelf Android en conservant les jaquettes 3D ; intégrer
   l’APK natif PartyBoard et ses activités existantes, sans Dolphin.
3. Ajouter les autres jeux lorsqu’un port natif jouable est disponible.
4. Traiter le jeu croisé PartyBoard dans son moteur, avec validation réelle sur
   téléphone et PC ; cela ne se résout pas dans le launcher seul.
