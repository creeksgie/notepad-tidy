# Format binaire du TabState de Notepad (Windows 11)

Rétro-ingénierie complète, validée empiriquement sur 106 onglets réels
le 2026-08-10. Tout ce qui est marqué ✅ a été vérifié sur des données
réelles, pas déduit.

## 1. Emplacement

```
%LOCALAPPDATA%\Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\
├── TabState\
│   ├── {guid}.bin          ← un onglet (contenu réel)
│   ├── {guid}.0.bin        ← enregistrement d'état, séquence 0
│   ├── {guid}.1.bin        ← enregistrement d'état, séquence 1
│   └── {guid}.bin.bak      ← sauvegarde ponctuelle de Notepad
└── WindowState\
    ├── {guid}.0.bin        ← index des onglets, séquence 0
    └── {guid}.1.bin        ← index des onglets, séquence 1
```

Notepad 11 est une application Store en bac à sable
(`Microsoft.WindowsNotepad_8wekyb3d8bbwe`). **Aucune API d'extension, aucun
point de plugin.** Un « mod » au sens propre est impossible : la seule voie
est un outil externe qui manipule ces fichiers pendant que Notepad est fermé.

## 2. Rien n'est chiffré ni obfusqué ✅

Le contenu est du **UTF-16LE brut**, précédé d'un en-tête binaire mince et
suivi d'un CRC. Aucune compression, aucun chiffrement, aucune obfuscation.

## 3. Verrouillage ✅

Tant que Notepad tourne, il maintient un **handle exclusif** sur chaque
`.bin` d'onglet ouvert. Conséquences :

- Pour **lire** pendant que Notepad tourne : ouvrir en `FileShare.ReadWrite`.
  Un `File.ReadAllBytes()` classique échoue avec `IOException`.
- Pour **écrire** : Notepad doit être fermé. Il maintient l'état en mémoire
  et réécrit les fichiers à sa guise ; toute écriture concurrente est perdue.

Notepad n'écrit pas « à la fermeture » : il écrit **en continu** pendant la
frappe. La fermeture ne fait que flusher et relâcher les verrous.

## 4. Structure d'un onglet `{guid}.bin`

| Offset | Taille | Contenu |
|--------|--------|---------|
| 0 | 2 | Magic `4E 50` = `"NP"` |
| 2 | 1 | Numéro de séquence (`00` sur les fichiers principaux) |
| 3 | 1 | **Flag** : `00` = note volante, `01` = onglet lié à un fichier |
| 4 | 1 | Inconnu, vaut `01` sur tous les échantillons |
| 5 | varint | Position du curseur — début de sélection |
| … | varint | Position du curseur — fin de sélection |
| … | 3 | `01 00 00` |
| … | 1 | **Compteur N** — observé à `02` ou `03` |
| … | N | N octets valant `01` |
| … | varint | **Longueur du contenu, en caractères UTF-16** |
| … | 2×n | Le texte, UTF-16LE, sans BOM |
| L-5 | 1 | Marqueur, vaut `01` |
| L-4 | 4 | **CRC32, big-endian** |

### Le flag de l'octet 3

- `00` → note volante, jamais sauvegardée. **C'est ce que l'outil traite.**
- `01` → l'onglet reflète un vrai fichier sur disque. **Ne jamais toucher.**
  L'en-tête contient alors un chemin, et le layout ci-dessus ne s'applique pas.

Sur le corpus de référence : 100 notes volantes, 6 liées à un fichier.

### Varints

Encodage LEB128 non signé, 7 bits utiles par octet, bit de poids fort =
« il y a une suite ».

```
0x0A            → 10
0xDF 0x02       → 0x5F | (0x02 << 7) = 95 + 256 = 351
```

**Piège majeur** : la taille du varint varie avec la valeur, donc
**l'en-tête n'a pas une taille fixe**. Sur le corpus de référence :

| Taille du varint de longueur | Nombre de fichiers |
|---|---|
| 1 octet | 33 |
| 2 octets | 64 |
| 3 octets | 3 |

Conséquences :

1. Le texte commence à l'offset **15, 18, ou davantage** selon les cas.
2. **Cet offset peut être impair.** Décoder tout le buffer en UTF-16 depuis
   l'offset 0 pour y chercher du texte produit du charabia sur ces
   fichiers-là — il faut parser l'en-tête proprement.
3. Faire franchir à une note le seuil de 127 ou 16383 caractères fait
   grossir l'en-tête d'un octet et **décale tout le bloc de texte**. Il n'y a
   pas de patch en place : on réécrit le fichier entier.

## 5. Le CRC32 ✅

C'est la pièce qui décide si Notepad accepte ou jette le fichier.

```
Algorithme  : CRC32 standard (zlib)
Polynôme    : 0xEDB88320  (réfléchi)
Init        : 0xFFFFFFFF
XOR final   : 0xFFFFFFFF
Plage       : octets [3 .. L-5]  — on saute le magic et l'octet de séquence
Stockage    : BIG-ENDIAN sur les 4 derniers octets
```

**Validé sur 106 fichiers sur 106, zéro échec.**

Le piège qui coûte une soirée : le CRC est stocké en big-endian alors que
tout le reste du format (varints, UTF-16) est little-endian.

