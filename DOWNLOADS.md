# Gestion des téléchargements

La page **Téléchargements** (Ctrl+4) distingue l’opération active, la file d’attente
et l’historique. Les jaquettes proviennent du catalogue et des fichiers locaux ;
la page n’envoie aucune requête réseau pour les afficher.

- **Réseau / écriture disque** : octets réellement transférés ou écrits par les
  opérations de la file, échantillonnés chaque seconde sur une fenêtre d’une minute.
  Ce sont les écritures de CubeShelf, pas l’activité totale du disque de Windows.
- **Débit** : illimité, 1, 5, 10, 20 ou 50 Mio/s. Le réglage est sauvegardé dans le
  profil et prend effet pendant le transfert courant. Les recherches de métadonnées
  et les images de l’onglet Mods ne font pas partie de cette limite.
- **Pause / reprise** : le flux HTTP reste ouvert, sans relancer l’installation ni
  perdre les octets déjà reçus. L’annulation reste possible pendant la pause.
  Une extraction ou une transaction d’installation déjà commencée se termine ;
  « Tout suspendre » empêche ensuite le lancement de la prochaine opération.
- **Priorité** : glisser la jaquette d’une ligne avant une autre, utiliser les
  flèches haut/bas, ou placer une opération en premier. L’ordre affiché est celui
  de l’exécution. Une opération en cours n’est pas interrompue pour changer de priorité.
- **Progression** : le transfert ne signifie pas que le jeu est installé. La
  vérification et l’installation ont leurs propres états ; 100 % n’est affiché
  qu’une fois l’opération réussie. Une archive sans checksum d’éditeur conserve
  l’indication d’installation non vérifiée dans la fiche du jeu.
- **Historique** : les échecs et interruptions restent visibles avec les actions de
  reprise/réessai existantes. « Effacer les terminés » conserve ces erreurs et ne
  supprime aucun jeu. Une opération interrompue par la fermeture est retrouvée au
  démarrage suivant ; la reprise HTTP Range est utilisée lorsque le service et
  le serveur la prennent en charge.

Les moteurs de jeux, les mods, les sources, DolphinTool et les téléchargements de
mise à jour de CubeShelf utilisent les mêmes points de mesure. La mise à jour de
CubeShelf est ajoutée à la file après confirmation dans les paramètres.

## Vérification

```powershell
dotnet build src/CubeShelf.Desktop/CubeShelf.Desktop.csproj -c Release
dotnet run --project tests/CubeShelf.Core.Tests -c Release
```

Pour ne lancer que les tests du gestionnaire :

```powershell
$env:CUBESHELF_TEST_FILTER = 'downloads:'
dotnet run --project tests/CubeShelf.Core.Tests -c Release
```
