# Changelog

## 0.18.0 — DecompShelf, 26 octobre 2026

- Application renommée DecompShelf ; catalogues GameCube, Wii, Switch et Nintendo 64 dans un menu déroulant, logos adaptés et choix des ports sans doublon.
- Jaquettes aux proportions d’origine, boîtes Nintendo 64 et cartouches scannées, inspection des faces et animations de lancement propres à chaque console.
- Big Picture : navigation stick/croix configurable, menus inspirés de Recalbox, transitions de plateforme et rendu optimisé ; disque descendant et tournant au lancement.
- Android : cinq ports natifs Nintendo 64, téléchargement officiel vérifié, installation avec confirmation Android et lancement des applications installées. Pilotwings 64 reste expérimental et Pokémon Snap en bêta.
- Android versionCode 44, signature persistante compatible avec la version 0.17.0. Aucun jeu ni ROM fourni ; les jeux n’ont pas été validés sur un téléphone physique.
- Cette release réunit les modifications des aperçus 0.18.0 ci-dessous.

## 0.18.0-preview.11 — Ports natifs Android, non publiée

- Android : catalogue Nintendo 64 de cinq ports natifs (Super Mario 64, Ocarina of Time, Majora’s Mask, Pilotwings 64, Pokémon Snap), boîtes et cartouches, sélection de plateforme dans DecompShelf.
- Téléchargements officiels épinglés, reprise HTTP, SHA-256 du paquet et de l’APK, contrôle de l’identité et de la signature avant mise à jour du port, confirmation dans Android et lancement de son application installée.
- PartyBoard et les mises à jour du launcher restent disponibles. Android versionCode 43, identifiant de paquet conservé pour les mises à jour.
- Vérification des architectures, version Android, OpenGL ES et Vulkan selon les ports ; versions expérimentales signalées. Aucun jeu ni ROM fourni.
- Tests de téléchargement et d’interface avec ports simulés ; aucun téléphone connecté pour valider les jeux.

## 0.18.0-preview.10 — Cartes adaptées aux boîtes, non publiée

- La hauteur des jaquettes de la bibliothèque suit les proportions de chaque image, au lieu d’un cadre vertical de hauteur fixe.
- Boîtes Nintendo 64 horizontales ou japonaises verticales sans bandes noires, charnière ni effet de pochette plastique.
- Les boîtiers GameCube, Wii et Switch conservent leur habillage et affichent l’intégralité de leur jaquette.

## 0.18.0-preview.9 — Forme des cartons Nintendo 64, non publiée

- Impression jusqu’aux arêtes, suppression du cadre beige et des détails de boîtier plastique sur les boîtes Nintendo 64.
- Ouverture par le petit côté pour les boîtes horizontales, par le haut pour les éditions japonaises verticales ; rabat rentrant, petites pattes et support intérieur en carton.
- La cartouche sort avec le support puis descend devant la boîte au lancement Big Picture.

## 0.18.0-preview.8 — Boîtes Nintendo 64, non publiée

- Inspection Nintendo 64 par clic-glisser pour tourner la boîte et voir ses bords.
- Rendu des six faces : face, dos, deux tranches, dessus et dessous. Textures transmises à la bibliothèque, à la fiche et au lancement Big Picture.
- Scans complets pour 22 jeux ; les sept autres conservent les scans disponibles et des faces unies quand une source manque. Éditions de boîte conservées ensemble.
- Textures des côtés et du rabat dans l’animation de sortie de cartouche ; conservation des six faces dans le profil.

## 0.18.0-preview.7 — Nintendo 64, non publiée

- Catalogue Nintendo 64 dans la même application : 34 ports natifs, 29 jeux uniques, choix des versions sans doublon.
- Jaquettes et cartouches scannées ; formats carton horizontal et japonais vertical.
- Big Picture : rabat de boîte animé, cartouche sortante, logo Nintendo 64 et navigation entre quatre consoles.
- Version Windows locale ; aucun nouvel APK Android publié.

