# Sons Big Picture

Sons originaux synthétisés pour DecompShelf, sans extrait audio de console ou de jeu.
Régénération : `python eng/generate-big-picture-sounds.py`.

- `click.wav` : clic doux de 55 ms pour les commandes et la navigation.
- `launch.wav` : trois notes montantes sur 420 ms, après démarrage réussi du processus.

PCM mono 16 bits / 24 kHz, amplitude volontairement faible. Windows lit les fichiers de façon asynchrone, dans la session audio de l'application ; le volume et le silence Windows sont respectés. Aucun son système de remplacement si une ressource ou un périphérique manque. Les clics sont espacés d'au moins 80 ms et ne coupent pas la tonalité de lancement.

« Sons de Big Picture » dans le menu principal ou les paramètres conserve le choix dans le profil. Le bureau reste silencieux. La lecture de ces effets est actuellement implémentée sur Windows.

Contrôle du 1er novembre 2026 : 197 tests Core et suite ShelfUi réussis, y compris anciens profils, silence du bureau, souris/clavier/manette simulée, répétitions, coupure et lancement de processus réussi/échoué. Lecture des deux WAV acceptée par WinMM sur Windows, retour asynchrone mesuré à 19 ms puis 0 ms. Compilation Windows x64 autonome réussie et fichiers audio publiés identiques aux sources. L'écoute sur le matériel du joueur et une manette physique restent à confirmer.
