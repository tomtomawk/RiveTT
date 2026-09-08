# RiveTT 0.5.3

## Installation des clients IA

- Le skill RiveTT conserve une source unique :
  `src/resources/documentation/SKILL.md`.
- L'installateur copie le guide général sous
  `%LOCALAPPDATA%\RiveTT\documentation\skills_RiveTT.md` et affiche ce chemin sur
  la page finale.
- La copie personnelle auto-détectée par ChatGPT et Codex est installée sous
  `%USERPROFILE%\.agents\skills\rivett\SKILL.md`, sans changement de nom.
- La connexion Claude Desktop prend également en charge les installations MSIX qui
  n'exposent que leur chemin de configuration sous `%LOCALAPPDATA%\Packages`.
- La préparation de l'ancien ZIP Claude et la copie source qui lui était dédiée sont
  supprimées.

## Signature des livrables

- La création d'un installateur est refusée sans certificat Authenticode dont le
  signataire est exactement `Thomas Thébault`.
- Les binaires RiveTT, le script d'enregistrement MCP, l'installateur et son
  désinstalleur sont signés et horodatés avec le même certificat.
- Le workflow de release GitHub importe le PFX depuis les secrets
  `RIVETT_SIGN_PFX_BASE64` et `RIVETT_SIGN_PFX_PASSWORD`. Une clé absente ou une
  identité différente arrête la release avant publication.
- Les builds de développement sans installateur restent possibles avec
  `-SkipInstaller -SkipSigning`.

## Recette

- Ajout du rapport de recette Revit 2027 du 7 septembre 2026, couvrant les blocs à
  valeur métier et consignant explicitement les limites non testées.

## Vérification

- Builds et tests automatisés exécutés pour Revit 2026 et 2027.
- Installateur 0.5.3 construit localement et signature Authenticode contrôlée avant
  la création du tag `v0.5.3`.
