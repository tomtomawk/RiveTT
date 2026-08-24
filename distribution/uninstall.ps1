#requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $RemoveLocalData,
    # Must match the version passed to install.ps1 -RevitYear.
    [ValidateSet('2026', '2027')]
    [string] $RevitYear = '2027'
)

$ErrorActionPreference = 'Stop'
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$pluginTarget = Join-Path $addinRoot 'RiveTT'
$manifestTarget = Join-Path $addinRoot 'RiveTT.addin'
$localRoot = Join-Path $env:LOCALAPPDATA 'RiveTT'
$serverTarget = Join-Path $localRoot 'server'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw "Fermez Revit $RevitYear avant la désinstallation pour libérer les DLL du plugin."
}
$runningServers = @(Get-Process -Name 'RiveTT.Server' -ErrorAction SilentlyContinue)
if ($runningServers.Count -gt 0) {
    $pids = ($runningServers | ForEach-Object { $_.Id }) -join ', '
    throw "Le serveur MCP tourne encore (PID $pids). Fermez le client MCP avant la désinstallation."
}

if (Test-Path -LiteralPath $pluginTarget) {
    Remove-Item -LiteralPath $pluginTarget -Recurse -Force
}
if (Test-Path -LiteralPath $manifestTarget) {
    Remove-Item -LiteralPath $manifestTarget -Force
}
if (Test-Path -LiteralPath $serverTarget) {
    Remove-Item -LiteralPath $serverTarget -Recurse -Force
}
if ($RemoveLocalData -and (Test-Path -LiteralPath $localRoot)) {
    Remove-Item -LiteralPath $localRoot -Recurse -Force
}

Write-Host "RiveTT désinstallé pour Revit $RevitYear." -ForegroundColor Green
if (-not $RemoveLocalData) {
    Write-Host "Données locales conservées : $localRoot"
}
