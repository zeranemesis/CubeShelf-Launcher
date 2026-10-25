# Aperçus portables PC

Le workflow **Build portable desktop previews (manual)** se lance manuellement dans GitHub Actions. Il compile le launcher Avalonia en .NET 8 autonome pour six cibles :

| Système | Architectures | Archive |
| --- | --- | --- |
| Windows | x64, arm64 | ZIP, launcher et updater de même architecture |
| Linux avec glibc | x64, arm64 | TAR.GZ, exécutable `CubeShelf` |
| macOS | Intel x64, Apple Silicon arm64 | TAR.GZ contenant `CubeShelf.app` |

Les archives et leur SHA-256 se trouvent dans les artefacts du workflow pendant 14 jours. Ce workflow ne lance aucun test, n'exécute pas le launcher, ne publie aucune release et ne remplace pas le circuit de publication stable. Les cibles ARM peuvent être compilées depuis un runner d'une autre architecture : un build réussi ne constitue pas une validation sur appareil.

## Utilisation

Extraire toute l'archive avant de lancer le programme. Sous macOS, ouvrir `CubeShelf.app` ; sous Linux, lancer `./CubeShelf/CubeShelf` depuis le dossier extrait ; sous Windows, ouvrir `CubeShelf/CubeShelf.exe`. Le runtime .NET est inclus. Les préférences restent dans le profil utilisateur habituel : « portable » décrit ici l'absence d'installateur, pas un déplacement automatique des données utilisateur.

Ces aperçus ne sont ni signés avec un certificat de distribution ni notarisés. Les mécanismes de sécurité du système peuvent demander une autorisation d'ouverture. Linux requiert un environnement graphique et les bibliothèques natives utilisées par Avalonia, notamment X11, Fontconfig et les dépendances système de Skia. Ce paquet ne vise pas Alpine/musl ni les machines 32 bits.

La disponibilité du launcher sur une architecture ne garantit pas celle des jeux. Chaque runtime amont doit fournir un binaire compatible avec le système et l'architecture sélectionnés ; l'installation et le jeu en ligne restent soumis à ces capacités. Les artefacts ARM et macOS doivent être essayés sur leurs appareils avant d'être annoncés comme pris en charge. Android utilise un projet et un cycle de packaging distincts.

## Packaging local

Publier `src/CubeShelf.Desktop/CubeShelf.Desktop.csproj` avec `dotnet publish -c Release -r <rid> --self-contained true`. Pour Windows, publier également `src/CubeShelf.Updater/CubeShelf.Updater.csproj` avec le même RID et copier `CubeShelf.Updater.exe` dans le dossier publié du launcher.

Depuis la racine du dépôt, utiliser PowerShell 7 :

```powershell
./packaging/Package-Desktop.ps1 -RuntimeIdentifier linux-arm64 -PublishDirectory publish/linux-arm64 -OutputDirectory dist/linux-arm64
```

Assembler les archives Linux sur Linux et les bundles macOS sur macOS pour conserver les permissions Unix. Choisir un nouveau dossier de sortie à chaque assemblage : le script refuse de réutiliser son dossier de préparation afin d'éviter d'inclure des fichiers d'une ancienne compilation.