## 0.18.0-preview.6 — DecompShelf et logos par catalogue, non publiée

- L'application s'appelle désormais DecompShelf dans la fenêtre, les en-têtes, les réglages, les menus Big Picture et la zone de notification. Le paquet de travail fournit le lanceur DecompShelf.
- Le logo en haut à gauche suit GameCube, Wii ou Switch, dans la bibliothèque et en Big Picture. Le logo GameCube existant est partagé en mémoire ; les marques de catalogue Wii et Switch sont dessinées en vectoriel, sans téléchargement ni décodage au changement de console.
- Les dossiers, identifiants de profil, noms d'assembly et contrats de mise à jour CubeShelf restent stables pour conserver les réglages, sauvegardes et installations. Le lanceur historique reste disponible pour les outils de mise à jour et de restauration.

## 0.18.0-preview.5 — fluidité Big Picture et disque en rotation, non publiée

- Ruban Big Picture déplacé et redimensionné par le compositeur sans recalculer la géométrie des boîtiers à chaque changement de taille. Dessins regroupés via Skia pour les modèles GameCube et les textures Wii/Switch, avec filtrage de qualité et préparation des textures en arrière-plan.
- Le disque GameCube/Wii descend devant le boîtier et tourne sur son axe avec une accélération progressive. L'ouverture et le démarrage visuel durent environ 2,1 secondes ; B permet toujours d'annuler avant le lancement.
- Calculs de caméra et points du cercle réutilisés pendant l'animation. Une sélection inchangée conserve son inspection ; les menus évitent les mises à jour de focus redondantes et les cartes bureau cachées ne relisent plus leurs profils à chaque rafraîchissement d'état Big Picture.
- Banc de rendu hors écran reproductible : médianes de 46–52 ms à 14–22 ms selon la console sur la machine de test ; allocations par image réduites de 76–95 %. Ce sont des coûts de rendu à 1000 × 600, pas une garantie de FPS ni une mesure sur manette physique. Détails dans `docs/BIG-PICTURE-PERFORMANCE.md`.

## 0.18.0-preview.4 — navigation et lancement Big Picture, non publiée

- Uniquement en Big Picture : stick gauche horizontal pour les jeux, vertical pour changer de console ; une impulsion verticale change d'une plateforme. Croix directionnelle pour les menus. START ouvre et ferme le menu principal, A valide, B revient en arrière.
- Menu central à grandes lignes inspiré de Recalbox : jeu sélectionné, plateformes, manette, disposition, amis et accès aux paramètres avancés. L'inversion des rôles stick/croix est mémorisée dans les paramètres manette. XInput et SDL conservent la distinction des deux sources et leur zone morte.
- Ouverture du boîtier, puis extraction du mini-disque GameCube, disque Wii ou de la cartouche Switch, avant de démarrer le jeu. 47 scans de supports réels GameTDB ajoutés avec provenance et SHA-256 ; les trois jeux dématérialisés sans scan utilisent un support neutre. La préparation habituelle des ports reste nécessaire.
- Le lancement attend la fin de l'animation (environ 1,6 seconde). B, le bouton de retour, la perte de focus et l'annulation de la préparation empêchent le lancement pendant l'ouverture. Le mode classique et le lancement mobile gardent leur comportement existant.
- Tests hors écran : axes et zone morte, séparation stick/croix, navigation des consoles et jeux, inversion persistante, menus, rendu des trois supports et fin/annulation de l'ouverture. Une manette physique et des parties réelles restent à vérifier.

## 0.18.0-preview.3 — fidélité des boîtiers et transitions, non publiée

