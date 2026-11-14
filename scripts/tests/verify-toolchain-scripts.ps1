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
$requiredKeys = @('TargetArchitecture', 'DotNetSdk9', 'VsCppAndWin10Sdk', 'SwiftWindows', 'Git', 'AllInstalled', 'EstimatedDownloadMB')
foreach ($k in $requiredKeys) {
    if ($null -eq $report.$k) {
        throw "Missing key $k in check-toolchain.ps1 output"
    }
}
if ($report.TargetArchitecture -ne 'x64') {
    throw "Expected default TargetArchitecture 'x64', got '$($report.TargetArchitecture)'"
}

# Validate structure of each prerequisite entry
foreach ($k in @('DotNetSdk9', 'VsCppAndWin10Sdk', 'SwiftWindows', 'Git')) {
    if ($null -eq $report.$k.Installed -or $null -eq $report.$k.EstimatedDownloadMB) {
        throw "Prerequisite '$k' must contain Installed and EstimatedDownloadMB fields"
    }
}

# 1b. check-toolchain.ps1 -Architecture arm64 -Json must return TargetArchitecture 'arm64' and valid fields
$armJsonRaw = & powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript -Architecture arm64 -Json
$armReport = ($armJsonRaw -join "`n") | ConvertFrom-Json
if ($armReport.TargetArchitecture -ne 'arm64') {
    throw "Expected TargetArchitecture 'arm64', got '$($armReport.TargetArchitecture)'"
}
foreach ($k in @('DotNetSdk9', 'VsCppAndWin10Sdk', 'SwiftWindows', 'Git')) {
    if ($null -eq $armReport.$k.Installed -or $null -eq $armReport.$k.EstimatedDownloadMB) {
        throw "ARM64 prerequisite '$k' must contain Installed and EstimatedDownloadMB fields"
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

# 2b. install-toolchain.ps1 -Architecture arm64 without -ConfirmDownload must also respect metered guard
$armInstallOut = & powershell -NoProfile -ExecutionPolicy Bypass -File $installScript -Architecture arm64 2>&1
if ($armReport.AllInstalled) {
    if ($LASTEXITCODE -ne 0) {
        throw "Expected exit code 0 when all ARM64 prerequisites are installed, got $LASTEXITCODE"
    }
}
else {
    if ($LASTEXITCODE -ne 2) {
        throw "Expected exit code 2 for ARM64 when -ConfirmDownload is omitted, got $LASTEXITCODE"
    }
    if (($armInstallOut -join "`n") -notmatch 'ConfirmDownload') {
        throw "Expected metered-data warning mentioning -ConfirmDownload for ARM64"
    }
}

# 3. VRemindWin.csproj and build scripts must declare multi-arch x64/ARM64 support
$repoRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$csprojPath = Join-Path $repoRoot 'VRemindWin\src\VRemindWin\VRemindWin.csproj'
$csprojText = Get-Content -LiteralPath $csprojPath -Raw
if ($csprojText -notmatch '<Platforms>x64;ARM64</Platforms>') {
    throw "VRemindWin.csproj must declare <Platforms>x64;ARM64</Platforms>"
}
if ($csprojText -notmatch '<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>') {
    throw "VRemindWin.csproj must declare <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>"
}

$buildCliText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts\build-cli.ps1') -Raw
$buildAppText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts\build-app.ps1') -Raw
if ($buildCliText -notmatch '\$Architecture' -or $buildCliText -notmatch 'aarch64-unknown-windows-msvc') {
    throw "build-cli.ps1 must support -Architecture arm64 and aarch64-unknown-windows-msvc"
}
if ($buildAppText -notmatch '\$Architecture' -or $buildAppText -notmatch 'VRemindWin-arm64') {
    throw "build-app.ps1 must support -Architecture arm64 and dist\VRemindWin-arm64"
}

# 4. GitHub Actions ARM64 workflow and volunteer documentation must exist and reference Windows 11 ARM64
$workflowPath = Join-Path $repoRoot '.github\workflows\windows-arm64.yml'
if (-not (Test-Path -LiteralPath $workflowPath)) {
    throw "Missing GitHub Actions workflow at $workflowPath"
}
$workflowText = Get-Content -LiteralPath $workflowPath -Raw
if ($workflowText -notmatch 'windows-11-arm' -or $workflowText -notmatch '-Architecture arm64' -or $workflowText -notmatch 'VRemindWin-arm64\.zip') {
    throw "windows-arm64.yml must run on windows-11-arm, invoke -Architecture arm64, and package VRemindWin-arm64.zip"
}

$armDocPath = Join-Path $repoRoot 'docs\WINDOWS-ARM64.md'
if (-not (Test-Path -LiteralPath $armDocPath)) {
    throw "Missing ARM64 volunteer guide at $armDocPath"
}
$armDocText = Get-Content -LiteralPath $armDocPath -Raw
if ($armDocText -notmatch 'Windows 11' -or $armDocText -notmatch 'Windows 10' -or $armDocText -notmatch '-Architecture arm64') {
    throw "docs\WINDOWS-ARM64.md must specify Windows 11 ARM64 requirement, Windows 10 ARM exclusion, and -Architecture arm64 build instructions"
}

$readmeText = Get-Content -LiteralPath (Join-Path $repoRoot 'README.md') -Raw
if ($readmeText -notmatch 'docs/WINDOWS-ARM64\.md') {
    throw "README.md must link to docs/WINDOWS-ARM64.md"
}

Write-Host "PASS: Toolchain scripts verified."
