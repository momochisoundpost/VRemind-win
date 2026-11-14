<#
.SYNOPSIS
    Generates app-icon.ico, tray-normal.ico, and tray-live.ico for VRemindWin.
#>
[CmdletBinding()]
param(
    [string]$SourcePng,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'VRemindWin\src\VRemindWin\Assets'
}
if ([string]::IsNullOrWhiteSpace($SourcePng)) {
    $bundledPng = Join-Path $repoRoot 'VRemindWin\src\VRemindWin\Assets\app-icon.png'
    $upstreamPng = Join-Path $repoRoot 'VRemind\Resources\Icons\app-icon.png'
    $SourcePng = if (Test-Path -LiteralPath $bundledPng) { $bundledPng } else { $upstreamPng }
}

if (-not (Test-Path -LiteralPath $SourcePng)) {
    throw "Source app icon PNG not found at: $SourcePng"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$destPng = Join-Path $OutputDir 'app-icon.png'
if ([System.IO.Path]::GetFullPath($SourcePng) -ne [System.IO.Path]::GetFullPath($destPng)) {
    Copy-Item -LiteralPath $SourcePng -Destination $destPng -Force
}

function Convert-BitmapToIconFrame {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap]$Bitmap,
        [switch]$UsePng
    )

    if ($UsePng -or $Bitmap.Width -ge 256 -or $Bitmap.Height -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        [byte[]]$bytes = $ms.ToArray()
        $ms.Dispose()
        return ,$bytes
    }

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $andStride = [int](([Math]::Floor(($w + 31) / 32)) * 4)
    $xorSize = $w * $h * 4
    $andSize = $andStride * $h

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER (40 bytes)
    $bw.Write([uint32]40)
    $bw.Write([int32]$w)
    $bw.Write([int32]($h * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]0)
    $bw.Write([uint32]$xorSize)
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0)

    # XOR mask: bottom-up BGRA
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $Bitmap.GetPixel($x, $y)
            $bw.Write([byte]$c.B)
            $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R)
            $bw.Write([byte]$c.A)
        }
    }

    # AND mask: bottom-up 1-bit transparency mask
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $andStride
        for ($x = 0; $x -lt $w; $x++) {
            $c = $Bitmap.GetPixel($x, $y)
            if ($c.A -lt 16) {
                $byteIndex = [int][Math]::Floor($x / 8)
                $bitIndex = 7 - ($x % 8)
                $row[$byteIndex] = $row[$byteIndex] -bor ([byte](1 -shl $bitIndex))
            }
        }
        $bw.Write($row, 0, $row.Length)
    }

    $bw.Flush()
    [byte[]]$bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return ,$bytes
}

function Write-MultiSizeIco {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap[]]$Bitmaps
    )

    $frames = New-Object System.Collections.Generic.List[object]
    foreach ($bmp in $Bitmaps) {
        [byte[]]$bytes = Convert-BitmapToIconFrame -Bitmap $bmp
        $frames.Add([PSCustomObject]@{
            Width  = $bmp.Width
            Height = $bmp.Height
            Bytes  = $bytes
        })
    }

    $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $bw = New-Object System.IO.BinaryWriter($fs)

    # ICONDIR
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        [byte[]]$frameBytes = $frame.Bytes
        $wByte = if ($frame.Width -ge 256) { [byte]0 } else { [byte]$frame.Width }
        $hByte = if ($frame.Height -ge 256) { [byte]0 } else { [byte]$frame.Height }
        $bw.Write($wByte)
        $bw.Write($hByte)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$frameBytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $frameBytes.Length
    }

    foreach ($frame in $frames) {
        [byte[]]$frameBytes = $frame.Bytes
        $bw.Write($frameBytes, 0, $frameBytes.Length)
    }

    $bw.Flush()
    $bw.Dispose()
    $fs.Dispose()
}

