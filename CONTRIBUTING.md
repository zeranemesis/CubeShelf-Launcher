# Contribuer à CubeShelf

## Compiler et vérifier

Prérequis : SDK .NET 8. Depuis la racine :

```powershell
dotnet build CubeShelf.Portable.slnf -c Release
dotnet run --project tests/CubeShelf.Core.Tests/CubeShelf.Core.Tests.csproj -c Release
dotnet run --project tests/CubeShelf.Tests/CubeShelf.Tests.csproj -c Release
```

Sous Windows, `dotnet build CubeShelfLauncher.sln -c Release` construit aussi l’updater WPF. Inno Setup 6 est requis uniquement pour fabriquer l’installateur avec `BUILD_INSTALLER.ps1`.

Les tests sont des exécutables : utiliser `dotnet run`, pas seulement `dotnet test`. Pour cibler les tests d’intégration des runtimes sous PowerShell :

```powershell
$env:CUBESHELF_TEST_FILTER = 'runtime:'
dotnet run --project tests/CubeShelf.Core.Tests/CubeShelf.Core.Tests.csproj -c Release
Remove-Item Env:CUBESHELF_TEST_FILTER
```

## Ajouter ou modifier un jeu

Modifier `src/CubeShelf.Desktop/games.json` : révisions, acquisition du runtime, artefacts par plateforme, `DataPreparation`, `DiscFormats`, `LaunchArguments` et `LaunchEnvironment`. Vérifier les paramètres dans les sources de l’éditeur ; ne pas supposer que les variables PartyBoard sont comprises par un autre jeu.

`{disc}` désigne l’image sélectionnée ; `{mods}` désigne la liste active. Les arguments restent des éléments distincts, sans shell. Les champs de distribution sont remplacés depuis le catalogue livré ; les chemins utilisateur et statistiques sont conservés. Ajouter un scénario dans `RuntimeIntegrationTests.LaunchContracts` et effectuer un lancement réel avec son propre disque.

## Tests manuels et signalement d’un problème

- Tester un profil neuf et un profil existant. `CUBESHELF_DATA_DIR` peut désigner un dossier absolu de test afin d’isoler les données.
- Vérifier l’installation, le premier lancement, le lancement suivant et l’arrêt du jeu.
- Interrompre un téléchargement de mod, redémarrer CubeShelf, reprendre puis vérifier la confirmation du plan et les fichiers installés.
- Pour le réseau et les invitations, suivre `TEST_DEUX_PC.md` et consigner les versions, types de connexion et résultats.
- Pour un bug, indiquer la version de CubeShelf, l’OS et son architecture, le jeu/runtime, les étapes de reproduction et le résultat attendu. Masquer clés, codes amis, jetons, adresses et chemins personnels avant de joindre un journal.

Les journaux se trouvent dans le dossier `Logs` du profil. Ne jamais joindre de disque ni de données extraites de jeu au dépôt ou à une issue.

## Limites de validation

Une compilation et des tests unitaires réussis ne valident pas le rendu graphique, le lancement réel des jeux, les différentes box, Steam Deck ou macOS. Distinguer les vérifications exécutées des essais encore nécessaires dans chaque proposition de modification.

Le dépôt ne contient actuellement pas de fichier de licence : son choix revient au détenteur des droits. Ce guide n’accorde aucune licence sur le code ou les ressources.
