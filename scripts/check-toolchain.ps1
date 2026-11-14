[CmdletBinding()]
param(
    [switch]$Json,

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$targetArch = $Architecture.ToLowerInvariant()

function Find-ExecutablePath {
    param([string[]]$Candidates)
    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $resolved = Get-Item -Path $candidate -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($resolved -and (Test-Path $resolved.FullName)) {
            return $resolved.FullName
        }
    }
    return $null
}

# 1. .NET 9 SDK
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
$dotnetPath = Find-ExecutablePath @(
    $dotnetCmd,
    (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
)
$dotnet9Installed = $false
$dotnet9Detail = 'Not installed'
if ($dotnetPath) {
    $sdks = & $dotnetPath --list-sdks 2>$null
    $sdk9 = $sdks | Where-Object { $_ -match '^9\.' } | Select-Object -First 1
    if ($sdk9) {
        $dotnet9Installed = $true
        $dotnet9Detail = "$sdk9 ($dotnetPath)"
    } else {
        $dotnet9Detail = "dotnet found at $dotnetPath, but no 9.x SDK installed"
    }
}

# 2. Visual Studio C++ Build Tools + Windows 10/11 SDK (>= 10.0.17763.0)
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsCppInstalled = $false
$vsDetail = 'Not installed'
$vcComponent = if ($targetArch -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' } else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }
$vcShortLabel = if ($targetArch -eq 'arm64') { 'VC.Tools.ARM64' } else { 'VC.Tools.x86.x64' }
if (Test-Path $vswherePath) {
    $vsInstall = & $vswherePath -all -products * -requires $vcComponent -property displayName 2>$null | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($vsInstall)) {
        $vsCppInstalled = $true
        $vsDetail = "$vsInstall ($vcShortLabel)"
    } else {
        $vsDetail = "Visual Studio Installer found, but $vcShortLabel is missing"
    }
}

$winSdkIncludeRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
$winSdkVersion = $null
if (Test-Path $winSdkIncludeRoot) {
    $winSdkVersion = Get-ChildItem -Path $winSdkIncludeRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        ForEach-Object { [Version]$_.Name } |
        Where-Object { $_ -ge [Version]'10.0.17763.0' } |
        Sort-Object -Descending |
        Select-Object -First 1
}
$winSdkInstalled = ($null -ne $winSdkVersion)
$vsAndSdkInstalled = $vsCppInstalled -and $winSdkInstalled
$vsAndSdkEstMB = if ($vsAndSdkInstalled) { 0 } elseif (-not $vsCppInstalled) { 2800 } else { 900 }
$vsAndSdkDetail = if ($vsAndSdkInstalled) {
    "$vsDetail + Windows SDK $winSdkVersion"
} elseif (-not $vsCppInstalled -and -not $winSdkInstalled) {
    'Missing VS C++ Build Tools and Windows 10 SDK (>= 10.0.17763.0)'
} elseif (-not $vsCppInstalled) {
    "Windows SDK $winSdkVersion found, but VS C++ Build Tools missing"
} else {
    "$vsDetail found, but Windows 10 SDK (>= 10.0.17763.0) missing"
}

# 3. Swift for Windows (>= 5.9)
$swiftCmd = Get-Command swift -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
$swiftPath = Find-ExecutablePath @(
    $swiftCmd,
    (Join-Path $env:LOCALAPPDATA 'Programs\Swift\Toolchains\*\usr\bin\swift.exe'),
    'C:\Library\Developer\Toolchains\*\usr\bin\swift.exe',
    (Join-Path $env:ProgramFiles 'Swift\Toolchains\*\usr\bin\swift.exe')
)
$swiftInstalled = $false
$swiftDetail = 'Not installed'
if ($swiftPath) {
    $swiftVerOut = (& $swiftPath --version 2>$null | Out-String).Trim()
    if ($swiftVerOut -match 'Swift version (\d+)\.(\d+)') {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        if ($major -gt 5 -or ($major -eq 5 -and $minor -ge 9)) {
            $swiftInstalled = $true
        }
        $swiftDetail = "Swift $major.$minor ($swiftPath)"
    } else {
        $swiftInstalled = $true
        $swiftDetail = $swiftPath
    }
}

# 4. Git
$gitCmd = Get-Command git -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
$gitPath = Find-ExecutablePath @(
    $gitCmd,
    (Join-Path $env:ProgramFiles 'Git\cmd\git.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Git\cmd\git.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe')
)
$gitInstalled = ($null -ne $gitPath)
$gitDetail = if ($gitInstalled) {
    $gitVer = (& $gitPath --version 2>$null | Out-String).Trim()
    "$gitVer ($gitPath)"
} else {
    'Not installed'
}

$dotNetEstMB = if ($dotnet9Installed) { 0 } else { 215 }
$swiftEstMB = if ($swiftInstalled) { 0 } else { 720 }
$gitEstMB = if ($gitInstalled) { 0 } else { 65 }
$totalEstMB = $dotNetEstMB + $vsAndSdkEstMB + $swiftEstMB + $gitEstMB
$allInstalled = $dotnet9Installed -and $vsAndSdkInstalled -and $swiftInstalled -and $gitInstalled

$report = [ordered]@{
    OSBuild             = [System.Environment]::OSVersion.Version.ToString()
    TargetArchitecture  = $targetArch
    DotNetSdk9          = [ordered]@{
        Installed           = $dotnet9Installed
        Detail              = $dotnet9Detail
        EstimatedDownloadMB = $dotNetEstMB
    }
    VsCppAndWin10Sdk    = [ordered]@{
        Installed           = $vsAndSdkInstalled
        Detail              = $vsAndSdkDetail
        EstimatedDownloadMB = $vsAndSdkEstMB
    }
    SwiftWindows        = [ordered]@{
        Installed           = $swiftInstalled
        Detail              = $swiftDetail
        EstimatedDownloadMB = $swiftEstMB
    }
    Git                 = [ordered]@{
        Installed           = $gitInstalled
        Detail              = $gitDetail
        EstimatedDownloadMB = $gitEstMB
    }
    AllInstalled        = $allInstalled
    EstimatedDownloadMB = $totalEstMB
}

if ($Json) {
    $report | ConvertTo-Json -Depth 4
    if ($allInstalled) { exit 0 } else { exit 1 }
}

Write-Host ""
Write-Host "VRemind-win Build Toolchain Check (Offline / 0 MB used)" -ForegroundColor Cyan
Write-Host "OS Version: $($report.OSBuild)"
Write-Host ""

$rows = @(
    [pscustomobject]@{ Component = '.NET 9 SDK';                    Installed = $dotnet9Installed;  EstMB = $dotNetEstMB;   Detail = $dotnet9Detail }
    [pscustomobject]@{ Component = 'VS C++ Build Tools + Win10 SDK'; Installed = $vsAndSdkInstalled; EstMB = $vsAndSdkEstMB; Detail = $vsAndSdkDetail }
    [pscustomobject]@{ Component = 'Swift for Windows (5.9+)';      Installed = $swiftInstalled;    EstMB = $swiftEstMB;    Detail = $swiftDetail }
    [pscustomobject]@{ Component = 'Git for Windows';               Installed = $gitInstalled;      EstMB = $gitEstMB;      Detail = $gitDetail }
)
$rows | Format-Table -AutoSize

if ($allInstalled) {
    Write-Host "All build prerequisites are installed!" -ForegroundColor Green
    exit 0
}

Write-Host "Missing packages total estimated download: ~$totalEstMB MB" -ForegroundColor Yellow
Write-Host "When off metered mobile data, install missing packages with:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\install-toolchain.ps1 -ConfirmDownload" -ForegroundColor White
exit 1
