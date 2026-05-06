$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-IconPng {
    param(
        [int]$Size,
        [string]$Path
    )

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    function S([float]$v) { [int][Math]::Round($v * $scale) }

    $screenRect = New-Object System.Drawing.Rectangle (S 24), (S 38), (S 208), (S 160)
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $radius = S 30
    $shape.AddArc($screenRect.X, $screenRect.Y, $radius, $radius, 180, 90)
    $shape.AddArc($screenRect.Right - $radius, $screenRect.Y, $radius, $radius, 270, 90)
    $shape.AddArc($screenRect.Right - $radius, $screenRect.Bottom - $radius, $radius, $radius, 0, 90)
    $shape.AddArc($screenRect.X, $screenRect.Bottom - $radius, $radius, $radius, 90, 90)
    $shape.CloseFigure()

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $screenRect, ([System.Drawing.Color]::FromArgb(255, 10, 14, 22)), ([System.Drawing.Color]::FromArgb(255, 22, 34, 52)), 90
    $g.FillPath($bg, $shape)
    $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 62, 112, 175)), (S 5)
    $g.DrawPath($border, $shape)

    $moonBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 209, 110))
    $cutBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 14, 23, 36))
    $g.FillEllipse($moonBrush, (S 72), (S 70), (S 58), (S 58))
    $g.FillEllipse($cutBrush, (S 91), (S 62), (S 58), (S 58))

    $barBack = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 64, 82, 106)), (S 13)
    $barFront = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 48, 130, 255)), (S 13)
    $barBack.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $barBack.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $barFront.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $barFront.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($barBack, (S 64), (S 157), (S 192), (S 157))
    $g.DrawLine($barFront, (S 64), (S 157), (S 142), (S 157))

    $knobBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 238, 244, 255))
    $g.FillEllipse($knobBrush, (S 132), (S 144), (S 26), (S 26))

    $standPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 72, 92, 120)), (S 9)
    $standPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $standPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($standPen, (S 128), (S 198), (S 128), (S 220))
    $g.DrawLine($standPen, (S 92), (S 222), (S 164), (S 222))

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $standPen.Dispose()
    $knobBrush.Dispose()
    $barBack.Dispose()
    $barFront.Dispose()
    $moonBrush.Dispose()
    $cutBrush.Dispose()
    $border.Dispose()
    $bg.Dispose()
    $shape.Dispose()
    $g.Dispose()
    $bmp.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngFiles = @()
foreach ($size in $sizes) {
    $path = Join-Path $assets "AppIcon-$size.png"
    New-IconPng -Size $size -Path $path
    $pngFiles += $path
}

Copy-Item -LiteralPath (Join-Path $assets "AppIcon-256.png") -Destination (Join-Path $assets "AppIcon.png") -Force

$icoPath = Join-Path $assets "AppIcon.ico"
$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter $fs
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$pngFiles.Count)

$offset = 6 + (16 * $pngFiles.Count)
$entries = @()
foreach ($file in $pngFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($file)
    $size = [int]([System.IO.Path]::GetFileNameWithoutExtension($file).Split('-')[-1])
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)
    $entries += ,$bytes
    $offset += $bytes.Length
}

foreach ($bytes in $entries) {
    $writer.Write($bytes)
}

$writer.Dispose()
$fs.Dispose()

Write-Host "Wrote $icoPath"
