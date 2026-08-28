# Contrôle du modèle, vues et annotations

**Portée :** vérifier la santé d'une maquette, et produire tags, couleurs, cotes et
vues sans buter sur les contraintes de vue active.
**Sources :** outils de santé, de conflits, de vues et d'annotations RiveTT.
**Vérifié le :** 2026-08-28

## Contrôle rapide du matin

Séquence canonique, 500 à 800 jetons :

1. `check_model_health` avec `compact: true` ;
2. `list_warnings` avec `maxWarnings: 10` — **jamais le défaut**, qui en rend 500 ;
3. facultatif : `detect_clashes` sur un couple de disciplines.

Puis s'arrêter. Ne pas enchaîner sur de l'authoring dans la même session : le
contrôle qualité et la production ont des rythmes et des coûts différents.

| Besoin | `maxWarnings` |
|---|---|
| Contrôle rapide | 10 |
| Analyse d'une catégorie | 50 |
| Export complet | sans limite, assumé |

## Conflits

| Besoin | Outil | Coût |
|---|---|---|
| Compte et liste d'identifiants | `detect_clashes` | 400 à 600 jetons |
| Revue visuelle 3D avec boîte de coupe | `show_clashes` | 800 et plus |

Préciser les deux catégories exactes. Sur un modèle d'architecture, les poteaux sont
`OST_Columns`, **pas** `OST_StructuralColumns` — l'erreur rend un résultat vide qui
ressemble à « aucun conflit ».

## count_lines_per_view

**Cet outil peut faire tomber le serveur sur un modèle de plus de 300 vues.**

- Ne jamais le lancer en parallèle d'un autre outil.
- Toujours avec `threshold >= 20`.
- Sur une grosse maquette, envisager de ne pas l'appeler du tout.

## Vues et annotations

### Ce qui dépend de la vue active

`tag_rooms`, `tag_walls` et `color_elements` n'opèrent que sur la **vue active de
Revit**, et seulement si elle contient des éléments visibles de la catégorie visée.
Vérifier avec `get_current_view_info` avant, systématiquement.

`color_elements` échoue sur une feuille ou une page de garde : basculer d'abord sur
un plan ou une vue 3D. Il attend par ailleurs des noms de catégorie **localisés**,
donc dépendants de la langue de Revit.

### Cotes

Le Z passé à `create_dimensions` doit correspondre **exactement** à l'altitude du
niveau. La prendre dans `get_project_info`, jamais à la main : une approximation ne
produit pas une cote approximative, elle produit une cote qui n'accroche rien.

### Nommer une vue

Ces caractères sont refusés par Revit dans un nom de vue :

```
:  \  /  {  }  [  ]  |
```

Pour un horodatage, `HH-mm-ss` — jamais `HH:mm:ss`.

## À éviter

- `workflow_model_audit` pour un contrôle rapide : 3000 jetons contre 500 à 800.
- `list_warnings` sans `maxWarnings`.
- `count_lines_per_view` en parallèle, ou sans `threshold`.
- Mélanger contrôle qualité et production dans une même longue session.
- Poser des tags ou des couleurs sans avoir vérifié la vue active.
- Approximer le Z d'une cote.
- Mettre `:` ou `/` dans un nom de vue.
