#requires -Version 5.1
<#
.SYNOPSIS
    Builds RiveTT for every supported Revit target and packages the per-user installer.

.DESCRIPTION
    Two output trees, and the split between them is the point of this layout:

        builder\staging\      intermediate payload, rebuilt from scratch every run
          2026\plugin\        add-in + tools built against Revit 2026.5
          2027\plugin\        add-in + tools built against Revit 2027
          server\             RiveTT.Server.exe, self-contained, shared by both
          RiveTT.addin        manifest, identical for both targets
          documentation\      copy of src\resources\documentation, ships with the product

        dist\                 deliverables only
          RiveTT-Setup-<version>.exe

    Both are generated and both are gitignored. The rule the split buys: everything in
    dist\ is publishable as it stands. The binaries used to land there too, which made
    "is this folder shippable?" a judgement call instead of a fact. ISCC reads
    builder\staging\ and writes dist\, never the other way round.

    The server carries no Revit API reference, so it is built ONCE and shared. It used
    to be published into each version folder; self-contained that would be 38 MB
    duplicated for nothing.

    The installer is compiled by Inno Setup (ISCC.exe). When Inno is not installed the
    payload is still produced in builder\staging\ and the packaging step is skipped with
    a warning -- a developer building locally does not need it to run the tests.

    ENCODING: this file is UTF-8 WITH BOM, and must stay that way. Windows PowerShell
    5.1 reads a BOM-less script as Windows-1252, so every multi-byte character becomes
    three garbage ones -- and some of those are curly quotes, which PowerShell honours
    as string delimiters. A box-drawing dash in a comment silently swallowed the rest
    of its line and stripped $LASTEXITCODE out of a guard. Comments and code here stay
    ASCII; only the user-facing French strings use accents. BuildScriptEncodingTests
    fails the suite if the BOM ever goes missing.

.PARAMETER RevitVersion
    Build a single target instead of all of them. Accepts 2026 or 2027.

.PARAMETER SkipTests
    Skip the xUnit run. Tests that need the real RevitAPI.dll locate a local Revit
    install themselves and report a clean Skip when there is none.

.PARAMETER SkipInstaller
    Build the payload into builder\staging\ but do not invoke Inno Setup. dist\ is then
    not created at all: with no installer there is nothing publishable to put in it.

.PARAMETER AllowTestFailures
    Package anyway when the test run fails. Off by default: the Revit-typed tests now
    report a clean Skip where Revit is absent, so a non-zero exit code is a real
    failure and must not produce a shippable installer. This switch exists for the one
    case it is legitimate -- a known-flaky test blocking an urgent build -- and it says
    so in the output rather than hiding it behind a warning nobody reads.

.PARAMETER CertificateThumbprint
    Authenticode certificate to sign with, by thumbprint, looked up in the current
    user's certificate store. Defaults to $env:RIVETT_SIGN_THUMBPRINT. An installer
    build fails when this is missing: every deliverable in dist must be signed by
    Thomas Thébault. Developers without the certificate can still compile and test
    with -SkipInstaller -SkipSigning.

    Create the temporary self-signed certificate with
    builder\New-SigningCertificate.ps1. A CA-issued certificate also uses this
    parameter, but adopting one must additionally restore payload signing.

.PARAMETER TimestampUrl
    RFC 3161 timestamp server. Countersigning is what keeps a signature valid after the
    certificate expires; without it the installer becomes untrusted on the expiry
    date, retroactively.

.PARAMETER SkipSigning
    Allow a development payload build without access to the installer-signing
    certificate. Accepted only with -SkipInstaller, because an installer is never
    allowed into dist unsigned.
#>
[CmdletBinding()]
param(
    # Omit to build every supported target. Both run on .NET 10 (Revit 2026.5 update);
    # the plugin and tools projects pick the matching Nice3point.Revit.Api.RevitAPI
    # version and the REVIT2027_OR_GREATER-gated code through this.
    [ValidateSet('2026', '2027')]
    [string[]] $RevitVersion = @('2026', '2027'),
    [switch] $SkipTests,
    [switch] $SkipInstaller,
    [switch] $AllowTestFailures,
    [string] $CertificateThumbprint = $env:RIVETT_SIGN_THUMBPRINT,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [switch] $SkipSigning
)

$ErrorActionPreference = 'Stop'
$expectedSigner = 'Thomas Thébault'

