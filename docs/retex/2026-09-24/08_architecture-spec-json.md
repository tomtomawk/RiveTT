# 08 — Proposition d'architecture : concevoir en JSON, valider, construire

## 1. Problème

Aujourd'hui l'agent écrit du C# qui dessine directement dans Revit. Deux métiers sont mélangés :
- **concevoir** un plan (distribution, dimensions, conformité) ;
- **construire** dans Revit (types, hôtes, altitudes, points d'insertion, transactions).

Conséquences observées : chaque erreur de conception coûte une reconstruction complète ; chaque erreur de construction (familles, altitudes) se répète à chaque script ; l'utilisateur relit le plan une fois qu'il existe dans Revit.

## 2. Options

| Option | Principe | Pour | Contre |
|---|---|---|---|
| A. Statu quo + outils par lots | Garder `send_code`, ajouter les lots (02 § 3.2) | Peu de développement | Conception toujours validée après construction |
| B. Spécification JSON + validateur + constructeur | L'agent produit un JSON, un validateur applique les règles, un outil construit | Itérations de conception gratuites ; construction fiable ; charte exécutable | Schéma à définir et maintenir |
| C. Bibliothèque de plans types paramétriques | Plans T1–T4 agence, adaptés par paramètres | Qualité architecturale maîtrisée | Moins de souplesse, bibliothèque à constituer |

**Recommandation : B, alimentée progressivement par C.** Les plans types agence deviennent des JSON de référence que l'agent adapte ; le validateur protège la qualité ; le constructeur rend la pose dans Revit déterministe.

## 3. Flux

```
Programme ─► Agent : spec JSON ─► validate_spec (règles 05) ─► rapport
                    ▲                                   │
                    └──────── corrections ◄─────────────┘
                                 │ validé + schéma relu par l'architecte
                                 ▼
                        build_from_spec (RiveTT, 1 transaction, best effort)
                                 ▼
                 capture_view + list_warnings + validate_dwelling (sur le modèle)
```

## 4. Schéma JSON (ébauche)

```jsonc
{
  "project": { "levels": { "RDC": 0, "R+1": 2890, "R+2": 5610, "R+3": 8330 } },
  "dwellings": [
    {
      "id": "A2", "level": "R+1", "typology": "T2", "building": "A",
      "rooms": [
        { "name": "Entrée", "polygon": [[8090,6830],[10400,6830],[10400,9000],[8090,9000]] },
        { "name": "Séjour / Cuisine", "polygon": [[10400,6830],[14200,6830],[14200,11620],[10400,11620]] }
      ],
      "partitions": [
        { "type": "CLO_Distribution_10", "p0": [8000,9000], "p1": [10400,9000] },
        { "type": "CLO_Distribution_7",  "p0": [14200,6740], "p1": [14200,11620] }
      ],
      "separations": [ { "p0": [10400,6830], "p1": [10400,9000] } ],
      "doors": [
        { "family": "PTE_Porte palière", "type": "PP93x220 16", "at": [9000,6740] },
        { "family": "PTE_Porte simple", "type": "PP 93x204", "at": [9200,9000] }
      ],
      "equipment": [
        { "family": "ELC_ETEL", "type": "ETEL", "hostAt": [9950,9000], "facing": [0,-1] },
        { "family": "SAN_WC", "type": "Suspendu PMR", "room": "Sde", "rotation": 270, "against": ["E","S"] },
        { "family": "MOB_Lit", "type": "140 x 190 cm_Gabarit PMR 90 x 120 x 90 cm", "room": "Chambre", "rotation": 270, "against": ["E"] }
      ]
    }
  ],
  "commons": { "corridors": [...], "shafts": [...], "stairs": [...], "lifts": [...] }
}
```

Principes :
- Coordonnées en mm, polygones de pièces explicites (le validateur calcule surfaces, largeurs, cercles libres).
- L'équipement est exprimé par pièce + mur d'appui : le constructeur applique les règles de 06.
- Les identifiants de règles de 05 (`RÈGLE_…`) sont la référence commune du validateur et de la checklist.

## 5. Outils RiveTT correspondants

| Outil | Rôle |
|---|---|
| `validate_spec(json)` | Contrôles géométriques sans Revit (peut tourner côté serveur, en Python ou JS) |
| `render_spec(json)` | Image du schéma (SVG/PNG) pour relecture humaine avant construction |
| `build_from_spec(json, mode)` | Construction dans une transaction ; retour : ids par clé, échecs, avertissements |
| `extract_spec(level, dwelling)` | Inverse : lire un logement existant dans Revit et produire son JSON (permet de réutiliser un plan type d'un projet réel) |
| `validate_dwelling` | Mêmes règles, sur le modèle construit |

`extract_spec` est le moyen le plus rapide de constituer la bibliothèque de plans types : extraire des logements de projets livrés validés.

## 6. Étapes de mise en œuvre

1. Figer le schéma JSON sur un T2 et un T3.
2. Écrire `validate_spec` avec 8 règles : entrée ouverte, gabarit d'entrée, ETEL, cercle Sde, lit PMR, largeur de dégagement, ratio circulation, surfaces minimales.
3. `build_from_spec` pour murs, portes, pièces, séparations ; équipement ensuite (dépend de `describe_family`).
4. `extract_spec` sur 3 à 5 logements de projets agence → premiers plans types.
5. Brancher `capture_view` en fin de construction.
