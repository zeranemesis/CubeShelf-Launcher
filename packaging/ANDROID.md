# Aperçu Android

CubeShelf Android est un projet Avalonia distinct du launcher PC :
`src/CubeShelf.Android/CubeShelf.Android.csproj`, .NET 8 pour Android,
Avalonia 11.3.21, identifiant `io.github.zeranemesis.cubeshelf`.
La source de cet aperçu porte la version affichée `0.17.0-preview.1` et le code
de version Android `4`. Cela ne signifie pas qu’un nouvel APK a été publié.

## Produire les APK

Dans GitHub Actions, lancer manuellement **Build Android previews (manual)**
sur la branche contenant le projet Android. Le workflow prépare .NET 8,
le workload Android, Java 17 et les outils Android API 34, puis publie deux
artefacts conservés 14 jours :

| Architecture | Fichier | Usage |
| --- | --- | --- |
| ARM64 `arm64-v8a` | `CubeShelf-android-arm64-v8a-preview.apk` | Téléphones et tablettes ARM64 |
| x86-64 `x86_64` | `CubeShelf-android-x86_64-preview.apk` | Appareils ou émulateurs Android x86-64 |

Chaque artefact comprend son SHA-256. Ce workflow compile et signe le launcher ;
il ne lance aucun test, ne démarre aucun appareil et ne publie aucune release.
Un APK compilé ne constitue pas une validation sur téléphone.

La signature utilise une clé de développement temporaire créée dans le runner,
avec le mot de passe public conventionnel `android`. Elle sert uniquement à
installer cet aperçu ; ce n'est pas une signature de distribution. Aucun
keystore n'est inclus dans les artefacts. La clé change entre les exécutions
et les architectures : Android peut refuser une mise à jour d'un aperçu
précédent. Il faut alors désinstaller l'ancien CubeShelf avant d'installer le
nouvel APK ; cette désinstallation efface ses préférences locales.

## Installer et démarrer un jeu

1. Télécharger l'artefact adapté à l'architecture, extraire le ZIP et installer
   l'APK CubeShelf. Android peut demander d'autoriser cette source d'installation.
2. Installer séparément l'APK **natif Android de Mario Party 4 / Party Board**
   proposé par son projet amont. Le launcher et le jeu sont deux applications.
3. Au premier démarrage de Party Board, sélectionner son propre disque de jeu
   depuis le jeu. Les fichiers du profil PC ne sont pas importés automatiquement.
4. Utiliser CubeShelf pour ouvrir l'application du jeu déjà installée.

CubeShelf ne contient aucun disque, fichier extrait ou autre donnée de jeu.
Le binaire Windows d'un runtime CubeShelf ne fonctionne pas sous Android.

## Parties en ligne

L'aperçu Android vise les parties **Android vers Android** avec des versions
compatibles de Party Board et le même disque et les mêmes mods. Le parcours
Android bloque la connexion à un salon PC : la compatibilité PC/Android n'est
pas annoncée. La connexion réelle reste assurée par Party Board, dont les
capacités réseau déterminent les fonctions disponibles.

## Limites de cet aperçu

- Aucun appareil physique n'a encore validé cet APK, sa navigation ou ses
  performances. Aucune partie Android vers Android n'a été validée ici.
- Seul Mario Party 4 dispose du parcours de jeu Android prévu dans cet aperçu.
  Les autres jeux du catalogue PC ne deviennent pas compatibles par la seule
  installation du launcher Android.
- Les projets Android expérimentaux de Melee et Mario Party 5 ne sont pas
  présentés comme des jeux complets jouables. Les décompilations de Mario
  Party 6, Mario Kart Double Dash, TTYD, F-Zero GX, Super Paper Mario et Sunshine
  ne constituent pas, à elles seules, des ports Android.
- Les installations, autorisations et sélections de fichiers Android utilisent
  les écrans du système et de l'application du jeu.
- Le catalogue et le renderer GLB sont partagés avec le PC. La disposition
  s’adapte au portrait et au paysage ; les images sont libérées quand
  l’application passe en arrière-plan. La file de téléchargements, les amis
  CubeShelf et les profils de mods PC ne sont pas encore portés dans cet aperçu.
- Aucun bouton fléché : parcourir les jaquettes par balayage horizontal ou à la
  manette. Toucher une voisine la sélectionne ; toucher la sélection la retourne.
  Un mouvement vertical reste réservé au défilement de la page.
- L’APK PartyBoard publié est ARM64 uniquement. Sur x86-64, CubeShelf masque son
  installation et explique cette limite ; un port installé séparément peut
  être ouvert s’il expose les mêmes activités.

### Compilation locale

Avec SDK .NET 8, workload `android`, Android SDK API34 et Java17 :

```powershell
dotnet publish src/CubeShelf.Android/CubeShelf.Android.csproj -c Release -r android-arm64 -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk17>
```

Sans clé de distribution fournie, l’APK `*-Signed.apk` emploie la clé de
développement locale. Garder cette même clé pour pouvoir mettre à jour ses
propres aperçus sans désinstallation. Ne pas distribuer la clé privée.

## Références de compilation

La configuration APK et signature suit les propriétés officielles de
[.NET pour Android](https://learn.microsoft.com/dotnet/android/building-apps/build-properties)
et les indications de déploiement
[Avalonia Android](https://docs.avaloniaui.net/docs/deployment/android).
