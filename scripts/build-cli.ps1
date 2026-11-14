[CmdletBinding()]
param(
    [ValidateSet('debug', 'release')]
    [string]$Configuration = 'release',

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [string]$OutputDir = '',

    [string]$SwiftRuntimeDir = ''
)

$ErrorActionPreference = 'Stop'
$swiftConfig = $Configuration.ToLowerInvariant()
$targetArch = $Architecture.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = if ($targetArch -eq 'arm64') {
        Join-Path $PSScriptRoot '..\artifacts\cli-arm64'
    } else {
        Join-Path $PSScriptRoot '..\artifacts\cli'
    }
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$cliPackage = Join-Path $repoRoot 'VRemindCoreCLI'

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Not a valid PE binary: $Path"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($bytes.Length -lt ($peOffset + 6)) {
        throw "Truncated PE header in: $Path"
    }
    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found. Run .\scripts\check-toolchain.ps1 -Architecture $targetArch first."
}
$vcComponent = if ($targetArch -eq 'arm64') {
    'Microsoft.VisualStudio.Component.VC.Tools.ARM64'
} else {
    'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
}
$vsInstallPath = & $vswhere -latest -products * -requires $vcComponent -property installationPath
if ([string]::IsNullOrWhiteSpace($vsInstallPath)) {
    throw "VS C++ Build Tools ($vcComponent) not found. Run .\scripts\check-toolchain.ps1 -Architecture $targetArch first."
}
if ($targetArch -eq 'arm64') {
    $vcvarsArm64Native = Join-Path $vsInstallPath 'VC\Auxiliary\Build\vcvarsarm64.bat'
    $vcvarsArm64Cross = Join-Path $vsInstallPath 'VC\Auxiliary\Build\vcvarsamd64_arm64.bat'
    $vcvars = if (Test-Path $vcvarsArm64Native) { $vcvarsArm64Native } else { $vcvarsArm64Cross }
} else {
    $vcvars = Join-Path $vsInstallPath 'VC\Auxiliary\Build\vcvars64.bat'
}
if (-not (Test-Path $vcvars)) {
    throw "vcvars batch script not found at $vcvars"
}

$env:SDKROOT = [Environment]::GetEnvironmentVariable('SDKROOT', 'User')
if ([string]::IsNullOrWhiteSpace($env:SDKROOT)) {
    $env:SDKROOT = [Environment]::GetEnvironmentVariable('SDKROOT', 'Machine')
}
$env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User')

$swiftTriple = if ($targetArch -eq 'arm64') { 'aarch64-unknown-windows-msvc' } else { 'x86_64-unknown-windows-msvc' }
$tripleArg = if ($targetArch -eq 'arm64') { "--triple $swiftTriple" } else { '' }

# -Xcc -D__MM_MALLOC_H avoids a circular Clang module dependency (ucrt -> _Builtin_intrinsics -> ucrt)
# introduced by Windows SDK 10.0.26100.0 when compiling with Swift 6.0.x on Windows.
Write-Host "Building VRemindCoreCLI ($swiftConfig, $targetArch)..." -ForegroundColor Cyan
$buildCmd = "call `"$vcvars`" && swift build -c $swiftConfig $tripleArg --package-path `"$cliPackage`" -Xcc -D__MM_MALLOC_H"
cmd /c $buildCmd
if ($LASTEXITCODE -ne 0) {
    throw "swift build failed with exit code $LASTEXITCODE"
}

$binDir = Join-Path $cliPackage ".build\$swiftTriple\$swiftConfig"
$exePath = Join-Path $binDir 'vremind-core.exe'
if (-not (Test-Path $exePath)) {
    throw "Expected CLI binary not found at $exePath"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Path $exePath -Destination (Join-Path $OutputDir 'vremind-core.exe') -Force

# Stage Swift Windows runtime DLLs so vremind-core.exe runs on machines without Swift installed
if (-not [string]::IsNullOrWhiteSpace($SwiftRuntimeDir)) {
    $runtimeDir = $SwiftRuntimeDir
} else {
    $swiftCmd = (Get-Command swift.exe -ErrorAction Stop).Source
    $toolchainRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $swiftCmd))
    $toolchainsDir = Split-Path -Parent $toolchainRoot
    $swiftBaseDir = Split-Path -Parent $toolchainsDir
    $runtimeDir = Get-ChildItem -Path (Join-Path $swiftBaseDir 'Runtimes') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1 -ExpandProperty FullName |
        ForEach-Object { Join-Path $_ 'usr\bin' }
}

if (-not $runtimeDir -or -not (Test-Path $runtimeDir)) {
    throw "Could not locate Swift runtime DLL directory at '$runtimeDir'"
}

$expectedMachine = if ($targetArch -eq 'arm64') { [uint16]0xAA64 } else { [uint16]0x8664 }
$swiftCoreDll = Join-Path $runtimeDir 'swiftCore.dll'
if (-not (Test-Path $swiftCoreDll)) {
    throw "swiftCore.dll not found in '$runtimeDir'"
}
$actualMachine = Get-PeMachine -Path $swiftCoreDll
if ($actualMachine -ne $expectedMachine) {
    $expectedHex = ('0x{0:X4}' -f $expectedMachine)
    $actualHex = ('0x{0:X4}' -f $actualMachine)
    throw "Swift runtime DLL architecture mismatch in '$runtimeDir': expected PE machine $expectedHex ($targetArch), found $actualHex. On an x64 host, pass -SwiftRuntimeDir pointing to extracted ARM64 Swift runtime DLLs, or build natively on a Windows 11 ARM64 machine / GitHub windows-11-arm runner."
}

$stagedCount = 0
foreach ($dll in (Get-ChildItem -Path $runtimeDir -Filter '*.dll' -ErrorAction SilentlyContinue)) {
    Copy-Item -Path $dll.FullName -Destination (Join-Path $OutputDir $dll.Name) -Force
    $stagedCount++
}

Write-Host "Staged vremind-core.exe and $stagedCount Swift runtime DLLs ($targetArch) into $OutputDir" -ForegroundColor Green
