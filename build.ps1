#requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $SkipTests,
    # 2027 (default) or 2026 — both run on .NET 10 (Revit 2026.5 update); the plugin
    # and tools projects pick the matching Nice3point.Revit.Api.RevitAPI version and
    # the REVIT2027_OR_GREATER-gated code (e.g. Coordination Models) via this.
    [ValidateSet('2026', '2027')]
    [string] $RevitVersion = '2027'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configuration = 'Release'
# Per-version output folder: distribution\2026\ and distribution\2027\ can both
# exist at once, so building one target never overwrites the other, and
# install.ps1 -RevitYear reads from the matching folder by construction —
# there is no separate "wrong binaries in the wrong Addins folder" state to
# reach, rather than one caught after the fact.
$versionOut = Join-Path $root "distribution\$RevitVersion"
$pluginOut = Join-Path $versionOut 'plugin'
$serverOut = Join-Path $versionOut 'server'
$versionArg = "-p:RevitVersion=$RevitVersion"

Write-Host "Cible : Revit $RevitVersion" -ForegroundColor Cyan

Push-Location $root
try {
    dotnet build .\src\RiveTT.Tools\RiveTT.Tools.csproj -c $configuration $versionArg
    dotnet build .\src\RiveTT.Plugin\RiveTT.Plugin.csproj -c $configuration $versionArg
    dotnet build .\src\RiveTT.Server\RiveTT.Server.csproj -c $configuration
    if (-not $SkipTests) { dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c $configuration $versionArg }

    if (Test-Path $versionOut) { Remove-Item $versionOut -Recurse -Force }
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
    Copy-Item .\src\RiveTT.Plugin\RiveTT.addin (Join-Path $versionOut 'RiveTT.addin') -Force

    Write-Host "Paquet prêt dans distribution\$RevitVersion\." -ForegroundColor Green
}
finally { Pop-Location }
