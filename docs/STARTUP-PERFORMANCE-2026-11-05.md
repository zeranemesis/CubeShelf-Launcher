# Démarrage et taille — 5 novembre 2026

Premier lot du plan de performances. Mesures Windows headless (Skia), profil neuf, poste de développement chargé par d'autres applications : les durées varient du simple au double d'un passage à l'autre. Les chiffres ci-dessous sont des minimums sur cinq passages alternés avant/après, pas une promesse de temps de démarrage réel.

| Mesure | Avant | Après |
|---|---|---|
| Construction de la fenêtre | 4,1 s | 3,0 s |
| Construction + première image, compilation JIT | 8,2 s | 7,2 s |
| Même chose, code précompilé (ReadyToRun) | — | 5,5 s |
| Mémoire après démarrage | 200 Mo | 187 Mo |
| Logos de jeux embarqués | 390 Mo | 58 Mo |
| Jaquettes embarquées (PC) | 249 Mo | 215 Mo |

## Ce qui a changé

- **Illustrations** : `eng/optimize-artwork.py` réduit les logos à 640 × 260 et les jaquettes à 1200 px au plus (taille maximale affichée). 499 fichiers, 432 → 66 Mo. Le plus gros logo faisait 13 720 × 3 822 px et demandait 250 ms de décodage. Les empreintes `*FileSha256` de `covers.json` sont recalculées ; les empreintes de provenance des sources téléchargées ne changent pas. Après un import d'illustrations, relancer le script ; `--check` échoue si un fichier dépasse encore la limite.
- **Langue** : les textes sont posés avant la construction de la fenêtre, et une valeur inchangée n'est plus réécrite. Chaque écriture de ressource faisait reparcourir toute l'interface (1,4 s au profileur).
- **Catalogue PC** : ses cartes et jaquettes ne sont construites que lorsqu'il est réellement à l'écran. Au démarrage, l'accueil des consoles le recouvre ; en Big Picture, il est masqué. Il se met à jour dès qu'il redevient visible.
- **Clés de jeu** : `NativePortCatalog.GameKey` mémorise la normalisation de chaque titre (comparaisons deux à deux au démarrage).
- **GameBanana** : le client HTTP n'est créé qu'à la première ouverture des mods.
- **ReadyToRun** : activé pour toute publication avec un runtime ciblé (installateur, archives portables). Les compilations de développement et les tests ne changent pas.

## Refaire la mesure

```powershell
dotnet run --project tests/CubeShelf.ShelfUi.Tests -c Release -- --startup-benchmark
```

Lancer plusieurs fois et comparer les médianes. Relevé après ce lot, poste moins chargé, trois passages : fenêtre 1,33–1,37 s, première image 1,41–1,44 s, 203–206 Mo (code JIT, application déjà initialisée par le banc). Le profileur utilisé est `dotnet-trace` (format speedscope).

## Reste à faire (lot 2)

Le travail de l'application dans la première image tombe à environ 1,3 s ; le reste est la mise en page et le rendu d'une fenêtre qui construit encore toutes ses pages (1 115 lignes de XAML, 50 panneaux masqués). Prochaines étapes : pages créées à la première ouverture, grilles virtualisées, accueil des consoles décodé en arrière-plan, puis mesures sur Android et sur un démarrage à froid réel.
