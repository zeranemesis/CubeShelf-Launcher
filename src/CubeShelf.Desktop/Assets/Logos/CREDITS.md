# Logos des jeux

Source : [LaunchBox Games Database](https://gamesdb.launchbox-app.com/), métadonnées publiques du 4 novembre 2026 provenant de [Metadata.zip](https://gamesdb.launchbox-app.com/Metadata.zip).

Les images sont les fichiers PNG « Clear Logo » transparents fournis par la base, sans recadrage ni modification. Les logos et marques appartiennent à leurs ayants droit respectifs ; cet accès public ne constitue pas une licence libre.

`catalog.json` conserve pour chaque association la console d'origine, le titre DecompShelf, le titre de la base, son DatabaseID interne, la région, l'URL du fichier original, ses dimensions et son SHA-256. Ce DatabaseID n'est pas l'identifiant des pages web LaunchBox.

Les quatre logos intégrés antérieurement restent disponibles pour les identifiants GameCube historiques. Les entrées sans correspondance sûre gardent leur titre en texte. Aucun logo de remake n'est substitué à celui d'une autre console.

Réimport : `python eng/import-game-logos.py chemin/Metadata.zip`. Contrôle hors ligne : `python eng/import-game-logos.py --check`. L'import génère aussi `CubeShelf.Mobile/LogoAssets.props` pour embarquer uniquement les logos des jeux du catalogue Android, ainsi que `docs/missing-game-logos.json`.
