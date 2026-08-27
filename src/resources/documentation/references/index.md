# Références RiveTT

Ces documents sont la source canonique commune : l'opérateur humain et l'agent
lisent les mêmes fichiers. `../SKILL.md` indique à l'agent lesquels charger selon
la demande ; ce tableau sert à les retrouver à la main.

| Document | Ce qu'il couvre |
|---|---|
| `session-et-locale.md` | Premier appel d'une session, détection de la langue du document |
| `choix-des-outils.md` | Retenir l'outil le moins coûteux qui résout la demande |
| `operations-destructives.md` | Prévisualiser avant d'écrire, `dryRun` |
| `parametres.md` | Paramètres : unitaire, en masse, CSV, recopie |
| `sante-du-modele.md` | Contrôles rapides, avertissements, conflits |
| `vues-et-annotations.md` | Étiquettes, couleurs, cotes, gabarits, fenêtres de plan |
| `workflows-ifc.md` | Liaison, reconstruction et export IFC |
| `escalade-send-code-to-revit.md` | Quand un script devient légitime, et à quelles conditions |
| `signatures-des-outils.md` | Où trouver la signature exacte d'un outil |
| `inventaire-des-outils.md` | **Généré.** Tous les outils publiés, leur nature, leur `dryRun`, leurs défauts connus |

`inventaire-des-outils.md` n'est jamais édité à la main : il sort de
`tools/audit-tool-surface.py`, qui croise les attributs `[McpServerTool]` du
serveur et les classes `ICortexTool` du runtime. C'est la seule liste d'outils
exacte par construction.

## Références de développement

Elles ne sont pas installées avec le produit et vivent dans le dépôt, sous
`docs/references/` : `nouvel-outil.md`, `contrats-et-erreurs.md`,
`outils-dynamiques-et-capacites.md`, `securite-et-audit.md`,
`checklist-release.md`. Écrire un outil C# ou produire une release relève du
dépôt, pas du poste de travail.