function New-ScaledBitmapFromPng {
    param(
        [System.Drawing.Image]$SourceImage,
        [int]$Size
    )

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($SourceImage, 0, 0, $Size, $Size)
    $g.Dispose()
    return $bmp
}

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $Radius * 2.0
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-TrayAntennaBitmap {
    param(
        [int]$Size,
        [bool]$IsLive
    )

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$Size / 16.0

    $bodyColor = if ($IsLive) {
        [System.Drawing.Color]::FromArgb(255, 239, 68, 68)
    } else {
        [System.Drawing.Color]::FromArgb(255, 244, 244, 245)
    }
    $outlineColor = if ($IsLive) {
        [System.Drawing.Color]::FromArgb(220, 127, 29, 29)
    } else {
        [System.Drawing.Color]::FromArgb(215, 24, 24, 27)
    }
    $innerColor = if ($IsLive) {
        [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    } else {
        [System.Drawing.Color]::FromArgb(255, 39, 39, 42)
    }

    # Antenna rabbit-ears
    $antennaOutlinePen = New-Object System.Drawing.Pen($outlineColor, [Math]::Max(2.2 * $s, 2.2))
    $antennaOutlinePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $antennaOutlinePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $antennaPen = New-Object System.Drawing.Pen($bodyColor, [Math]::Max(1.3 * $s, 1.3))
    $antennaPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $antennaPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $g.DrawLine($antennaOutlinePen, [float](4.0 * $s), [float](1.8 * $s), [float](7.4 * $s), [float](5.0 * $s))
    $g.DrawLine($antennaOutlinePen, [float](12.0 * $s), [float](1.8 * $s), [float](8.6 * $s), [float](5.0 * $s))
    $g.DrawLine($antennaPen, [float](4.0 * $s), [float](1.8 * $s), [float](7.4 * $s), [float](5.0 * $s))
    $g.DrawLine($antennaPen, [float](12.0 * $s), [float](1.8 * $s), [float](8.6 * $s), [float](5.0 * $s))

    # Rounded TV body
    $tvPath = New-RoundedRectPath -X (1.5 * $s) -Y (4.5 * $s) -Width (13.0 * $s) -Height (9.5 * $s) -Radius (2.2 * $s)
    $bodyBrush = New-Object System.Drawing.SolidBrush($bodyColor)
    $borderPen = New-Object System.Drawing.Pen($outlineColor, [Math]::Max(1.0 * $s, 1.0))
    $g.FillPath($bodyBrush, $tvPath)
    $g.DrawPath($borderPen, $tvPath)

    # Play / broadcast triangle inside TV screen
    $triPoints = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([float](6.2 * $s), [float](7.0 * $s))),
        (New-Object System.Drawing.PointF([float](6.2 * $s), [float](11.6 * $s))),
        (New-Object System.Drawing.PointF([float](10.6 * $s), [float](9.3 * $s)))
    )
    $innerBrush = New-Object System.Drawing.SolidBrush($innerColor)
    $g.FillPolygon($innerBrush, $triPoints)

    $innerBrush.Dispose()
    $borderPen.Dispose()
    $bodyBrush.Dispose()
    $tvPath.Dispose()
    $antennaPen.Dispose()
    $antennaOutlinePen.Dispose()
    $g.Dispose()
    return $bmp
}

$sourceImage = [System.Drawing.Image]::FromFile($SourcePng)
try {
    $appBitmaps = @(16, 32, 48, 256) | ForEach-Object { New-ScaledBitmapFromPng -SourceImage $sourceImage -Size $_ }
    Write-MultiSizeIco -Path (Join-Path $OutputDir 'app-icon.ico') -Bitmaps $appBitmaps
    foreach ($b in $appBitmaps) { $b.Dispose() }
}
finally {
    $sourceImage.Dispose()
}

$normalBitmaps = @(16, 32) | ForEach-Object { New-TrayAntennaBitmap -Size $_ -IsLive $false }
try {
    Write-MultiSizeIco -Path (Join-Path $OutputDir 'tray-normal.ico') -Bitmaps $normalBitmaps
}
finally {
    foreach ($b in $normalBitmaps) { $b.Dispose() }
}

$liveBitmaps = @(16, 32) | ForEach-Object { New-TrayAntennaBitmap -Size $_ -IsLive $true }
try {
    Write-MultiSizeIco -Path (Join-Path $OutputDir 'tray-live.ico') -Bitmaps $liveBitmaps
}
finally {
    foreach ($b in $liveBitmaps) { $b.Dispose() }
}

foreach ($icoName in @('app-icon.ico', 'tray-normal.ico', 'tray-live.ico')) {
    $icoPath = Join-Path $OutputDir $icoName
    $loaded = New-Object System.Drawing.Icon($icoPath)
    $loaded.Dispose()
}

Write-Host "Generated and verified icons in $OutputDir"
