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
$pluginOut = Join-Path $root 'distribution\plugin'
$serverOut = Join-Path $root 'distribution\server'
$versionArg = "-p:RevitVersion=$RevitVersion"

Write-Host "Cible : Revit $RevitVersion" -ForegroundColor Cyan

Push-Location $root
try {
    dotnet build .\src\RiveTT.Tools\RiveTT.Tools.csproj -c $configuration $versionArg
    dotnet build .\src\RiveTT.Plugin\RiveTT.Plugin.csproj -c $configuration $versionArg
    dotnet build .\src\RiveTT.Server\RiveTT.Server.csproj -c $configuration
    if (-not $SkipTests) { dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c $configuration $versionArg }

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

    # install.ps1 reads this to refuse installing a 2027-targeted plugin DLL into the
    # 2026 Addins folder (or vice versa): the wrong RevitAPI version is referenced and
    # Revit fails to load it, with no obvious error pointing back to the mismatch.
    Set-Content -LiteralPath .\distribution\.build-target -Value $RevitVersion -NoNewline

    Write-Host "Paquet prêt dans distribution\ (Revit $RevitVersion)." -ForegroundColor Green
}
finally { Pop-Location }
