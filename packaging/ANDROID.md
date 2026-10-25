# Aperçu Android

CubeShelf Android est un projet Avalonia distinct du launcher PC :
`src/CubeShelf.Android/CubeShelf.Android.csproj`, .NET 8 pour Android,
Avalonia 11.3.21, identifiant `io.github.zeranemesis.cubeshelf`.
La version 0.17.0 porte le code Android `42` et comprend la mise à jour
de l'application depuis les releases GitHub.

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

La signature utilise la clé de développement persistante conservée dans le
secret GitHub `CUBESHELF_ANDROID_KEYSTORE_BASE64`, commune aux architectures
et aux exécutions. Le build échoue si cette clé manque, au lieu de produire
un APK incompatible avec les mises à jour. Aucun keystore n'est publié.
Cette clé reste une clé de développement, pas une signature de distribution.
Un ancien aperçu signé avec une autre clé ne peut pas être remplacé directement.
Ne pas le désinstaller pour contourner ce refus sans sauvegarder ses données.

## Mettre CubeShelf à jour

La recherche de la dernière release stable est automatique au démarrage puis
toutes les cinq minutes tant que l'application est au premier plan. Un point
sur le bouton ↻ indique une version disponible. Ce bouton ouvre la carte
**Application CubeShelf** dans **Téléchargements**, avec les actions Vérifier,
Télécharger, Annuler et Installer.

Le launcher choisit l'APK de son architecture, vérifie sa taille et son SHA-256
contre `checksums.txt` et, lorsqu'elle est fournie, l'empreinte GitHub. Il vérifie
ensuite l'identité de l'application, un code de version supérieur et la même
signature avant d'ouvrir l'installateur Android. Android demande l'autorisation
d'installer depuis CubeShelf, puis la confirmation de la mise à jour. Au retour
des autorisations, toucher à nouveau Installer. Aucune installation silencieuse
ni désinstallation automatique ; les données sont conservées par Android lors
d'une mise à jour compatible. L'ancienne version doit être remplacée une première
fois manuellement pour bénéficier de cette nouvelle fonction.

## Installer et démarrer un jeu

1. Télécharger l'artefact adapté à l'architecture, extraire le ZIP et installer
   l'APK CubeShelf. Android peut demander d'autoriser cette source d'installation.
2. Sur ARM64, préparer PartyBoard depuis CubeShelf : le moteur natif est inclus
   et seules ses ressources officielles sont téléchargées puis vérifiées.
3. Au premier démarrage de Party Board, sélectionner son propre disque de jeu
   depuis le jeu. Les fichiers du profil PC ne sont pas importés automatiquement.
4. Utiliser Jouer pour ouvrir l'activité native PartyBoard intégrée à CubeShelf.

CubeShelf ne contient aucun disque, fichier extrait ou autre donnée de jeu.
Le binaire Windows d'un runtime CubeShelf ne fonctionne pas sous Android.

## Parties en ligne

L'aperçu Android vise les parties **Android vers Android** avec des versions
compatibles de Party Board et le même disque et les mêmes mods. Le parcours
Android bloque la connexion à un salon PC : la compatibilité PC/Android n'est
pas annoncée. La connexion réelle reste assurée par Party Board, dont les
capacités réseau déterminent les fonctions disponibles.

## Limites de cet aperçu

- Les essais du moteur et de l'interface consignés dans le changelog concernent
  un Galaxy S20. La nouvelle installation d'une mise à jour doit encore être
  validée de bout en bout sur téléphone ; aucune partie Android vers Android
  n'est annoncée comme validée ici.
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
  l’application passe en arrière-plan. Une file de téléchargements mobile est
  disponible ; les amis CubeShelf et profils de mods PC restent absents.
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
