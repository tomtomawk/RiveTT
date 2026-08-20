#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$revitYear = '2027'
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$revitYear"
$pluginSource = Join-Path $scriptRoot 'plugin'
$serverSource = Join-Path $scriptRoot 'server'
$pluginTarget = Join-Path $addinRoot 'MCPRVTT27'
$manifestSource = Join-Path $scriptRoot 'MCPRVTT27.addin'
$manifestTarget = Join-Path $addinRoot 'MCPRVTT27.addin'
$serverTarget = Join-Path $env:LOCALAPPDATA 'MCPRVTT27\server'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Fermez Revit 2027 avant l''installation pour libérer les DLL.'
}
if (-not (Test-Path $pluginSource) -or -not (Test-Path $manifestSource)) {
    throw 'Paquet plugin incomplet. Exécutez build.ps1 avant de lancer cet installateur.'
}
if (-not (Test-Path (Join-Path $serverSource 'MCPRVTT27.Server.exe'))) {
    throw 'Paquet serveur incomplet. Exécutez build.ps1 avant de lancer cet installateur.'
}

New-Item -ItemType Directory -Path $addinRoot -Force | Out-Null
if (Test-Path $pluginTarget) { Remove-Item $pluginTarget -Recurse -Force }
Copy-Item $pluginSource $pluginTarget -Recurse -Force
Copy-Item $manifestSource $manifestTarget -Force

New-Item -ItemType Directory -Path (Split-Path $serverTarget -Parent) -Force | Out-Null
if (Test-Path $serverTarget) { Remove-Item $serverTarget -Recurse -Force }
Copy-Item $serverSource $serverTarget -Recurse -Force
Get-ChildItem $pluginTarget, $serverTarget -Recurse -File | ForEach-Object {
    Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue
}

$serverExe = Join-Path $serverTarget 'MCPRVTT27.Server.exe'
Write-Host 'MCPRVTT27 installé pour Revit 2027.' -ForegroundColor Green
Write-Host "Add-in : $pluginTarget"
Write-Host "Serveur stdio : $serverExe"
Write-Host 'Ouvrez Revit 2027 : la connexion par pipe local démarre automatiquement, sans port TCP.'
Write-Host 'Ajoutez le serveur MCP avec la commande :'
Write-Host "  codex mcp add MCPRVTT27 -- `"$serverExe`""
