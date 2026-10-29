# DecompShelf — état de la finition

Candidate : **0.19.0-rc.1**, Android code **50**. Périmètre confirmé : bureau Windows, Big Picture, catalogue Android et distribution ; **Bip reste inchangé et hors de cette passe**.

Les résultats détaillés et les liens de contrôle sont dans [le rapport de validation](VALIDATION-0.19.0-rc.1.md). Les cases non cochées restent des limites, pas des essais réussis.

## Consolidation et navigation

- [x] Sources et ressources utiles regroupées dans des commits poussés ; outils personnels et fichiers temporaires exclus du dépôt.
- [x] Numéro de version central et code Android incrémenté ; identités techniques et profil existant conservés.
- [x] Accueil par console à chaque ouverture, photos au bureau et carrousel 3D stylisé en Big Picture.
- [x] Menu gauche conservé, défilant en petite hauteur ; Bibliothèque, Consoles, Téléchargements et Paramètres couverts par les tests.
- [x] Retour des jeux aux consoles par bouton, B et Échap, avec sélection conservée ; F11 fonctionne depuis le carrousel.
- [x] Tri, proportions des jaquettes, logos dans les sélecteurs et l’en-tête ; accueil français/anglais et thèmes clair/sombre vérifiés automatiquement.
- [ ] Dernier parcours manuel complet à la souris et au clavier, à toutes les résolutions de référence. La session Windows s’est verrouillée pendant la passe complémentaire : paramètres et F11 vérifiés, test de jeu supplémentaire non exécuté.
- [ ] Essai de la navigation et de l’inversion stick/croix sur une manette physique.

## Catalogues et fiabilité

- [x] 30 catalogues PC, regroupement des variantes sur une carte, sources épinglées et état expérimental conservés ; Melee-3D exclu conformément à la décision de l’utilisateur.
- [x] Catalogue Android distinct limité aux six plateformes ayant un moteur ou un APK Android identifié.
- [x] Installation automatique uniquement avec paquet vérifiable compatible ; sinon association d’un programme avec instructions explicites.
- [x] Contrôles automatisés : téléchargements, pause/reprise, annulation, archives invalides, empreintes, compatibilité et préservation des installations.
- [x] 259 jaquettes exactes de l’extension, 30 photos de consoles et logos locaux avec provenance ; 19 correspondances de titres supplémentaires contrôlées manuellement.
- [ ] 54 jaquettes exactes encore absentes, notamment Arcade, prototypes et éditions sans image trouvée. Aucun autre titre substitué.
- [ ] Affinage artistique des silhouettes 3D simplifiées ; les modèles sont annoncés comme stylisés.
- [ ] Parcours réels d’installation, préparation, jeu, sauvegarde et second lancement sur un ensemble représentatif de ports. La présence dans le catalogue n’est pas une preuve de jouabilité.

## Fluidité

- [x] Rendu à la demande, images bornées, absence de décodage supplémentaire à la recherche à chaud et libération des ressources contrôlés.
- [x] Benchmark reproductible hors écran et résultats bruts enregistrés dans le dépôt : médianes optimisées de 11,6 à 14,6 ms pour les trois formats historiques mesurés.
- [ ] Mesures complètes de démarrage à froid, de consommation au repos et de présentation GPU sur les machines de référence ; aucune promesse universelle de 60/120 FPS.

## Android : essais physiques en attente

- [x] Tests du catalogue, de l’intégrité des APK, de l’installation externe, des annulations et des mises à jour ; builds signés ARM64 et x86-64 réussis.
- [x] Confirmations système nécessaires conservées et différences entre moteurs intégrés et applications séparées documentées.
- [ ] Reprendre les essais sur le téléphone seulement lorsque l’utilisateur le déclare disponible.
- [ ] Installation neuve et mise à niveau conservant profil et sauvegardes ; autorisations, portrait/paysage, tactile, manette, arrière-plan et retour du jeu.
- [ ] Jouabilité des nouveaux ports et refus Android/ARM64/Vulkan sur les appareils concernés. Un APK vérifié ne suffit pas.

## Distribution

- [x] README, démarrage, commandes Big Picture, catalogues, crédits, mises à jour et documentation Android actualisés.
- [x] Six suites locales et validation GitHub Windows/Linux réussies sur la révision applicative `0602e32`.
- [x] Configuration de signature vérifiée : Android avec clé de développement persistante ; aucun certificat Authenticode de distribution annoncé pour Windows.
- [x] Premier installateur candidat : mise à niveau depuis 0.18.0, contrôle des fichiers installés, désinstallation et conservation du profil réussis sur runner jetable.
- [ ] Répétition du contrôle sur les paquets finaux Windows, six builds PC complets et vérification finale des signatures/manifestes/empreintes avant publication.
- [ ] Publication de la candidate avec notes de version et contrôles des fichiers mis à disposition.

## Condition de sortie

La candidate peut servir aux essais après contrôle de ses paquets. Elle ne remplace pas automatiquement la stable 0.18.0. Une qualification stable nécessite les essais physiques et les parcours de jeu ci-dessus ; les ports tiers expérimentaux et les jaquettes manquantes doivent rester signalés. Aucun travail supplémentaire sur Bip n’est inclus dans cette liste.
