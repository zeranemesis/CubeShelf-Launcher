# Bip : aide en jeu — préversion PC 0.18.1-preview.5

## Windows

Après le choix Indice ou Solution, les boutons disparaissent : seule une petite bulle « Je réfléchis… » reste visible. La réponse apparaît ensuite dans une bulle dont la hauteur suit le texte, avec défilement si elle dépasse la place disponible. Cliquer sur la réponse ouvre un champ de précision et **Préciser** relance le même type d’aide avec une nouvelle capture et les précisions cumulées (limitées à 1 000 caractères). Le clic droit sur la bulle donne accès aux options et sources, à un nouveau choix et à Réduire. La recherche utilise aussi Wikibooks et reçoit les mots-clés affinés avec les précisions du joueur ; les captures restent locales.

Lancer un jeu depuis DecompShelf, puis cliquer une fois sur Bip, en haut à droite de la fenêtre du jeu, ou utiliser **Ctrl+Maj+H**. Deux choix proposent **Indice** et **Solution**. Le choix prend une capture du jeu et lance l’analyse ; écrire reste facultatif dans **Options**. Ouvrir les choix ne capture rien et ne charge pas le modèle.

Maintenir le bouton gauche sur Bip et glisser permet de le déplacer. Un déplacement de moins de six pixels logiques reste un clic ; un glissement ne lance aucune analyse et n’ouvre pas l’aide au relâchement. La position relative est conservée jusqu’à la fermeture de DecompShelf et suit le redimensionnement du jeu. La bulle se place du côté disponible et reste dans la zone du jeu. Les fenêtres de jeu très petites peuvent imposer un chevauchement avec Bip.

Les choix partagent une seule surface sombre : boutons de même taille, libellés centrés et action Réduire discrète. La bulle affiche directement la réponse, sans description du décor ni messages techniques. Un indice est demandé en une ou deux phrases ; une solution en trois étapes maximum. Les sources, la voix et les précisions sont accessibles seulement après ouverture d’**Options**. Ces limites sont des consignes au modèle, pas une garantie d’exactitude.

La première utilisation propose **Préparer Bip · 1,3 Go**. Le modèle est facultatif, téléchargé séparément du programme, sans compte ni clé API. Bip et sa bulle se masquent lorsque le premier plan quitte le jeu pour une autre application. La demande déjà lancée peut se terminer en arrière-plan ; aucune nouvelle capture n’est effectuée. La réponse est conservée et un clic sur Bip la réaffiche en revenant dans le jeu. Il suit uniquement les processus de jeux lancés et suivis par DecompShelf. Le raccourci ne l’ouvre pas dans la bibliothèque.

Un clic droit ouvre **Masquer Bip**. Ce choix masque le compagnon et la bulle, annule la demande en cours et reste actif quand on revient dans le jeu. **Ctrl+Maj+H** permet de le réafficher dans le jeu ; le lancement d’une nouvelle session réactive aussi le compagnon.

Le compagnon et sa bulle sont des fenêtres superposées, sans injection. Réduire ferme la bulle et rend le premier plan au jeu. Le jeu ne se met pas automatiquement en pause. Les mouvements doux respectent la réduction des animations et s’arrêtent au repos. La dictée et la synthèse françaises utilisent les composants vocaux Windows installés, uniquement sur demande.

## Analyse et guides web

LFM3-VL-2B analyse localement une image du jeu, limitée à 960 pixels sur son plus grand côté. La capture exclut temporairement Bip et sa bulle. Elle reste en mémoire, puis son tampon est effacé après la demande ; aucun fichier image n’est enregistré par le produit.

L’observation produit une recherche textuelle envoyée aux API publiques de StrategyWiki et MarioWiki. Ni image, ni sauvegarde, ni cookie, ni profil ne leur sont envoyés. Le nom du jeu et quelques mots décrivant la scène quittent donc le PC. Cette aide web nécessite Internet ; le modèle fonctionne localement. En cas de panne réseau ou de guide non pertinent, Bip utilise les éléments visibles. Le volet **Pages consultées** affiche les titres et liens des sources réellement récupérées sans ouvrir de navigateur.

