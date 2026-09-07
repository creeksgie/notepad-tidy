# notepad-tidy

Range automatiquement les onglets du Notepad de Windows 11.

Notepad garde tes notes sans que tu aies à les sauvegarder — c'est son
meilleur atout, et c'est aussi pour ça qu'on finit avec cent onglets en
vrac. `notepad-tidy` tourne en fond, attend que tu fermes Notepad, regroupe
les notes par thème, et les fusionne dans quelques onglets — **qui restent
des onglets non sauvegardés**. Tu relances Notepad, tu retrouves tes notes
rangées, avec le même confort qu'avant.

Aucune catégorie à déclarer. Un thème se nomme en l'écrivant, une fois.

*An English version of this documentation is available:
[README.md](README.md).*

## État du projet

La rétro-ingénierie du format est **terminée et validée** sur un corpus réel
de 106 onglets. La lecture, l'écriture et la fusion fonctionnent de bout en
bout, et le service de fond range à la fermeture.

| Composant | État |
|---|---|
| Parseur du format TabState | ✅ 100/100 onglets, zéro écart |
| CRC32 (lecture + génération) | ✅ validé sur 106 fichiers |
| Writer — Notepad accepte nos fichiers | ✅ vérifié en conditions réelles |
| Fusion de notes | ✅ CLI fonctionnelle |
| Découverte des thèmes et rangement | ✅ 80/82 notes rangées sur le corpus de référence |
| Watcher événementiel | ✅ service de fond, 0 % CPU au repos |
| Installateur | ✅ `install.ps1`, sans droits administrateur |

## Pourquoi ce n'est pas un plugin

Notepad 11 est une application Store en bac à sable. Aucune API d'extension,
aucun point de greffe. La seule voie praticable est un outil externe qui
manipule les fichiers d'état pendant que Notepad est fermé.

Tout le format est documenté dans [`docs/FORMAT.fr.md`](docs/FORMAT.fr.md) —
c'est le cœur du projet et le résultat le plus réutilisable.

## Installation

Prérequis : Windows 11 et le SDK .NET 10. Il n'y a pas encore de binaire
publié, donc on compile les deux exécutables et on laisse l'installateur les
poser :

```powershell
dotnet publish src/NotepadTidy.Cli/NotepadTidy.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o dist
dotnet publish src/NotepadTidy.Service/NotepadTidy.Service.csproj -c Release -r win-x64 -p:PublishAot=true -o dist
.\install.ps1 -Source .\dist
```

`install.ps1` copie les binaires dans `%LOCALAPPDATA%\notepad-tidy\bin` et
enregistre une tâche planifiée qui démarre le service à l'ouverture de
session. **Aucun droit administrateur n'est nécessaire** — le service ne
touche jamais qu'au dossier Notepad de l'utilisateur courant.

Pour désinstaller : `.\uninstall.ps1`. Tes notes restent exactement en l'état.

Le guide complet — prérequis NativeAOT, surveillance du service, lecture du
journal, restauration d'une sauvegarde — est dans
[`docs/INSTALL.md`](docs/INSTALL.md).

## Démarrage

Pour travailler sur le code plutôt que l'installer :

```bash
dotnet build -c Release
dotnet test
```

### Travailler sur une copie, pas sur tes vraies notes

C'est le mode recommandé pour tout essai. `--path` fait travailler l'outil
sur une copie isolée du TabState :

```bash
nptidy backup C:\bac-a-sable          # duplique TabState et WindowState
nptidy stats  --path C:\bac-a-sable
nptidy merge  <cible> <src> --path C:\bac-a-sable --apply
```

Notepad ne connaît que son propre dossier : il ne peut rien écraser dans le
bac à sable, et rien de ce que tu y fais ne remonte vers tes vraies notes. Tu
peux donc y travailler **pendant que Notepad est ouvert**.

`--path` n'est pas une porte dérobée : si le chemin fourni désigne le vrai
dossier de Notepad — quelles que soient la casse, un séparateur final ou un
détour par `..` — les protections liées au processus restent actives. C'est
testé.

### Commandes

```bash
nptidy stats                        # état de santé du TabState — lecture seule
nptidy analyze                      # mesure les signaux de ton corpus
nptidy themes                       # regroupe les notes par thème
nptidy list                         # liste les onglets avec un aperçu
nptidy dump <guid>                  # contenu et en-tête d'un onglet
nptidy backup <dossier>
nptidy merge <cible> <src...>       # simulation, n'écrit rien
nptidy merge <cible> <src> --apply
```

Sortie typique de `stats` :

```
Onglets : 106
  Ok               100   réécriture sûre
  FileBacked         6   vrais fichiers — ne pas toucher
Exploitables : 100 onglets, 171 093 caractères
```

## Comment le rangement fonctionne

### 1. Titre explicite

Une note dont la première ligne commence par `#` déclare son thème.

```
# project beta
le lien pour les testeurs
```

→ thème `project-beta`, créé à la volée.

