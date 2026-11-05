# Power Moon et Recharged — 5 novembre 2026

Le catalogue PC compte désormais **664 références de projets**. Power Moon est une version supplémentaire de Super Mario Odyssey, regroupée avec les autres ports dans la même fiche Switch et avec la jaquette existante. Recharged était déjà présent sous Mario Strikers Charged dans le catalogue Wii ; aucune seconde fiche n'est créée. Le catalogue Android reste inchangé pour ces deux projets.

| Jeu | Port et version épinglée | Programme Windows | Source primaire |
| --- | --- | --- | --- |
| Super Mario Odyssey — Switch | Power Moon Recomp **v0.2.1** | `Power Moon Recomp/PowerMoonRecomp.exe` | [Release GitLab](https://gitlab.com/zzzarathustra-dev/power-moon-recomp/-/releases/v0.2.1) · [README épinglé](https://gitlab.com/zzzarathustra-dev/power-moon-recomp/-/blob/cdbe05c9df0e2ff4bfbecece89ad3f0fd9806193/README.md) |
| Mario Strikers Charged — Wii | Recharged **v1.1.1** | `recharged.exe` | [Release GitHub](https://github.com/previouslevelgames/recharged/releases/tag/v1.1.1) · [Instructions d'installation](https://github.com/previouslevelgames/recharged/blob/f3664f7789553b0d4ba497a75d7963916704c670/docs/INSTALL.md) |

Le lien Power Moon demandé visait v0.2.0. La v0.2.1, publiée ensuite, est la version retenue après contrôle. Son archive contient aussi Zig et Python : le chemin du programme de jeu est déclaré explicitement pour éviter de sélectionner ces outils comme exécutable principal. Recharged utilise également un chemin déclaré.

## Contrôles des paquets

Les archives Windows ont été téléchargées, comparées aux tailles et SHA-256 publiés, puis contrôlées par lecture ZIP et CRC. Ces vérifications portent sur les fichiers ; **aucune partie n'a été exécutée dans cette passe**.

| Archive | Taille en octets | SHA-256 |
| --- | ---: | --- |
| `Power_Moon_Recomp-0.2.1-windows.zip` | 85 502 153 | `b14f6b7acdfa06915f383a9edaabd041e336e8a89f5e1a367615a3fae4837f44` |
| `recharged-1.1.1+gf3664f778955-windows-x86_64.zip` | 44 048 825 | `bcdd3ef197af355ac17581a30ded5a29add666137a4755a87f8546cdc5a6e182` |

Le téléchargement GitLab passe par l'API des pièces jointes du projet déclaré. La vérification amont reconnaît les familles de paquets déjà connues, une empreinte SHA-256 publiée sans ambiguïté et la taille annoncée par HEAD. Les versions de prévisualisation identifiées sont écartées. Les contrôles réels C# et Python ont reconnu v0.2.1 ; Recharged est toujours reconnu en v1.1.1.

## Préparation et compatibilité

Power Moon demande les dossiers `exefs` et `romfs` d'une copie personnelle de **Super Mario Odyssey 1.0.0**, sans mise à jour. Le premier démarrage prépare environ 6 Go de données et recompile le code. L'auteur indique Windows 10/11 x64, un processeur AVX2, Vulkan 1.3, 8 Go de RAM minimum et environ **7 Go libres** ; 16 Go de RAM sont recommandés. Ces prérequis et la jouabilité déclarée par l'auteur ne constituent pas un essai matériel par DecompShelf.

Recharged demande une image ISO ou RVZ compatible de Mario Strikers Charged. La release précise les révisions acceptées : USA `R4QE01` révision 1, Europe `R4QP01` révision 1 ou 2, Japon `R4QJ01` et Corée `R4QK01`. Le jeu prépare lui-même les fichiers nécessaires.

Aucun APK Android officiel n'a été identifié pour ces deux projets dans les releases contrôlées ; ils ne sont pas ajoutés au catalogue mobile.

## Données lors d'une mise à jour

L'installation gérée conserve l'ancien moteur et copie les sauvegardes et réglages reconnus vers le nouveau dossier. Pour Power Moon, elle ne reprend pas automatiquement `extracted/romfs.bin` ni `odyssey_game.dll`. Une mise à jour dans un nouveau dossier peut donc demander de sélectionner à nouveau les fichiers personnels et de refaire la préparation. Cette passe n'ajoute pas de migration des quelque 6 Go de données préparées ; l'ancien dossier reste conservé.
