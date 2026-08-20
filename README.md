# MCPRVTT27

MCP local pour **Autodesk Revit 2027**, basé sur RevitCortex (licence MIT).

## Principes

- une seule cible : Revit 2027 / .NET 10 / x64 ;
- transport local par **Windows Named Pipe**, jamais par port TCP ;
- démarrage automatique de l'add-in avec Revit, sans ruban ni interrupteur ;
- mode automatique permanent : aucune boîte d'autorisation ou licence ;
- pas de Power BI, télémétrie, mise à jour automatique ni compte commercial ;
- serveur MCP standard sur `stdio`, nommé `MCPRVTT27`.

Les appels transitent uniquement entre le client MCP, le serveur stdio et le
processus Revit de l'utilisateur courant. Les écritures Revit restent dans des
transactions et sont consignées dans `%LOCALAPPDATA%\MCPRVTT27\audit.jsonl`.

## Fonctions ajoutées

- création de murs avec niveaux bas/haut et décalage haut ;
- création de portes et fenêtres sur familles réellement présentes dans le projet ;
- garde-corps natifs (`create_railing`) ;
- association d'un mur à son mur hôte en Revit 2027 (`set_wall_host`) ;
- sauvegarde et sauvegarde sous du projet actif (`save_document`,
  `save_as_document`).

## Compiler et installer

Prérequis : .NET SDK 10 et Revit 2027.

```powershell
cd MCPRVTT27
.\build.ps1
.\distribution\install.ps1
```

L'installation est par utilisateur dans
`%APPDATA%\Autodesk\Revit\Addins\2027\MCPRVTT27` et ne demande pas de droits
administrateur. Elle prépare le serveur dans `%LOCALAPPDATA%\MCPRVTT27\server`.

Pour enregistrer le serveur dans Codex :

```powershell
codex mcp add MCPRVTT27 -- "%LOCALAPPDATA%\MCPRVTT27\server\MCPRVTT27.Server.exe"
```

Fermez Revit avant une réinstallation. Ouvrez ensuite Revit 2027 et un projet :
la session est publiée automatiquement et le serveur MCP la découvre sans
configuration de port.

## Vérifier

```powershell
dotnet test .\src\RevitCortex.Tests\RevitCortex.Tests.csproj -c Release
dotnet build .\RevitCortex.sln -c Release
```

Le build compile les DLL du plugin, les outils et le serveur, puis prépare le
paquet dans `distribution`.

## Licence

Ce dérivé conserve la licence [MIT](LICENSE) du projet source RevitCortex.
