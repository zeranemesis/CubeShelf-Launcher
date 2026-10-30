# Catalogue Android ARM64 — contrôle du 30 octobre 2026

14 nouveaux ports, soit 23 entrées de ports Android et PartyBoard intégré, sur 8 plateformes. Les variantes d’un même jeu restent dans son port : pas de deuxième carte pour DX, les disques Wii acceptés par Dusklight/Metroid Prime ou les mods de Mario Kart Wii.

Chaque APK ci-dessous a été téléchargé depuis la release du projet, comparé à sa taille et à son digest GitHub lorsqu’il existe, sa signature vérifiée avec apksigner, puis analysé avec aapt2. Toutes ses bibliothèques du dossier `lib/arm64-v8a` ont un en-tête ELF64 AArch64 (machine 183). Le catalogue épingle URL, version, paquet, taille et SHA-256 ; le téléchargeur existant les vérifie avant l’installation. Les SDK inférieurs à 24 sont relevés à 24 dans le catalogue pour respecter le format du catalogue ; DecompShelf lui-même nécessite Android 8 (API 26). Aucun APK, ROM ou BIOS n’est stocké dans le dépôt.

Cela confirme l’architecture du binaire et son identité, pas sa jouabilité. Les prérequis et limites viennent des README et notes de release des projets liés. Les jeux restent indiqués « partie non testée ». Le téléphone de l’utilisateur n’a pas été sollicité.

| Jeu | Source primaire et version | Paquet / code | minSdk APK | SHA-256 APK |
|---|---|---|---|---|
| Mario Kart 64 | [SpaghettiKart 2.0.0](https://github.com/izzy2lost/SpaghettiKart/releases/tag/2.0.0) | com.izzy.kart / 7 | 24 | `530beca60c7d55a901e621ddfcbaf8ba32d87b88a718b6d7793fb9ee03f9d4e2` |
| Star Fox 64 | [Starship v2.0.0](https://github.com/izzy2lost/Starship/releases/tag/v2.0.0) | com.starship.android / 8 | 24 | `092818cc7f3a6bef4207f1403db601b6ebd875a6e775b0f6a657df364fc985df` |
| Perfect Dark | [Perfect Dark Android 2.0.0](https://github.com/izzy2lost/perfect_dark/releases/tag/2.0.0) | com.perfectdark.port / 2 | 21 | `7b782d785d4f3fd012da94157aec61379cbb295e525777adb27128c6235d19cb` |
| Banjo-Kazooie | [Banjo: Recompiled Android android-v0.1.2](https://github.com/AurelioB/BanjoRecomp-Android/releases/tag/android-v0.1.2) | com.aure.banjorecomp / 102 | 24 | `4c2a5e57c41119bb6149ffcfb6b012d44466460b29ca54ae81008afc5d13f5ac` |
| Donkey Kong 64 | [DK64 Recompiled Android v1.0.3-android-r7](https://github.com/deivid22srk/dk64-recomp-android/releases/tag/v1.0.3-android-r7) | com.deivid22srk.dk64recomp / 4 | 26 | `041a9e3794e5593ebdd75a2ce28c577f376ffb2477dc0cf876175e4c8072755d` |
| Harvest Moon 64 | [Harvest Moon 64 Recompiled v0.2.0](https://github.com/igawa6/HarvestMoon64Recomp/releases/tag/v0.2.0) | io.github.hm64recomp / 3 | 28 | `8547d75f74d5c86d6042526fd8912fd0446e2013e654bd3ef98d7f5673b9f6fe` |
| Mystical Ninja Starring Goemon | [Goemon 64 Recompiled Android v1.0.8](https://github.com/ogdanimal/Goemon64Recomp-Android/releases/tag/v1.0.8) | com.goemon64.recomp / 10008 | 28 | `0c7a6490e0c9d22cd51aac217461bd8aee6e6bd23540a052430e325d34d3e559` |
| Super Mario Land 2: 6 Golden Coins | [Super Mario Land 2 Recompiled v0.2.0](https://github.com/mstan/SuperMarioLand2Recomp/releases/tag/v0.2.0) | com.mstan.supermarioland2recomp / 200 | 24 | `372ada0a7cb43c12eef72d03f00cbb94ab7c5ee5335ac23434ee559e6fd60d6f` |
| Fire Emblem: The Sacred Stones | [Sacred Stones Recompiled v0.1.20](https://github.com/Lordingard/SacredStonesRecomp/releases/tag/v0.1.20) | com.lordingard.sacredstonesrecomp / 19 | 28 | `031818433c534f46dd7b202d1669d6887c257024a42e088901925750f682560d` |
| The Legend of Zelda: A Link to the Past | [Moonpearl v1.0.0](https://github.com/DoutorRaposo/moonpearl/releases/tag/v1.0.0) | io.github.doutorraposo.moonpearl / 10000 | 26 | `4d65448b89df4cee4e8aa43c2895f55d83b3168fce7dca7c63eeded9ca93d2b1` |
| The Legend of Zelda: Twilight Princess | [Dusklight v2.0.3](https://github.com/TwilitRealm/dusklight/releases/tag/v2.0.3) | dev.twilitrealm.dusk / 20003000 | 28 | `44aa6ed4717fb0fc6094b62c45e4ddcc8b26ce6ecd9bbca11e97d837cf56fad6` |
| Metroid Prime | [Metroid Prime Unbound v0.36.0](https://github.com/Odrannnn/MetroidPrimePort/releases/tag/v0.36.0) | org.metroidprime.port / 44 | 28 | `5c58b670a5e710ae43b57722f683d5829cbad89b87616fa0aad0161bf1e48186` |
| Mario Party 6 | [Starlit Party v0.5.0](https://github.com/iirg4x/mp6-native-port/releases/tag/v0.5.0) | com.mp6.game / 40300 | 28 | `96308819b11c55ace1ace202cf358b99ec412e0a7a464aab7659c86d1035af7e` |
| Mario Kart Wii | [DriftDroid v1.3.0](https://github.com/dorPXP/driftdroid/releases/tag/v1.3.0) | com.driftdroid.android / 6 | 29 | `f2285cabf3d86f62c513e0ab6c80717f92d44e9be4efe403b9c14cc5666333ea` |

Les jaquettes et, lorsqu’elles existent, leurs faces et médias réutilisent les illustrations exactes du catalogue PC (`Catalogs/covers.json`, avec sources et empreintes), copiées comme ressources mobiles. Aucun visuel généré.

Non ajoutés dans ce lot : HuntersRecomp (release Thor à double écran, usage sur téléphone simple écran non établi), les projets sans APK vérifiable, les émulateurs/JIT, les jeux seulement compilés pour Linux/Windows ARM64 et les variantes déjà représentées. Les autres ports ARM64 restent à contrôler : ce catalogue ne prétend pas être exhaustif.

Les ports externes s’installent avec la confirmation Android et restent des applications distinctes. Seuls les moteurs déjà intégrés, PartyBoard et Ghostship, s’exécutent dans les processus de DecompShelf. Changer de signature Perfect Dark peut imposer une réinstallation : DecompShelf ne désinstalle pas automatiquement un jeu et ne supprime pas ses sauvegardes.
