# Stockage et sauvegardes locales

Le gestionnaire est disponible dans **Réglages → Stockage et sauvegardes** sur PC, y compris depuis les réglages du mode Big Picture, et dans **Téléchargements → Stockage et sauvegardes** sur Android.

## Ce qui est géré

- Espace disponible sur le volume de DecompShelf et tailles des moteurs, fichiers du jeu, sauvegardes, téléchargements et copies locales.
- À l'ouverture, liste des jeux occupant de l'espace dans les emplacements connus, du plus volumineux au plus léger. Chaque ligne montre sa jaquette sans recadrage, son total et les catégories présentes ; le clic ouvre le détail. Une image absente affiche « Sans jaquette ». Retour remonte à la liste, puis ferme le gestionnaire.
- Comptage unique par chemin : une archive téléchargée ou une sauvegarde déjà située dans le dossier d'installation n'est pas ajoutée deux fois au total du jeu. Aucun total global n'additionne des dossiers éventuellement partagés entre jeux.
- Création de copies locales, export ZIP, import et restauration d'une copie précédente.
- Choix du dossier de sauvegardes lorsqu'un port utilise un emplacement non standard. Sur Android, les dossiers des données privées du jeu sont proposés dans le gestionnaire.
- Nettoyage des paquets téléchargés sans supprimer le moteur déjà préparé ou la progression.
- Retrait des moteurs hébergés Android, en conservant les fichiers du jeu et les sauvegardes. Les moteurs fournis dans l'APK ne peuvent pas être retirés individuellement.
- Sur PC, le retrait d'un port natif géré supprime uniquement son programme `.exe` situé dans les fichiers de DecompShelf. Les dépendances et autres fichiers restent conservés. Aucun programme associé depuis un dossier externe n'est supprimé. L'outil de désinstallation PartyBoard existant conserve ses options de préservation des données préparées.
- L'action distincte **Supprimer l'installation** retire les fichiers des emplacements explicitement gérés : paquets et données privées Android ; révisions des ports natifs, runtimes et données préparées gérés sur PC. Les installations restant sur disque après perte de leur association sont aussi proposées lorsque leur identifiant est encore connu du catalogue.

La suppression d'installation pose la question **Conserver la sauvegarde ?** : **Oui** est sélectionné par défaut et garde la progression sur place. **Non** retire uniquement les fichiers de progression du dossier connu ou choisi, dont le chemin est affiché, y compris s'il est externe à l'installation. Les autres fichiers de sauvegarde reconnus et les dossiers usuels supplémentaires restent conservés. Une copie préalable de la progression connue est créée avant la première suppression dans les deux cas ; les copies locales restent disponibles et ne sont pas effacées par ce choix.

Si aucun emplacement de progression n'est connu, la confirmation reste désactivée jusqu'à la reconnaissance explicite de l'utilisateur qu'aucune progression non détectée n'est à conserver ; pour un format non standard, choisir d'abord son dossier de sauvegardes. Les dossiers partagés avec un autre jeu et les installations externes sont refusés avant toute suppression. La présence d'une image de jeu externe reconnue dans un dossier de sauvegardes mal choisi bloque aussi la suppression. Une interruption peut laisser l'installation ou la progression sélectionnée partiellement retirée ; la copie préalable reste restaurable. Le résultat affiche les octets supprimés, sans les présenter comme un gain net après la création de cette copie.

Quand un port PC mélange code et données dans le même dossier, le panneau affiche **Moteur et fichiers du jeu** plutôt qu'une séparation fictive. Les images originales et fichiers du jeu ne sont pas des cibles du nettoyage des téléchargements. Les emplacements des sauvegardes non détectés doivent être configurés ; le gestionnaire ne prétend pas connaître les conventions de tous les projets.

## Copies et restauration

Les copies restent dans `game-save-backups`, sous le dossier de configuration PC ou `files/storage` sur Android. Le réglage du dossier de sauvegardes est dans `game-storage.json`. Le profil de restauration PC inclut aussi ce réglage et les associations de ports natifs.

Une archive comprend un manifeste de jeu, le moteur d'origine, une date, une liste de fichiers et leurs SHA-256. L'import présente le jeu et le moteur annoncés avant confirmation. L'archive doit correspondre au jeu choisi ; les formats propres aux moteurs ne sont pas convertis. L'export/import reste manuel et local, sans synchronisation cloud.

Avant remplacement, tous les fichiers importés sont contrôlés. Les sauvegardes actuelles sont copiées ; une interruption pendant les remplacements rétablit les fichiers déjà modifiés. La copie préalable reste sélectionnable dans le gestionnaire. Une copie locale peut être supprimée séparément, après confirmation, sans modifier les fichiers utilisés par le jeu.

Limites : 10 000 fichiers et 2 Go de données par archive. Les chemins absolus, traversées de dossiers, liens symboliques, collisions de noms et fichiers exécutables sont refusés. Les cartes mémoire PartyBoard sont filtrées par le contrat existant, sans inclure les réglages de compte ou les fichiers du moteur. Un jeu ou une préparation en cours bloque les modifications.

## Contrôles effectués

