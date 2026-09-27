# Documentation RiveTT — retour d'expérience et évolutions

Issue de la session de test du 23–24/09/2026 : création d'un projet test (plots R+2 / R+3, 14 logements) avec RiveTT 0.5.4 sur Revit 2026, gabarit `club_GABARIT 2026.rte`, piloté par un agent Claude.

## Public

| Lecteur | Fichiers |
|---|---|
| Développeur RiveTT | 02, 03, 04, 08, 09 |
| Agent qui pilote Revit | 07 (à lire en premier), 05, 06, 04 |
| Base documentaire (RAG) | 05, 06, 07 |
| Chef de projet / BIM manager | 01, 09 |

## Fichiers

| Fichier | Contenu |
|---|---|
| `01_retex-session-test.md` | Déroulé, erreurs, temps perdu, analyse des causes |
| `02_rivett-evolutions-outils.md` | Outils à créer, corriger, compléter — spécifications et priorités |
| `03_rivett-capture-view.md` | Spécification du retour image Revit → MCP → agent |
| `04_rivett-bugs-contournements.md` | Bugs RiveTT et pièges de l'API Revit rencontrés, avec contournement |
| `05_rag-plan-logement.md` | Règles de conception des plans de logement, contrôles chiffrés |
| `06_rag-familles-points-insertion.md` | Catalogue des familles du gabarit : origine, orientation, emprise, règles de pose |
| `07_protocole-agent.md` | Méthode de travail de l'agent : phases, carte des outils par tâche, contrôles |
| `08_architecture-spec-json.md` | Proposition d'architecture : conception en JSON, validation, construction déterministe |
| `09_points-oublies.md` | Sujets non abordés en session mais à traiter |

## Conventions

- Unités : mm dans les documents ; l'API Revit travaille en pieds (1 pi = 304,8 mm).
- Repère projet : X vers l'est, Y vers le nord, Z vers le haut.
- « Agent » = modèle de langage qui pilote Revit via le MCP RiveTT.
- Les valeurs relevées (familles, surfaces) valent pour le gabarit 2026 ; à re-relever si une famille change.
