---
name: rivett
description: À utiliser pour toute opération RiveTT — automatisation d'une maquette Revit 2026.5+ ou 2027, workflows d'outils MCP, développement du plugin ou du serveur C#. Couvre les écritures sûres, l'escalade vers send_code_to_revit, l'IFC, la création d'outils, l'audit de sécurité et les contrôles de build .NET 10.
---

# Routeur RiveTT

Ne charger que les références utiles à la demande en cours. Elles sont dans
`references/`, à côté de ce fichier, et ce sont **les mêmes documents que lit
l'opérateur humain** : il n'existe pas de copie séparée pour l'IA qui pourrait
diverger.

## Règles permanentes

1. Pour toute intervention sur une maquette, lire d'abord
   `references/conduite-de-session.md`.
2. Pour toute écriture, lire `references/ecritures.md` ; prévisualiser avec
   `dryRun: true` dès que l'outil le propose, et ne jamais supposer le verrou ouvert.
   Ce que l'outil propose se lit dans `execution.supportsDryRun`, jamais supposé :
   un `dryRun` demandé à un outil qui n'en a pas est **refusé** (`InvalidInput`),
   l'outil n'est pas lancé, la maquette n'est pas touchée.
3. `send_code_to_revit` est un dernier recours : lire la section « Escalader » de
   `references/ecritures.md` avant de le proposer.
4. Le développement C# cible Revit 2026.5+ et 2027, .NET 10, x64 uniquement. Une
   compilation donnée tourne contre **une** version de Revit : le plugin est rebâti
   par cible, il n'est pas multi-cible.
5. Ne pas toucher à l'isolation par canal nommé, au journal d'audit, aux erreurs
   structurées ni au bac à sable Roslyn.
6. `execution.toolReadOnly` classe **l'outil qui a répondu**, ce n'est pas un verrou de
   session. Le verrou de session, c'est `execution.writesAllowed` : chaque session
   Revit démarre en lecture seule, et seul un humain la déverrouille depuis le panneau
   RiveTT du ruban (onglet Compléments). Sur un `PermissionDenied` avec
   `writesAllowed: false`, s'arrêter et demander le déverrouillage — aucun outil, et
   aucun `dryRun`, ne passe outre. `execution.cached: true` signale une réponse de
   cache.
7. `execution.documentTitle` et `execution.revitProcessId` nomment la maquette et
   l'instance de Revit réellement atteintes. Avec deux Revit ouverts, le serveur joint
   le plus récemment démarré sans le demander : vérifier ces champs avant la première
   écriture, et à chaque fois qu'ils changent en cours de session.
8. `execution.versionMismatch` signifie que RiveTT n'est mis à jour qu'à moitié : la
   liste de commandes visible est celle de la moitié la plus ancienne, donc un outil
   renommé répond « not found » et un paramètre récent est ignoré en silence.
   S'arrêter et le dire à l'utilisateur **dans ses termes** : quitter complètement
   l'application d'IA utilisée avec Revit — quitter, pas seulement fermer la fenêtre —
   relancer l'installateur, la rouvrir. Redémarrer Revit ne sert à rien. Ne pas
   expliquer la séparation plugin/serveur si on ne le demande pas.
9. Les noms de paramètres se résolvent en anglais **ou** dans la langue du document. Un
   nom non résolu revient dans `unresolvedParameterNames` (ou `skippedFields[].reason`),
   jamais sous forme de valeur vide : une colonne vide **sans** ce signalement est une
   vraie donnée vide.
10. Les valeurs numériques portent `unit` et `internalValue`. Ne jamais lire un nombre
   nu comme s'il était dans les unités du projet.
11. Préférer `categoryBic` (`OST_*`) au libellé localisé : Revit FR nomme la catégorie
    des vues portées « Fenêtres », comme les fenêtres.
12. Les types système — murs, sols, garde-corps, escaliers, cartouches — ne sont pas
    des familles chargeables : les énumérer avec `list_system_types`, les dupliquer
    avec `duplicate_system_type`.

## Routage

| Demande | Référence |
|---|---|
| Ouvrir une session, trouver des éléments, choisir un outil | `conduite-de-session.md` |
| Toute écriture : paramètres, création, suppression, scripts | `ecritures.md` |
| Santé du modèle, avertissements, conflits, vues, annotations | `production.md` |
| IFC | `workflows-ifc.md` |
| Quels outils existent, et lesquels ont un défaut connu | `inventaire-des-outils.md` |
| Signature exacte d'un outil | `signatures-des-outils.md` |

La plupart des demandes tiennent dans un seul fichier. `conduite-de-session.md`
d'abord sur une maquette qu'on n'a pas encore touchée cette session, puis celui qui
correspond à la tâche.

`references/index.md` les liste tous. `references/inventaire-des-outils.md` est généré
depuis le code : c'est la seule liste d'outils exhaustive, et aucune liste tenue à la
main ne doit lui être préférée.

## Travailler sur RiveTT lui-même

Écrire un outil C#, changer le contrat de réponse ou produire une release relève du
dépôt, pas du poste de travail : ces références **ne sont pas installées**. Dans un
clone, elles sont sous `docs/references/` — `nouvel-outil.md`,
`contrats-et-erreurs.md`, `outils-dynamiques-et-capacites.md`, `securite-et-audit.md`,
`checklist-release.md` — et `AGENTS.md` à la racine se lit en premier.
