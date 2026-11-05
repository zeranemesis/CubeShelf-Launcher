# Logos des jeux — 4 novembre 2026

555 associations console/titre du catalogue DecompShelf disposent d'un logo transparent provenant de LaunchBox Games Database après les ajouts du 5 novembre, soit 549 fichiers originaux distincts. L'inventaire inclut les catalogues PC, le catalogue Android et les jeux historiques. Les 17 entrées sans correspondance sûre, entrées regroupant plusieurs jeux ou fichiers non conformes sont détaillées dans [missing-game-logos.json](missing-game-logos.json) et gardent leur titre en texte.

Le rapprochement utilise la console d'origine et le titre exact normalisé, les noms alternatifs publiés par la base, puis 37 correspondances explicites revues pour les titres abrégés/régionaux. Aucun rapprochement approximatif entre consoles. Les titres N64 et leurs remakes 3DS ont des logos distincts. Les images originales restent sans modification ni recadrage ; l'affichage conserve leurs proportions.

Sur Windows, les 549 PNG sont des fichiers de contenu dans `Assets/Logos/Database` (environ 405 Mo décimaux), pour conserver un exécutable léger et permettre l'usage hors ligne. Android embarque seulement les 32 fichiers nécessaires à ses jeux, environ 28 Mo, au moyen du fichier généré `LogoAssets.props`. Le manifeste et les quatre images historiques restent partagés. L'import des illustrations reste indépendant de l'ajout de ports compatibles au catalogue.

Les vues HOME, Big Picture classique, bibliothèque mobile et animation de lancement utilisent le même contrôle. Il conserve uniquement le bitmap du titre affiché, décodé dans une limite de 640 × 260 pixels, puis le libère au changement de titre ou au détachement.

Réimport : `python eng/import-game-logos.py chemin/Metadata.zip` (Python + Pillow, outils déjà utilisés pour les assets). La base complète se télécharge depuis <https://gamesdb.launchbox-app.com/Metadata.zip> ; elle n'est pas embarquée dans l'application. Les téléchargements sont limités à quatre simultanés, PNG transparents visibles de moins de 8 Mo/32 mégapixels, écritures atomiques, SHA-256 et provenance conservés. `--check` vérifie les fichiers, la couverture de l'inventaire et les ressources Android sans réseau.

Les [crédits](../src/CubeShelf.Desktop/Assets/Logos/CREDITS.md) documentent la source et les ayants droit. Aucun commit, push ou release GitHub effectué dans cette étape.

Validation : contrôle hors ligne des 555 associations et des ressources mobiles réussi ; suites Mobile et ShelfUi réussies, dont décodage réel de tous les logos PC, distinction Zelda N64/3DS, repli texte et libération/réattachement du bitmap. Captures initiales dans `../android-embedded-engines/game-logos-ui-2026-11-04/`, complétées par les contrôles du 5 novembre.

Windows x64 autonome est publié dans `../decompshelf-logos-windows-2026-11-04/` et démarré sur le profil existant, fenêtre répondante. L'APK ARM64 `versionCode 62`, `0.19.0-rc.3`, est publié dans `../decompshelf-logos-android-2026-11-04/` (168 851 847 octets). Signature locale inchangée, 23 hôtes/59 composants privés, alignement 16 Ko et absence de stub, ROM ou APK imbriqué vérifiés. SHA-256 adjacent au fichier. Aucun essai ni installation sur téléphone pour cette révision.
