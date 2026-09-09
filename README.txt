PartyBoard + CubeShelf — vrai flux "Installer -> Jouer"

J'ai identifié le blocage réel dans le dépôt actuel :
le job Windows échoue au link sur msmSePlay / msmSeStop / msmSeGetStatus.

Pourquoi :
- rollback_audio.cpp utilise l'API msmSe ;
- le build PC par défaut compile src/port/audio.c ;
- src/msm/msmse.c n'est compilé que si le backend MusyX expérimental est activé.

Ce patch :
1) rend le backend PC silencieux compatible avec le pont rollback ;
2) fournit les symboles msmSe manquants dans src/port/audio.c ;
3) ajoute une GitHub Action Windows dédiée à CubeShelf ;
4) publie automatiquement une prerelease `cubeshelf-nightly` contenant
   `PartyBoard-win-x64.zip`, `manifest.json`, `checksums.txt`.

CubeShelf v0.6.7 est déjà configuré pour ce tag et cet asset.

Pour appliquer :
APPLY_AND_PUSH.bat "C:\CHEMIN\VERS\Marioparty4"

Le script checkout/pull audio-local, applique, vérifie, commit et push.

Après un build GitHub Actions réussi, CubeShelf pourra enfin faire :
Télécharger le jeu -> PartyBoard Windows -> ISO/RVZ -> Jouer.

Aucun asset Nintendo n'est inclus dans le ZIP Windows.
