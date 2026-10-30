# Validation de DecompShelf 0.19.0-rc.1

Révision applicative : `b25c9d42b0acf830918f24f55fefdd25bd304015`. Android : code 50. Bip reste inchangé ; seule la configuration du compilateur natif Windows ARM64 a été corrigée pour construire les paquets existants.

## Vérifications effectuées

- Six suites locales réussies : Core, archives/disques, NativePorts, ShelfUi, Mobile, AppUpdates. ShelfUi et Mobile repassées après les corrections concernées ; les régressions F11, second Échap à l’accueil et synchronisation de l’en-tête de la fiche ont été ajoutées puis vérifiées.
- Catalogues : 30 plateformes PC, titres regroupés sans cartes dupliquées ; origines et empreintes des paquets, refus des archives invalides, préservation des installations et commandes d’annulation contrôlés par les suites.
- Android : six plateformes issues du catalogue Android, intégrité des téléchargements, choix des APK compatibles, remise à l’installateur et annulation testés sans téléphone.
- Ressources : empreintes des 30 photos et 28 logos importés vérifiées contre leurs métadonnées de provenance.
- Interface : photos et logos intégrés, retours aux consoles par bouton/B/Échap, maintien de la sélection, menu gauche accessible, défilement en petite hauteur, thèmes clair/sombre, titres longs et accueil français/anglais couverts par les tests.
- Windows réel : accueil à photos, choix GameCube et Nintendo 64, bibliothèque, carrousel Big Picture, retour par bouton, retour F11 depuis le carrousel corrigé et page Paramètres observés avec un profil de test distinct. Cette passe ne mesure pas les FPS de présentation.
- Premier installateur candidat construit sur GitHub : installation de 0.18.0, mise à niveau, contrôle des fichiers de la candidate, désinstallation et conservation du marqueur de profil réussis sur un runner Windows jetable. Le paquet final a repassé le même contrôle avec succès sur la révision ci-dessus.

## Performances reproductibles

Mesure Skia hors écran du 29 octobre 2026, 1000 × 600, sept boîtiers, 56 rendus par configuration. Résultats bruts : `benchmarks/big-picture-2026-10-29.json`.

| Format | Médiane optimisée | P95 | Allocation gérée/rendu |
| --- | ---: | ---: | ---: |
| GameCube | 14,06 ms | 24,37 ms | 62,0 Kio |
| Wii | 14,60 ms | 17,98 ms | 94,4 Kio |
| Switch | 11,60 ms | 18,01 ms | 88,0 Kio |

Le test de recherche à chaud ne déclenche aucun décodage de jaquette supplémentaire ; estimation du cache d’images : 34,2 Mio. Ces mesures ne comprennent pas une partie, le démarrage à froid complet ou la cadence GPU d’un écran physique. Aucune garantie universelle de 60/120 FPS.

## Validation GitHub

La campagne précédente (`0602e32`) a réussi intégralement, y compris la mise à niveau de l’installateur. Les liens ci-dessous identifient la campagne finale, réussie sur les trois jobs Windows/Linux, les deux APK et les six paquets PC.

- [Builds PC, six architectures/systèmes](https://github.com/zeranemesis/CubeShelf-Launcher/actions/runs/41015562973).
- [APK ARM64 et x86-64](https://github.com/zeranemesis/CubeShelf-Launcher/actions/runs/41015565829) : dernière campagne de compilation, tests mobile et contrôle des moteurs intégrés.
- [Suites Windows et Linux](https://github.com/zeranemesis/CubeShelf-Launcher/actions/runs/41015568808).

La publication vérifie la même révision applicative pour PC/Android, les SHA-256, le contenu des archives, les ABI, le certificat Android historique, l’identifiant et le code 50. Son résultat doit être consigné après exécution ; le simple lancement d’un workflow n’est pas une validation.

La tentative complémentaire de lancement réel de Mario Party 4 a été suspendue après verrouillage de Windows : aucun lancement de jeu ni parcours de sauvegarde ne doit être déduit de cette tentative.

## Limites avant qualification stable

- Téléphone physique indisponible : installation/mise à niveau, autorisations, rotation, arrière-plan, manette et jouabilité des nouveaux ports restent à tester à sa disponibilité.
- Manette physique, autres architectures PC et parcours de sauvegarde en partie non qualifiés sur cette candidate. Les simulations d’entrées ne remplacent pas ces essais.
- 54 jaquettes exactes restent absentes ; aucune image d’un autre titre ne leur est substituée. Les consoles 3D restent stylisées, plusieurs silhouettes simplifiées.
- Les catalogues sont documentés, pas une garantie de jouabilité ni d’exhaustivité mondiale des ports.
- Windows sans certificat Authenticode de distribution. Android conserve une clé de développement persistante, compatible avec les aperçus précédents utilisant cette clé.
- Candidate installée manuellement : elle ne remplace pas la dernière release stable dans les recherches de mise à jour automatique.