Les guides sont limités en taille et en durée de téléchargement. Les pages sans rapport avec le jeu ou la scène sont rejetées. La réponse finale utilise de nouveau l’image et les passages retenus. Les guides et textes de l’image sont traités comme des données. L’exactitude des réponses et le respect du niveau de spoiler restent imparfaits ; l’assistant n’a ni l’historique des actions ni la sauvegarde.

## Ressources et intégrité

Modèle épinglé à `LiquidAI/LFM3-VL-2B-GGUF`, révision `7c3b4a43c9023e80bf234fec5e826a3365c8dda8` :

- `LFM3-VL-2B-Q4_K_M.gguf` : 1 099 627 808 octets, SHA-256 `56dac2e192ff0d9ab8507f4f1aeb4aed0463449bbbefaac79497f041c95f21eb`.
- `mmproj-LFM3-VL-2B-Q8_0.gguf` : 203 031 872 octets, SHA-256 `c5c5b58d544a18da919172d2d0368090ea1a4a351b6feb7f8a326a264a5918ab`.

Chaque fichier est vérifié avant installation et avant inférence. Une interruption conserve les fichiers déjà validés. Les poids ne sont inclus ni dans Git ni dans les paquets. L’ancien modèle textuel LFM3-1.7B reste pris en charge pour les questions sans capture.

Le moteur CPU utilise au plus quatre threads, un contexte de 4 096 tokens et une réponse de 384 tokens. Poids, projecteur et contexte sont libérés après chaque inférence, erreur ou annulation. Les deux analyses visuelles successives prennent du temps et nécessitent davantage de mémoire que les 1,3 Go de fichiers. Windows x64 nécessite AVX2, FMA et BMI2.

`eng/prepare-assistance.py` compile llama.cpp avec mtmd, épinglé au commit `8ca4060b7e30e76baaa5670ee1ec1b760b7f8552` et vérifié par SHA-256. Les licences du moteur, de ses dépendances et de Liquid AI sont incluses dans `licenses/`.

## État Android

Le code Android possède la capture via MediaProjection, une ouverture par un appui et les deux choix. L’autorisation de partage d’écran reste une interface système obligatoire. Le service d’assistance est isolé des moteurs de jeu. L’analyse d’image a commencé sur le téléphone ARM64 avec Mario Party 4, sans validation d’une réponse finale. Les essais physiques sont suspendus jusqu’à disponibilité annoncée par l’utilisateur.

La recherche web et le masquage automatique hors du jeu décrits ci-dessus concernent actuellement **Windows**. Ils ne sont pas encore raccordés à l’interface Android. Ne pas présenter cette préversion comme leur validation Android.

## Validation et limites

Tests hors ligne : `dotnet run --project tests/CubeShelf.Assistance.Tests -c Release`. Ils vérifient intégrité, annulation, limites RGB, filtrage des guides et erreurs réseau. `--web-smoke` vérifie les API réelles. `--vision-smoke` charge réellement le modèle et différencie deux images de couleurs distinctes.

`--web-vision-smoke` utilise une capture de test du menu carte mémoire de Mario Party 4 ; il exige une courte instruction pertinente et rejette les anciennes pages sans rapport. Variables : `DECOMPSHELF_AI_MODEL_DIR`, `DECOMPSHELF_AI_NATIVE`, `DECOMPSHELF_TEST_RGB`, `DECOMPSHELF_TEST_WIDTH`, `DECOMPSHELF_TEST_HEIGHT`.

Les tests Avalonia vérifient les deux choix, l’absence d’ouverture hors session de jeu, l’annulation, la dictée simulée et une capture à la demande sans saisie. La capture Windows réelle au-dessus d’un jeu et l’audio réel restent à valider. Le plein écran exclusif peut masquer la superposition ou produire une capture noire : préférer le mode fenêtré sans bordure. Aucune nouvelle release n’a été publiée sur GitHub.
