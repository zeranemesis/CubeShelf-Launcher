# Sons Big Picture

## Thème Double écran · HOME

Ambiance et trois effets originaux, générés par `python eng/generate-home-audio.py` : `home-ambience.wav` (boucle de 16 secondes), `home-move.wav` (120 ms), `home-open.wav` (300 ms) et `home-launch.wav` (700 ms). PCM mono 16 bits, 24 kHz ; aucune musique ni son extrait d'iiSU ou de Nintendo. Les assertions du générateur vérifient saturation et extrémités silencieuses.

Ces quatre fichiers sont embarqués sur Windows et Android. L'ambiance prend le relais quand aucun extrait local n'est associé au jeu. Le bouton Audio permet de couper séparément musique et effets. La musique s'arrête quand le thème est masqué, que l'application perd son activité ou qu'un jeu démarre. Les répétitions de sélection sont limitées à un effet toutes les 85 ms ; l'animation d'aperçu dure 220 ms, sans boucle de rendu permanente. Le réglage de mouvement réduit du PC est respecté.

Les sections suivantes décrivent les sons conservés pour le thème Classique.

Sons originaux synthétisés pour DecompShelf, sans extrait audio de console ou de jeu.
Régénération : `python eng/generate-big-picture-sounds.py`.

- `click.wav` : toucher feutré de 65 ms, corps à 235 Hz, attaque progressive de 9 ms, pour les commandes et la navigation.
- `launch.wav` : accord grave et chaud sur 420 ms, attaque progressive de 70 ms, après démarrage réussi du processus.

La révision douce abaisse les fréquences, retire les harmoniques aiguës et remplace les trois notes successives par un seul accord fondu. Les enveloppes commencent et finissent au silence, avec une pente continue et un niveau réduit.

PCM mono 16 bits / 24 kHz, amplitude volontairement faible. Windows lit les fichiers de façon asynchrone, dans la session audio de l'application ; le volume et le silence Windows sont respectés. Aucun son système de remplacement si une ressource ou un périphérique manque. Les clics sont espacés d'au moins 80 ms et ne coupent pas la tonalité de lancement.

« Sons de Big Picture » dans le menu principal ou les paramètres conserve le choix dans le profil. Le bureau reste silencieux. La lecture de ces effets est actuellement implémentée sur Windows.

Contrôle initial du 1er novembre 2026 : 197 tests Core et suite ShelfUi réussis, y compris anciens profils, silence du bureau, souris/clavier/manette simulée, répétitions, coupure et lancement de processus réussi/échoué. Lecture des deux WAV acceptée par WinMM sur Windows, retour asynchrone mesuré à 19 ms puis 0 ms. Compilation Windows x64 autonome réussie et fichiers audio publiés identiques aux sources.

Révision douce du même jour : contrôles de format PCM, absence de saturation, transitions aux bornes et génération reproductible réussis ; lecture des deux nouveaux fichiers acceptée par WinMM. Niveau RMS réduit de 8,1 dB pour la navigation et 8,9 dB pour le lancement par rapport aux fichiers initiaux. Ressources remplacées dans la version Windows ouverte et empreintes identiques aux sources. Le confort sonore reste à juger à l'écoute ; aucune nouvelle validation sur manette physique.
