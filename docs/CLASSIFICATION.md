# Thèmes auto-créés, zéro administration

Réponse courte : **oui, c'est faisable**, et c'est même le mode de
fonctionnement naturel. Tu ne déclares jamais de thème. Ils émergent des
notes, se nomment tout seuls, et de nouveaux apparaissent quand tu commences
à parler d'autre chose.

## Le principe

On ne classe pas dans des catégories prédéfinies. On mesure la **proximité
sémantique** entre notes, on laisse les paquets se former, et on nomme
chaque paquet d'après ce qu'il contient.

```
note → embedding (vecteur de 384 dims) → clustering → thème → nom auto
```

## Les deux régimes

### Démarrage à froid — une seule fois

Tes ~100 notes existantes n'ont aucune structure. On les embedde toutes, on
lance un clustering global, et on obtient N thèmes d'un coup.

Algorithme : **HDBSCAN**, ou clustering agglomératif avec seuil de distance.
L'avantage de HDBSCAN ici est qu'il ne demande pas de fixer N à l'avance —
il le découvre — et qu'il sait dire « cette note ne ressemble à rien »
plutôt que de la forcer dans un paquet.

Ces orphelines vont dans un thème `Divers` qui sert de purgatoire : dès que
2 ou 3 notes s'y ressemblent, elles s'en détachent en thème propre.

### Régime permanent — à chaque fermeture

Tu fermes Notepad avec 1 à 3 notes nouvelles. Pas de reclustering complet :

```
pour chaque note nouvelle :
    v = embedding(note)
    sim, thème = plus proche centroïde de thème
    si sim > SEUIL_RATTACHEMENT (~0.55) :
        append dans ce thème, mise à jour du centroïde
    sinon :
        garder de côté
si ≥ 3 notes gardées de côté se ressemblent entre elles :
    créer un nouveau thème, le nommer
sinon :
    les envoyer dans Divers
```

C'est ça, le zéro admin : **la création de thème est une conséquence
automatique du seuil**, pas une action de ta part.

Un reclustering complet une fois par mois (ou sur commande) rattrape la
dérive des centroïdes.

## Le nommage automatique

C'est la partie qui décide si l'outil paraît intelligent ou bête.

Technique : **c-TF-IDF**, celle de BERTopic. On concatène toutes les notes
d'un cluster en un seul document, et on cherche les termes qui distinguent
ce document des autres clusters. Les 2-3 premiers termes deviennent le nom.

Sur tes vraies données, ça devrait produire des choses comme
`project-promo`, `gamma-beta`, `postgres-docker`.

Deux points d'attention :

- **Tes notes sont en français.** Il faut une liste de stopwords français,
  sinon tes thèmes s'appelleront « pour-que-avec ».
- Un nom auto-généré reste modifiable à la main. Si tu renommes un thème,
  l'outil garde ton nom et ne le régénère plus — c'est la seule
  « administration » possible, et elle est facultative.

## Le modèle d'embedding

**Il doit être multilingue.** Tes notes sont en français ; les modèles
anglophones par défaut (`all-MiniLM-L6-v2`) dégradent nettement.

| Modèle | Dims | Taille ONNX | Remarque |
|---|---|---|---|
| `paraphrase-multilingual-MiniLM-L12-v2` | 384 | ~120 Mo | bon rapport qualité/taille |
| `multilingual-e5-small` | 384 | ~130 Mo | souvent meilleur en retrieval |

Les deux tournent en ONNX Runtime, sur CPU, sans réseau. Les notes ne
sortent jamais de la machine — ce qui compte vu qu'il y a une clé API et des
brouillons clients dedans.

## Coût en performance

C'est le critère qui a guidé tout le design. Le point clé :

> Le modèle n'est **jamais chargé en permanence**. Le service résident ne
> fait que dormir sur un handle noyau. Le modèle est chargé à la fermeture
> de Notepad, utilisé pendant ~200 ms, puis déchargé.

| Phase | RAM | CPU |
|---|---|---|
| Au repos (99,99 % du temps) | ~15 Mo | **0 %** |
| Rafale à la fermeture de Notepad | ~150 Mo pendant 1-2 s | 1 cœur brièvement |
| Reclustering complet (mensuel) | ~200 Mo pendant ~10 s | 1 cœur |

Embedder 3 notes coûte quelques dizaines de millisecondes. Le vrai coût est
le chargement du modèle, d'où le fait de ne le faire qu'en rafale.

## Ce qui va mal marcher, honnêtement

- **Les notes très courtes.** Une note qui contient juste une URL n'a
  presque aucun signal sémantique. Prévoir un chemin dédié : extraire le
  domaine, grouper les liens à part.
- **Les notes fourre-tout.** Une note qui parle de trois sujets ira dans un
  seul cluster. Découper une note en sections avant d'embedder est une piste,
  mais ça complique beaucoup — à garder pour plus tard.
- **Le seuil de rattachement.** 0,55 est un point de départ, pas une
  vérité. Il se règle en regardant les résultats sur tes 100 notes. Prévoir
  une commande `--dry-run` qui affiche le classement proposé sans rien
  écrire : c'est l'outil de réglage, et c'est aussi le filet de sécurité.

## Ordre de travail conseillé

1. `--dry-run` qui affiche les clusters proposés sur tes 100 notes, **sans
   jamais écrire**. C'est ici que tu passeras le plus de temps, et c'est
   sans risque.
2. Réglage du seuil et du nommage jusqu'à ce que le classement te paraisse
   juste.
3. Seulement ensuite, brancher l'écriture.
