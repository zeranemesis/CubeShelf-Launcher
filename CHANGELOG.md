# Changelog

## 0.17.0-preview.5 — continuité Big Picture et jeux en plein écran, non publié

- Big Picture reste en plein écran derrière le jeu : aucune réduction automatique du launcher, retour direct à CubeShelf à la fermeture du jeu.
- La transition reste affichée jusqu’à la perte du focus au profit du jeu ; après 0,5 seconde elle reste statique. Retour à la bibliothèque possible par bouton ou Échap.
- Plein écran natif au démarrage : Sunshine/Eclipse via `SMS_WINDOW_MODE=fullscreen` et résolution du bureau, Strikers via `STRIKERS_FULLSCREEN=1`, PartyBoard via `video.enableFullscreen`, RingOut via `[Display] Fullscreen=True`.
- Les fichiers de préférences modifiés sont sauvegardés avec le suffixe `.cubeshelf-before-fullscreen` avant le premier changement ; les autres options sont conservées. Aucun moteur recompilé.

## 0.17.0-preview.4 — transition de lancement, non publié

- Big Picture PC et Android : jaquette 3D agrandie au centre, fond assombri et titre du jeu avec « Lancement… » pendant une transition de 0,5 seconde.
- Animation à la demande via le compositeur, plafonnée à 120 mises à jour/s ; aucune animation continue après la transition et libération des textures au masquage.
- PC : le processus démarre immédiatement ; seule la réduction du launcher attend la fin de la transition. L’animation s’arrête si le launcher perd le focus.
- Android : transition de 0,5 seconde avant ouverture de l’activité native ; annulation si le launcher passe en arrière-plan avant le lancement. Les échecs rendent la bibliothèque accessible.
- Code Android 8. Aucun moteur de jeu modifié.

## Android — Big Picture permanent, en préparation

- Une seule interface Big Picture, directement au démarrage : jaquettes 3D et commandes tactiles, sans sélecteur déroulant ni changement vers une vue classique.
- Plein écran immersif réappliqué au retour dans le launcher ; navigation système accessible par un geste depuis le bord.
- Étagère et fiche côte à côte en paysage, superposées en portrait ; fiche défilante pour conserver l’accès aux actions sur petit écran.
- Manette : A sélectionne, B revient au bouton principal, X retourne la jaquette, Y ouvre les salons ; croix verticale pour les actions et horizontale pour les jeux.
- Code de version Android 7. Aucun moteur ni catalogue de jeux modifié.

## 0.17.0-preview.3 — jaquettes et plein écran, non publié

- Copie explicite de toutes les jaquettes dans le dossier publié, hors bundle single-file ; la publication échoue si une image de couverture est absente.
- Big Picture applique son plein écran sans décoration après l’ouverture native et suspend l’ajustement de taille du bureau tant qu’il est actif. Les réglages de fenêtre sont restaurés en quittant ce mode.
- État du plein écran et nombre de fichiers de jaquettes manquants dans `Logs/BigPicture-display.log`.

## 0.17.0-preview.2 — correction du rendu 3D, non publié

- Correction du crash `DrawingGroupDrawingContext.DrawBitmap` lors du rendu des jaquettes GLB : les textures sont dessinées directement par le compositeur ; seuls les clips et matrices du maillage sont mis en cache. Le modèle 3D et le partage des bitmaps sont conservés.
- Démarrage direct en Big Picture avec `CubeShelf.exe --big-picture`.

## 0.17.0-preview.1 — améliorations du launcher, non publié

