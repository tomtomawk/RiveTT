# RiveTT

MCP local pour **Autodesk Revit 2026.5+ et 2027**, basé sur RevitCortex (licence MIT).

## Principes

- deux cibles, un seul code source : Revit 2026.5+ ou 2027 / .NET 10 / x64,
  sélectionnées au build via `-p:RevitVersion=2026|2027` (voir « Compiler et installer » ci-dessous) ;
- transport local par **Windows Named Pipe**, jamais par port TCP ;
- démarrage automatique de l'add-in avec Revit, sans interrupteur de démarrage ;
- verrou d'écriture au ruban (*Compléments → RiveTT*) : chaque session Revit
  démarre en lecture seule, aucun outil ne peut lever le verrou ;
- mode automatique permanent : aucune boîte d'autorisation ou licence ;
- pas de Power BI, télémétrie, mise à jour automatique ni compte commercial ;
- serveur MCP standard sur `stdio`, nommé `RiveTT`.

Les appels transitent uniquement entre le client MCP, le serveur stdio et le
processus Revit de l'utilisateur courant. Les écritures Revit restent dans des
transactions et sont consignées dans `%LOCALAPPDATA%\RiveTT\audit.jsonl`.

## Fonctions ajoutées

- capacités serveur et contrat d'exécution (`get_server_capabilities`) ;
- création de murs avec aperçu et validation des niveaux/décalages réels ;
- création de portes et fenêtres sur familles réellement présentes dans le projet ;
- garde-corps natifs (`create_railing`) ;
- association d'un mur à son mur hôte, Revit 2027 uniquement (`set_wall_host`) ;
- sélection temporaire stable (`capture_selection`) et scopes bulk explicites ;
- synchronisation localisée via `BuiltInParameter` ;
- recherches paginées avec modes résumé, IDs et détails ;
- duplication transactionnelle d'étage (`duplicate_storey`) ;
- contraintes/attaches de murs et gestion contrôlée des groupes ;
- diagnostics Revit normalisés et audit entrée/sortie ;
- sauvegarde et sauvegarde sous du projet actif (`save_document`,
  `save_as_document`), avec aperçu `dryRun` ;
- énumération des types système (`list_system_types`) : murs, sols, plafonds,
  toits, garde-corps, escaliers, cartouches ;
- lignes de détail, lignes de modèle et séparations de pièces
  (`create_detail_line`, `create_model_line`, `create_room_separation_line`) ;
- pose d'un cartouche sur une feuille existante (`place_title_block`) ;
- création d'un **projet vierge** depuis un gabarit `.rte` (`create_document`) et
  ouverture/activation d'un fichier (`open_document`) ;
- **escaliers** par composant entre deux niveaux, volées droites et paliers
  automatiques (`create_stair`) ;
- édition des membres d'un groupe dans les limites de l'API
  (`edit_group_members`).

## Contrat de réponse

- noms de paramètres résolus en anglais **ou** dans la langue du document
  (`Mark`/`Repère`, `Level`/`Niveau`) ; un nom non résolu est signalé, jamais
  rendu par une valeur vide ;
- valeurs numériques accompagnées de leur unité et de la valeur interne Revit ;
- catégories accompagnées de leur code `OST_*` (`categoryBic`), les libellés
  localisés étant ambigus ;
- `execution.toolReadOnly` classe l'outil, pas la session ;
  `execution.writesAllowed` donne l'état du verrou d'écriture du ruban — faux au
  démarrage de chaque session Revit, et aucun outil ne peut le lever ;
  `execution.cached` signale une réponse issue du cache.

- corrections issues de la campagne de tests :
  [docs/MCP_AGENT_FIXES.md](docs/MCP_AGENT_FIXES.md) ;
- lecture de l'audit de la surface d'outils et ordre de chantier :
  [docs/AUDIT_OUTILS.md](docs/AUDIT_OUTILS.md) ;
- inventaire des 196 outils, effet par effet, avec les défauts probables et les
  capacités API non outillées : [docs/INVENTAIRE_OUTILS.md](docs/INVENTAIRE_OUTILS.md),
  ou la même matière filtrable dans [docs/inventaire.html](docs/inventaire.html).
  Les deux sont générés par `tools/audit-tool-surface.py`.

## Installer

Téléchargez `RiveTT-Setup-<version>.exe` et lancez-le. **Aucun droit administrateur
n'est demandé** : le manifeste de l'installateur est `asInvoker`, Windows n'affiche
donc jamais d'invite UAC.

Il détecte les Revit présents et n'installe que pour ceux-là :

| Revit | Pris en charge |
|---|---|
| 2026.5 et supérieur | oui |
| 2026.0 à 2026.4 | **non** — tourne sur .NET 8, l'installateur le dit et s'arrête |
| 2027.x | oui |

La détection lit la version de `Revit.exe`, pas le registre : la valeur `Version`
du registre garde celle de l'installation d'origine et affiche encore
`26.0.4.409` sur un poste réellement en 2026.5.

Pour préparer un poste où Revit n'est pas encore installé :
`RiveTT-Setup-<version>.exe /REVIT=2026,2027`.

## Compiler

Prérequis : .NET SDK 10, et [Inno Setup 6](https://jrsoftware.org/isdl.php) pour
produire l'installateur (`winget install JRSoftware.InnoSetup --scope user`).

```powershell
cd RiveTT
.\build.ps1                              # les deux cibles Revit + l'installateur
.\build.ps1 -RevitVersion 2027           # une seule cible
.\build.ps1 -SkipInstaller               # binaires seuls, sans Inno Setup
```

Tout ce qui est généré atterrit dans `dist\` (ignoré par git) :

    dist\2026\plugin\   add-in compilé contre Revit 2026.5
    dist\2027\plugin\   add-in compilé contre Revit 2027
    dist\server\        RiveTT.Server.exe, autonome, partagé par les deux
    dist\RiveTT-Setup-<version>.exe

Le serveur ne référence pas l'API Revit : il est compilé une fois et partagé. Il est
**autonome** (~38 Mo) et n'exige aucun runtime .NET installé — c'était la seule pièce
qui aurait imposé des droits administrateur.

L'installation est par utilisateur dans
`%APPDATA%\Autodesk\Revit\Addins\<2026|2027>\RiveTT`, le serveur dans
`%LOCALAPPDATA%\RiveTT\server`.

Pour enregistrer le serveur dans Codex :

```powershell
codex mcp add RiveTT -- "%LOCALAPPDATA%\RiveTT\server\RiveTT.Server.exe"
```

Fermez Revit avant une réinstallation. Ouvrez ensuite Revit et un projet :
la session est publiée automatiquement et le serveur MCP la découvre sans
configuration de port.

## Vérifier

```powershell
dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release
dotnet build .\RiveTT.sln -c Release
```

Le build compile les DLL du plugin, les outils et le serveur, puis prépare le
paquet dans `distribution`.

## Contribuer

Règles d'architecture, contrat outils, verrou d'écriture et commandes de
vérification : voir [AGENTS.md](AGENTS.md).

## Licence

Ce dérivé conserve la licence [MIT](LICENSE) du projet source RevitCortex.
