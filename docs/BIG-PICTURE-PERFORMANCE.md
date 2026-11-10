# Fluidité Big Picture — 26 octobre 2026

Le ruban conserve désormais des boîtiers à taille fixe et le compositeur les place et les redimensionne. Leurs textures et leurs maillages sont dessinés avec les commandes Skia déjà utilisées par le moteur mobile, activées par instance en Big Picture. Les textures gardent leur définition et utilisent un filtrage de qualité. Les textures de faces, dos, tranches et supports peuvent être préparées en arrière-plan.

Les calculs de caméra et les coordonnées des cercles de l'ouverture sont partagés au lieu de refaire la trigonométrie pour chaque point. Les disques descendent devant le boîtier et tournent avec une accélération progressive. Une sélection inchangée ne recrée pas les boîtiers et conserve l'inspection. Les mises à jour d'état évitent de relire les profils de toutes les cartes bureau cachées ; leurs statuts sont remis à jour à la sortie de Big Picture.

## Mesures hors écran

Mesure Windows avec Skia, 1000 × 600 pixels, sept boîtiers visibles, 56 rendus par configuration. Le mode de comparaison désactive les deux optimisations du ruban. Chaque rendu comprend le placement puis le dessin vers un bitmap ; le chargement initial et l'appel de changement de sélection sont hors de ce chronométrage. La médiane et le maximum des appels de sélection figurent séparément dans le JSON.

| Console | Médiane avant → après | 95e percentile avant → après | Allocations gérées par rendu avant → après |
| --- | --- | --- | --- |
| GameCube | 52,12 → 14,46 ms | 64,95 → 20,77 ms | 1134,5 → 61,9 Kio |
| Wii | 46,58 → 18,30 ms | 52,86 → 29,64 ms | 425,6 → 94,4 Kio |
| Switch | 52,35 → 21,63 ms | 357,21 → 81,66 ms | 359,9 → 88,0 Kio |

Ces résultats sont une mesure de coût de rendu, pas une cadence d'écran, une mesure de latence manette ou une promesse de 60/120 FPS. Le processeur, la charge du système, le pilote graphique, la résolution et les chargements à froid influencent le résultat. Les premières images et les cas extrêmes restent plus coûteux que la médiane ; le 95e percentile Switch le montre.

Les résultats bruts sont dans `benchmarks/big-picture-2026-10-26.json`. Pour refaire l'essai :

```powershell
$env:CUBESHELF_RENDER_BENCHMARK_FILE = 'C:/temp/cubeshelf-render.json'
dotnet run --project tests/CubeShelf.ShelfUi.Tests -c Release -- --render-benchmark
```

`CUBESHELF_UI_SNAPSHOT_DIR` permet aussi de conserver les captures de chaque configuration. Les tests habituels contrôlent le défilement, les menus, l'inversion des commandes, les annulations, la rotation du disque et les statuts au retour au bureau. Les essais sur manette physique et les mesures de présentation GPU en jeu restent à faire.
