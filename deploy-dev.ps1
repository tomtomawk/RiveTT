param(
    [ValidateSet("2023","2024","2025","2026","2027")]
    [string]$RevitVersion = "2025",
    [ValidateSet("Debug","Release")]
    [string]$Config = "Debug"
)

# --- Dev-only side-by-side deploy ---
# Mirrors deploy.ps1's build+copy shape, but installs into a completely
# separate folder/manifest/AddInId/port so a dev build can run next to the
# production RevitCortex install without touching any prod file:
#   - user-scope only (no ProgramData / no elevation)
#   - RevitCortexDev\ folder (never RevitCortex\)
#   - RevitCortexDev.addin manifest, distinct AddInId GUID
#   - CortexEnvironment.Detect() sees "RevitCortexDev" in the assembly path
#     and switches the whole plugin to the dev profile (settings/audit/port/
#     ribbon tab all separate from prod) — see src/RevitCortex.Core/Hosting/CortexEnvironment.cs

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Configuration = "$Config R$($RevitVersion.Substring(2))"
$PublishDir = Join-Path $RepoRoot "publish\R$($RevitVersion.Substring(2))-dev"
$UserAddinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$TargetDir = Join-Path $UserAddinsDir "RevitCortexDev"

# Fixed AddInId dedicated to the dev profile. Must differ from the prod GUID
# (A1B2C3D4-E5F6-7890-ABCD-EF1234567890 in RevitCortex.addin) — two manifests
# sharing an AddInId collide in Revit even with different Assembly paths.
$DevAddInId = "d3f8a2c4-9b1e-4e5f-8a7c-2f6d0b9e4a11"

Write-Host "=== RevitCortex DEV Deploy ===" -ForegroundColor Cyan
Write-Host "Revit: $RevitVersion | Config: $Configuration"
Write-Host "Target: $TargetDir (user-scope only)"

# --- Hard guard: never allow this script to write into the prod folder. ---
# deploy.ps1 owns "RevitCortex\" (ProgramData, machine-scope, elevated). This
# script must only ever touch "RevitCortexDev\" under %APPDATA%. If a future
# edit accidentally changes $TargetDir to the prod name, refuse to proceed
# rather than silently overwriting/deleting the production install.
if ($TargetDir -notlike "*RevitCortexDev") {
    throw "Refusing to deploy: TargetDir '$TargetDir' does not end with 'RevitCortexDev'. Aborting to protect the production install."
}
$leafName = Split-Path $TargetDir -Leaf
if ($leafName -eq "RevitCortex") {
    throw "Refusing to deploy: TargetDir leaf is 'RevitCortex' (the production folder name). Aborting."
}

# --- Pre-flight: refuse to deploy while Revit is running (DLLs would be locked) ---
$revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revit) {
    Write-Host ""
    Write-Host "ERROR: Revit is currently running (PID $($revit.Id -join ', ')). Close Revit and re-run." -ForegroundColor Red
    exit 1
}

# Kill orphan RevitCortex.Server processes that may hold satellite assemblies in lock
$orphans = Get-Process -Name 'RevitCortex.Server' -ErrorAction SilentlyContinue
if ($orphans) {
    Write-Host "Killing $($orphans.Count) orphan RevitCortex.Server process(es)..." -ForegroundColor Yellow
    $orphans | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# Clean publish dir
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

# Build & publish Plugin
Write-Host "`nPublishing Plugin (dev)..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Plugin\RevitCortex.Plugin.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed" }

# Build & publish Tools (to same output)
Write-Host "Publishing Tools (dev)..." -ForegroundColor Yellow
dotnet publish -c "$Configuration" "$RepoRoot\src\RevitCortex.Tools\RevitCortex.Tools.csproj" -o $PublishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Tools publish failed" }

# --- Safety re-check right before any filesystem mutation ---
if ($leafName -eq "RevitCortex" -or $TargetDir -notlike "*RevitCortexDev") {
    throw "Refusing to write: TargetDir '$TargetDir' failed the prod-folder guard a second time. Aborting."
}

# Wipe + recreate the dev target so stale satellite assemblies don't survive
if (Test-Path $TargetDir) { Remove-Item $TargetDir -Recurse -Force }
New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

# Copy DLLs
Write-Host "Copying files..." -ForegroundColor Yellow
Copy-Item "$PublishDir\*" $TargetDir -Recurse -Force

# --- Write the dev .addin manifest (never overwrite the prod one) ---
if (-not (Test-Path $UserAddinsDir)) {
    New-Item -ItemType Directory -Path $UserAddinsDir -Force | Out-Null
}
$DevManifestPath = Join-Path $UserAddinsDir "RevitCortexDev.addin"
$manifestXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitCortex Dev</Name>
    <Assembly>RevitCortexDev\RevitCortex.Plugin.dll</Assembly>
    <FullClassName>RevitCortex.Plugin.RevitCortexApp</FullClassName>
    <AddInId>$DevAddInId</AddInId>
    <VendorId>RevitCortex</VendorId>
    <VendorDescription>RevitCortex MCP Server for Autodesk Revit (Dev build)</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
Set-Content -Path $DevManifestPath -Value $manifestXml -Encoding UTF8

$dllCount = (Get-ChildItem "$TargetDir\*.dll").Count

Write-Host "`n=== Dev deploy complete ===" -ForegroundColor Green
Write-Host "$dllCount DLLs deployed to $TargetDir"
Write-Host ".addin manifest written to $DevManifestPath"
Write-Host "`nThis dev build uses its own settings/audit/port/ribbon tab (CortexEnvironment dev profile)."
Write-Host "It never touches the production RevitCortex install."
Write-Host "`nRestart Revit $RevitVersion to load the dev plugin."
