# Sécurité de RiveTT

RiveTT est une intégration locale pour Revit 2027. Le serveur MCP échange
sur `stdio`; le relais avec Revit utilise un named pipe Windows créé avec
`CurrentUserOnly`. Aucun port TCP n'est ouvert.

## Limites de confiance

- Le client MCP et Revit doivent s'exécuter sous le même compte Windows.
- Les appels Revit passent par `ExternalEvent`, puis par les transactions de
  l'API Revit.
- Chaque appel est consigné dans
  `%LOCALAPPDATA%\RiveTT\audit.jsonl`.
- Il n'y a ni télémétrie, ni compte, ni licence, ni mise à jour automatique.

## Écritures

Les outils dédiés restent préférables. Utiliser `dryRun: true` avant une
écriture quand le schéma le propose, puis contrôler le résultat après
exécution. Le mode automatique permanent supprime les boîtes de confirmation,
mais pas les transactions, le journal d'audit ou la validation des entrées.

## Exécution C#

`send_code_to_revit` est un dernier recours. `CodeSandbox` bloque notamment
les accès fichiers et réseau, la création de processus, le registre, l'interop
native et l'émission dynamique. Toute modification de ce chemin doit conserver
les tests de durcissement présents dans `RiveTT.Tests/Security`.

## Données locales

Les fichiers de session, scripts temporaires et journaux sont stockés sous
`%LOCALAPPDATA%\RiveTT`. La désinstallation du programme conserve le
journal par défaut afin d'éviter une suppression de données implicite.
