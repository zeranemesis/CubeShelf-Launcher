# Roadmap

## v0.6
- [x] Français / English
- [x] thème sombre / clair
- [x] transition de thème animée
- [x] sélecteur de thème animé
- [x] animations de survol
- [x] ComboBox custom lisible
- [x] scrollbars custom
- [x] page Paramètres
- [x] préférences persistantes
- [x] pop-up de mise à jour de jeu
- [x] résumé des changements dans la pop-up
- [x] détection de révision ISO/GCM
- [x] affichage des 6 révisions MP4 prises en charge
- [x] RVZ sélectionnable
- [x] GameBanana actualisable à l'ouverture des Mods

## v0.7
- [x] installateur Windows sans droits administrateur
- [x] extraction sécurisée des archives et limites de téléchargement
- [x] vérification SHA-256 du paquet Dolphin épinglé
- [x] pipeline CI et releases stables signées
- [x] publication Windows PartyBoard indépendante de Linux/macOS
- [x] chargement ISO/RVZ via PartyBoard et Dolphin
- [ ] pause/reprise/annulation de la file
- [ ] file persistante après redémarrage
- [x] détection des conflits de fichiers entre mods activés
- [ ] dépendances déclaratives entre mods

## v0.8 - socle multiplateforme

- [x] extraction du noyau portable `CubeShelf.Core`
- [x] chemins Windows et XDG injectables et testés
- [x] lancement de processus abstrait par plateforme
- [x] manifeste de release v2 strict par OS, architecture et type d’artefact
- [x] prototype Avalonia commun à Windows et Linux
- [x] compilation et tests du noyau sur Windows et Ubuntu
- [x] sélection persistante ISO/GCM/RVZ et exécutable dans Avalonia
- [x] validation des révisions ISO/GCM partagée avec Windows
- [x] installation PartyBoard par manifeste, plateforme et architecture
- [x] contrôle de taille et SHA-256 avant activation du runtime
- [x] progression et annulation de l’installation dans Avalonia
- [x] détection de la version installée et des installations incomplètes
- [x] mise à jour, réparation et désinstallation confinée du runtime
- [x] nettoyage des anciennes versions après activation réussie
- [x] paramètres persistants, thèmes et stockage dans Avalonia
- [x] migration non destructive du profil WPF
- [x] préparation transactionnelle ISO/GCM/RVZ dans Avalonia
- [x] vérification et mise à jour intégrée Windows dans Avalonia
- [x] page d’activité des téléchargements persistante dans Avalonia
- [x] suivi des runtimes et mods, erreurs, annulations et interruptions
- [x] reprise HTTP Range des runtimes et mods interrompus
- [x] bouton de reprise depuis l’historique après redémarrage
- [x] catalogue GameBanana et installation sécurisée des mods dans Avalonia
- [x] activation, priorité, suppression et conflits dans Avalonia
- [x] descriptions, galerie mise en cache et dépendances déclaratives des mods dans Avalonia
- [x] confirmation du plan et rollback atomique d’installation des mods


## v0.8 - catalogue multi-jeux

- [x] révisions de disque déclarées par jeu au lieu d'être compilées pour Mario Party 4
- [x] identifiant de disque nu accepté pour couvrir toutes les révisions
- [x] second mode d'acquisition : asset de release GitHub, sans manifeste éditeur
- [x] sélection de l'asset par motif à joker, version dans le nom de fichier
- [x] chemin de lancement déclaré par plateforme, avec repli par recherche du binaire
- [x] installation non vérifiable autorisée sans blocage, mais enregistrée comme telle
- [x] état « installé sans vérification » conservé et affiché dans la fiche du jeu
- [x] préparation du disque déléguée au runtime quand il la fait lui-même
- [x] détection de mise à jour via l'API releases pour les dépôts sans manifeste
- [x] Soulcalibur II / Ring Out ajouté au catalogue
- [x] Super Mario Strikers / Strikers ajouté au catalogue
- [x] checksums publiés par l'éditeur (`SHA256SUMS`) lus et appliqués
- [x] archives `.tar.gz` acceptées en plus des `.zip`
- [ ] SHA-256 épinglé pour Ring Out dès qu'un checksum est publié en amont
- [x] jaquettes Super Mario Strikers (jaquette PAL Mario Smash Football, GameTDB)
- [ ] mods GameBanana pour Soulcalibur II (identifiant de jeu à déterminer)


## v0.9 - amis décentralisés

Pair-à-pair sans service central : chacun publie un document chiffré à un endroit qu'il contrôle, les amis le relisent en HTTPS.

- [x] identité P-256 générée localement, sans compte ni délivrance
- [x] code ami portant clé publique + adresse + somme de contrôle
- [x] document chiffré une fois, clé de contenu emballée par ami
- [x] nombre d'amis masqué par rembourrage des coffres
- [x] séquence strictement croissante contre le rejeu, réservée par lots et plancher horloge
- [x] présence périmée lue hors ligne plutôt que figée sur le dernier jeu
- [x] lecture des mods sans réécrire le fichier qu'un jeu en cours lit
- [x] publication dans un dossier déjà synchronisé, sans secret au repos
- [x] adresse vérifiée par aller-retour réel avant délivrance du code ami
- [x] lecture conditionnelle des amis (ETag/304), plafond de taille, recul persistant
- [x] un ami injoignable n'interrompt pas les autres et ne lève pas le bandeau réseau
- [x] boucle de publication : anti-rebond, battement, document d'adieu à la fermeture
- [x] page Amis, carte Paramètres, ajout par code collé
- [x] blocage avec pierre tombale : le code recollé ne défait pas la décision
- [x] profil publié (avatar réduit en 96×96, statut, agrégats, jeu mis en avant)
- [x] invitations limitées au seul jeu dont le runtime a un compagnon en ligne
- [ ] rejoindre sans coller : demande une option de ligne de commande côté PartyBoard
- [ ] mesurer la latence réelle d'un client de synchro et réajuster la fenêtre de fraîcheur
- [ ] partage décidé ami par ami plutôt que globalement
- [ ] rembourrage du clair pour masquer la taille de la bibliothèque
- [ ] rotation d'adresse assistée, qui prévienne les amis existants
- [ ] second transport (Gist, WebDAV) derrière `IPresencePublisher`

## v0.9 - Linux et Steam Deck

- [x] publication autonome `linux-x64`
- [x] pipeline AppImage de préversion avec outil épinglé et vérifié
- [x] installation versionnée de PartyBoard sur Linux depuis l’AppImage publiée
- [x] mise à jour et réparation explicites de PartyBoard sur Linux
- [x] intégration bureau/AppImage Steam Deck
- [ ] test réel en mode Jeu sur matériel Steam Deck
- [ ] artefact `linux-arm64` après validation des dépendances natives

## v1.0 - distribution de confiance

- [ ] signatures Ed25519 des manifestes de mise à jour
- [ ] rotation et révocation documentées des clés
- [ ] Windows signé par certificat de confiance
- [ ] signature et notarisation des archives macOS publiées
- [ ] promotion Linux stable après tests matériels
- [x] compilation macOS expérimentale
- [x] macOS x64 et arm64 dans le train de release
