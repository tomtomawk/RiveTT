#requires -Version 5.1
[CmdletBinding()]
param(
    # Must match the Revit version build.ps1 -RevitVersion produced the package for.
    [ValidateSet('2026', '2027')]
    [string] $RevitYear = '2027'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$revitYear = $RevitYear
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$revitYear"
$pluginSource = Join-Path $scriptRoot 'plugin'
$serverSource = Join-Path $scriptRoot 'server'
$pluginTarget = Join-Path $addinRoot 'RiveTT'
$manifestSource = Join-Path $scriptRoot 'RiveTT.addin'
$manifestTarget = Join-Path $addinRoot 'RiveTT.addin'
$serverTarget = Join-Path $env:LOCALAPPDATA 'RiveTT\server'

if (-not (Test-Path $pluginSource) -or -not (Test-Path $manifestSource)) {
    throw 'Paquet plugin incomplet. Exécutez build.ps1 avant de lancer cet installateur.'
}
if (-not (Test-Path (Join-Path $serverSource 'RiveTT.Server.exe'))) {
    throw 'Paquet serveur incomplet. Exécutez build.ps1 avant de lancer cet installateur.'
}

# Refuse a version mismatch rather than let Revit fail to load a plugin DLL built
# against the wrong RevitAPI (2026 vs 2027) with no error pointing back to this.
$buildTargetFile = Join-Path $scriptRoot '.build-target'
if (Test-Path -LiteralPath $buildTargetFile) {
    $builtFor = (Get-Content -LiteralPath $buildTargetFile -Raw).Trim()
    if ($builtFor -and $builtFor -ne $RevitYear) {
        throw "Ce paquet a été construit pour Revit $builtFor (build.ps1 -RevitVersion $builtFor), " +
              "mais vous installez pour Revit $RevitYear (-RevitYear $RevitYear). " +
              "Relancez .\build.ps1 -RevitVersion $RevitYear avant d'installer, ou passez -RevitYear $builtFor."
    }
}
else {
    Write-Host "Avertissement : aucun marqueur .build-target trouvé — impossible de vérifier que ce paquet correspond à Revit $RevitYear. Reconstruisez avec la version actuelle de build.ps1 si le paquet est ancien." -ForegroundColor Yellow
}

$revitProcesses = @(Get-Process -Name Revit -ErrorAction SilentlyContinue)
$serverProcesses = @(Get-Process -Name 'RiveTT.Server' -ErrorAction SilentlyContinue)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# Windows refuses to OVERWRITE a file that a process has loaded — a DLL held by
# Revit, the stdio server's own .exe — but it allows RENAMING it: the open handle
# follows the old name while the new file takes its place for the next start.
# Installing that way removes the need to close Revit and the MCP client just to
# update, which was the main friction of every upgrade.
function Copy-TreeWithRenameOnLock {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][string] $Stamp
    )

    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    # Drop the parked copies of a previous update: nothing holds them any more.
    Get-ChildItem $Destination -Recurse -File -Filter '*.old-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }

    $renamed = New-Object System.Collections.Generic.List[string]
    $prefix = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')

    # A plain foreach, and a named $file rather than $_: inside a catch block $_ is
    # the ErrorRecord, not the pipeline item, so the rename fallback below used to
    # call Copy-Item with a null path — it broke on exactly the locked file it was
    # written for.
    foreach ($file in @(Get-ChildItem -LiteralPath $Source -Recurse -File)) {
        $relative = $file.FullName.Substring($prefix.Length).TrimStart('\', '/')
        $target = Join-Path -Path $Destination -ChildPath $relative
        $targetDir = Split-Path -Path $target -Parent
        if (-not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }

        try {
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
        catch {
            Move-Item -LiteralPath $target -Destination "$target.old-$Stamp" -Force
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            $renamed.Add($relative) | Out-Null
        }
    }

    return , $renamed
}

New-Item -ItemType Directory -Path $addinRoot -Force | Out-Null
$pluginRenamed = Copy-TreeWithRenameOnLock -Source $pluginSource -Destination $pluginTarget -Stamp $stamp

try {
    Copy-Item $manifestSource $manifestTarget -Force
}
catch {
    Move-Item $manifestTarget "$manifestTarget.old-$stamp" -Force
    Copy-Item $manifestSource $manifestTarget -Force
}

New-Item -ItemType Directory -Path (Split-Path $serverTarget -Parent) -Force | Out-Null
$serverRenamed = Copy-TreeWithRenameOnLock -Source $serverSource -Destination $serverTarget -Stamp $stamp

Get-ChildItem $pluginTarget, $serverTarget -Recurse -File | ForEach-Object {
    Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue
}

$serverExe = Join-Path $serverTarget 'RiveTT.Server.exe'
Write-Host "RiveTT installé pour Revit $revitYear." -ForegroundColor Green
Write-Host "Add-in : $pluginTarget"
Write-Host "Serveur stdio : $serverExe"

if ($pluginRenamed.Count -gt 0 -or $serverRenamed.Count -gt 0) {
    Write-Host ''
    Write-Host 'Fichiers verrouillés remplacés par renommage :' -ForegroundColor Yellow
    if ($pluginRenamed.Count -gt 0) { Write-Host ("  plugin  : " + ($pluginRenamed -join ', ')) -ForegroundColor Yellow }
    if ($serverRenamed.Count -gt 0) { Write-Host ("  serveur : " + ($serverRenamed -join ', ')) -ForegroundColor Yellow }
}

if ($revitProcesses.Count -gt 0) {
    Write-Host ''
    Write-Host ("Revit tourne encore (PID " + (($revitProcesses | ForEach-Object { $_.Id }) -join ', ') +
        ') : il utilise la version chargée en mémoire. Redémarrez Revit pour charger ce plugin.') -ForegroundColor Yellow
}
if ($serverProcesses.Count -gt 0) {
    Write-Host ("Le serveur MCP tourne encore (PID " + (($serverProcesses | ForEach-Object { $_.Id }) -join ', ') +
        ') : reconnectez-le dans votre client pour charger cette version.') -ForegroundColor Yellow
}

Write-Host ''
Write-Host "Ouvrez Revit $revitYear : la connexion par pipe local démarre automatiquement, sans port TCP."
Write-Host 'Ajoutez le serveur MCP avec la commande :'
Write-Host "  codex mcp add RiveTT -- `"$serverExe`""