- Changement de console avec glissement directionnel et fondu de 380 ms, capture temporaire bornée à 1600 × 1200 pixels, annulation lors des changements rapides ou de la fermeture/masquage. Option de réduction des animations dans les paramètres.
- Big Picture change de bibliothèque sans quitter puis rétablir le plein écran. Suppression des reconstructions redondantes lors du changement de console.
- Miniatures partagées et limitées à 512 pixels, cache inactif borné à 24 Mio, décodage hors du verrou partagé et sans agrandir les petites sources. Les images déjà utilisées sont conservées pendant le remplacement des cartes.
- Boîtiers de cartes avec charnière, rebords et reflet discret ; matériaux Wii blancs et Switch translucides stylisés. Rendu 3D avec épaisseur adaptée, tranche imprimée uniquement sur la charnière et bord d'ouverture avec joint. Aucun faux dos remplacé par la face avant. La pose d'inspection est conservée lors des rafraîchissements sans changement de jaquette.
- Matériaux partagés, rendu direct pour les faces sans perspective et maillage simplifié pour les voisins du jeu sélectionné. Tests Skia hors écran du rendu, transitions interrompues, réduction des animations, plein écran et absence de nouveaux décodages lors d'une recherche avec images en cache.

## 0.18.0-preview.2 — versions regroupées et jaquettes, non publiée

- Une carte par jeu et par console. Les ports alternatifs se choisissent dans un menu déroulant sur la carte, la fiche ou en Big Picture ; choix mémorisé et favoris communs aux versions.
- 47 nouvelles faces GameTDB, 41 dos et tranches, avec registre de provenance. Les jeux intégrés conservent leurs jaquettes. Boîtiers Wii blancs et Switch plus étroits ; proportions conservées dans la bibliothèque et le rendu 3D.
- Tests d'interface : 21 jeux GameCube, 13 Wii, 16 Switch sans doublons de ports, sélection persistante et synchronisation Big Picture. Construction du renderer partagé mobile vérifiée.

## 0.18.0-preview.1 — suite de ports natifs, non publiée

- Une seule application conserve l'interface CubeShelf : sélecteur GameCube / Wii / Switch en haut à gauche, synchronisé en Big Picture. Même bibliothèque, cartes et favoris ; dernier choix de console conservé au redémarrage.
- GameCube : 19 projets supplémentaires ; Wii : 13 projets ; Switch : 20 projets pour 16 titres. Identifiants des jeux distincts par console, sources primaires, état du projet, architecture et empreintes des paquets. Les ports Apple Silicon ne sont pas présentés comme des paquets Windows et les projets uniquement mobiles sont écartés.
- Recherche, filtre des paquets compatibles, téléchargement annulable, contrôle taille/SHA-256, extraction bornée dans un nouveau dossier, association explicite du programme et lancement natif sans arguments inventés.
- Les ports préparent eux-mêmes les fichiers du joueur selon leurs instructions. Aucune installation n'écrase le runtime précédent ni ses sauvegardes, aucun programme téléchargé n'est exécuté automatiquement.
- Script de construction d'une application Windows/Linux/macOS, inventaire daté et tests de catalogue, sélection des consoles, provenance, intégrité, traversée de chemins, annulation et persistance. Les parties réelles restent à valider et l'exhaustivité de tous les projets existants n'est pas garantie.

## 0.17.0 — 2026-10-25

- Regroupe les évolutions depuis la version 0.16.0 : récupération du launcher, sauvegardes de profils et de sauvegardes de jeux, reprise après mise à jour et contrôles d'espace disque.
- Big Picture : démarrage plein écran, transition de lancement, logos des jeux, navigation des mods à la manette et prise en charge SDL facultative sur Linux et macOS.
- Rendu des boîtiers 3D et cache des jaquettes optimisés, animations à la demande et chemins d'images adaptés à chaque plateforme.
- Android ARM64 : moteur natif PartyBoard intégré, téléchargement vérifié de ses ressources et retour à la bibliothèque après le jeu ; disque personnel toujours nécessaire.
- Gestionnaire de téléchargements mobile avec progression et débit réels, graphique, pause/reprise, file persistante et historique illustré.
- Mise à jour de l'application Android depuis GitHub : recherche au démarrage et toutes les cinq minutes au premier plan, téléchargement du bon APK, contrôle de taille et SHA-256, puis confirmation dans l'installateur Android. Accès depuis ↻ et Téléchargements, progression, annulation et reprise après erreur. Les brouillons, préversions, APK d'une autre architecture et paquets sans empreinte sont refusés.
- Version Android 42. Signature persistante commune aux architectures pour permettre les mises à jour suivantes sans désinstallation ; contrôle du nom de paquet, du code de version et du certificat avant installation. Les APK restent des aperçus signés avec une clé de développement ; les paquets PC ne disposent pas de signature de distribution ni de notarisation macOS.
- Les notes des aperçus ci-dessous détaillent les essais effectués. Aucune garantie de 120 images/s constantes ni de compatibilité réseau Android–PC ; les parties complètes et les manettes physiques restent à valider.