Trouvé par force brute sur l'espace {polynôme réfléchi/normal} ×
{init 0 / 0xFFFFFFFF} × {xor final 0 / 0xFFFFFFFF} × {offset de départ 0..8}
× {LE, BE}, avec exigence de match simultané sur 3 fichiers de tailles
différentes. Une seule combinaison survit.

## 6. Les enregistrements d'état `.0.bin` / `.1.bin` ⚠️

Ce **ne sont pas des sauvegardes**. Ce sont des enregistrements de 22 octets
qui rejouent les métadonnées de l'onglet :

```
4E 50 00 0E 00 D5 05 DF 02 DF 02 01 00 00 03 01 01 01 | E7 71 0F 2A
      ^^          ^^^^^ ^^^^^ ^^^^^ ^^^^^^^^^^^^^^^^^   ^^^^^^^^^^^
      seq         ?     351   351   même bloc config     CRC
```

`DF 02` = 351 = **exactement la longueur du contenu principal**.

Les deux fichiers alternent via l'octet 2 (séquence `00` / `01`), en double
buffering, pour qu'une écriture interrompue ne corrompe jamais l'état.

**Impact critique** : si on réécrit le `.bin` avec une longueur différente
sans traiter ces enregistrements, ils continuent d'annoncer l'ancienne
longueur et contredisent le contenu.

Traitement retenu et validé : **les supprimer**. Notepad n'a pas bronché et
les recrée à la prochaine modification.

## 7. Le `WindowState` — à ne pas toucher ✅

`WindowState\{guid}.{0,1}.bin` contient la **liste ordonnée des GUID
d'onglets**, à raison de 16 octets bruts par GUID (`Guid.ToByteArray()`),
précédée d'un en-tête d'une centaine d'octets.

Vérifié : les 106 GUID d'onglets présents sur disque étaient tous
référencés dans ce fichier.

Son checksum **ne suit pas** le schéma de la section 5 — aucun offset de
départ de 0 à 6 ne matche. Il reste non résolu.

**Ce n'est pas grave, parce qu'on n'en a pas besoin.** Test décisif du
2026-08-10 :

> Suppression d'un `.bin` d'onglet en laissant son GUID orphelin dans le
> WindowState, puis relance de Notepad.
>
> Résultat : Notepad démarre normalement, **n'affiche aucun onglet fantôme**,
> ne plante pas, ne recrée pas le fichier, et ne se plaint pas. Les 105
> autres onglets sont intacts. Confirmé visuellement dans la barre d'onglets.

D'où la stratégie de l'outil : **recycler des GUID d'onglets existants**
comme conteneurs thématiques. Leur GUID est déjà indexé, donc on ne touche
jamais au WindowState.

## 8. Écriture — validée de bout en bout ✅

Test du 2026-08-10 : fusion d'une note de 12 caractères dans une note de
351 caractères.

1. Parse des deux onglets
2. Concaténation avec séparateur → 399 caractères
3. Génération d'un `.bin` complet de 821 octets (en-tête + varints + texte
   + marqueur + CRC recalculé)
4. Écriture dans le `.bin` de la cible, suppression de la source,
   suppression des enregistrements d'état de la cible
5. Relance de Notepad

**Résultat : Notepad a accepté le fichier, l'a relu, et l'a réécrit sans
rien changer — 821 octets, CRC toujours valide, contenu intact.** Il ne fait
aucune distinction entre un fichier qu'il a produit et un fichier forgé.

C'est la preuve que la boucle complète de l'outil est réalisable.

## 9. La variante à compteur — résolue ✅

Une première version du parseur traitait `01 00 00 03 01 01 01` comme un
bloc constant de 7 octets. Résultat : **83 fichiers sur 100 se vérifiaient,
17 échouaient**.

La cause : ce bloc n'est pas de taille fixe. L'octet en 4ᵉ position est un
**compteur**, suivi d'exactement N octets.

```
Compteur 03 :  01 00 00 03 01 01 01     (7 octets)
Compteur 02 :  01 00 00 02 01 01        (6 octets)
```

Supposer 7 octets décale d'un octet sur les fichiers à compteur `02`, et
tout le reste du parsing part de travers.

Vérification après correction : **100 fichiers sur 100, zéro écart, zéro CRC
invalide.** Le format est intégralement couvert.

La signification du compteur reste inconnue (nombre de champs optionnels ?
version d'un sous-bloc ?), mais elle n'est pas nécessaire : il suffit de le
lire pour sauter le bon nombre d'octets.

**Règle de sécurité maintenue** : le writer refuse de toucher tout fichier
dont le layout ne se vérifie pas à l'octet près. Si une future mise à jour de
Notepad introduit une variante inconnue, l'outil s'abstient au lieu de
détruire. Un fichier mal réécrit, c'est une note perdue définitivement — ces
notes n'ont, par définition, aucune copie ailleurs.

## 10. Fragilité dans le temps

Microsoft met Notepad à jour fréquemment et peut changer ce format sans
préavis ni documentation. L'outil doit :

- vérifier le magic `NP` et l'octet de version à chaque exécution ;
- refuser de travailler si quoi que ce soit dévie, plutôt que de parser à
  l'aveugle ;
- sauvegarder `LocalState` avant toute écriture.
