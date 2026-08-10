# Journal des tests empiriques

Tout ce qui est affirmé dans `FORMAT.md` vient d'un test listé ici, sur un
corpus réel de 106 onglets accumulés entre juillet 2024 et août 2026.

## 2026-08-10 — Verrouillage des fichiers

**Question** : peut-on lire les onglets pendant que Notepad tourne ?

`File.ReadAllBytes()` échoue avec `IOException` — Notepad garde un handle
exclusif sur chaque onglet ouvert, en permanence.

**Conclusion** : lire via `FileStream` avec `FileShare.ReadWrite`. Écrire
exige que Notepad soit fermé.

**Corollaire** : Notepad n'écrit pas « à la fermeture », il écrit en continu
pendant la frappe. La fermeture ne fait que flusher et libérer les verrous.

## 2026-08-10 — Recherche du CRC

**Question** : quel checksum protège les fichiers ?

CRC32 zlib et CRC32C sur les plages évidentes : aucun match. Force brute sur
{polynôme réfléchi, normal} × {init 0, 0xFFFFFFFF} × {xor final 0,
0xFFFFFFFF} × {offset de départ 0..8} × {octets exclus en fin 0..4} ×
{little-endian, big-endian}, avec exigence de match simultané sur 3 fichiers
de tailles différentes.

**Une seule combinaison survit** :

```
poly=0xEDB88320 réfléchi, init=0xFFFFFFFF, xor=0xFFFFFFFF,
plage=[3 .. L-5], stockage BIG-ENDIAN
```

Validation élargie : **106 fichiers sur 106**.

Ce qui faisait échouer les premiers essais : le stockage big-endian, alors
que tout le reste du format est little-endian.

## 2026-08-10 — GUID orphelin dans le WindowState

**Question** : que fait Notepad si un `.bin` référencé dans le WindowState
n'existe plus ? C'est la question qui décidait s'il fallait reverser le
format du WindowState.

**Protocole** : sauvegarde intégrale, création d'une note de test, fermeture
de Notepad, suppression de son `.bin` seul, GUID laissé dans le WindowState,
relance.

**Résultat** :

| Observation | Résultat |
|---|---|
| Notepad démarre | oui, aucun crash, aucun avertissement |
| Onglet fantôme dans la barre | **aucun** — confirmé visuellement |
| Les 105 autres onglets | intacts |
| `.bin` recréé | non |
| WindowState réécrit | non |

**Conséquence majeure** : on n'a jamais besoin de toucher au WindowState. En
recyclant des GUID d'onglets existants comme conteneurs thématiques, leur
GUID reste indexé, et supprimer les notes absorbées est sans effet de bord.

Le checksum du WindowState reste non résolu — et sans importance.

## 2026-08-10 — Les fichiers `.0.bin` / `.1.bin`

**Question** : sauvegardes ou autre chose ?

Autre chose. Enregistrements de 22 octets qui rejouent les métadonnées :

```
4E 50 00 0E 00 D5 05 DF 02 DF 02 01 00 00 03 01 01 01 | E7 71 0F 2A
      ^^                ^^^^^ ^^^^^                      ^^^^^^^^^^^
      séquence          351   351                        CRC
```

`351` = exactement la longueur du contenu principal. Les deux fichiers
alternent (octet 2 = séquence) en double buffering.

**Impact** : réécrire un onglet sans les traiter laisse des enregistrements
qui annoncent l'ancienne longueur. Les supprimer fonctionne — Notepad les
recrée à la modification suivante.

## 2026-08-10 — Écriture de bout en bout

**Question** : Notepad accepte-t-il un fichier qu'il n'a pas produit ?

**Protocole** : fusion d'une note de 12 caractères dans une note de 351.
Génération d'un `.bin` complet de 821 octets, écriture dans le conteneur,
suppression de la source et des enregistrements d'état, relance.

**Résultat** : Notepad démarre, affiche le contenu fusionné, et **réécrit le
fichier sans rien changer** — 821 octets, CRC toujours valide, contenu
intact. Aucune distinction entre un fichier produit par lui et un fichier
forgé.

Auto-vérification appliquée avant écriture : le writer régénère puis
reparse son propre produit et compare au texte attendu. Un writer qui se
trompe est détecté sur une copie mémoire, jamais sur les données réelles.

## 2026-08-10 — La variante à compteur

**Symptôme** : 83 fichiers sur 100 vérifiaient
`offset_texte + 2×longueur + 5 == taille_fichier`. 17 non, avec des écarts
allant de -161 à +14412 octets — donc pas une corruption, un décalage de
parsing.

**Cause** : le bloc traité comme constant sur 7 octets
(`01 00 00 03 01 01 01`) est en réalité `01 00 00`, puis un **compteur**,
puis N octets. Les 17 fichiers portent le compteur `02` et ne font donc que
6 octets.

Vérification manuelle avant correction :

| Fichier | Compteur | Longueur | Calcul | Taille réelle |
|---|---|---|---|---|
| `359ad331` | 02 | 404 c | 17 + 808 + 5 = 830 | 830 ✓ |
| `3b5ab54c` | 02 | 372 c | 17 + 744 + 5 = 766 | 766 ✓ |
| `50b9beb8` | 02 | 1593 c | 17 + 3186 + 5 = 3208 | 3208 ✓ |

**Après correction : 100/100, zéro écart, zéro CRC invalide.**

La signification du compteur reste inconnue, mais le lire suffit.

## Corpus de référence

106 onglets, juillet 2024 → août 2026, ~171 000 caractères.

| Catégorie | Nombre |
|---|---|
| Notes volantes (flag `00`) | 100 |
| Onglets adossés à un fichier (flag `01`) | 6 |
| Varint de longueur sur 1 octet | 33 |
| … sur 2 octets | 64 |
| … sur 3 octets | 3 |

Le corpus n'est pas dans le dépôt et ne doit jamais y entrer.
