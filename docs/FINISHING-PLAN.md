# DecompShelf — plan de finition

État examiné le 29 octobre 2026. Passe de finition en cours sur **0.19.0-rc.1**, Android code **50**. Bip est exclu de cette passe. Les cases non cochées ne sont pas des validations déjà réalisées.

Les six suites locales cœur, archives, ports natifs, interface, mobile et mises à jour passent. Interface/mobile repassées après les dernières corrections. Publication Windows x64 réussie ; campagne GitHub et validation des paquets encore à consigner. La version reste candidate : téléphone, manette physique et parcours de jeu réels non qualifiés.

## Ce que « fini » doit signifier

Un nouvel utilisateur installe une version clairement identifiée, choisit sa console, installe un port compatible, fournit ses propres fichiers, joue, retrouve sa sauvegarde et met le launcher à jour sans perdre son profil. Il peut effectuer ce parcours au clavier/souris, à la manette en Big Picture, ou au tactile sur Android, selon les fonctions annoncées pour chaque appareil.

Le launcher peut être stable avec des ports tiers expérimentaux, à condition que leur état et leur compatibilité soient explicites. La présence dans un catalogue ne doit jamais être présentée comme une preuve de jouabilité.

## 1. Figer et consolider la version à terminer — priorité bloquante

- [x] Conserver les choix acceptés : accueil par console à chaque lancement ; photos dans les cases du bureau ; menu gauche accessible ; carrousel 3D en Big Picture ; retour aux consoles par bouton, B et Échap ; ports natifs uniquement ; catalogue Android distinct.
- [ ] Relire et organiser les modifications encore locales, séparer les sources et ressources utiles des fichiers temporaires et outils personnels avant les commits.
- [ ] Établir une version de référence unique à tester. Éviter que plusieurs dossiers de builds portant le même numéro soient confondus avec la dernière application.
- [x] Conserver les identités techniques et emplacements du profil existant pendant le renommage visuel en DecompShelf.

Terminé quand les sources de la version candidate sont identifiables et que son contenu est fixé.

## 2. Valider le parcours de jeu et la fiabilité du catalogue — priorité bloquante

- [ ] Pour les jeux présentés comme prêts : vérifier paquet disponible, système, architecture, prérequis graphiques, révision des données attendues et méthode de préparation.
- [ ] Afficher des états compréhensibles : prêt, à installer, fichiers du jeu manquants, incompatible, expérimental, compilation nécessaire.
- [x] Sur Windows, ne pas proposer l'installation d'un paquet réservé à Linux ou macOS ; conserver l'information sur les autres versions sans confusion.
- [ ] Valider sur un ensemble représentatif de ports les parcours : installation neuve, choix des fichiers personnels, lancement, manette, sauvegarde, fermeture et second lancement.
- [ ] Vérifier pause/reprise des téléchargements, annulation, coupure réseau, espace disque insuffisant, archive invalide, mise à jour du port et préservation des données.
- [ ] Consigner pour chaque port réellement essayé la version, l'appareil et le résultat. Les autres restent signalés comme non testés.
- [x] Conserver l'absence de doublons et la distinction entre variantes d'un jeu. Melee-3D reste exclu conformément à la décision de l'utilisateur.

Terminé quand les états affichés correspondent aux capacités vérifiées et que les jeux de référence passent le parcours complet. L'exhaustivité mondiale de tous les ports n'est pas un critère de sortie.

## 3. Terminer l'interface et mesurer sa fluidité — priorité haute

- [ ] Faire une passe manuelle sur bureau et Big Picture : démarrage, consoles, jeux, retours, menus latéraux, paramètres et téléchargements.
- [ ] Vérifier chaque commande à la manette physique, y compris l'inversion stick/croix, les menus et l'annulation d'un lancement.
- [ ] Vérifier français/anglais sans mélange, libellés exacts des raccourcis, contraste, focus visible et textes longs.
- [ ] Contrôler petites fenêtres, 1080p, écran haute définition et mise à l'échelle Windows ; garantir l'accès aux menus et actions même lorsque la hauteur manque.
- [ ] Compléter les jaquettes manquantes à partir d'une source exacte ; uniformiser le cadrage des photos et logos tout en conservant leurs proportions et crédits.
- [ ] Affiner les modèles 3D simplifiés de Big Picture après validation du parcours principal.
- [ ] Mesurer le chargement à froid, les changements rapides de console, le défilement, la mémoire et l'activité au repos. Vérifier la présentation réelle à l'écran, pas seulement le rendu hors écran.

