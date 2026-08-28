#requires -Version 5.1
<#
.SYNOPSIS
    Registers (or removes) the RiveTT MCP server in a desktop AI client's own config.

.DESCRIPTION
    Called by the RiveTT installer from an optional, unchecked task. Two clients, two
    completely different formats, and neither file belongs to us -- they hold the user's
    whole configuration for that product. Every path below is built on the same rule:
    touch the RiveTT entry and nothing else, prove it, and put the file back if the
    proof fails.

        Claude   %APPDATA%\Claude\claude_desktop_config.json     JSON
        Codex    %CODEX_HOME%\config.toml (default ~/.codex)     TOML

    ENCODING: this file is UTF-8 WITH BOM and must stay that way, for the same reason
    builder\build.ps1 is. Windows PowerShell 5.1 reads a BOM-less script as
    Windows-1252, so the accented French strings below would decode into garbage --
    including curly quotes, which PowerShell honours as string delimiters.
    BuildScriptEncodingTests fails the suite if the BOM goes missing.

    Three facts measured on a real workstation on 2026-08-28, each of which would have
    caused a silent failure:

    1. Claude Desktop is an MSIX package. Its config exists at two paths that are a
       HARD LINK to one file: %APPDATA%\Claude\... and
       %LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\...
       Writing in place keeps the link. Writing a temp file and renaming over the
       target -- the usual "safe" pattern -- BREAKS it: the packaged app would go on
       reading the old content and the registration would appear to succeed while doing
       nothing. Everything here writes in place, and checks the link afterwards.

    2. ConvertTo-Json in PowerShell 5.1 defaults to -Depth 2. The Claude config is 6
       levels deep and holds per-folder permission grants. At the default depth those
       become the string "@{pinnedOrder=System.Object[]}" -- irreversibly. Depth 100
       everywhere, and a whole-document comparison before the write is committed.

    3. Neither file has a BOM and both use LF. Claude Desktop is Electron: Node's
       JSON.parse throws on a leading BOM. So: UTF8Encoding($false), LF preserved.

.PARAMETER Client
    Claude or Codex.

.PARAMETER ServerPath
    Full path to RiveTT.Server.exe. Required unless -Remove.

.PARAMETER Remove
    Delete the RiveTT entry instead of writing it. Used at uninstall, so the client is
    not left launching an executable that no longer exists.

.OUTPUTS
    Exit code 0 registered, updated, already correct, or removed.
               1 failure -- the config was restored from the rolling backup.
               3 client not installed, nothing done.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Claude', 'Codex')][string] $Client,
    [string] $ServerPath,
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'

$logPath = Join-Path $env:LOCALAPPDATA "RiveTT\register-mcp-$Client.log"

