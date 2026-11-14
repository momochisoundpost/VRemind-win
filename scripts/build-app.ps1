<#
.SYNOPSIS
    Builds VRemindCoreCLI and publishes the unpackaged self-contained VRemindWin desktop app
    (compatible with Windows 10 version 1809 LTSC through Windows 11) into dist/VRemindWin/.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'dist\VRemindWin'
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

$cliStageDir = Join-Path $repoRoot 'artifacts\cli'
& (Join-Path $PSScriptRoot 'build-cli.ps1') -Configuration $Configuration -OutputDir $cliStageDir
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
& dotnet publish $winuiProj -c $Configuration -r win-x64 -o $OutputDir
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

Write-Host "Packaged VRemindWin ($Configuration) to: $OutputDir"
