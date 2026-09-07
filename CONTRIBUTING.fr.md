# Contribuer

Merci de jeter un œil. Cet outil réécrit des fichiers qui contiennent des
notes n'existant nulle part ailleurs, donc l'exigence sur tout ce qui écrit
sur le disque est volontairement élevée.

*This document is also available in English: [CONTRIBUTING.md](CONTRIBUTING.md).*

## Ne jamais commiter de vraies notes

Les onglets de Notepad contiennent des clés d'API, des brouillons de mails,
des liens privés et des noms de clients. Le `.gitignore` bloque `*.bin`,
`TabState/`, `LocalState/` et les dossiers de sauvegarde, mais c'est un filet,
pas une garantie.

Ça vaut aussi pour les **fixtures de test, les commentaires de code et les
exemples de documentation** : utilise des noms neutres (`project`, `alpha`,
`beta`, `example.com`, Contoso, Fabrikam). Un vrai nom qui atteint un commit
reste dans l'historique même après suppression.

## Compiler

```bash
dotnet build -c Release
dotnet test
```

Prérequis : SDK .NET 10. `NotepadTidy.Core` n'a aucune dépendance externe ni
Windows — il compile et se teste partout. Seuls le CLI et le service touchent
aux chemins et aux processus Windows.

## Travailler sans risque

Ne pointe jamais l'outil sur tes vraies notes pendant le développement.
`--path` le fait travailler sur une copie isolée :

```bash
nptidy backup C:\bac-a-sable
nptidy stats --path C:\bac-a-sable
```

## Ce qu'une pull request doit apporter

- **Des tests pour chaque chemin de refus.** Les tests les plus utiles du
  dépôt sont ceux qui vérifient que rien n'a été écrit et rien supprimé.
  `TabMerger` travaille derrière `ITabFileSystem` précisément pour que ces
  tests tournent en mémoire.
- **Une CI verte.** Build et tests tournent sur `windows-latest` à chaque PR.
- **Un message de commit qui explique le pourquoi.** L'historique existant
  sert de modèle : ce qui a été observé, ce qui a été essayé, ce que la mesure
  a donné. Un message qui paraphrase le diff ne suffit pas.
- **Aucune nouvelle dépendance externe dans `Core`** sans en discuter avant.

## Modifier le format binaire

`docs/FORMAT.md` est le résultat le plus réutilisable de ce projet, et chaque
affirmation qu'il contient s'appuie sur une expérience consignée dans
`docs/FINDINGS.md`. Si une mise à jour de Notepad décale le format :

1. ajoute l'expérience à `FINDINGS.md` — la question, la méthode, les chiffres ;
2. mets `FORMAT.md` à jour ;
3. fais **refuser** la variante inconnue par le parseur, plutôt que deviner.

Refuser est toujours correct. Deviner peut détruire des notes.

## La documentation est bilingue

L'anglais est la langue de base du code, des commentaires et des sorties du
CLI. La documentation existe dans les deux langues : `README.md` /
`README.fr.md`, `docs/X.md` / `docs/X.fr.md`. Quand tu modifies l'une, modifie
l'autre dans le même commit — elles ont déjà divergé.

## Signaler un bug

Ouvre une issue avec tes versions de Windows et de Notepad, la commande
lancée, et ce qu'affiche `nptidy stats`. **Ne colle pas le contenu de tes
notes** — un GUID et un nombre d'octets suffisent.

Pour tout ce qui pourrait abîmer des notes, voir [`SECURITY.md`](SECURITY.md).
