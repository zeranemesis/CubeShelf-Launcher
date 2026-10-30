# Android — barre d’état du téléphone

Version 0.19.0-rc.3, Android code 52. La candidate rc.2 publiée reste intacte.

- Heure en haut à droite, au format 12/24 h choisi dans Android et dans le fuseau du téléphone.
- Batterie : jauge, pourcentage, charge, couleur faible sous 16 %. Une mesure indisponible apparaît « — % », jamais comme une fausse batterie vide.
- Connexion active : Wi-Fi, réseau mobile 2G/3G/4G/5G, Ethernet, VPN ou hors ligne. Android peut masquer le sous-type ; le libellé reste alors « Mobile ». La 5G NSA peut être signalée comme LTE. Aucune déduction à partir du débit ou de l’absence de Wi-Fi.
- Barre extérieure aux fenêtres de la bibliothèque : elle reste visible à l’accueil des consoles, dans la bibliothèque, les téléchargements, l’inspection et l’animation d’ouverture. L’heure est plus grande que les indicateurs secondaires. Les zones sûres automatiques d’Avalonia sont conservées.
- Actualisation toutes les cinq secondes au premier plan ; arrêt à la pause/détachement et rafraîchissement immédiat à la reprise. Aucun service de fond, aucune nouvelle permission ni dépendance.

Le réseau utilise les capacités de la connexion par défaut, puis le [sous-type NetworkInfo](https://developer.android.com/reference/android/net/NetworkInfo#getSubtype()) pour la génération mobile. Cette ancienne API reste disponible avec la permission ACCESS_NETWORK_STATE existante ; les API téléphoniques plus récentes demanderaient un accès supplémentaire au téléphone. La batterie lit la dernière [notification système ACTION_BATTERY_CHANGED](https://developer.android.com/reference/android/content/Intent#ACTION_BATTERY_CHANGED), sans inscrire de récepteur permanent. Le format de l’heure vient d’Android.

## Contrôles

- Suite Mobile : affichage initial, reprise avec nouvelle heure/charge/connexion, valeur de batterie invalide, arrêt du minuteur hors premier plan, libellés réseau connus/inconnus et maintien de l’affichage au-dessus des fenêtres. Cadrages 320×700, 390×844, 844×390 et zone sûre simulée. Captures portrait/paysage examinées.
- Compilation Android locale réussie sans avertissement pour le fournisseur natif ; suite complète d’interface bureau réussie pour le calcul de l’espace de l’animation partagé.
- L’animation d’ouverture utilise l’espace de son conteneur plutôt que la hauteur totale de l’écran, pour conserver le bouton Retour sous la nouvelle barre.

Le téléphone réel n’a pas été utilisé : précision du sous-type fourni par son opérateur et comportement autour de son encoche restent à vérifier sur l’appareil. Cette modification ne rend pas la barre visible au-dessus des jeux externes ; elle concerne l’interface DecompShelf.
