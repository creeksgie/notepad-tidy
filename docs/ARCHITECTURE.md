# Architecture

## Contrainte directrice

L'outil tourne en permanence sur la machine. **Il ne doit rien coûter au
repos.** Tout le design découle de là.

## Zéro polling

Une boucle qui vérifie toutes les N secondes si Notepad tourne, c'est du
gaspillage. Windows sait réveiller un processus sur événement.

```
        ┌─────────────────────────────────────────┐
        │  Notepad ne tourne pas                  │
        │  → attente sur FileSystemWatcher        │
        │    (TabState\)                          │
        │  → 0 % CPU, le noyau nous réveille      │
        └──────────────┬──────────────────────────┘
                       │ Notepad démarre / écrit
                       ▼
        ┌─────────────────────────────────────────┐
        │  Notepad tourne                         │
        │  → OpenProcess(SYNCHRONIZE)             │
        │  → WaitForSingleObject(handle, INFINITE)│
        │  → 0 % CPU, le thread dort              │
        └──────────────┬──────────────────────────┘
                       │ Notepad se ferme
                       ▼
        ┌─────────────────────────────────────────┐
        │  Rafale de travail (1-2 s)              │
        │  → attendre la libération des verrous   │
        │  → sauvegarder, parser, classer, écrire │
        │  → décharger le modèle                  │
        └──────────────┬──────────────────────────┘
                       │
                       └──> retour à l'état d'attente
```

`WaitForSingleObject` sur un handle de processus est **exactement** le
« call to action à la fermeture » recherché : le thread est descendu par
l'ordonnanceur, il ne consomme aucun cycle, et il est réveillé à la
milliseconde où le processus meurt.

En .NET : `Process.WaitForExit()` / `Process.Exited`, qui s'appuient
dessus. Attention, Notepad peut avoir plusieurs processus — il faut attendre
la disparition du **dernier**.

## Séquence à la fermeture

```
1. Notepad a disparu
2. Attendre ~500 ms que les handles retombent
3. VÉRIFIER qu'aucun processus Notepad n'est revenu     ← sinon, abandon
4. Sauvegarder LocalState\ (rotation des N dernières)
5. Parser TabState\
      - ignorer les onglets flag=01 (liés à un fichier)
      - ignorer les onglets dont le layout ne se vérifie pas
6. Diff via le fichier d'état : quelles notes sont nouvelles ou modifiées ?
7. Embedder + classer uniquement les nouveautés
8. Pour chaque thème touché :
      - lire le .bin du conteneur
      - append le nouveau contenu
      - réécrire le .bin (varints + CRC recalculés)
      - supprimer les .0.bin / .1.bin du conteneur
9. Supprimer les .bin des notes absorbées
10. Mettre à jour le fichier d'état
```

L'étape 3 n'est pas cosmétique : si tu relances Notepad pendant qu'on
travaille, il reprend la main sur les fichiers et tout se perd.

## Idempotence

Sans état, le cycle 2 refusionne ce que le cycle 1 a déjà fusionné, et le
contenu enfle à chaque ouverture.

Fichier d'état, à côté de la config :

```json
{
  "containers": {
    "28e8c91b-7b1e-4155-a174-f6b1b743dc06": {
      "theme": "project-promo",
      "contentHash": "sha256:…",
      "lastMerged": "2026-08-10T18:56:56Z",
      "centroid": [0.021, -0.114, …]
    }
  },
  "absorbed": ["b9bd4408-d11d-4eab-a4e2-edad2380814e"],
  "modelVersion": "multilingual-e5-small@1"
}
```

Règles :

- Une note dont le GUID est dans `absorbed` et qui réapparaît est ignorée.
- Un conteneur dont le `contentHash` ne correspond plus a été **édité à la
  main** : on append quand même, on ne régénère jamais depuis zéro. Tes
  éditions manuelles sont sacrées.
- Changer de modèle d'embedding invalide tous les centroïdes → reclustering
  complet forcé.

## Sécurité des données

Ces notes n'existent nulle part ailleurs. Les règles :

1. **Sauvegarde avant toute écriture.** Non négociable, non désactivable.
2. **Refus par défaut.** Layout non vérifié, magic inattendu, CRC invalide
   en lecture → on ne touche pas au fichier et on log.
3. **`--dry-run` de première classe.** Doit pouvoir tout afficher sans
   jamais ouvrir un fichier en écriture.
4. **Jamais toucher aux onglets flag=01.** Ce sont de vrais fichiers de
   l'utilisateur.
5. **Abandon si Notepad revient.** À vérifier avant chaque écriture.

## Découpage

```
NotepadTidy.Core/       parseur, writer, CRC — aucune dépendance
NotepadTidy.Classify/   embeddings ONNX, clustering, nommage
NotepadTidy.Service/    watcher événementiel, orchestration
NotepadTidy.Cli/        dump, verify, dry-run, merge manuel
```

`Core` est volontairement sans dépendance et testable seul : c'est la
brique qui peut détruire des données, elle doit être la plus simple possible.

## Publication

```
dotnet publish -c Release -r win-x64 /p:PublishAot=true
```

NativeAOT : un `.exe` autonome, pas de runtime .NET à installer sur la
machine cible, démarrage quasi instantané, empreinte mémoire réduite.

Note : ONNX Runtime ne se prête pas toujours bien à NativeAOT. Si ça coince,
publier `NotepadTidy.Service` en AOT et laisser la partie classification
dans un processus séparé lancé à la demande — ce qui est de toute façon
souhaitable, puisque le modèle ne doit pas rester résident.