## 0.17.0-preview.19 — gestionnaire mobile inspiré du PC, non publié

- Écran Téléchargements avec jaquette du jeu, carte du transfert actif, progression, débit réseau, pic de session et estimation du temps restant. Graphique des relevés réels sur une fenêtre de 60 secondes ; aucune activité réseau ou disque simulée.
- File séquentielle indépendante de l'écran, pause/reprise globale, reprise et annulation par transfert, priorités des éléments en attente et mémorisation de leur ordre. Les travaux interrompus restent en pause jusqu'à une reprise explicite.
- Historique persistant des 32 derniers téléchargements terminés, avec jaquettes, version, taille et date. Migration d'une installation existante à partir de la date réelle de son marqueur ; effacer l'historique ne retire pas les fichiers du jeu.
- Parcours haut/bas/A/B dans les commandes du panneau, focus visible et défilement vers l'action ciblée. Commandes tactiles d'au moins 48 DIP, sans flèches ajoutées à l'écran.
- Relevés bornés à 240 points, sans timer d'animation au repos. Décodage des miniatures limité à 256 pixels de large, cache propre au panneau et libération à sa fermeture. Les lignes de la file et de l'historique ne sont reconstruites que lors d'un changement structurel.
- PartyBoard reste le seul moteur Android enregistré. Le gestionnaire ne propose pas le téléchargement des moteurs PC incompatibles ; les priorités multiples prendront effet lorsque d'autres moteurs Android seront enregistrés.
- Compilation Mobile Release sans erreur ni avertissement, publication Android ARM64 réussie et APK code 40 installé sur le S20. Affichage de la jaquette, des sections et de l'historique existant observé ; aucun nouveau transfert ni scénario à plusieurs moteurs exécuté dans cette passe. Aucun test automatisé ajouté ou exécuté.

## 0.17.0-preview.18 — PartyBoard intégré et téléchargements Android, non publié

- Accès Téléchargements depuis la bibliothèque Big Picture : progression réelle, octets reçus, vitesse, pause, reprise, annulation, erreurs et préparation des ressources. Parcours tactile et commandes haut/bas/A/B ; notification de transfert ouvrant ce panneau.
- Moteur ARM64 PartyBoard v0.20.0 embarqué dans l'APK CubeShelf. Sources Java/SDL adaptées et épinglées ; compilation Java 8 et extraction des bibliothèques natives intégrées à la publication Android. Aucun APK PartyBoard séparé à installer.
- Téléchargement officiel épinglé à 52 716 096 octets avec vérification SHA256 avant extraction des seules ressources. Fichier partiel conservé après interruption, reprise HTTP Range et préparation dans le stockage privé ; les sauvegardes, réglages et disques ne sont pas supprimés.
- Activités natives internes dans des processus privés ; redémarrages du jeu limités à son processus. Désactivation de l'ancien mécanisme de mise à jour de l'application PartyBoard séparée. Le disque personnel reste nécessaire, aucun disque fourni.
- Correction de l'écran noir Vulkan au retour du moteur après changement d'orientation : Android recrée la vue native, tandis que la durée de vie Avalonia conserve la bibliothèque. Consommation unique de l'action de notification, pour ne pas rouvrir le panneau après rotation. Progression complète restaurée pour une installation déjà prête.
- Sur Galaxy S20 Android 13 : téléchargement réel, vérification, préparation, ouverture de l'écran natif de sélection du disque et retour visible aux jaquettes 3D observés. Deuxième lancement depuis le paysage et retour également observés, dans le même processus principal CubeShelf. Aucune application PartyBoard séparée présente.
- Partie complète, session en ligne, pause/reprise réseau et manette physique non validées dans cette passe. Aucun test automatisé ajouté ou exécuté. Documentation dans `docs/ANDROID-PARTYBOARD.md` ; aucune garantie nouvelle de 120 images/s dans le jeu.