- Big Picture : retour à la page précédente et à son focus ; navigation manette suspendue quand la fenêtre est masquée ou réduite ou pendant une session de jeu ; recherche des manettes débranchées espacée.
- Téléchargements : aucun rafraîchissement de la vue cachée ; timer arrêté au repos ou en pause ; contrôle d’espace disque avant les transferts ; priorité visible et débit indépendant de l’historique.
- Jaquettes 3D conservées : images partagées entre vues, décodage adapté et cache inactif plafonné à 24 Mio ; géométries et maillages texturés réutilisés lorsque la pose ne change pas.
- Profils de mods avec ordre de priorité, aperçu des fichiers en conflit et accès direct au profil original, sans supprimer les mods installés.
- Paramètres : points de restauration du profil, restauration au prochain démarrage, restauration des cartes mémoire déjà sauvegardées par les transferts téléphone, mesure CPU/mémoire du launcher à la demande.
- Point de restauration du profil avant mise à jour, accès aux notes de version et conservation d’une version précédente du launcher : Windows après une mise à jour avec ce nouvel updater, Linux pour les AppImages mises à jour avec ce nouveau helper. L’installation macOS reste manuelle.
- Une fermeture inattendue propose de réessayer, d’ouvrir les journaux ou de revenir à la bibliothèque. Les arrêts demandés par l’utilisateur ne sont pas présentés comme des plantages.
- Mobile : partage du cache des jaquettes et mémorisation de la dernière sélection et de la vue.
- Lecture facultative des manettes SDL2 sur Linux/macOS lorsque cette bibliothèque est installée ; XInput conservé sur Windows.

Aucun moteur ni fichier de jeu modifié. Compilation PC, interface mobile et updater effectuée ; pas de test automatisé ni d’essai physique de manette ou de téléphone dans cette passe. Les gains de performances ne constituent pas une mesure de FPS.

## 0.16.0 — 22 octobre 2026

- Gestionnaire de téléchargements illustré : file, pause/reprise, priorités et limite de débit.
- Lancement direct depuis les jaquettes, préparation automatique et options avancées dépliées.
- Big Picture avec étagère 3D par défaut et modèle GLB fourni, tranche visible, rendu mis en cache et animations à la demande.
- Accueil Reprendre, profils de mods, clavier à l’écran et parcours de salons avec vérification avant connexion.
- Premier aperçu Android natif : mêmes jaquettes 3D, navigation tactile/manette sans boutons fléchés, lancement de PartyBoard installé séparément. Les autres jeux, amis et téléchargements PC ne sont pas encore portés ; jeu croisé Android/PC non disponible.
- Génération d’archives PC Windows/Linux/macOS x64 et ARM64.
- Correction d’un délai NAT-PMP devenu négatif à expiration, susceptible d’interrompre le repli UPnP.

Les binaires PC ne sont pas signés avec un certificat de distribution. Android reste un aperçu signé avec une clé de développement ; aucun essai physique de ce launcher mobile ni partie réelle Android n’a été effectué ici.

## Non publié — consolidation du launcher

- Le disque sélectionné est transmis selon le runtime : variables PartyBoard, argument Ring Out, `STRIKERS_DATA` pour Strikers. Les chemins contenant des espaces restent un seul argument.
- Le catalogue déclare les formats proposés par jeu : Strikers accepte CISO/GCZ et ne propose plus RVZ. Les champs de lancement sont migrés sans effacer les chemins utilisateur, favoris ou statistiques.
- Les ISO/GCM tronqués ou dépourvus de signature GameCube sont refusés. Les images compressées sont explicitement signalées comme non vérifiées par le launcher.
- Les tâches de mods sont enregistrées avant téléchargement et analyse des dépendances. Après redémarrage, elles peuvent être reprises depuis l’historique, avec reconstitution et confirmation du plan avant activation.
- Un serveur répondant HTTP 416 à une reprise d’archive complète déclenche un nouveau téléchargement. Une opération terminée n’affiche plus de reprise possible.
- Le diagnostic ne réclame plus une extraction CubeShelf ou DolphinTool pour les runtimes qui ouvrent eux-mêmes leur disque.
- README et roadmap actualisés ; guide de contribution ajouté.

Validation locale sous Windows : compilation de la solution sans erreur ni avertissement, 168 tests Core et 6 tests archives/disques réussis. Les cinq nouveaux scénarios couvrent les contrats du catalogue, la migration de profils, la reconnaissance des disques, la reprise des tâches de mods et les réponses HTTP Range/416. Le rendu graphique et les lancements réels des jeux restent à vérifier avec des disques utilisateur ; les essais entre deux box, Steam Deck et macOS restent à réaliser.