# This script lives in builder\, one level BELOW the repository root, so the root is
# its grandparent and not its own folder. Every relative path below (.\src\...) is
# resolved against $root through the Push-Location at the bottom; getting this wrong
# would silently build nothing and copy nothing.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$configuration = 'Release'
$stagingRoot = Join-Path $root 'builder\staging'
$distRoot = Join-Path $root 'dist'
$serverOut = Join-Path $stagingRoot 'server'
$docsSource = Join-Path $root 'src\resources\documentation'
$docsOut = Join-Path $stagingRoot 'documentation'
$issPath = Join-Path $root 'builder\installer\RiveTT.iss'

# Single source of truth for the version, the same file the assemblies read.
$propsPath = Join-Path $root 'Directory.Build.props'
$version = ([xml](Get-Content $propsPath)).Project.PropertyGroup.Version
if (-not $version) { throw "Version introuvable dans $propsPath." }

function Invoke-Dotnet {
    <#
        Windows PowerShell 5.1 wraps a native command's stderr in an ErrorRecord, and
        with $ErrorActionPreference = 'Stop' that TERMINATES the script even when the
        command itself succeeded. `dotnet test` prints its failure list to stderr, so
        a warning-only test run has to judge the outcome on the exit code alone.

        The exit code is the only trustworthy signal, so the preference is relaxed
        around the call and the result judged on $LASTEXITCODE alone.
    #>
    param(
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $FailureMessage,
        [switch] $WarnOnly
    )

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & dotnet @Arguments } finally { $ErrorActionPreference = $previous }

    if ($LASTEXITCODE -ne 0) {
        if ($WarnOnly) { Write-Warning $FailureMessage } else { throw $FailureMessage }
    }
}

function Resolve-SignTool {
    <#
        signtool.exe ships with the Windows SDK and is versioned by SDK build, so there
        is no stable path to hardcode. The newest one wins: older SDK builds predate
        /fd, and a signature with no explicit file digest defaults to SHA1, which
        current Windows rejects outright.

        Sorted on the PARENT-OF-PARENT directory name (the SDK version, 10.0.22621.0)
        rather than the full path, because sorting full paths puts x64 and arm64 of
        different SDK versions in an order that has nothing to do with age.
    #>
    $kits = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    ) | Where-Object { Test-Path $_ }

    $candidate = $kits |
        ForEach-Object { Get-ChildItem -Path $_ -Filter 'signtool.exe' -Recurse -File -ErrorAction SilentlyContinue } |
        Where-Object { $_.DirectoryName -match '\\x64$' } |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
        Select-Object -First 1

    if ($candidate) { return $candidate.FullName }

    $onPath = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

function Invoke-SignTool {
    param(
        [Parameter(Mandatory = $true)][string] $SignTool,
        [Parameter(Mandatory = $true)][string] $Thumbprint,
        [Parameter(Mandatory = $true)][string[]] $Paths,
        [string] $Timestamp
    )

    $arguments = @('sign', '/sha1', $Thumbprint, '/fd', 'sha256')
    if ($Timestamp) { $arguments += @('/tr', $Timestamp, '/td', 'sha256') }
    $arguments += $Paths

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $SignTool @arguments | Out-Null } finally { $ErrorActionPreference = $previous }

    return ($LASTEXITCODE -eq 0)
}

