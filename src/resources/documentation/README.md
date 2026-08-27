# Documentation RiveTT

Installée avec le connecteur, dans `%LOCALAPPDATA%\RiveTT\documentation`.

C'est la **source canonique commune** : l'opérateur humain et l'agent lisent les
mêmes fichiers. Il n'existe pas une documentation utilisateur d'un côté et une
documentation IA de l'autre — c'est ce qui les empêche d'expliquer la même
opération de deux façons différentes.

## Par où commencer

| Document | Quand |
|---|---|
| [USER_GUIDE.md](USER_GUIDE.md) | Installation, verrou d'écriture, gestes courants |
| [IFC.md](IFC.md) | Export, liaison, reconstruction d'IFC en éléments natifs |
| [SECURITY.md](SECURITY.md) | Ce que le connecteur s'autorise, et ce qui l'en empêche |
| [references/](references/) | Le détail, opération par opération |
| [references/inventaire-des-outils.md](references/inventaire-des-outils.md) | Tous les outils publiés, leur nature, leurs défauts connus |

## Pour l'agent

[SKILL.md](SKILL.md) est le routeur : il dit quelles références charger selon la
demande, et porte les règles qui valent en permanence — verrou d'écriture,
`dryRun` avant toute écriture, résolution des noms de paramètres, unités.

Le fichier est **présent** sur le poste, pas **actif**. L'activer dans Codex CLI
demande de le copier dans le dossier personnel des skills, ce que l'installateur
propose par une case à cocher décochée par défaut : cela modifie la configuration
d'un autre produit. À la main :

```powershell
$dest = "$env:USERPROFILE\.codex\skills\rivett"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$env:LOCALAPPDATA\RiveTT\documentation\*" $dest -Recurse -Force
```

Claude Code lit `SKILL.md` directement ; `agents/openai.yaml` ne sert qu'à Codex.

## Ce qui n'est pas ici

Écrire un outil C#, changer le contrat de réponse, produire une release : ces
références vivent dans le dépôt, sous `docs/references/`, et ne sont pas
installées. Elles ne servent à rien sur un poste de production.
