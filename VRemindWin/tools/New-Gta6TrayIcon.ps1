$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'src\VRemindWin\Assets'
$sourcePath = Join-Path $assetsDir 'tray-live.ico'
$targetPath = Join-Path $assetsDir 'tray-gta6.ico'

$bytes = [System.IO.File]::ReadAllBytes($sourcePath)
$count = [BitConverter]::ToUInt16($bytes, 4)

for ($i = 0; $i -lt $count; $i++) {
    $dirOffset = 6 + ($i * 16)
    $w = if ($bytes[$dirOffset] -eq 0) { 256 } else { [int]$bytes[$dirOffset] }
    $h = if ($bytes[$dirOffset + 1] -eq 0) { 256 } else { [int]$bytes[$dirOffset + 1] }
    $imgOffset = [BitConverter]::ToInt32($bytes, $dirOffset + 12)

    $headerSize = [BitConverter]::ToInt32($bytes, $imgOffset)
    $bpp = [BitConverter]::ToUInt16($bytes, $imgOffset + 14)
    $compression = [BitConverter]::ToUInt32($bytes, $imgOffset + 16)
    if ($headerSize -ne 40 -or $bpp -ne 32 -or $compression -ne 0) {
        throw "Unexpected DIB format at entry ${i}: header=$headerSize, bpp=$bpp, compression=$compression"
    }

    $pixelStart = $imgOffset + 40
    $pixelCount = $w * $h
    for ($p = 0; $p -lt $pixelCount; $p++) {
        $idx = $pixelStart + ($p * 4)
        $b = [int]$bytes[$idx]
        $g = [int]$bytes[$idx + 1]
        $r = [int]$bytes[$idx + 2]

        if ($r -gt ($g + 30) -and $r -gt ($b + 30)) {
            $s = $r / 239.0
            $bytes[$idx]     = [byte][Math]::Round([Math]::Min(255.0, [Math]::Max(0.0, 0xF7 * $s)))
            $bytes[$idx + 1] = [byte][Math]::Round([Math]::Min(255.0, [Math]::Max(0.0, 0x55 * $s)))
            $bytes[$idx + 2] = [byte][Math]::Round([Math]::Min(255.0, [Math]::Max(0.0, 0xA8 * $s)))
        }
    }
}

[System.IO.File]::WriteAllBytes($targetPath, $bytes)
Write-Host "Wrote $targetPath ($($bytes.Length) bytes)"
