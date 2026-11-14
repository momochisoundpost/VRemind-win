[CmdletBinding()]
param(
    [ValidateSet('debug', 'release')]
    [string]$Configuration = 'release',
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
$swiftConfig = $Configuration.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot '..\artifacts\cli'
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$cliPackage = Join-Path $repoRoot 'VRemindCoreCLI'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found. Run .\scripts\check-toolchain.ps1 first."
}
$vsInstallPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($vsInstallPath)) {
    throw "VS C++ Build Tools not found. Run .\scripts\check-toolchain.ps1 first."
}
$vcvars = Join-Path $vsInstallPath 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvars)) {
    throw "vcvars64.bat not found at $vcvars"
}

$env:SDKROOT = [Environment]::GetEnvironmentVariable('SDKROOT', 'User')
if ([string]::IsNullOrWhiteSpace($env:SDKROOT)) {
    $env:SDKROOT = [Environment]::GetEnvironmentVariable('SDKROOT', 'Machine')
}
$env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')

# -Xcc -D__MM_MALLOC_H avoids a circular Clang module dependency (ucrt -> _Builtin_intrinsics -> ucrt)
# introduced by Windows SDK 10.0.26100.0 when compiling with Swift 6.0.x on Windows.
Write-Host "Building VRemindCoreCLI ($swiftConfig)..." -ForegroundColor Cyan
$buildCmd = "call `"$vcvars`" && swift build -c $swiftConfig --package-path `"$cliPackage`" -Xcc -D__MM_MALLOC_H"
cmd /c $buildCmd
if ($LASTEXITCODE -ne 0) {
    throw "swift build failed with exit code $LASTEXITCODE"
}

$binDir = Join-Path $cliPackage ".build\x86_64-unknown-windows-msvc\$swiftConfig"
$exePath = Join-Path $binDir 'vremind-core.exe'
if (-not (Test-Path $exePath)) {
    throw "Expected CLI binary not found at $exePath"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Path $exePath -Destination (Join-Path $OutputDir 'vremind-core.exe') -Force

# Stage Swift Windows runtime DLLs so vremind-core.exe runs on machines without Swift installed
$swiftCmd = (Get-Command swift.exe -ErrorAction Stop).Source
$toolchainRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $swiftCmd))
$toolchainsDir = Split-Path -Parent $toolchainRoot
$swiftBaseDir = Split-Path -Parent $toolchainsDir
$runtimeDir = Get-ChildItem -Path (Join-Path $swiftBaseDir 'Runtimes') -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    Select-Object -First 1 -ExpandProperty FullName |
    ForEach-Object { Join-Path $_ 'usr\bin' }

if (-not $runtimeDir -or -not (Test-Path $runtimeDir)) {
    throw "Could not locate Swift runtime DLL directory under $swiftBaseDir\Runtimes"
}

$stagedCount = 0
foreach ($dll in (Get-ChildItem -Path $runtimeDir -Filter '*.dll' -ErrorAction SilentlyContinue)) {
    Copy-Item -Path $dll.FullName -Destination (Join-Path $OutputDir $dll.Name) -Force
    $stagedCount++
}

Write-Host "Staged vremind-core.exe and $stagedCount Swift runtime DLLs into $OutputDir" -ForegroundColor Green