- Tests Core : copie/restauration, copie préalable réutilisable, refus d'un autre jeu, altération, chemins inattendus, annulation, rollback si un jeu démarre pendant la restauration, protection des sauvegardes pendant le nettoyage et conservation des emplacements manuels.
- Tests du widget partagé : tailles 320, 390 et 900 pixels, variantes sombre et claire, création réelle d'une copie locale et retour à la manette.
- Tests du panneau mobile : ouverture depuis les téléchargements, conservation de la vue lors d'une pause/reprise correspondant au sélecteur Android et retour vers les téléchargements.
- Harmonisation visuelle : ressources et cartes DecompShelf, tailles alignées, action principale violette, suppressions distinctes, une colonne sur téléphone et deux sur PC. Le bouton Retour reste visible pendant le défilement. Tests et revue Impeccable `ship` sur les captures du widget partagé.
- Le test d'ouverture mobile utilise un clic envoyé à travers la fenêtre headless, sans déclencher directement l'événement du bouton.
- Tests de l'inventaire : classement des tailles et exclusion des entrées vides, ouverture de la ligne au clic, sélection à la manette et retour détail → liste → fermeture. Tests de suppression sur données synthétiques : fichiers de jeu retirés, progression et copie préalable conservées, sauvegardes supplémentaires retenues, refus des cibles partagées/externes, jeu actif, annulation et reconnaissance obligatoire si l'emplacement des sauvegardes est inconnu.
- Revue Impeccable : corrections des cibles 48 et espacements 8 contrôlées ; verdict `ship` limité aux corrections examinées sur les rendus headless. Les gestes, insets et gros caractères sur téléphone réel ne sont pas annoncés comme vérifiés.
- Choix de progression : Oui par défaut, Non sélectionnable à la manette, annulation sans modification ; suppression explicite et restauration de la copie préalable, interruption en cours et refus des sauvegardes partagées ou d'une image de jeu externe dans le dossier choisi. Les jaquettes réelles du catalogue sont contrôlées sans recadrage aux trois largeurs.

Les essais utilisent des fichiers de progression synthétiques dans des dossiers de test ; aucune sauvegarde personnelle n'a été restaurée ou supprimée pour vérifier cette fonctionnalité.

## Fichiers préparés et limites de validation

- Windows : publication autonome `win-x64`, dossier voisin `decompshelf-storage-windows-2026-11-03`.
- Android : APK ARM64, version interne 57, dossier voisin `decompshelf-storage-android-2026-11-03`. L'empreinte courante est publiée dans le fichier `.apk.sha256` voisin.
- Les 198 tests Core passaient avant ces derniers ajouts. Pour les jaquettes et le choix de conservation : groupe de tests Core stockage et suite Mobile repassés ; vérifications du paquet Android effectuées après reconstruction.
- Après ajout de l'inventaire : groupe de tests Core stockage et suite Mobile repassés ; publications Windows et Android reconstruites. Revue Impeccable `ship`, limitée à la liste, au détail et à la confirmation montrés dans les captures headless synthétiques.

Sur l'émulateur Android de test, la variante x64 affiche le panneau des téléchargements, mais les injections tactiles ADB des boutons donnent un résultat non concluant. Le parcours complet d'export/import dans le sélecteur système n'est donc pas annoncé comme validé sur Android. Les moteurs hébergés restent expérimentaux : les démarrages contrôlés ne constituent pas une validation de parties complètes.

Sur demande suivante, installation ADB de la version ARM64 57 sur un Samsung Galaxy S25+ (SM-S936B), sans effacer les données de la version 52. Le test tactile ouvre les téléchargements, le stockage et le détail de Mario Party 4 ; les jaquettes, tailles, Oui par défaut, sélection de Non et annulation s'affichent et répondent. Aucun jeu ni fichier de progression personnel n'a été supprimé. Ce test a révélé une erreur au retour à la liste : Avalonia demande un rendu avec une entrée nulle pendant le recyclage des lignes. La protection du template et un test attendant la fin réelle du rafraîchissement corrigent ce cas ; l'ancien test ne vérifiait que l'affichage immédiat de la liste.

Après correction, le retour détail → liste et le bouton Actualiser passent sur le téléphone, avec les trois mêmes jeux et tailles. Le relancement explicite de l'activité au premier plan par ADB a aussi révélé une seconde activité essayant d'attacher la même vue Avalonia ; le mode `SingleTop` réutilise l'activité déjà au premier plan.

La dernière publication Android utilise un dossier intermédiaire neuf (`-p:BaseIntermediateOutputPath=obj/storage-clean/` et `-p:DefaultItemExcludes=**/obj/**`) après une assertion native Mono au démarrage d'une publication incrémentale. Aucune donnée Android n'a été effacée pour contourner cette erreur. L'APK reconstruit est réinstallé avec `adb install -r` ; un arrêt du processus suivi d'un lancement par le lanceur crée le processus, et le second lancement explicite est délivré à cette même activité (`intent has been delivered to currently running top-most instance`, PID conservé).

Après déverrouillage, le dernier APK est revérifié visuellement sur le téléphone : ouverture tactile des téléchargements puis du stockage, trois jaquettes et tailles réelles, détail de Mario Party 4, confirmation Oui par défaut, sélection de Non avec chemin du dossier, annulation puis retour à la liste et actualisation. Les trois entrées et leurs tailles restent présentes, dont Mario Party 4 à 35,7 Mo avec 16 Mo de sauvegardes. Aucun message d'erreur dans la liste et aucune entrée dans le tampon de crash pour le processus testé. Captures de cette passe : `phone-adb-final-inventory.png`, `phone-adb-final-keep-default.png`, `phone-adb-final-delete-choice.png` et `phone-adb-final-cancel-return.png`, dans le dossier de travail voisin `android-embedded-engines/storage-ui`. Suppression réelle et export/import système restent hors de ce test sur données personnelles.
