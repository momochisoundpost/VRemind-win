[CmdletBinding()]
param(
    [switch]$ConfirmDownload,
    [switch]$SkipDotNet,
    [switch]$SkipVsBuildTools,
    [switch]$SkipSwift,
    [switch]$SkipGit
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$checkScript = Join-Path $PSScriptRoot 'check-toolchain.ps1'
$jsonRaw = & powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript -Json
$report = ($jsonRaw -join "`n") | ConvertFrom-Json

if ($report.AllInstalled) {
    Write-Host "All prerequisites are already installed. No downloads required." -ForegroundColor Green
    exit 0
}

if (-not $ConfirmDownload) {
    Write-Warning "Metered connection guard: refusing to download missing packages (~$($report.EstimatedDownloadMB) MB)."
    Write-Host "Run '.\scripts\check-toolchain.ps1' to inspect missing items, or re-run with -ConfirmDownload from an Administrator PowerShell:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\install-toolchain.ps1 -ConfirmDownload"
    exit 2
}

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script from an Administrator PowerShell window (Right-click Start -> Windows PowerShell (Admin))."
    exit 3
}

function Invoke-FastDownload {
    param(
        [string]$Url,
        [string]$OutFile,
        [string]$Label
    )
    if ((Test-Path $OutFile) -and ((Get-Item $OutFile).Length -gt 0)) {
        Write-Host "Using cached $Label at $OutFile" -ForegroundColor DarkGray
        return
    }
    Write-Host "Downloading $Label..." -ForegroundColor Cyan
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source -fL --progress-bar -o $OutFile $Url
        if ($LASTEXITCODE -ne 0) {
            throw "curl.exe failed downloading $Label (exit code $LASTEXITCODE)"
        }
    } else {
        Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    }
}

$tempDir = Join-Path $env:TEMP 'vremind-toolchain'
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# 1. Git for Windows
if (-not $SkipGit -and -not $report.Git.Installed) {
    $cachedGit = Get-ChildItem -Path $tempDir -Filter 'Git-*-64-bit.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -gt 10MB } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($cachedGit) {
        $gitInstaller = $cachedGit.FullName
        Write-Host "Using cached Git installer at $gitInstaller" -ForegroundColor DarkGray
    } else {
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/git-for-windows/git/releases/latest' -Headers @{ 'User-Agent' = 'VRemind-win-Setup' }
        $asset = $release.assets | Where-Object { $_.name -match '^Git-.*-64-bit\.exe$' } | Select-Object -First 1
        if (-not $asset) { throw "Could not locate Git 64-bit installer asset." }
        $gitInstaller = Join-Path $tempDir $asset.name
        Invoke-FastDownload -Url $asset.browser_download_url -OutFile $gitInstaller -Label $asset.name
    }
    Write-Host "Installing Git for Windows..." -ForegroundColor Cyan
    $gitProc = Start-Process -FilePath $gitInstaller -ArgumentList '/VERYSILENT /NORESTART /SP- /SUPPRESSMSGBOXES' -Wait -PassThru
    if ($gitProc.ExitCode -ne 0) {
        throw "Git installer failed with exit code $($gitProc.ExitCode)"
    }
}

# 2. .NET 9 SDK
if (-not $SkipDotNet -and -not $report.DotNetSdk9.Installed) {
    $dotnetInstaller = Join-Path $tempDir 'dotnet-sdk-win-x64.exe'
    Invoke-FastDownload -Url 'https://aka.ms/dotnet/9.0/dotnet-sdk-win-x64.exe' -OutFile $dotnetInstaller -Label '.NET 9 SDK (x64)'
    Write-Host "Installing .NET 9 SDK..." -ForegroundColor Cyan
    $dotnetProc = Start-Process -FilePath $dotnetInstaller -ArgumentList '/install /quiet /norestart' -Wait -PassThru
    if ($dotnetProc.ExitCode -notin @(0, 3010)) {
        throw ".NET 9 SDK installer failed with exit code $($dotnetProc.ExitCode)"
    }
}

# 3. Visual Studio 2022 Build Tools + Windows SDK
if (-not $SkipVsBuildTools -and -not $report.VsCppAndWin10Sdk.Installed) {
    $vsInstaller = Join-Path $tempDir 'vs_buildtools.exe'
    Invoke-FastDownload -Url 'https://aka.ms/vs/17/release/vs_buildtools.exe' -OutFile $vsInstaller -Label 'Visual Studio Build Tools bootstrapper'
    Write-Host "Installing Visual Studio C++ Build Tools + Windows SDK (this takes a few minutes)..." -ForegroundColor Cyan
    $vsArgs = @(
        '--quiet', '--wait', '--norestart', '--nocache',
        '--add', 'Microsoft.VisualStudio.Workload.VCTools',
        '--includeRecommended'
    )
    $vsProc = Start-Process -FilePath $vsInstaller -ArgumentList $vsArgs -Wait -PassThru
    if ($vsProc.ExitCode -notin @(0, 3010)) {
        throw "VS Build Tools installer failed with exit code $($vsProc.ExitCode)"
    }
}

# 4. Swift for Windows
if (-not $SkipSwift -and -not $report.SwiftWindows.Installed) {
    $swiftInstaller = Join-Path $tempDir 'swift-6.0.3-RELEASE-windows10.exe'
    Invoke-FastDownload -Url 'https://download.swift.org/swift-6.0.3-release/windows10/swift-6.0.3-RELEASE/swift-6.0.3-RELEASE-windows10.exe' -OutFile $swiftInstaller -Label 'Swift 6.0.3 for Windows x64'
    Write-Host "Installing Swift 6.0.3 for Windows..." -ForegroundColor Cyan
    $swiftProc = Start-Process -FilePath $swiftInstaller -ArgumentList '/quiet' -Wait -PassThru
    if ($swiftProc.ExitCode -notin @(0, 3010)) {
        throw "Swift installer failed with exit code $($swiftProc.ExitCode)"
    }
}

Write-Host ""
Write-Host "Re-checking toolchain..." -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript
exit $LASTEXITCODE
