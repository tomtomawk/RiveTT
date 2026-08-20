#requires -Version 5.1
[CmdletBinding()]
param([switch] $SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configuration = 'Release'
$pluginOut = Join-Path $root 'distribution\plugin'
$serverOut = Join-Path $root 'distribution\server'

Push-Location $root
try {
    dotnet build .\src\RevitCortex.Tools\RevitCortex.Tools.csproj -c $configuration
    dotnet build .\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj -c $configuration
    dotnet build .\src\RevitCortex.Server\RevitCortex.Server.csproj -c $configuration
    if (-not $SkipTests) { dotnet test .\src\RevitCortex.Tests\RevitCortex.Tests.csproj -c $configuration }

    if (Test-Path $pluginOut) { Remove-Item $pluginOut -Recurse -Force }
    if (Test-Path $serverOut) { Remove-Item $serverOut -Recurse -Force }
    New-Item -ItemType Directory -Path $pluginOut, $serverOut -Force | Out-Null

    $pluginBuild = Join-Path $root 'src\RevitCortex.Plugin\bin\Release\net10.0-windows'
    $toolsBuild = Join-Path $root 'src\RevitCortex.Tools\bin\Release\net10.0-windows'
    Get-ChildItem -LiteralPath $pluginBuild -File |
        Copy-Item -Destination $pluginOut -Force
    # Tools are loaded dynamically by the add-in, so their full dependency set
    # must travel with the plugin rather than only MCPRVTT27.Tools.dll.
    Get-ChildItem -LiteralPath $toolsBuild -File |
        Copy-Item -Destination $pluginOut -Force

    dotnet publish .\src\RevitCortex.Server\RevitCortex.Server.csproj -c $configuration -r win-x64 --self-contained false -o $serverOut
    Copy-Item .\src\RevitCortex.Plugin\RevitCortex.addin .\distribution\MCPRVTT27.addin -Force
    Write-Host 'Paquet prêt dans distribution\.' -ForegroundColor Green
}
finally { Pop-Location }
