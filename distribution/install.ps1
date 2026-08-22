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
    throw 'Fermez Revit 2027 avant l''installation pour libérer les DLL du plugin.'
}
# Windows interdit d'écraser un .exe en cours d'exécution, mais autorise son
# RENOMMAGE : on met l'ancien de côté et on écrit le neuf à sa place. Le client
# MCP continue de tourner sur le fichier renommé jusqu'à sa prochaine
# reconnexion, ce qui évite de devoir fermer le client pour mettre à jour.
$runningServers = @(Get-Process -Name 'MCPRVTT27.Server' -ErrorAction SilentlyContinue)
$serverWasRunning = $runningServers.Count -gt 0
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
New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null

# Purge des reliquats d'une mise à jour précédente (l'exe renommé n'est plus
# verrouillé une fois le client MCP redémarré).
Get-ChildItem $serverTarget -Filter '*.old-*' -File -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$locked = @()
Get-ChildItem $serverSource -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($serverSource.Length).TrimStart('')
    $destination = Join-Path $serverTarget $relative
    $destinationDir = Split-Path $destination -Parent
    if (-not (Test-Path $destinationDir)) { New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null }

    try {
        Copy-Item $_.FullName $destination -Force
    }
    catch {
        # Fichier verrouillé (typiquement l'exe et ses DLL chargées) : on le
        # renomme, ce que Windows autorise, puis on écrit le neuf à sa place.
        $parked = "$destination.old-$stamp"
        Move-Item $destination $parked -Force
        Copy-Item $_.FullName $destination -Force
        $locked += $relative
    }
}
Get-ChildItem $pluginTarget, $serverTarget -Recurse -File | ForEach-Object {
    Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue
}

$serverExe = Join-Path $serverTarget 'MCPRVTT27.Server.exe'
Write-Host 'MCPRVTT27 installé pour Revit 2027.' -ForegroundColor Green
Write-Host "Add-in : $pluginTarget"
Write-Host "Serveur stdio : $serverExe"
if ($serverWasRunning) {
    Write-Host ''
    Write-Host ("Le serveur MCP tournait pendant l'installation (PID " +
        (($runningServers | ForEach-Object { $_.Id }) -join ', ') + ').') -ForegroundColor Yellow
    if ($locked.Count -gt 0) {
        Write-Host ("Fichiers verrouillés remplacés par renommage : " + ($locked -join ', ')) -ForegroundColor Yellow
    }
    Write-Host 'Reconnectez le serveur MCP dans votre client pour charger cette version.' -ForegroundColor Yellow
    Write-Host ''
}
Write-Host 'Ouvrez Revit 2027 : la connexion par pipe local démarre automatiquement, sans port TCP.'
Write-Host 'Ajoutez le serveur MCP avec la commande :'
Write-Host "  codex mcp add MCPRVTT27 -- `"$serverExe`""