Terminé quand les parcours ne se bloquent pas et que les mesures sont reproductibles sur les appareils de référence. Aucun engagement universel de 60/120 FPS sans mesure correspondante.

## 4. Valider Android sur un téléphone réel — priorité bloquante pour une version Android annoncée stable

- [ ] Reprendre les essais physiques seulement lorsque l'utilisateur annonce son téléphone disponible.
- [ ] Tester installation neuve et mise à jour conservant profil, préférences, données et sauvegardes.
- [ ] Tester portrait/paysage, zones système, tactile, manette, passage en arrière-plan et retour du jeu.
- [ ] Valider les ports intégrés et les nouveaux APK externes : téléchargement, vérification, autorisations, installation, lancement et retour à DecompShelf.
- [ ] Vérifier les refus de compatibilité Android/ARM64/Vulkan sur les appareils concernés. Un APK vérifié ne suffit pas à qualifier une partie comme fonctionnelle.
- [ ] Garder les confirmations système nécessaires et expliquer clairement quand un port s'installe comme application séparée.

Terminé quand la version ARM64 signée destinée à la distribution passe ces essais, avec une liste claire des appareils et limites validés.

## 5. Bip — hors périmètre à la demande de l’utilisateur

- [ ] Sur PC, essayer l'analyse au-dessus de vrais jeux : menu, exploration, énigme et combat ; comparer plusieurs demandes Indice/Solution avec des guides pertinents.
- [ ] Vérifier la concision, le niveau de spoiler, les précisions ajoutées et l'absence d'affirmations inventées lorsque l'image ou les guides ne suffisent pas.
- [ ] Vérifier le téléchargement du modèle, le temps de réponse, la mémoire, l'annulation et la libération effective des ressources.
- [ ] Tester déplacement, clic, clic droit, masquage, changement de fenêtre et retour répété à la réponse. Documenter le cas du plein écran exclusif.
- [ ] Sur Android, terminer le raccordement de la recherche web et du masquage hors jeu ; valider une réponse visuelle complète sur le téléphone.
- [ ] Vérifier que les captures restent locales, que les requêtes textuelles web sont expliquées et que les autorisations nécessaires sont gérées correctement.
- [ ] Tester voix et audio réels si ces fonctions sont proposées dans la version candidate.

Terminé quand les fonctions annoncées sont démontrées sur leur appareil cible. Sinon Bip doit rester explicitement en préversion ; il ne doit pas être annoncé comme finalisé sur Android.

## 6. Préparer une distribution maintenable — priorité bloquante avant publication

- [ ] Mettre README, aide de démarrage, captures, inventaire et documentation Android à jour : certains passages décrivent encore CubeShelf limité à GameCube ou d'anciennes versions.
- [ ] Exécuter les suites pertinentes sur la même révision : cœur, installation/archives, ports natifs, interface, assistance, mobile et mises à jour.
- [ ] Vérifier les builds GitHub Actions depuis un checkout propre et la présence des photos, logos, jaquettes, licences et composants natifs dans les paquets.
- [ ] Tester l'installateur Windows et sa mise à jour, ainsi que l'APK Android avec une signature persistante compatible. Ne pas assimiler un APK local de débogage à une distribution stable.
- [ ] Vérifier la configuration de signature réellement disponible avant d'annoncer une signature Windows ou Android de distribution.
- [x] Choisir les nouveaux numéros de version et incrémenter le code Android : VERSION 0.19.0-rc.1 et ApplicationVersion 50.
- [ ] Vérifier manifestes et empreintes publiés, détection de la nouvelle version, remplacement de l'ancienne installation et conservation du profil.
- [ ] Tester une dernière version candidate sans nouvelle fonction ajoutée, corriger les problèmes bloquants, puis préparer commit, notes de version et release.

Terminé quand une installation et une mise à jour à partir des vrais paquets distribuables passent les essais de bout en bout.

## Ordre de travail conseillé

1. Consolider la version candidate et fiabiliser un petit ensemble de jeux de référence.
2. Terminer les parcours et contrôles manuels Windows/Big Picture.
3. Effectuer la validation Android lorsque le téléphone est disponible, et terminer Bip selon les capacités annoncées.
4. Achever les ressources visuelles, la documentation et les paquets.
5. Faire une dernière campagne sur la version candidate, puis publier.

L'ajout de nouvelles plateformes et de nouveaux jeux peut attendre cette première version stabilisée. Ce plan repose sur l'examen des sources et les validations déjà consignées ; aucune nouvelle campagne de jeu réelle n'a été effectuée pour le rédiger.
