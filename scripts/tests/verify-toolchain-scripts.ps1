$ErrorActionPreference = 'Stop'
$checkScript = Join-Path $PSScriptRoot '..\check-toolchain.ps1'
$installScript = Join-Path $PSScriptRoot '..\install-toolchain.ps1'

if (-not (Test-Path $checkScript)) {
    throw "Missing script: $checkScript"
}
if (-not (Test-Path $installScript)) {
    throw "Missing script: $installScript"
}

# 1. check-toolchain.ps1 -Json must output valid JSON with required keys and make zero network calls
$jsonRaw = & powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript -Json
$report = ($jsonRaw -join "`n") | ConvertFrom-Json
$requiredKeys = @('DotNetSdk9', 'VsCppAndWin10Sdk', 'SwiftWindows', 'Git', 'AllInstalled', 'EstimatedDownloadMB')
foreach ($k in $requiredKeys) {
    if ($null -eq $report.$k) {
        throw "Missing key $k in check-toolchain.ps1 output"
    }
}

# Validate structure of each prerequisite entry
foreach ($k in @('DotNetSdk9', 'VsCppAndWin10Sdk', 'SwiftWindows', 'Git')) {
    if ($null -eq $report.$k.Installed -or $null -eq $report.$k.EstimatedDownloadMB) {
        throw "Prerequisite '$k' must contain Installed and EstimatedDownloadMB fields"
    }
}

# 2. install-toolchain.ps1 without -ConfirmDownload must never download:
#    - exit 2 with -ConfirmDownload guard when prerequisites are missing
#    - exit 0 cleanly with "already installed" when all prerequisites are present
$output = & powershell -NoProfile -ExecutionPolicy Bypass -File $installScript 2>&1
if ($report.AllInstalled) {
    if ($LASTEXITCODE -ne 0) {
        throw "Expected exit code 0 when all prerequisites are installed, got $LASTEXITCODE"
    }
    if (($output -join "`n") -notmatch 'already installed') {
        throw "Expected message noting prerequisites are already installed"
    }
}
else {
    if ($LASTEXITCODE -ne 2) {
        throw "Expected exit code 2 when -ConfirmDownload is omitted, got $LASTEXITCODE"
    }
    if (($output -join "`n") -notmatch 'ConfirmDownload') {
        throw "Expected metered-data warning mentioning -ConfirmDownload"
    }
}

Write-Host "PASS: Toolchain scripts verified."
