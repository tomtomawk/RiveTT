# Références RiveTT

Source canonique commune : l'opérateur humain et l'agent lisent les mêmes fichiers.
`../SKILL.md` indique à l'agent lesquels charger selon la demande ; ce tableau sert à
les retrouver à la main.

| Document | Ce qu'il couvre |
|---|---|
| `conduite-de-session.md` | Ouvrir une session, lire une réponse, choisir l'outil le moins coûteux, documents et groupes |
| `ecritures.md` | Verrou d'écriture, `dryRun`, paramètres, escalade vers `send_code_to_revit` |
| `production.md` | Contrôle de santé, conflits, vues et annotations |
| `workflows-ifc.md` | Lier, reconstruire en natif, exporter de l'IFC |
| `signatures-des-outils.md` | Où trouver la signature exacte d'un outil |
| `inventaire-des-outils.md` | **Généré.** Tous les outils publiés, leur nature, leur `dryRun`, leurs défauts connus |

`inventaire-des-outils.md` n'est jamais édité à la main : il sort de
`tools/audit-tool-surface.py`, qui croise les attributs `[McpServerTool]` du serveur
et les classes `ICortexTool` du runtime. C'est la seule liste d'outils exacte par
construction — les autres documents la citent, ils ne la recopient pas.

Ces six fichiers en remplacent onze. Les précédents étaient hérités du projet source
et pour la plupart restés en italien, avec trois tables recopiées d'un fichier à
l'autre et une affirmation fausse sur `Document.EditFamily` qui a longtemps bloqué
les outils de famille.

## Références de développement

Non installées avec le produit : écrire un outil C# ou produire une release relève du
dépôt, pas du poste de travail. Elles sont sous `docs/references/` dans un clone :
`nouvel-outil.md`, `contrats-et-erreurs.md`, `outils-dynamiques-et-capacites.md`,
`securite-et-audit.md`, `checklist-release.md`.