## 0.17.0-preview.17 — première évolution de l’interface mobile, non publié

- Action principale Jouer/Installer fixe, hors de la zone défilante. En-tête plus compact, logo moins haut et état explicite Prêt/À installer/Port indisponible ; aucun lancement proposé pour un moteur Android absent.
- Boutons Retourner et Informations retirés à la demande de l’utilisateur. Toucher court pour retourner ; maintien de 550 ms pour examiner une jaquette 3D agrandie, annulé dès qu’un déplacement est détecté. Fermeture explicite ou avec B ; R3 ouvre aussi l’agrandissement à la manette et X retourne le boîtier.
- Boutons redessinés : dégradé violet pour l’action principale, actions secondaires sombres avec contour, angles arrondis, états pressé/survol et focus blanc de taille constante. Pictogrammes et zones tactiles d’au moins 48 DIP.
- Aides tactiles ou de manette selon la dernière entrée, parcours de commandes ordonné. B ferme d’abord l’agrandissement ou l’invitation privée, puis revient à l’action principale. L’étagère est masquée pendant l’agrandissement, sans flou plein écran ni animation permanente ; les ressources de la vue agrandie sont libérées à sa fermeture et en arrière-plan.
- Invitation privée regroupée dans un panneau repliable, disponible seulement quand PartyBoard est installé. Boîtiers, textures, moteur de rendu et lancement natif conservés.
- APK Android arm64 code 34 installé sur le Galaxy S20. Appui long, retournement agrandi et retour Android contrôlés à l’écran ; R3/X/B exercés par événements Android injectés, sans validation sur une manette physique. Compilation Mobile et Android sans erreur ni avertissement, publication réussie. Aucun test automatisé ajouté ou exécuté.
- Relevé court après utilisation du zoom : 667 intervalles, 113,21 images/s de moyenne, P99 16,73 ms ; 31 des 39 intervalles supérieurs à 12 ms pendant le premier défilement. Série trop courte pour établir une évolution de la cadence par rapport à preview.16. Présentations inchangées pendant deux secondes au repos ; 120 images/s constantes non validées.

## 0.17.0-preview.16 — coût du rendu et diagnostic de cadence, non publié

- Dessins GPU d’un même boîtier regroupés dans une seule opération native et un seul emprunt du contexte Skia, avec conservation de l’ordre des couches et des replis géométriques. Les sommets et paints restent propres à chaque opération de scène ; les ressources non soumises sont libérées en cas d’erreur. Modèle, maillages et résolution conservés.
- Instrumentation Android facultative : durée CPU de construction des dessins, allocations de ce parcours, durée CPU des appels natifs et retard des callbacks Choreographer. Désactivée au lancement normal ; elle ne mesure pas la durée d’exécution GPU.
- Sur deux séries instrumentées de 16 gestes, le regroupement divise approximativement par quatre le nombre d’opérations natives et réduit leur durée CPU cumulée par callback actif d’environ 0,28 à 0,20 ms après le premier bloc. Aucun gain d’images/s établi par cette comparaison.
- Suppression expérimentale des passages de disposition retirée faute de gain mesuré. Comparaison OpenGL ES/Vulkan sur le S20 : environ 82,3 contre 117–118 images/s dans ces séries ; Vulkan reste prioritaire. Ces relevés séquentiels ne constituent pas une comparaison contrôlée de tous les appareils.
- Publication Android arm64 code 31 installée sur le Galaxy S20. Série finale sans instrumentation : 16 défilements et 16 retournements, 2 796 intervalles sur 52,1 secondes, moyenne 8,48 ms (117,90 images/s), P99 16,59 ms, maximum retenu 33,29 ms. 51 intervalles supérieurs à 12 ms (1,82 %). La cadence reste comparable à preview.15 ; 120 images/s constantes non atteintes.
- Présentations inchangées pendant deux secondes au repos ; statut thermique Android 0. Compilation Desktop Release sans erreur ni avertissement. Aucun test automatisé ajouté ou exécuté.
- Proposition de hiérarchie Big Picture dans `docs/BIG-PICTURE-UX-NEXT.md` : action principale persistante, fiche plus courte, aides contextuelles tactile/manette et états propres aux moteurs compatibles. Ce document prépare la prochaine interface, sans annoncer ces changements comme déjà intégrés.

