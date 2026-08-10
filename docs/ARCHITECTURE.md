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

## Extinction brutale, plantage, mise à jour Windows

Si le PC s'éteint sans que Notepad soit fermé, Windows tue Notepad — et tue
aussi notre service, souvent avant qu'il ait pu travailler. Des notes restent
donc à ranger, sans que personne n'ait rien remarqué.

**Il n'y a pas de cas particulier à écrire.** Le fichier d'état rend le
travail en attente détectable de façon déclarative : est en attente tout ce
qui, dans `TabState`, ne correspond pas à l'état enregistré — peu importe la
raison, extinction brutale, plantage ou service arrêté à la main.

Le service exécute donc une **passe de réconciliation à chaque démarrage**,
avant d'entrer en attente :

```
démarrage du service
  → Notepad tourne-t-il déjà ?
        oui  → ne rien faire, entrer directement en attente de sa fermeture
        non  → passe de réconciliation, puis attente
```

Le premier cas n'est pas théorique : Windows peut relancer Notepad tout seul
au démarrage de session, via « Rouvrir les applications au redémarrage ». La
réconciliation obéit donc exactement aux mêmes gardes que le reste — si
Notepad est là, on s'abstient.

Deux conséquences agréables :

- Le service est **redémarrable à tout moment** sans rien perdre.
- Le chemin de démarrage et le chemin nominal sont le **même code**, donc
  testés par les mêmes tests.

Une tuerie brutale de Notepad peut aussi laisser ses enregistrements d'état
`.0/.1` désynchronisés du `.bin`. C'est déjà couvert : tout fichier dont le
layout ne tombe pas juste est refusé plutôt que réécrit.

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
NotepadTidy.Core/
    Crc32              le checksum, isolé et sans état
    TabRecord          parse + build d'un onglet — pur, aucune I/O
    TabPaths           résolution de chemins — pur, aucune I/O
    TabStore           lecture et énumération du TabState
    TabMerger          la fusion : le seul code qui détruit des données
    IO/                ITabFileSystem, INotepadGuard + implémentations Windows
NotepadTidy.Classify/   embeddings ONNX, clustering, nommage        (à faire)
NotepadTidy.Service/    watcher événementiel, orchestration          (à faire)
NotepadTidy.Cli/        stats, list, dump, backup, merge
```

`Core` n'a aucune dépendance externe. Les trois quarts de son code
(`Crc32`, `TabRecord`, `TabPaths`) sont des fonctions pures, testables sans
disque ni Windows.

### Pourquoi les interfaces `IO/`

Elles n'existent pas par principe SOLID mais pour une raison précise :
`TabMerger` supprime des fichiers irrécupérables. Derrière
`ITabFileSystem` et `INotepadGuard`, ses tests tournent en mémoire et
peuvent vérifier qu'un refus n'a **rien** écrit ni supprimé — ce qui est
impossible avec des appels statiques à `File` et `Process`.

C'est la seule abstraction du projet. Le reste est concret par défaut.

### Résistance aux mises à jour de Notepad

L'octet 4 de l'en-tête vaut `01` sur la totalité du corpus de référence et sa
signification est inconnue. Il sert de sentinelle de version : toute autre
valeur bascule le fichier en `TabStatus.UnknownVariant`, et tout ce qui n'est
pas `TabStatus.Ok` est refusé à l'écriture.

Autrement dit, si une mise à jour Windows modifie le format, le comportement
par défaut est **l'abstention**, jamais un parsing décalé qui détruirait des
notes. Ajouter le support d'une nouvelle variante consistera à étendre la
détection, pas à réécrire le parseur.

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
