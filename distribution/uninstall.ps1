#requires -Version 5.1
[CmdletBinding()]
param([switch] $RemoveLocalData)

$ErrorActionPreference = 'Stop'
$addinRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'
$pluginTarget = Join-Path $addinRoot 'MCPRVTT27'
$manifestTarget = Join-Path $addinRoot 'MCPRVTT27.addin'
$localRoot = Join-Path $env:LOCALAPPDATA 'MCPRVTT27'
$serverTarget = Join-Path $localRoot 'server'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Fermez Revit 2027 avant la désinstallation pour libérer les DLL du plugin.'
}
$runningServers = @(Get-Process -Name 'MCPRVTT27.Server' -ErrorAction SilentlyContinue)
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

Write-Host 'MCPRVTT27 désinstallé pour Revit 2027.' -ForegroundColor Green
if (-not $RemoveLocalData) {
    Write-Host "Données locales conservées : $localRoot"
}