function Resolve-InstallerSigning {
    <#
        Validates the configured publisher identity and prepares Inno Setup signing.

        The current certificate is self-signed. It is suitable for identifying local
        build ownership but Windows cannot chain it to a trusted public root. Revit
        therefore reports a signed plugin as having an invalid signature, which is
        worse than its normal unsigned-add-in prompt. Until a CA-issued code-signing
        certificate replaces it, Revit-side DLLs remain unsigned. The MCP server and
        registration script can still be signed because Revit never loads them.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $Thumbprint,
        [Parameter(Mandatory = $true)][string] $StagingRoot,
        [string] $Timestamp
    )

    $certificate = Get-ChildItem -Path "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
    if (-not $certificate) {
        throw ("Certificat $Thumbprint introuvable dans Cert:\CurrentUser\My. " +
               "Creez-en un avec .\builder\New-SigningCertificate.ps1, ou corrigez " +
               "RIVETT_SIGN_THUMBPRINT.")
    }
    if ($certificate.NotAfter -lt (Get-Date)) {
        throw "Le certificat $Thumbprint a expire le $($certificate.NotAfter.ToString('yyyy-MM-dd'))."
    }
    if (-not $certificate.HasPrivateKey) {
        throw "Le certificat $Thumbprint ne contient pas de cle privee."
    }
    $signerName = $certificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    if ($signerName -ne $expectedSigner) {
        throw ("Mauvais certificat de signature : '$signerName'. " +
               "L'installateur RiveTT doit etre signe par '$expectedSigner'.")
    }

    $signTool = Resolve-SignTool
    if (-not $signTool) {
        throw ("signtool.exe introuvable : installez le SDK Windows 10/11 " +
               "(winget install Microsoft.WindowsSDK), ou relancez avec -SkipSigning.")
    }

    $serverBinary = Join-Path $StagingRoot 'server\RiveTT.Server.exe'
    if (-not (Test-Path -LiteralPath $serverBinary)) {
        throw "Executable du serveur MCP introuvable dans $StagingRoot\server."
    }

    $signed = Invoke-SignTool -SignTool $signTool -Thumbprint $Thumbprint `
                              -Paths @($serverBinary) -Timestamp $Timestamp
    $timestamped = $signed
    if (-not $signed -and $Timestamp) {
        Write-Warning ("Horodatage impossible ($Timestamp injoignable) : signature sans " +
                       "horodatage. Elle expirera le " +
                       "$($certificate.NotAfter.ToString('yyyy-MM-dd')).")
        $signed = Invoke-SignTool -SignTool $signTool -Thumbprint $Thumbprint `
                                  -Paths @($serverBinary)
        $timestamped = $false
    }
    if (-not $signed) { throw "signtool a echoue (code $LASTEXITCODE)." }

    $script = Join-Path $StagingRoot 'register-mcp.ps1'
    if (-not (Test-Path -LiteralPath $script)) {
        throw "Script MCP introuvable dans $StagingRoot."
    }
    $signature = if ($timestamped) {
        Set-AuthenticodeSignature -FilePath $script -Certificate $certificate `
                                  -HashAlgorithm SHA256 -TimestampServer $Timestamp
    } else {
        Set-AuthenticodeSignature -FilePath $script -Certificate $certificate `
                                  -HashAlgorithm SHA256
    }
    $applied = $signature.SignerCertificate -and
               ($signature.Status -in @('Valid', 'UnknownError'))
    if (-not $applied) {
        throw ("Signature de register-mcp.ps1 en echec ($($signature.Status)) : " +
               $signature.StatusMessage)
    }

    Write-Host ("Serveur MCP et script signes. DLL Revit non signees pour eviter " +
                "l'alerte de signature incorrecte.") -ForegroundColor Green

    # Returned so the caller can hand ISCC the tool, certificate and timestamp for
    # setup and the generated uninstaller only.
    return [pscustomobject]@{
        SignTool  = $signTool
        Timestamp = if ($timestamped) { $Timestamp } else { '' }
    }
}

# Set by the test step when -AllowTestFailures let a red run through, read at the very
# end so the caveat lands next to the installer path instead of scrolling away.
$script:shippedOverFailingTests = $false

