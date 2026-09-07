# Configurer Claude ou ChatGPT Desktop

Dans l'installateur RiveTT, cochez l'application que vous utilisez. Vous pouvez
cocher les deux. Les cases sont décochées au départ ; la documentation est toujours
installée. Quittez complètement votre application d'IA avant l'installation, puis
rouvrez-la après. Aucun droit administrateur n'est nécessaire.

## Configurer pour Claude (config + skill)

L'installateur déclare la connexion Revit dans la configuration de Claude Desktop,
en conservant les autres réglages et serveurs. Il prépare aussi un ZIP contenant
le skill RiveTT et toutes ses références.

| Élément | Emplacement Windows |
|---|---|
| Configuration Claude | %APPDATA%\Claude\claude_desktop_config.json |
| Skill prêt à importer | %LOCALAPPDATA%\RiveTT\integrations\Claude\rivett.zip |

Après l'installation, ouvrez **Personnaliser → Skills → + → Importer un skill**
(Customize → Skills → + → Create skill → Upload a skill selon la langue/version).
Choisissez rivett.zip au chemin ci-dessus, puis activez le skill. Lors d'une mise à
jour, remplacez également le skill importé : recréer le ZIP local ne met pas à jour
la copie de votre compte Claude.

Le skill de Claude Desktop s'importe dans l'application. Copier un fichier dans
un dossier local de Claude Code ne l'active pas dans les conversations Claude.
La page finale indique donc « prêt à importer », jamais « activé ». Si les skills
sont désactivés par votre organisation, leur activation appartient à son administrateur.
La désinstallation de RiveTT ne retire pas le skill déjà importé dans votre compte ;
vous pouvez le supprimer dans la même page Skills.

## Configurer pour ChatGPT (config + skill)

Cette option vise **l'application ChatGPT Desktop avec son moteur local**, partagé
avec Codex. Elle ne configure pas le site ChatGPT dans un navigateur.
L'installateur déclare la connexion Revit et installe le skill personnel avec ses
références. Rouvrez ChatGPT ; si un skill a été désactivé dans vos réglages, réactivez-le.

| Élément | Emplacement Windows |
|---|---|
| Configuration ChatGPT Desktop | %USERPROFILE%\.codex\config.toml |
| Skill personnel, nouvelle installation | %USERPROFILE%\.agents\skills\rivett\SKILL.md |
| Références du skill | %USERPROFILE%\.agents\skills\rivett\references\ |

Si CODEX_HOME est défini, la configuration est dans CODEX_HOME\config.toml.
Si un ancien skill RiveTT existe déjà dans CODEX_HOME\skills\rivett (par défaut
%USERPROFILE%\.codex\skills\rivett), l'installateur le met à jour sur place pour
éviter un doublon. La page finale affiche le dossier réellement utilisé.

Sur le poste de Thomas, les chemins par défaut sont :

- Claude : C:\Users\theba\AppData\Roaming\Claude\claude_desktop_config.json
- ZIP Claude : C:\Users\theba\AppData\Local\RiveTT\integrations\Claude\rivett.zip
- ChatGPT : C:\Users\theba\.codex\config.toml
- Nouveau skill ChatGPT : C:\Users\theba\.agents\skills\rivett\SKILL.md
- Ancien skill ChatGPT conservé s'il existe : C:\Users\theba\.codex\skills\rivett\SKILL.md

## Vérifier la connexion

La page finale distingue le résultat de la configuration et celui du skill. Si
l'application n'est pas détectée, lancez-la une première fois puis relancez la
configuration avec l'installateur. Un skill présent ne prouve pas que la connexion
Revit est configurée.

Ouvrez Revit et demandez à votre assistant de vérifier les capacités RiveTT.
Vérifiez les versions du plugin et du serveur, le document actif, puis le verrou.
Seul le bouton Écriture du ruban Revit autorise les modifications.

Les configurations existantes sont sauvegardées avec le suffixe .bak-rivett avant
modification. Les journaux de configuration sont dans %LOCALAPPDATA%\RiveTT\.
Ne modifiez pas les copies internes MSIX de Claude à la main : le helper préserve
la configuration existante et son éventuel lien avec la copie utilisée par l'application.

Sources vérifiées le 7 septembre 2026 :
[configuration MCP OpenAI](https://learn.chatgpt.com/docs/extend/mcp?surface=cli),
[skills locaux OpenAI](https://learn.chatgpt.com/docs/build-skills),
[import des skills Claude](https://support.claude.com/en/articles/12512180-use-skills-in-claude),
[configuration locale Claude](https://modelcontextprotocol.io/docs/2026-07-28/develop/connect-local-servers).
