<#
.SYNOPSIS
    Builds VRemindCoreCLI and publishes the unpackaged self-contained VRemindWin desktop app
    (x64: Windows 10 version 1809 LTSC through Windows 11; arm64: Windows 11 on ARM64) into dist/.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [string]$OutputDir,

    [string]$SwiftRuntimeDir = ''
)

$ErrorActionPreference = 'Stop'
$targetArch = $Architecture.ToLowerInvariant()

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = if ($targetArch -eq 'arm64') {
        Join-Path $repoRoot 'dist\VRemindWin-arm64'
    } else {
        Join-Path $repoRoot 'dist\VRemindWin'
    }
}

# Refresh toolchain environment variables in case the session started before installation.
$machinePath = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
if ($machinePath -or $userPath) {
    $env:PATH = (@($machinePath, $userPath) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ';'
}

if ([string]::IsNullOrWhiteSpace($env:SDKROOT)) {
    $userSdkRoot = [Environment]::GetEnvironmentVariable('SDKROOT', 'User')
    if (-not [string]::IsNullOrWhiteSpace($userSdkRoot)) {
        $env:SDKROOT = $userSdkRoot
    }
}

$assetsDir = Join-Path $repoRoot 'VRemindWin\src\VRemindWin\Assets'
$requiredIcons = @('app-icon.ico', 'app-icon.png', 'tray-normal.ico', 'tray-live.ico')
$missingIcon = $requiredIcons | Where-Object { -not (Test-Path -LiteralPath (Join-Path $assetsDir $_)) }
if ($missingIcon) {
    & (Join-Path $PSScriptRoot 'generate-icons.ps1')
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "generate-icons.ps1 failed with exit code $LASTEXITCODE"
    }
}

$cliStageDir = if ($targetArch -eq 'arm64') {
    Join-Path $repoRoot 'artifacts\cli-arm64'
} else {
    Join-Path $repoRoot 'artifacts\cli'
}
& (Join-Path $PSScriptRoot 'build-cli.ps1') -Configuration $Configuration -Architecture $targetArch -OutputDir $cliStageDir -SwiftRuntimeDir $SwiftRuntimeDir
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    throw "build-cli.ps1 failed with exit code $LASTEXITCODE"
}

$runningProcs = Get-Process -Name 'VRemindWin', 'vremind-core' -ErrorAction SilentlyContinue
if ($runningProcs) {
    Write-Host "Stopping running VRemindWin / vremind-core processes before packaging..."
    $runningProcs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
}

if (Test-Path -LiteralPath $OutputDir) {
    Get-ChildItem -LiteralPath $OutputDir -Force | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

$winuiProj = Join-Path $repoRoot 'VRemindWin\src\VRemindWin\VRemindWin.csproj'
$dotnetPlatform = $targetArch.ToUpperInvariant()
$dotnetRid = "win-$targetArch"
& dotnet publish $winuiProj -c $Configuration -r $dotnetRid "-p:Platform=$dotnetPlatform" -o $OutputDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item -Path (Join-Path $cliStageDir '*') -Destination $OutputDir -Force

$appExe = Join-Path $OutputDir 'VRemindWin.exe'
$cliExe = Join-Path $OutputDir 'vremind-core.exe'
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Published executable not found at: $appExe"
}
if (-not (Test-Path -LiteralPath $cliExe)) {
    throw "Bundled CLI executable not found at: $cliExe"
}

Write-Host "Packaged VRemindWin ($Configuration, $targetArch) to: $OutputDir"