Push-Location $root
try {
    Write-Host "RiveTT $version - cibles : $($RevitVersion -join ', ')" -ForegroundColor Cyan

    # A stale staging tree is worse than none: a file removed from the sources would
    # survive into the installer. dist\ goes too, so a previous version's setup can
    # never be mistaken for this run's output.
    foreach ($stale in @($stagingRoot, $distRoot)) {
        if (Test-Path $stale) { Remove-Item $stale -Recurse -Force }
    }
    New-Item -ItemType Directory -Path $stagingRoot, $serverOut -Force | Out-Null

    # --- The server: no Revit reference, built once, shared by every target ---
    Write-Host 'Serveur MCP (autonome, sans dependance runtime)...' -ForegroundColor Cyan
    Invoke-Dotnet -Arguments @(
        'publish', '.\src\RiveTT.Server\RiveTT.Server.csproj',
        '-c', $configuration, '-o', $serverOut, '--nologo'
    ) -FailureMessage 'Echec de la publication du serveur.'
    # PublishSingleFile still emits the .pdb beside the exe; it has no business shipping.
    Get-ChildItem $serverOut -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force

    # --- One plugin payload per Revit target ---
    foreach ($target in $RevitVersion) {
        Write-Host "Plugin Revit $target..." -ForegroundColor Cyan
        $versionArg = "-p:RevitVersion=$target"
        $pluginOut = Join-Path $stagingRoot "$target\plugin"
        New-Item -ItemType Directory -Path $pluginOut -Force | Out-Null

        Invoke-Dotnet -Arguments @(
            'build', '.\src\RiveTT.Tools\RiveTT.Tools.csproj',
            '-c', $configuration, $versionArg, '--nologo'
        ) -FailureMessage "Echec du build Tools pour Revit $target."

        Invoke-Dotnet -Arguments @(
            'build', '.\src\RiveTT.Plugin\RiveTT.Plugin.csproj',
            '-c', $configuration, $versionArg, '--nologo'
        ) -FailureMessage "Echec du build Plugin pour Revit $target."

        if (-not $SkipTests) {
            # A failing test stops the build. It used to warn and package anyway, from a
            # time when 13 tests could not pass off a Revit workstation; since they
            # report a clean Skip instead, a non-zero code is a real failure and an
            # installer built over it is not shippable.
            $testFailure = "Tests en echec pour Revit $target. Corrigez avant de " +
                           "packager, ou relancez avec -AllowTestFailures si vous " +
                           "assumez de diffuser ce build."
            Invoke-Dotnet -Arguments @(
                'test', '.\src\RiveTT.Tests\RiveTT.Tests.csproj',
                '-c', $configuration, $versionArg, '--nologo'
            ) -FailureMessage $testFailure -WarnOnly:$AllowTestFailures

            if ($AllowTestFailures -and $LASTEXITCODE -ne 0) {
                $script:shippedOverFailingTests = $true
            }
        }

        $pluginBuild = Join-Path $root 'src\RiveTT.Plugin\bin\Release\net10.0-windows'
        $toolsBuild = Join-Path $root 'src\RiveTT.Tools\bin\Release\net10.0-windows'

        # Tools are loaded dynamically by the add-in, so their whole dependency set must
        # travel with the plugin, not just RiveTT.Tools.dll. Symbols must not: they are
        # a third of a megabyte per target and Revit never reads them.
        #
        # Filtered with Where-Object, NOT -Exclude: Get-ChildItem silently ignores
        # -Exclude when the path comes from -LiteralPath, so the .pdb files shipped
        # anyway and the exclusion looked like it worked.
        foreach ($source in @($pluginBuild, $toolsBuild)) {
            Get-ChildItem -LiteralPath $source -File |
                Where-Object { $_.Extension -ne '.pdb' } |
                Copy-Item -Destination $pluginOut -Force
        }
    }

    # The manifest is identical for both targets -- its Assembly path is relative -- so
    # it sits once at the root of the staging tree rather than being copied per version.
    Copy-Item .\src\RiveTT.Plugin\RiveTT.addin (Join-Path $stagingRoot 'RiveTT.addin') -Force

    # --- Documentation, shipped with the product ---
    # Copied through staging rather than read straight out of src\ by Inno, so the rule
    # "ISCC only ever reads builder\staging\" stays true and the whole payload can be
    # inspected in one place before packaging.
    if (-not (Test-Path $docsSource)) {
        throw "Documentation introuvable : $docsSource."
    }
    New-Item -ItemType Directory -Path $docsOut -Force | Out-Null
    Copy-Item (Join-Path $docsSource 'SKILL.md') $docsOut -Force

    # The MCP registration helper: product content like the documentation, not a build
    # tool, so it lives under src\resources and travels through staging like the rest.
    $registerScript = Join-Path $root 'src\resources\register-mcp.ps1'
    if (-not (Test-Path $registerScript)) {
        throw "Script d'enregistrement MCP introuvable : $registerScript."
    }
    Copy-Item $registerScript $stagingRoot -Force

    $sizeMb = [math]::Round((Get-ChildItem $stagingRoot -Recurse -File |
        Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "Charge utile prete dans builder\staging\ ($sizeMb Mo)." -ForegroundColor Green

    # --- Signature ---
    # Temporary trust policy: leave Revit-side DLLs unsigned because the current
    # self-signed certificate makes Revit display "Signature incorrecte". The MCP
    # server/script are safe to sign; setup and uninstaller are signed by ISCC below.
    $signing = $null
    if ($SkipSigning) {
        if (-not $SkipInstaller) {
            throw '-SkipSigning exige -SkipInstaller : un installateur RiveTT ne peut jamais etre non signe.'
        }
        Write-Warning 'Signature ignoree pour la charge utile de developpement (-SkipInstaller).'
    }
    elseif (-not $CertificateThumbprint) {
        if (-not $SkipInstaller) {
            throw ("Signature obligatoire : aucun certificat '$expectedSigner' configure. " +
                   "Executez .\builder\New-SigningCertificate.ps1 puis definissez " +
                   "RIVETT_SIGN_THUMBPRINT.")
        }
        Write-Warning 'Charge utile de developpement non signee (-SkipInstaller).'
    }
    else {
        Write-Host 'Preparation de la signature de l installateur...' -ForegroundColor Cyan
        $signing = Resolve-InstallerSigning -Thumbprint $CertificateThumbprint `
                                            -StagingRoot $stagingRoot `
                                            -Timestamp $TimestampUrl
    }

    # --- Installer ---
    if ($SkipInstaller) {
        Write-Host "Installateur ignore (-SkipInstaller) : dist\ n'est pas cree." -ForegroundColor Yellow
        return
    }

    # The per-user location comes FIRST and is not an afterthought: a developer without
    # local admin installs Inno with --scope user, which lands in LOCALAPPDATA\Programs.
    # Checking only Program Files would have made the tool invisible to exactly the
    # people this whole admin-free packaging exists for.
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    # Last resort: on PATH, or installed somewhere else entirely.
    if (-not $iscc) {
        $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        if ($onPath) { $iscc = $onPath.Source }
    }

    if (-not $iscc) {
        Write-Warning ("Inno Setup 6 introuvable : la charge utile est prete dans " +
                       "builder\staging\ mais l'installateur n'a pas ete produit. " +
                       "Installez-le (winget install JRSoftware.InnoSetup) puis " +
                       "relancez, ou passez -SkipInstaller pour taire cet avertissement.")
        return
    }

    if ($RevitVersion.Count -lt 2) {
        Write-Warning ("Installateur construit avec une seule cible ($($RevitVersion -join ', ')) : " +
                       "il n'installera pas l'autre version de Revit.")
    }

    Write-Host "Compilation de l'installateur..." -ForegroundColor Cyan

    # The setup and its uninstaller are signed by ISCC itself, not afterwards, and that
    # ordering is forced: the uninstaller is generated INSIDE the compiled setup, so
    # once dist\ exists it can no longer be reached. Hence /S, which hands Inno the
    # command line to run per file, paired with /DSign so the .iss only declares
    # SignTool= when one was actually supplied -- a SignTool name with no matching /S
    # is a compile error, and that would break every build made without a certificate.
    #
    # $q and $f are INNO placeholders (quote, filename), not PowerShell variables, so
    # they are built in single-quoted strings and must stay that way.
    $isccArguments = @("/DAppVersion=$version", "/O$distRoot")
    if ($signing) {
        $signCommand = '$q' + $signing.SignTool + '$q sign /sha1 ' + $CertificateThumbprint + ' /fd sha256'
        if ($signing.Timestamp) { $signCommand += ' /tr ' + $signing.Timestamp + ' /td sha256' }
        $signCommand += ' $f'
        $isccArguments += @('/DSign', "/Srivett=$signCommand")
    }
    $isccArguments += $issPath

    & $iscc @isccArguments
    if ($LASTEXITCODE -ne 0) { throw "ISCC a echoue (code $LASTEXITCODE)." }

    $setup = Join-Path $distRoot "RiveTT-Setup-$version.exe"
    if (Test-Path $setup) {
        $setupSignature = Get-AuthenticodeSignature -FilePath $setup
        $setupSigner = if ($setupSignature.SignerCertificate) {
            $setupSignature.SignerCertificate.GetNameInfo(
                [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
        } else { '' }
        if (($setupSigner -ne $expectedSigner) -or
            ($setupSignature.Status -notin @('Valid', 'UnknownError'))) {
            throw ("Signature de l'installateur invalide : signataire '$setupSigner', " +
                   "etat $($setupSignature.Status). Attendu : '$expectedSigner'.")
        }
        $setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
        Write-Host "Installateur pret : $setup ($setupMb Mo)" -ForegroundColor Green
        Write-Host 'Aucune elevation administrateur requise a son execution.'
        Write-Host "Signe par $expectedSigner, desinstalleur compris."
        # Said at the END, where it is read. A warning printed 200 lines earlier,
        # between two dotnet builds, is a warning nobody sees.
        if ($script:shippedOverFailingTests) {
            Write-Warning ("Cet installateur a ete produit AVEC des tests en echec " +
                           "(-AllowTestFailures). Ne le diffusez pas sans savoir " +
                           "lesquels et pourquoi.")
        }
    }
}
finally { Pop-Location }