## 0.17.0-preview.15 — cadence 120 Hz, non publié

- Mode 120 Hz effectivement observé sur le Galaxy S20 après son activation dans les réglages Samsung : mode 3, période de présentation 8,33 ms. L’horloge native et le plafond de 120 mises à jour/s suivent déjà cette fréquence, sans changer la durée des animations.
- Coordonnées UV du maillage conservées tant que les dimensions de l’image ne changent pas ; interpolation des sommets calculée par ligne au lieu de recalculer les quatre coins à chaque sommet. Maillage et résolution conservés.
- Calcul du reflet spéculaire à puissance entière remplacé par des multiplications équivalentes. Calcul des limites géométriques en une passe, sans tableau ni séries de parcours LINQ.
- Effets de survol réservés aux pointeurs souris/stylet ; un geste tactile ne déclenche plus ces effets supplémentaires. Navigation tactile, retournement et animations de sélection conservés.
- Compilation Desktop Release sans avertissement ni erreur ; publication Android arm64 réussie. APK Android code 27 installé sur le S20. Mesures de présentation à 120 Hz conservées dans les fichiers `outputs/cubeshelf-s20-preview15-120hz-*` du workspace.
- Série finale de 36 défilements et 36 retournements sur environ deux minutes : 6 304 intervalles retenus, moyenne 8,46 ms (118,15 images/s), P95 8,55 ms, P99 16,60 ms et maximum retenu 33,52 ms. Pauses supérieures à 100 ms exclues. Cible de 120 images/s effective avec des retards ponctuels ; aucun verrouillage permanent à 120 images/s revendiqué.
- Statut thermique Android 0 pendant les relevés, température de surface finale rapportée de 30,9 °C. Présentations inchangées pendant deux secondes au repos ; aucune erreur AndroidRuntime observée dans le processus. Aucun test automatisé ajouté ou exécuté.

## 0.17.0-preview.14 — navigation et consommation de ressources, non publié

- Sauvegarde de la sélection mobile regroupée après 900 ms sans nouvelle sélection, uniquement si l’état diffère du dernier état enregistré ; écriture atomique du dernier choix à la mise en arrière-plan ou au détachement de la vue. La navigation rapide n’écrit plus chaque état intermédiaire.
- Jaquettes voisines dont la pose ne change pas conservées sans relancer leur animation. Réutilisation des ensembles de visibilité et des listes de retrait ; boucles de disposition et recherche tactile sans itérateurs ni tri temporaires.
- Shader natif partagé par image du cache, avec un paint propre à chaque opération de la scène. Libération du cache compatible avec les références natives des opérations de dessin en attente.
- Contours GPU des faces créés seulement lorsqu’un reflet le nécessite ; pinceau des ombres réutilisé et courbes du fond de l’étagère conservées jusqu’au redimensionnement.
- Nettoyage du cache sans tableaux ni tri LINQ intermédiaires, budget du cache inchangé. Qualité des images et modèle 3D conservés.
- Compilation Desktop Release et publication Android arm64 réussies, sans avertissement de compilation ; APK Android code 25 installé sur le S20. Relevés prolongés dans `outputs/cubeshelf-s20-preview14-session*.txt` du workspace. Aucun test automatisé ajouté ou exécuté.
- Session de 72 cycles (144 balayages et 144 retournements, environ 4 min 16 s de séquences) : 6 729 intervalles retenus, moyenne 16,76 ms (59,67 images/s), P99 17,19 ms, maximum retenu 33,57 ms. Pauses supérieures à 100 ms exclues ; ces résultats concernent le S20 à 60 Hz et ne garantissent pas l’absence de tout à-coup.
- Trois derniers relevés de mémoire PSS entre 260,5 et 260,8 Mio ; statut thermique Android 0, température de surface finale rapportée de 27,8 °C. Aucune nouvelle présentation pendant deux secondes au repos ; passage en arrière-plan et reprise observés sans erreur AndroidRuntime. Téléphone en charge : autonomie non mesurée.

