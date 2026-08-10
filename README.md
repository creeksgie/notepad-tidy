# notepad-tidy

Range automatiquement les onglets du Notepad de Windows 11.

Notepad garde tes notes sans que tu aies à les sauvegarder — c'est son
meilleur atout, et c'est aussi pour ça qu'on finit avec cent onglets en
vrac. `notepad-tidy` tourne en fond, attend que tu fermes Notepad, regroupe
les notes par thème, et les fusionne dans quelques onglets — **qui restent
des onglets non sauvegardés**. Tu relances Notepad, tu retrouves tes notes
rangées, avec le même confort qu'avant.

Pas de thèmes à déclarer. Ils se créent tout seuls.

## État du projet

La rétro-ingénierie du format est **terminée et validée** sur un corpus réel
de 106 onglets. La lecture, l'écriture et la fusion fonctionnent de bout en
bout. La classification automatique reste à implémenter.

| Composant | État |
|---|---|
| Parseur du format TabState | ✅ 100/100 onglets, zéro écart |
| CRC32 (lecture + génération) | ✅ validé sur 106 fichiers |
| Writer — Notepad accepte nos fichiers | ✅ vérifié en conditions réelles |
| Fusion de notes | ✅ CLI fonctionnelle |
| Watcher événementiel | ⬜ à faire |
| Embeddings + clustering | ⬜ à faire |
| Nommage automatique des thèmes | ⬜ à faire |

## Pourquoi ce n'est pas un plugin

Notepad 11 est une application Store en bac à sable. Aucune API d'extension,
aucun point de greffe. La seule voie praticable est un outil externe qui
manipule les fichiers d'état pendant que Notepad est fermé.

Tout le format est documenté dans [`docs/FORMAT.md`](docs/FORMAT.md) — c'est
le cœur du projet et le résultat le plus réutilisable.

## Démarrage

Prérequis : SDK .NET 10.

```bash
dotnet build -c Release
dotnet test
```

29 tests, dont la moitié sur `TabMerger` — le seul code qui supprime des
fichiers. Il travaille derrière un `ITabFileSystem`, donc ses tests tournent
entièrement en mémoire, sans jamais approcher un vrai TabState.

Ce qui est verrouillé, par ordre d'importance :

- **Les refus.** Notepad qui tourne, Notepad qui revient au milieu de
  l'opération, conteneur adossé à un fichier, source au CRC corrompu,
  variante de format inconnue. Dans chaque cas on vérifie que **rien** n'a
  été écrit ni supprimé.
- **Le tout ou rien.** Une seule source douteuse annule l'opération entière :
  absorber à moitié détruirait des notes sans contrepartie.
- **L'idempotence.** Une deuxième passe ne fait pas enfler le conteneur.
- **Les frontières de varint** (127/128, 16383/16384), où l'en-tête change de
  taille et décale tout le bloc de texte.
- Un vecteur binaire issu d'un onglet réel, comme détecteur de régression du
  format.

```bash
# état de santé du TabState — lecture seule
nptidy stats

# lister les onglets avec un aperçu
nptidy list

# afficher un onglet
nptidy dump <guid>

# sauvegarder avant de jouer
nptidy backup C:\chemin\vers\sauvegarde

# simuler une fusion (n'écrit rien)
nptidy merge <cible> <source1> <source2>

# appliquer pour de vrai
nptidy merge <cible> <source1> --apply
```

Sortie typique de `stats` :

```
Onglets : 106
  Ok               100   réécriture sûre
  FileBacked         6   vrais fichiers — ne pas toucher
Exploitables : 100 onglets, 171 093 caractères
```

## Sécurité des données

Ces notes n'existent nulle part ailleurs — c'est tout le principe des
onglets non sauvegardés. Le projet applique donc :

- **Sauvegarde avant toute écriture**, non désactivable.
- **Refus par défaut** : magic inattendu, layout qui ne tombe pas juste au
  octet près, ou CRC invalide → on ne touche pas au fichier.
- **Simulation de première classe** : `merge` sans `--apply` n'ouvre aucun
  fichier en écriture.
- **Les onglets adossés à un vrai fichier ne sont jamais modifiés.**
- **Abandon si Notepad tourne**, avant et pendant l'opération.

Fais une copie de
`%LOCALAPPDATA%\Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState`
avant tes premiers essais, et développe sur cette copie.

> ⚠️ Ne commite jamais de vraies notes dans ce dépôt. Elles contiennent
> facilement des clés d'API, des brouillons de mails et des liens privés.
> Le `.gitignore` bloque les `*.bin`, mais la vigilance reste manuelle.

## Documentation

| Fichier | Contenu |
|---|---|
| [`docs/FORMAT.md`](docs/FORMAT.md) | Le format binaire, intégralement |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Design événementiel, idempotence, sécurité |
| [`docs/CLASSIFICATION.md`](docs/CLASSIFICATION.md) | Thèmes auto-créés, zéro admin |
| [`docs/FINDINGS.md`](docs/FINDINGS.md) | Journal des tests empiriques |

## Performance

L'outil est destiné à tourner en permanence, donc il ne doit rien coûter au
repos. **Aucun polling** : on attend sur un handle noyau
(`WaitForSingleObject` sur le processus Notepad), le thread dort à 0 % CPU
et le noyau le réveille à la fermeture.

| Phase | RAM | CPU |
|---|---|---|
| Au repos | ~15 Mo | 0 % |
| Rafale à la fermeture | ~150 Mo, 1-2 s | 1 cœur brièvement |

Le modèle d'embedding n'est jamais résident : chargé en rafale, puis
déchargé.

## Licence

MIT.
