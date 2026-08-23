#requires -Version 5.1
# Generates the ribbon icons of the MCPRVTT27 panel. Committing the generator
# rather than only the PNGs keeps the set reproducible: rerun it to change a
# colour or a size instead of hand-editing binaries nobody can review.
#
#   .\tools\make-ribbon-icons.ps1
#
# Revit wants two sizes per button: 32x32 (LargeImage) and 16x16 (Image).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\RevitCortex.Plugin\Resources'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# Mid-saturation tones stay legible on both the light and the dark Revit theme;
# a pure pastel disappears on light grey, a pure dark tone on the dark theme.
$amber = [System.Drawing.Color]::FromArgb(255, 214, 122,  28)   # locked
$green = [System.Drawing.Color]::FromArgb(255,  42, 150,  78)   # writable
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

# A padlock: rounded body plus a shackle drawn as an arc. Closed = shackle
# centred on the body; open = shifted and cut short, which is what makes the two
# states tell each other apart at 16 px where nothing else is readable.
function Draw-Padlock($ctx, $colour, [bool] $open) {
    $s = [double] $ctx.Size
    $g = $ctx.Graphics
    $brush = New-Object System.Drawing.SolidBrush($colour)

    $shackleWidth = [math]::Max(1.6, $s * 0.11)
    $pen = New-Object System.Drawing.Pen($colour, $shackleWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $radius = $s * 0.165
    $cx = if ($open) { $s * 0.63 } else { $s * 0.5 }
    $cy = $s * 0.43
    $sweep = if ($open) { 150.0 } else { 180.0 }
    $g.DrawArc($pen, [single]($cx - $radius), [single]($cy - $radius),
        [single]($radius * 2), [single]($radius * 2), [single]180, [single]$sweep)

    $bodyX = $s * 0.20; $bodyY = $s * 0.43
    $bodyW = $s * 0.60; $bodyH = $s * 0.44
    $r = [math]::Max(1.0, $s * 0.09)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc([single]$bodyX, [single]$bodyY, [single]($r * 2), [single]($r * 2), 180, 90)
    $path.AddArc([single]($bodyX + $bodyW - $r * 2), [single]$bodyY, [single]($r * 2), [single]($r * 2), 270, 90)
    $path.AddArc([single]($bodyX + $bodyW - $r * 2), [single]($bodyY + $bodyH - $r * 2), [single]($r * 2), [single]($r * 2), 0, 90)
    $path.AddArc([single]$bodyX, [single]($bodyY + $bodyH - $r * 2), [single]($r * 2), [single]($r * 2), 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    # The keyhole is punched out, not painted over: a light-grey dot would show
    # as a smudge on the dark theme.
    if ($s -ge 24) {
        $hole = $s * 0.15
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)),
            [single]($s * 0.5 - $hole / 2), [single]($s * 0.58), [single]$hole, [single]$hole)
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    }

    $path.Dispose(); $pen.Dispose(); $brush.Dispose()
}

# Status: a filled disc carrying a white "i". Recognisable as information at both
# sizes, and it does not compete with the two padlocks for meaning.
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

Write-Host 'Icones ecrites dans src\RevitCortex.Plugin\Resources :'
foreach ($size in 32, 16) {
    $ctx = New-Bitmap $size; Draw-Padlock $ctx $amber $false; Save-Bitmap $ctx "lock-$size.png"
    $ctx = New-Bitmap $size; Draw-Padlock $ctx $green $true;  Save-Bitmap $ctx "unlock-$size.png"
    $ctx = New-Bitmap $size; Draw-Status  $ctx $blue;         Save-Bitmap $ctx "status-$size.png"
}