## 0.17.0-preview.13 — préparation des jaquettes et allocations, non publié

- Images du dos décodées et redimensionnées en arrière-plan avant leur premier retournement, avec une seule préparation simultanée. Publication des images préparées uniquement tant que leur entrée est utilisée ; annulation à l’éviction et en arrière-plan via la libération du cache.
- Décodage synchrone de secours effectué hors du verrou global du cache ; les opérations de dessin conservent leur propre référence native à la texture.
- Commandes et tableaux de points du modèle réutilisés ; tri sur place avec ordre stable explicite à profondeur égale, limites de l’ombre calculées sans liste temporaire. Tableaux de sommets/couleurs GPU conservés lorsque leur taille est inchangée et indices des textures construits une seule fois.
- Un délégué d’animation par démarrage, au lieu d’une nouvelle fermeture à chaque image ; protection conservée contre les callbacks des animations arrêtées.
- Qualité des images, maillages, modèle GLB et résolution inchangés. Compilation Desktop Release sans avertissement ni erreur ; APK Android code 24 installé et ouvert sur le Galaxy S20.
- Relevés courts de la surface à 60 Hz : premier retournement à 17,33 ms en moyenne sur 46 intervalles (maximum 33,28 ms) ; défilement à 16,99 ms sur 44 intervalles (maximum 33,24 ms). Ces échantillons ne mesurent pas les octets alloués, l’autonomie ou les longues sessions et ne garantissent pas l’absence de tout à-coup.

## 0.17.0-preview.12 — optimisation Android, non publié

- Rendu des boîtiers mobiles avec des maillages Skia envoyés au GPU : une opération par face texturée et par couche du boîtier, au lieu des découpes d’image et dessins de triangles séparés. Modèle GLB, résolution des textures, faces et tranches conservés.
- Déplacement et zoom mobiles par transformation graphique avec taille de géométrie fixe ; projections et trigonométrie communes à chaque pose, tableaux de sommets réutilisés.
- Signal natif Android Choreographer partagé entre les animations actives ; arrêt à l’immobilité et en arrière-plan. Tolérance de 0,25 ms pour éviter de sauter une image à cause des arrondis temporels au plafond de 120 mises à jour/s.
- Vulkan prioritaire avec replis EGL et logiciel ; demande de 120 Hz limitée à la fenêtre, sans modifier résolution ni réglages système.
- Compilation AOT des versions Android Release ; cache des images GPU comptabilisé dans la limite mémoire et libéré en arrière-plan. Les opérations de dessin conservent leur texture pendant les traitements en cours.
- Compilation Windows et Android ; mesures de présentation effectuées sur Galaxy S20 à 60 Hz. Le plafond de 120 mises à jour/s n’est pas une garantie de 120 images/s présentées.
- APK Android code 23 installé sur le S20 : défilement à 17,03 ms en moyenne sur 40 intervalles (environ 59 images/s) ; retour et retournement à 17,39 ms sur 85 intervalles (environ 57,5 images/s), avec un maximum ponctuel de 66,66 ms. Mesures courtes de la surface de CubeShelf, pauses supérieures à 100 ms exclues ; aucune mesure à 120 Hz. Présentation inchangée pendant deux secondes au repos, faces avant/arrière et sélection tactile contrôlées.

