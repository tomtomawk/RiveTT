#requires -Version 5.1
<#
.SYNOPSIS
    Creates the self-signed code-signing certificate builder\build.ps1 signs with.

.DESCRIPTION
    A self-signed certificate is worth exactly one thing, and it is worth it fully:
    inside the agency, where the public certificate is deployed to the workstations
    through Group Policy, Windows stops calling RiveTT an unknown publisher and the
    heuristic antivirus flags mostly go away. Outside it, it is worth nothing at all
    -- an unknown publisher warning and a self-signed certificate look identical to
    a machine that does not trust the issuer.

    So this is the interim answer, not the final one. The final one is a certificate
    from a real CA (SignPath Foundation is free for open source, which this is), and
    the point of this script is that switching to it changes nothing else: build.ps1
    signs by thumbprint, and a CA-issued certificate has a thumbprint too.

    WHAT THIS DOES NOT DO: install the certificate as trusted, on this machine or any
    other. Trusting it is a deliberate act, done once per workstation, and it belongs
    to whoever administers those workstations -- see the instructions this script
    prints when it finishes. Writing to the machine trust store also needs
    administrator rights, which nothing else in this build chain does.

    ENCODING: UTF-8 WITH BOM, like every other PowerShell script here. Windows
    PowerShell 5.1 reads a BOM-less script as Windows-1252 and turns the accented
    strings below into mojibake -- and curly quotes among the mojibake are honoured
    as string delimiters. BuildScriptEncodingTests fails the suite if the BOM goes
    missing. Comments and code stay ASCII; only user-facing French strings use
    accents.

.PARAMETER Subject
    The publisher name Windows will show. Use the legal identity that will appear on
    the real certificate later, so the two do not disagree.

.PARAMETER OutputDirectory
    Where the exportable files land. Defaults OUTSIDE the repository on purpose: a
    .pfx holds the private key, and the one place it must never end up is a git
    working tree.

.PARAMETER Years
    Validity. Three years by default -- long enough not to be a recurring chore,
    short enough that an abandoned key expires.

.PARAMETER ExportPfx
    Also export the private key, protected by a password this script asks for. Only
    needed to sign from a DIFFERENT machine than this one; the local build reads the
    certificate straight out of the user's store and never touches the .pfx.

.EXAMPLE
    .\builder\New-SigningCertificate.ps1 -Subject 'Thomas Thebault'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Subject,

    [string] $OutputDirectory = (Join-Path $env:USERPROFILE 'RiveTT-signing'),

    [ValidateRange(1, 10)]
    [int] $Years = 3,

    [switch] $ExportPfx
)

$ErrorActionPreference = 'Stop'

# CurrentUser\My, not LocalMachine\My: the private key then belongs to the account
# that builds, needs no elevation to create or to use, and cannot be read by another
# user of the same workstation.
$storePath = 'Cert:\CurrentUser\My'

Write-Host "Creation du certificat de signature pour : $Subject" -ForegroundColor Cyan

# HashAlgorithm SHA256 explicitly: the default follows the OS and SHA1 signatures are
# rejected outright by current Windows versions.
$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=$Subject" `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation $storePath `
    -NotAfter (Get-Date).AddYears($Years)

$thumbprint = $certificate.Thumbprint

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# The PUBLIC certificate. This is the file that goes to the workstations, and it
# carries no private key -- it can be mailed, shared, or committed without risk.
$cerPath = Join-Path $OutputDirectory "RiveTT-CodeSigning-$thumbprint.cer"
Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT | Out-Null

$pfxPath = $null
if ($ExportPfx) {
    # Read-Host -AsSecureString rather than a plain parameter: a password passed on the
    # command line lands in the PowerShell history file in clear text.
    $password = Read-Host 'Mot de passe pour proteger la cle privee (.pfx)' -AsSecureString
    if ($password.Length -eq 0) { throw 'Mot de passe vide : export annule.' }

    $pfxPath = Join-Path $OutputDirectory "RiveTT-CodeSigning-$thumbprint.pfx"
    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $password | Out-Null
}

Write-Host ''
Write-Host 'Certificat cree.' -ForegroundColor Green
Write-Host "  Empreinte (thumbprint) : $thumbprint"
Write-Host "  Magasin                : $storePath"
Write-Host "  Certificat public      : $cerPath"
if ($pfxPath) {
    Write-Host "  Cle privee (.pfx)      : $pfxPath" -ForegroundColor Yellow
    Write-Host '  Ce fichier contient la cle privee. Ne le committez jamais.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Etape suivante -- signer les builds :' -ForegroundColor Cyan
Write-Host "  [Environment]::SetEnvironmentVariable('RIVETT_SIGN_THUMBPRINT', '$thumbprint', 'User')"
Write-Host '  puis rouvrez le terminal et relancez .\builder\build.ps1'
Write-Host ''
Write-Host 'Etape suivante -- faire confiance au certificat sur les postes :' -ForegroundColor Cyan
Write-Host '  Sans cela, rien ne change : un certificat auto-signe non approuve vaut'
Write-Host "  exactement un binaire non signe. Deployez $([System.IO.Path]::GetFileName($cerPath))"
Write-Host '  dans DEUX magasins de chaque poste, via GPO'
Write-Host '  (Configuration ordinateur > Parametres Windows > Parametres de securite'
Write-Host '   > Strategies de cle publique) :'
Write-Host '    - Autorites de certification racines de confiance'
Write-Host '    - Editeurs approuves'
Write-Host ''
Write-Host 'Hors de l''agence, ce certificat n''a aucun effet : prevoyez SignPath'
Write-Host 'Foundation (gratuit, open source) pour la diffusion externe.'
