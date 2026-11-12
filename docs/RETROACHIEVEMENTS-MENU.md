# Bibliothèque RetroAchievements

Le bouton au logo couleur ouvre une page de jaquettes dédiée dans Big Picture sur Windows et dans DecompShelf sur Android. Sur la Thor, la grille s’affiche en bas et l’aperçu du jeu sélectionné reste en haut. La recherche et l’action du jeu sont disponibles dans cette page. B ou la flèche de retour ramène à la sélection précédente.

Sur Windows, la bibliothèque utilise la capacité `retroachievements-login` du manifeste du moteur associé au jeu. Un port non installé ou sans déclaration n'est donc pas annoncé compatible. Sur Android, PartyBoard intégré est reconnu ; les autres moteurs doivent déclarer cette capacité dans leur manifeste. Le binaire ARM64 PartyBoard épinglé contient le client `retroachievements.org`, les variables de session `CUBESHELF_RA_USER` / `CUBESHELF_RA_TOKEN` et le pont `achievements.json`. Ce contrôle confirme la présence de l'intégration, sans constituer un essai de déblocage en partie.

Le bouton ne connecte pas automatiquement les ports qui ne savent pas utiliser RetroAchievements. Les réglages de compte et la progression PC déjà présents sur la page Profil restent disponibles.

Tests : clic du bouton, logo disponible hors ligne, filtrage des capacités, exclusion des ports non confirmés, absence de doublons, navigation, retour à la sélection précédente et cibles tactiles en portrait/paysage. Aucune ROM téléchargée ni partie de Mario Party 4 lancée pour cette modification.

Provenance du logo : [crédits](../src/CubeShelf.Desktop/Assets/Home/Brand/CREDITS.md).
