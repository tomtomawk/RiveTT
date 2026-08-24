#requires -Version 5.1
# Generates the status ribbon icon of the RiveTT panel. lock-*.png and
# unlock-*.png are hand-authored artwork (isometric plates/bolt) and live only
# as committed PNGs under src\RiveTT.Plugin\Resources - this script does not
# touch them.
#
#   .\tools\make-ribbon-icons.ps1
#
# Revit wants two sizes per button: 32x32 (LargeImage) and 16x16 (Image).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\RiveTT.Plugin\Resources'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# Mid-saturation tone stays legible on both the light and the dark Revit theme;
# a pure pastel disappears on light grey, a pure dark tone on the dark theme.
$blue  = [System.Drawing.Color]::FromArgb(255,   6, 150, 215)   # status

function New-Bitmap([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    return @{ Bitmap = $bmp; Graphics = $g; Size = $size }
}

function Save-Bitmap($ctx, [string] $name) {
    $path = Join-Path $outDir $name
    $ctx.Graphics.Dispose()
    $ctx.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $ctx.Bitmap.Dispose()
    Write-Host ("  " + $name)
}


# Status: a filled disc carrying a white "i". Recognisable as information at both
# sizes, and it does not compete with the lock/unlock artwork for meaning.
function Draw-Status($ctx, $colour) {
    $s = [double] $ctx.Size
    $g = $ctx.Graphics
    $brush = New-Object System.Drawing.SolidBrush($colour)
    $inset = $s * 0.06
    $g.FillEllipse($brush, [single]$inset, [single]$inset,
        [single]($s - $inset * 2), [single]($s - $inset * 2))

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $dot = [math]::Max(1.5, $s * 0.13)
    $g.FillEllipse($white, [single]($s * 0.5 - $dot / 2), [single]($s * 0.22), [single]$dot, [single]$dot)
    $barW = [math]::Max(1.5, $s * 0.13)
    $g.FillRectangle($white, [single]($s * 0.5 - $barW / 2), [single]($s * 0.42),
        [single]$barW, [single]($s * 0.34))

    $white.Dispose(); $brush.Dispose()
}

Write-Host 'Icones ecrites dans src\RiveTT.Plugin\Resources :'
foreach ($size in 32, 16) {
    $ctx = New-Bitmap $size; Draw-Status $ctx $blue; Save-Bitmap $ctx "status-$size.png"
}