function Write-Log {
    param([string] $Message)
    $line = '{0}  {1}' -f (Get-Date -Format 's'), $Message
    try {
        $dir = Split-Path -Parent $logPath
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch { }
    Write-Host $line
}

function Stop-WithFailure {
    param([string] $Message)
    Write-Log "ECHEC : $Message"
    exit 1
}

<#
    Rolling backup: ONE file per config, overwritten at every run. It always holds the
    state immediately before the last modification, which is the only state anyone ever
    wants back. Timestamped copies accumulated instead -- three were already lying
    around this profile, from August 19 and 25, and nobody was ever going to read them.
#>
function Backup-Config {
    param([string] $Path)
    $backup = "$Path.bak-rivett"
    Copy-Item -LiteralPath $Path -Destination $backup -Force
    Write-Log "Sauvegarde : $backup"
    return $backup
}

function Restore-Config {
    param([string] $Path, [string] $Backup)
    if ($Backup -and (Test-Path -LiteralPath $Backup)) {
        Copy-Item -LiteralPath $Backup -Destination $Path -Force
        Write-Log "Fichier restaure depuis la sauvegarde."
    }
}

<#
    In place, no BOM, and never through a temp file: see fact 1 in the header. The
    encoding object is built explicitly because Set-Content -Encoding UTF8 emits a BOM
    on PowerShell 5.1.
#>
function Write-InPlace {
    param([string] $Path, [string] $Text)
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

<#
    ConvertTo-Json emits CRLF on PowerShell 5.1 and both client configs are LF. Left
    alone, a five-line registration rewrites every line of the file: harmless to the
    parser, but it turns "we touched one entry" into a diff nobody can review, and the
    promise this whole script is built on stops being checkable. So the original
    convention wins, and a file we create ourselves gets LF like the ones we have seen.
#>
function ConvertTo-OriginalNewlines {
    param([string] $Text, [string] $Original)
    if ($Original -and $Original.Contains("`r`n")) { return $Text }
    return $Text.Replace("`r`n", "`n")
}

# ---------------------------------------------------------------------------- Claude

function Get-ClaudeConfigPath {
    $dir = Join-Path $env:APPDATA 'Claude'
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    return Join-Path $dir 'claude_desktop_config.json'
}

<#
    Serializes everything EXCEPT our own entry, so before and after can be compared as
    text. If these two strings differ, something other than mcpServers.RiveTT moved and
    the write must not be committed.
#>
function Get-JsonFingerprint {
    param([object] $Config)
    $clone = $Config | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    if ($clone.PSObject.Properties.Name -contains 'mcpServers' -and $clone.mcpServers) {
        $clone.mcpServers.PSObject.Properties.Remove('RiveTT')
    }
    return ($clone | ConvertTo-Json -Depth 100)
}

function Update-ClaudeConfig {
    $path = Get-ClaudeConfigPath
    if (-not $path) {
        Write-Log 'Claude Desktop non detecte (aucun dossier %APPDATA%\Claude). Rien fait.'
        exit 3
    }

    $backup = $null
    if (Test-Path -LiteralPath $path) {
        $raw = [System.IO.File]::ReadAllText($path)
        try { $config = $raw | ConvertFrom-Json } catch {
            Stop-WithFailure "le fichier $path n'est pas un JSON lisible. Aucune modification."
        }
        $backup = Backup-Config -Path $path
    } else {
        if ($Remove) { Write-Log 'Aucun fichier de configuration. Rien a retirer.'; exit 0 }
        Write-Log "Aucun fichier de configuration : creation de $path"
        $config = [pscustomobject]@{}
        $raw = ''
    }

    # Measured BEFORE the write, and this ordering is the whole safeguard: the mirror is
    # only ours to touch if it was the same file to begin with. Without it, editing any
    # config that is not the canonical one -- a test copy, a relocated profile -- would
    # overwrite the real workstation file with content that does not belong to it.
    $wasLinked = Test-ClaudeMirrorMatches -Path $path

    $before = Get-JsonFingerprint -Config $config

    if (-not ($config.PSObject.Properties.Name -contains 'mcpServers') -or -not $config.mcpServers) {
        if ($Remove) { Write-Log 'Aucune section mcpServers. Rien a retirer.'; exit 0 }
        $config | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue ([pscustomobject]@{}) -Force
    }

    if ($Remove) {
        if (-not ($config.mcpServers.PSObject.Properties.Name -contains 'RiveTT')) {
            Write-Log 'Entree RiveTT absente. Rien a retirer.'
            exit 0
        }
        $config.mcpServers.PSObject.Properties.Remove('RiveTT')
        Write-Log 'Entree RiveTT retiree.'
    } else {
        $existing = $config.mcpServers.RiveTT
        if ($existing -and $existing.command -eq $ServerPath) {
            Write-Log "Entree RiveTT deja correcte ($ServerPath). Aucune ecriture."
            exit 0
        }
        $config.mcpServers | Add-Member -NotePropertyName 'RiveTT' `
            -NotePropertyValue ([pscustomobject]@{ command = $ServerPath }) -Force
        Write-Log "Entree RiveTT ecrite : $ServerPath"
    }

    $text = ConvertTo-OriginalNewlines -Text ($config | ConvertTo-Json -Depth 100) -Original $raw

    # The proof, before anything is written: re-parse our own output and check that
    # every part of the document we do not own came back identical.
    try { $roundTrip = $text | ConvertFrom-Json } catch {
        Stop-WithFailure 'la sortie JSON produite est illisible. Aucune modification.'
    }
    if ((Get-JsonFingerprint -Config $roundTrip) -ne $before) {
        Stop-WithFailure ('la reserialisation a modifie autre chose que l''entree RiveTT. ' +
                          'Aucune modification (voir la sauvegarde).')
    }

    try {
        Write-InPlace -Path $path -Text $text
    } catch {
        Restore-Config -Path $path -Backup $backup
        Stop-WithFailure "ecriture impossible dans $path : $($_.Exception.Message)"
    }

    Assert-ClaudeHardLink -Path $path -WasLinked $wasLinked
    Write-Log 'Termine.'
}

<#
    Fact 1 again, verified rather than assumed. If the two paths have drifted apart the
    packaged app is reading the copy we did not write, so the content is pushed there
    too -- in place, same rule.
#>
function Get-ClaudeMirrorPath {
    return (Join-Path $env:LOCALAPPDATA `
        'Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json')
}

function Test-ClaudeMirrorMatches {
    param([string] $Path)
    $mirror = Get-ClaudeMirrorPath
    if (-not (Test-Path -LiteralPath $mirror)) { return $false }
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq
           (Get-FileHash -LiteralPath $mirror -Algorithm SHA256).Hash
}

function Assert-ClaudeHardLink {
    param([string] $Path, [bool] $WasLinked)
    if (-not $WasLinked) {
        Write-Log 'Pas de vue packagee liee a ce fichier : rien a verifier.'
        return
    }
    if (Test-ClaudeMirrorMatches -Path $Path) {
        Write-Log 'Vue packagee identique (lien dur intact).'
        return
    }

    Write-Log 'Vue packagee differente : le lien dur a ete rompu, recopie en place.'
    Write-InPlace -Path (Get-ClaudeMirrorPath) -Text ([System.IO.File]::ReadAllText($Path))
}

# ----------------------------------------------------------------------------- Codex

function Get-CodexConfigPath {
    $home_ = $env:CODEX_HOME
    if (-not $home_) { $home_ = Join-Path $env:USERPROFILE '.codex' }
    if (-not (Test-Path -LiteralPath $home_)) { return $null }
    return Join-Path $home_ 'config.toml'
}

<#
    No TOML parser exists on PowerShell 5.1, and that is fine: not having one is what
    forces the safest possible edit. The file holds plugin settings, per-project trust
    levels and environment blocks; reserializing it would put all of that at risk to
    change five lines. So the section is located by text, replaced by text, and every
    other byte of the file is left exactly as it was.

    A section runs from its header to the next line starting with '[' -- excluding its
    own sub-tables, [mcp_servers.RiveTT.env] and the like, which belong to it.
#>
function Remove-TomlSection {
    param([string] $Text, [string] $Section)

    $lines = $Text -split "`n"
    $out = New-Object System.Collections.Generic.List[string]
    $inSection = $false

    foreach ($line in $lines) {
        $trimmed = $line.TrimEnd("`r")
        if ($trimmed -match '^\s*\[') {
            $isOurs = ($trimmed -match "^\s*\[$([regex]::Escape($Section))\]\s*$") -or
                      ($trimmed -match "^\s*\[$([regex]::Escape($Section))\.")
            $inSection = $isOurs
            if ($inSection) { continue }
        }
        if (-not $inSection) { $out.Add($line) }
    }
    return ($out -join "`n")
}

function Update-CodexConfig {
    $path = Get-CodexConfigPath
    if (-not $path) {
        Write-Log 'Codex non detecte (aucun dossier .codex). Rien fait.'
        exit 3
    }

    $backup = $null
    if (Test-Path -LiteralPath $path) {
        $raw = [System.IO.File]::ReadAllText($path)
        $backup = Backup-Config -Path $path
    } else {
        if ($Remove) { Write-Log 'Aucun fichier de configuration. Rien a retirer.'; exit 0 }
        Write-Log "Aucun fichier de configuration : creation de $path"
        $raw = ''
    }

    $section = 'mcp_servers.RiveTT'
    $stripped = Remove-TomlSection -Text $raw -Section $section

    if ($Remove) {
        if ($stripped -eq $raw) { Write-Log 'Entree RiveTT absente. Rien a retirer.'; exit 0 }
        $text = $stripped.TrimEnd("`n") + "`n"
        Write-Log 'Entree RiveTT retiree.'
    } else {
        # TOML basic strings take C-style escapes: the backslashes of a Windows path
        # must be doubled or the value silently becomes something else.
        $escaped = $ServerPath.Replace('\', '\\').Replace('"', '\"')
        $block = @(
            "[$section]",
            "command = `"$escaped`"",
            'startup_timeout_sec = 20',
            'tool_timeout_sec = 300',
            'enabled = true'
        ) -join "`n"

        if ($raw -match [regex]::Escape($block)) {
            Write-Log "Entree RiveTT deja correcte ($ServerPath). Aucune ecriture."
            exit 0
        }

        $body = $stripped.TrimEnd("`n")
        $text = if ($body) { "$body`n`n$block`n" } else { "$block`n" }
        Write-Log "Entree RiveTT ecrite : $ServerPath"
    }

    # Same proof as the JSON path, expressed in the terms this format allows: strip our
    # own section from the old text and from the new one. What remains must be identical
    # byte for byte, or we changed something that was not ours.
    $checkBefore = (Remove-TomlSection -Text $raw -Section $section).TrimEnd("`n")
    $checkAfter = (Remove-TomlSection -Text $text -Section $section).TrimEnd("`n")
    if ($checkBefore -ne $checkAfter) {
        Stop-WithFailure ('l''edition a modifie autre chose que la section RiveTT. ' +
                          'Aucune modification (voir la sauvegarde).')
    }

    try {
        Write-InPlace -Path $path -Text $text
    } catch {
        Restore-Config -Path $path -Backup $backup
        Stop-WithFailure "ecriture impossible dans $path : $($_.Exception.Message)"
    }
    Write-Log 'Termine.'
}

# ------------------------------------------------------------------------------ main

Write-Log "--- $Client / $(if ($Remove) { 'retrait' } else { 'enregistrement' }) ---"

if (-not $Remove -and -not $ServerPath) {
    Stop-WithFailure '-ServerPath est requis pour un enregistrement.'
}
if (-not $Remove -and -not (Test-Path -LiteralPath $ServerPath)) {
    Stop-WithFailure "le serveur est introuvable : $ServerPath"
}

try {
    switch ($Client) {
        'Claude' { Update-ClaudeConfig }
        'Codex'  { Update-CodexConfig }
    }
} catch {
    Stop-WithFailure $_.Exception.Message
}
exit 0