## 0.17.0-preview.11 — démarrage Android, non publié

- Correction du crash observé sur Samsung Galaxy S20 sous Android 13 : le thème hérite désormais de Theme.AppCompat, requis par l’activité Avalonia Android.
- Bibliothèque mobile bornée à la hauteur réellement disponible : étagère et fiche défilante restent dans l’écran en portrait et paysage ; logo adapté à la place disponible.
- Plein écran déclaré aussi auprès du gestionnaire d’insets Avalonia, fond de fenêtre Android sombre et dimensions du shell dans le journal `CubeShelf.Layout`.
- Capture tactile conservée pendant le transfert entre une jaquette et l’étagère, pour ne pas annuler le balayage.
- Code Android 16 pour la mise à jour de l’installation de diagnostic.
- Installation et lancement contrôlés sur Galaxy S20 (SM-G985F), Android 13 : rendu des jaquettes, vues portrait/paysage et balayage de sélection aller/retour. PartyBoard absent du téléphone ; aucun lancement de jeu ni essai de manette physique validé dans cette passe.

## 0.17.0-preview.10 — parcours des mods à la manette, non publié

- Navigation spatiale dans les cartes de mods avec défilement automatique et contour de sélection visible. A ouvre la fiche ; B revient d’abord à la grille, puis au jeu.
- LB/RB : onglets Mes mods et GameBanana ; X : activer/désactiver ou installer selon la sélection ; Y : recherche avec clavier à l’écran, lancée à la validation.
- Bandeau de commandes contextuelles, états vides explicites, sélection mémorisée par jeu et onglet pendant la session et retours visuels sur les actions.
- Confirmation avant suppression, avec Annuler ciblé par défaut. Navigation conservée pour les profils, filtres et priorités.
- Compilation Windows effectuée ; navigation physique à la manette à confirmer sur appareil.

## 0.17.0-preview.9 — gestion complète des mods en Big Picture, non publié

- Bouton Home retiré de la collection Big Picture. Amis et Paramètres restent accessibles directement dans sa barre supérieure.
- Le bouton des mods ouvre la gestion complète en plein écran avec deux onglets : Mes mods et GameBanana, sans revenir au bureau.
- Mes mods : filtre local, activation/désactivation, suppression, priorité, profils et conflits ; GameBanana : recherche, fiches et installation dans la file existante. Le catalogue n’est pas chargé en ouvrant seulement Mes mods.
- Onglets et bouton de recherche accessibles à la manette avec le clavier à l’écran existant ; retour au jeu par B, Échap ou bouton.
- Modifications de sélection, suppression et priorité bloquées pendant une partie ou sa préparation ; les réponses d’un catalogue chargé pour un autre jeu ne remplacent plus la liste courante.

## 0.17.0-preview.8 — choix du mode au démarrage, non publié

- Paramètres PC : choix mémorisé entre démarrage classique et démarrage directement en Big Picture plein écran, accessible depuis les deux modes. Aucun verrouillage : F11 reste disponible.
- Le choix s’applique au prochain démarrage ; `--big-picture` force ce mode pour une ouverture et `--desktop` force le bureau sans modifier la préférence. Les anciens profils conservent le démarrage classique par défaut.
- Android conserve son interface Big Picture unique.

## 0.17.0-preview.7 — transparence du logo, non publié

- Fond blanc retiré du logo Mario Party 4, remplacé par un PNG avec transparence.
- Décodage des logos à une largeur de 640 pixels pour borner leur mémoire d’affichage.

## 0.17.0-preview.6 — logos des jeux en Big Picture, non publié

- Logos fournis pour Sunshine, Mario Party 4, Soulcalibur II et Mario Smash Football à la place du titre simple dans Big Picture et la transition de lancement, sur PC et Android.
- Images embarquées et partagées entre les vues, proportions conservées et nom du jeu exposé aux lecteurs d’écran. Texte conservé lorsqu’aucun logo n’est disponible, notamment pour Eclipse.

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
