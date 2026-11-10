# Bouton RetroAchievements

Un seul bouton portant le logo couleur est placé en bas des jeux. Il ouvre une liste de ports natifs compatibles ; aucun compteur de progression permanent n'est ajouté. Sur les écrans étroits, le bouton conserve son logo, son nom accessible et une cible de 48 unités.

- Windows : bouton dans Big Picture Classique et HOME ; la liste utilise le menu Big Picture existant.
- Android : bouton dans Classique et HOME, y compris dans la grille de l'écran secondaire ; la liste utilise le panneau interne déjà navigable avec la manette et le tactile.
- Choisir une entrée ouvre le jeu dans son catalogue, sans le lancer. Fermer la liste conserve la sélection actuelle.
- Les ports non confirmés ne sont pas ajoutés parce que le jeu d'origine possède une fiche RetroAchievements. Les variantes d'un même jeu ne créent pas de doublon.

Sur Windows, la liste utilise la capacité `retroachievements-login` du manifeste du moteur associé au jeu. Un port non installé ou sans déclaration n'est donc pas annoncé compatible. Sur Android, PartyBoard intégré est reconnu ; les autres moteurs doivent déclarer cette capacité dans leur manifeste. Le binaire ARM64 PartyBoard épinglé contient le client `retroachievements.org`, les variables de session `CUBESHELF_RA_USER` / `CUBESHELF_RA_TOKEN` et le pont `achievements.json`. Ce contrôle confirme la présence de l'intégration, sans constituer un essai de déblocage en partie.

Le bouton ne connecte pas automatiquement les ports qui ne savent pas utiliser RetroAchievements. Les réglages de compte et la progression PC déjà présents sur la page Profil restent disponibles.

Tests : clic du bouton, logo disponible hors ligne, filtrage des capacités, exclusion des ports non confirmés, absence de doublons, navigation sur le même jeu sans lancement et cibles tactiles en portrait/paysage. Aucune ROM téléchargée ni partie de Mario Party 4 lancée pour cette modification.

Provenance du logo : [crédits](../src/CubeShelf.Desktop/Assets/Home/Brand/CREDITS.md).
