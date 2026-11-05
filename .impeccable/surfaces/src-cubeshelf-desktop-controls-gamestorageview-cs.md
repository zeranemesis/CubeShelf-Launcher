---
version: 1
slug: "src-cubeshelf-desktop-controls-gamestorageview-cs"
primary_target: "src/CubeShelf.Desktop/Controls/GameStorageView.cs"
related_targets: ["src/CubeShelf.Desktop/Controls/GameStorageView.Inventory.cs","src/CubeShelf.Mobile/MobileLibraryView.Storage.cs"]
---

# Stockage et sauvegardes

Mode : Operate. Extension des réglages PC et des téléchargements Android, dans le langage visuel déjà présent. Aucune nouvelle identité.

## Direction contract

THESIS : partir d'un jeu pour comprendre son espace occupé, protéger sa progression puis effectuer une seule opération explicite.

OWN-WORLD : cartes de réglages DecompShelf, rayon 18, boutons arrondis de rayon 12 et action principale violette. Sur PC, reprendre les ressources Bg, Panel, Panel2, Border, Text, Muted et Accent d'App.axaml ; sur Android, reprendre le fond #0B101B, les cartes #151D2D et les textes clairs des téléchargements. Actions de 48 pixels minimum et séparation de 8 pixels entre les actions. Aucun nouveau pictogramme ni effet décoratif.

STORY : choisir un jeu, lire les tailles, voir le dossier de sauvegardes, créer ou restaurer une copie, nettoyer uniquement les fichiers annoncés.

FIRST VIEWPORT : liste des installations connues qui occupent de l'espace, classées du plus volumineux au plus léger. Chaque ligne montre la jaquette du catalogue sans recadrage, le jeu, son total et le détail des catégories présentes. Un clic ouvre le détail avec le sélecteur de jeu, les tailles et les opérations. Retour ramène à la liste, puis ferme le gestionnaire. Les sauvegardes suivent les tailles sur téléphone et occupent la colonne voisine sur PC. La confirmation demande « Conserver la sauvegarde ? », avec Oui sélectionné par défaut. Non retire la progression du dossier connu ; les copies de sécurité restent disponibles.

FORM : extension d'un panneau opérationnel existant ; le tirage de concepts ne s'applique pas à cette extension du système déjà présent (new-work, section 3).

FINISH : unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance.

Contraintes : aucun cloud ; export/import local uniquement. Ne jamais promettre la compatibilité des formats entre moteurs. Un moteur actif bloque les modifications. Une restauration crée sa copie de sécurité avant remplacement. Les dossiers associés manuellement et les originaux des jeux ne sont pas supprimés par le nettoyage.

## Revue finale

Le reviewer Impeccable a vérifié les rendus du widget en 320, 390 et 900, la variante claire PC et la confirmation compacte. Les cibles ont été portées à 48, avec 8 d'espacement ; la passe de verdict les juge résolues et retourne `ship` à cette portée. Il ne s'agit pas d'une validation sur téléphone. L'absence préalable de PRODUCT.md/DESIGN.md est conservée : cette extension ne remplace pas le système visuel existant. Aucun raster de produit n'a été ajouté.

Harmonisation suivante demandée par l'utilisateur : « fais en sorte que ça s'accorde avec CubeShelf ». Périmètre : ce panneau partagé seulement ; conserver les opérations et protections de sauvegarde. Les rendus sont des captures headless avec données synthétiques, distinctes d'une validation sur téléphone réel.

Revue de cette harmonisation : `ship`. Le reviewer compare les captures actuelles et le widget aux styles des réglages PC et téléchargements Android ; aucune correction matérielle demandée. Portée : cartes, couleurs, typographie, retour fixe, repli en une colonne et hiérarchie des actions. Les tests vérifient aussi le changement de disposition, le retour visible après défilement et l'ouverture du panneau par un clic envoyé à travers la fenêtre. Le détecteur retourne `[]` ; il ne remplace pas cette inspection des rendus Avalonia. Téléphone réel et rendu du système Android non validés.

Extension demandée ensuite : détail des jeux qui prennent de la place et possibilité de faire du vide depuis CubeShelf. Ajout d'une liste de tailles réelles, d'un total par jeu sans double comptage des fichiers imbriqués et d'une suppression des installations explicitement gérées. Avant suppression, conserver les sauvegardes connues et créer une copie ; si le dossier de progression est inconnu, exiger sa sélection ou une reconnaissance explicite par l'utilisateur qu'aucune progression non détectée n'est à conserver. Aucun fichier personnel n'a été supprimé pour tester cette extension.

Revue de l'extension inventaire : `ship` sur les captures 320/390/900, le détail PC et la confirmation mobile. Le reviewer ne demande aucune correction matérielle ; il valide à cette portée le tri visible, les totaux alignés, la navigation à deux niveaux et la reconnaissance obligatoire pour un emplacement de progression inconnu. Les tests couvrent aussi le clic réel à travers la fenêtre headless et l'activation de la case à la manette. Le rendu OS et le téléphone réel restent non vérifiés.

Demande suivante : poser explicitement le choix de conserver la sauvegarde, et montrer les jaquettes pendant le choix du jeu à supprimer. Réutiliser les images et le cache existants du catalogue PC/mobile, avec proportions conservées et un état « Sans jaquette » si l'image est absente. Aucun nouveau raster n'est ajouté au produit. Le choix de suppression de progression nomme son dossier et conserve une copie préalable ; les emplacements partagés avec un autre jeu sont refusés avant toute suppression. Captures avec tailles et sauvegardes synthétiques, images authentiques déjà fournies par le catalogue.

Revue de ce choix et des jaquettes : `ship`. Les captures 320/390/900 et les confirmations Oui/Non ont été inspectées : proportions horizontales et verticales conservées, titres et tailles lisibles, Oui par défaut, conséquence de Non avec chemin du dossier et copie restaurable. Les cibles de 48 pixels, les actions de confirmation/annulation et la reconnaissance d'une progression inconnue restent visibles. Aucun correctif matériel demandé. Portée : widget partagé en rendu headless et données synthétiques ; téléphone réel, rendu système Android et suppression de progression personnelle non testés.

Contrôle ADB ultérieur demandé par l'utilisateur : affichage réel sur Galaxy S25+, liste avec jaquettes et tailles, détail de Mario Party 4, Oui par défaut, sélection tactile de Non et annulation. Le retour à la liste a permis d'identifier puis de corriger le template appelé avec une entrée nulle pendant son recyclage ; retour et actualisation revérifiés sur téléphone après reconstruction. Ces contrôles ne valident pas une suppression de progression personnelle ni le parcours du sélecteur système d'export/import.

Dernier APK reconstruit : contrôle visuel repris après déverrouillage, même parcours tactile Oui/Non → Annuler → Retour → Actualiser, sans erreur affichée. La liste conserve les trois jaquettes et les tailles initiales ; Mario Party 4 conserve ses 16 Mo de sauvegardes. Le téléphone est laissé sur cet inventaire. Cette passe complète les preuves matérielles, sans étendre le verdict du reviewer headless à la suppression effective de données personnelles.
