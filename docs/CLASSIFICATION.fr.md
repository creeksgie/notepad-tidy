# Classement des notes — zéro admin, zéro coût, zéro dépendance à la langue

Ce document a été réécrit après mesure sur un corpus réel de 99 notes. Les
chiffres qu'il cite viennent de `nptidy analyze`, pas d'une intuition.

## La contrainte qui a tout décidé

Un outil qui ne marche que sur des notes françaises ne vaut rien pour
quelqu'un d'autre. Or la première approche tentée — listes de salutations, de
mots vides, de jours de la semaine — était du sur-mesure pour un seul
utilisateur. Pire, l'heuristique « un mot capitalisé en milieu de phrase est
un nom propre » s'effondre en allemand, où tous les noms communs sont
capitalisés, et n'a aucun sens en japonais.

Tout ce qui suit est donc classé selon un seul critère : **est-ce que ça
marche chez quelqu'un d'autre, dans une autre langue ?**

## Niveau 1 — le titre explicite (mécanisme principal)

Une note dont la première ligne commence par `#` déclare son thème.

```
# project beta
le lien pour les testeurs, à renvoyer à Camille
```

→ thème `project-beta`, créé à la volée s'il n'existe pas.

C'est le seul mécanisme à la fois sans administration et **totalement
indépendant de la langue** : la catégorie n'est pas devinée, elle est
déclarée, dans les mots de l'utilisateur. `# Rechnungen`, `# 仕事のメモ` et
`# работа` fonctionnent à l'identique — c'est couvert par les tests.

### Pourquoi un marqueur explicite et pas une devinette

Il est tentant de traiter toute première ligne courte comme un titre. Mesuré
sur le corpus réel : **11 % des notes ont une première ligne d'allure
« titre », et la totalité sont des faux positifs** — codes couleur
(`2e343b 4a5568 1c1f26`), numéros de port (`port 5432`), horaires
(`mercredi 10h 14h`), et deux mots de passe.

Chacun aurait créé un thème-poubelle. Le marqueur ramène l'ambiguïté à zéro
pour le coût d'un caractère. `NoteHeading.GuessImplicit` conserve la
devinette, mais uniquement pour mesurer — jamais pour classer.

## Niveau 2 — rattachement par similarité (repli)

Une note sans `#` doit quand même aller quelque part. On la compare aux
thèmes existants et on la rattache au plus proche, au-dessus d'un seuil.

La similarité se calcule en **TF-IDF cosinus** sur le contenu accumulé de
chaque thème. Le point important : la pondération TF-IDF ne connaît aucune
langue. Elle mesure une distribution, pas un vocabulaire.

Deux réglages appris à la mesure :

- **TF sous-linéaire** (`1 + log(tf)`). Sans ça, une note qui répète 40 fois
  le même mot écrase tout le classement — c'est exactement ce qui s'est passé
  au premier essai, où le top était `fr`, `ville`, `24`, `00`.
- **Rejet des tokens purement numériques.** Horaires, dates et montants sont
  omniprésents dans des notes personnelles et n'identifient aucun sujet.

Sous le seuil, la note va dans `inbox`. Pas de thème inventé au hasard.

## Niveau 3 — les statistiques dérivées du corpus

`CorpusProfile` déduit des données ce que les autres outils codent en dur :

| Déduit | Comment | Vérifié |
|---|---|---|
| Mots vides | tout token présent dans plus de 25 % des notes | sur le corpus réel : `le, de, pas, la, et, pour, les, un, en, je…` |
| Mots d'ouverture | premiers mots récurrents des notes | trouve `salut` en français et `hi` en anglais, **même code** |
| Rareté d'un terme | IDF | fait remonter `streamelements`, enterre `faut` |

Aucune de ces listes n'est écrite nulle part. Sur un corpus anglais, la même
classe produit `the`, `and`, `to`. Les tests le vérifient sur du français, de
l'anglais et de l'allemand.

C'est ce qui rend le repli du niveau 2 transposable.

## Ce qui reste spécifique au français, et qui est optionnel

`NoteSignals` détecte les brouillons de message par salutation, les formules
de politesse et les noms propres par capitalisation. **Ces signaux sont des
compléments, jamais le socle.** Ils sont documentés comme tels dans le code.

Seuls les URL et les domaines y sont réellement universels — et ils sont
étonnamment informatifs : le domaine d'une plateforme, d'un hébergeur ou
d'un fournisseur identifie un projet sans ambiguïté.

## Ce que ça donne sur le corpus réel

| Signal | Notes | Part |
|---|---|---|
| Brouillon de message | 12 | 12,1 % |
| Registre épistolaire | 15 | 15,2 % |
| Marqueurs techniques | 15 | 15,2 % |
| Contient une URL | 12 | 12,1 % |
| Moins de 80 caractères | 13 | 13,1 % |

Longueur médiane 705 caractères, maximum 25 181.

## Le démarrage à froid

Les 100 notes existantes n'ont pas de `#`. Elles passeront donc toutes par le
repli, ce qui produira un classement approximatif — c'est attendu, et sans
gravité : le classement s'améliore à mesure que les notes récentes portent un
titre.

Trois options, à trancher à l'usage :

1. Tout envoyer dans `inbox` et titrer au fil de l'eau.
2. Lancer le niveau 2 sur tout le corpus et corriger à la main.
3. Une passe interactive unique, note par note.

L'option 1 est la plus honnête : elle n'invente rien.

## Coût

Aucun modèle, aucune API, aucune connexion. TF-IDF sur 100 notes coûte
quelques millisecondes. Le classement est **gratuit et hors ligne par
construction**, pas par configuration.

Un modèle d'embedding local reste possible plus tard pour améliorer le repli
du niveau 2, mais il n'est pas nécessaire — et il coûterait 150 Mo de RAM en
rafale pour un gain incertain sur des notes courtes.

## Ordre de travail

1. `nptidy analyze` pour regarder son propre corpus.
2. Niveau 1 : titres explicites. Simple, exact, sans risque.
3. Niveau 2 : repli TF-IDF, réglé en `--dry-run` jusqu'à ce que le classement
   paraisse juste.
4. Seulement ensuite, brancher l'écriture.