C'est le seul mécanisme à la fois sans administration et **totalement
indépendant de la langue** : la catégorie n'est pas devinée, elle est
déclarée, avec les mots de l'utilisateur. `# Rechnungen`, `# 仕事のメモ` et
`# работа` se comportent à l'identique.

Le marqueur est exigé plutôt que deviné. Mesuré sur un corpus réel : deviner
un titre à la forme de la première ligne donne 11 % de détections dont
**100 % de faux positifs** — codes couleur, numéros de port, horaires, mots
de passe. Un caractère lève toute l'ambiguïté.

### 2. Rangement par mention

Une note sans titre est rangée dans un thème que l'utilisateur a **déjà
déclaré ailleurs**. Un nouveau thème n'est jamais inventé.

Volontairement conservateur, parce qu'un mauvais rangement coûte plus cher
qu'une note non rangée :

- comparaison par jetons entiers, donc `play` ne matche pas dans `display` ;
- un thème en plusieurs mots ne compte que si tous ses jetons apparaissent à
  la suite ;
- les thèmes de moins de quatre caractères sont ignorés comme mentions ;
- refus net quand deux thèmes sont cités de façon comparable, avec une marge
  exigée de 2×.

### 3. Pas de création automatique de thèmes

Abandonnée volontairement. Elle inventait des noms que l'utilisateur n'avait
pas choisis — précisément le problème de départ. `CorpusProfile` et
`NoteSignals` restent comme diagnostics derrière `nptidy analyze`, pas comme
socle du rangement.

## Sécurité des données

Ces notes n'existent nulle part ailleurs — c'est tout le principe des
onglets non sauvegardés. Le projet applique donc :

- **Sauvegarde avant toute écriture**, non désactivable.
- **Refus par défaut** : magic inattendu, layout qui ne tombe pas juste à
  l'octet près, ou CRC invalide → on ne touche pas au fichier.
- **Simulation de première classe** : `merge` sans `--apply` n'ouvre aucun
  fichier en écriture.
- **Les onglets adossés à un vrai fichier ne sont jamais modifiés.**
- **Abandon si Notepad tourne**, avant et pendant l'opération.

Fais une copie de
`%LOCALAPPDATA%\Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState`
avant tes premiers essais, et développe sur cette copie.

> ⚠️ Les sauvegardes sous `%LOCALAPPDATA%\notepad-tidy\backups` contiennent le
> **texte intégral de tes notes**, et `uninstall.ps1` les conserve
> délibérément — c'est le seul retour en arrière possible après une fusion non
> désirée. Passe `-RemoveBackups` pour t'en débarrasser.

> ⚠️ Ne commite jamais de vraies notes dans ce dépôt. Elles contiennent
> facilement des clés d'API, des brouillons de mails et des liens privés.
> Le `.gitignore` bloque les `*.bin`, mais la vigilance reste manuelle.

## Tests

124 tests, dont un tiers sur `TabMerger` — le seul code qui supprime des
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
- **Les entrées malformées.** Tous les préfixes tronqués d'un onglet valide,
  un varint qui ne se termine jamais, une longueur déclarée absurde — chacun
  est refusé proprement au lieu de faire planter la passe.
- **Les frontières de varint** (127/128, 16383/16384), où l'en-tête change de
  taille et décale tout le bloc de texte.
- **L'indépendance à la langue** : mots vides et mots d'ouverture sont dérivés
  de corpus français, anglais et allemand par le même code.

## Documentation

| Fichier | Contenu |
|---|---|
| [`docs/FORMAT.fr.md`](docs/FORMAT.fr.md) | Le format binaire, intégralement |
| [`docs/ARCHITECTURE.fr.md`](docs/ARCHITECTURE.fr.md) | Design événementiel, idempotence, sécurité |
| [`docs/CLASSIFICATION.fr.md`](docs/CLASSIFICATION.fr.md) | Le rangement, et pourquoi il ne dépend d'aucune langue |
| [`docs/FINDINGS.fr.md`](docs/FINDINGS.fr.md) | Journal des tests empiriques |
| [`docs/INSTALL.md`](docs/INSTALL.md) | Installer, surveiller et retirer le service |

## Performance

L'outil est destiné à tourner en permanence, donc il ne doit rien coûter au
repos. **Aucun polling** : on attend sur un handle noyau
(`WaitForSingleObject` sur le processus Notepad), le thread dort à 0 % CPU
et le noyau le réveille à la fermeture.

| Phase | RAM | CPU |
|---|---|---|
| Au repos | ~15 Mo | 0 % |
| Rafale à la fermeture | ~150 Mo, 1-2 s | 1 cœur brièvement |

## Contribuer

Rapports de bugs et pull requests sont les bienvenus — voir
[`CONTRIBUTING.md`](CONTRIBUTING.md). Si tu trouves un moyen de faire abîmer
des notes par l'outil, lis d'abord [`SECURITY.md`](SECURITY.md).

## Licence

MIT — voir [`LICENSE`](LICENSE).
