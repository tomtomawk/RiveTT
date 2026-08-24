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
    dotnet build .\src\RiveTT.Tools\RiveTT.Tools.csproj -c $configuration
    dotnet build .\src\RiveTT.Plugin\RiveTT.Plugin.csproj -c $configuration
    dotnet build .\src\RiveTT.Server\RiveTT.Server.csproj -c $configuration
    if (-not $SkipTests) { dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c $configuration }

    if (Test-Path $pluginOut) { Remove-Item $pluginOut -Recurse -Force }
    if (Test-Path $serverOut) { Remove-Item $serverOut -Recurse -Force }
    New-Item -ItemType Directory -Path $pluginOut, $serverOut -Force | Out-Null

    $pluginBuild = Join-Path $root 'src\RiveTT.Plugin\bin\Release\net10.0-windows'
    $toolsBuild = Join-Path $root 'src\RiveTT.Tools\bin\Release\net10.0-windows'
    Get-ChildItem -LiteralPath $pluginBuild -File |
        Copy-Item -Destination $pluginOut -Force
    # Tools are loaded dynamically by the add-in, so their full dependency set
    # must travel with the plugin rather than only RiveTT.Tools.dll.
    Get-ChildItem -LiteralPath $toolsBuild -File |
        Copy-Item -Destination $pluginOut -Force

    dotnet publish .\src\RiveTT.Server\RiveTT.Server.csproj -c $configuration -r win-x64 --self-contained false -o $serverOut
    Copy-Item .\src\RiveTT.Plugin\RiveTT.addin .\distribution\RiveTT.addin -Force
    Write-Host 'Paquet prêt dans distribution\.' -ForegroundColor Green
}
finally { Pop-Location }
