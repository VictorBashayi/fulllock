<#
  Builds the FullLock Windows release:
    installer/out/FullLock-Setup-<version>.exe   (Inno Setup installer, bundles the drivers)
    installer/out/FullLock-<version>-portable.zip
  Usage: powershell -ExecutionPolicy Bypass -File installer/build.ps1 [-Version 1.0.0] [-SkipTests]
#>
param(
    [string]$Version,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$here = $PSScriptRoot
if (-not $Version) { $Version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, got '$Version'" }

$out = Join-Path $here 'out'
$publish = Join-Path $out 'publish'
$deps = Join-Path $here 'deps'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $publish, $deps | Out-Null

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}

$sln = Join-Path $root 'windows-receiver/F1Controller.sln'
$proj = Join-Path $root 'windows-receiver/F1Controller/F1Controller.csproj'
if (-not $SkipTests) { Invoke-Checked dotnet @('test', $sln, '-c', 'Release') }
Invoke-Checked dotnet @('publish', $proj, '-c', 'Release', '-r', 'win-x64', '--self-contained',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none', "-p:Version=$Version", '-o', $publish)

# Driver installers: downloaded once, pinned by SHA-256
$drivers = Get-Content (Join-Path $here 'drivers.json') -Raw | ConvertFrom-Json
foreach ($name in 'ViGEmBus', 'HidHide') {
    $d = $drivers.$name
    $file = Join-Path $deps $d.file
    if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $d.sha256.ToUpper()) {
        Write-Host "Downloading $name $($d.version)"
        Invoke-WebRequest -Uri $d.url -OutFile $file -UseBasicParsing
    }
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($hash -ne $d.sha256.ToUpper()) {
        Remove-Item $file -Force
        throw "$name installer hash mismatch: expected $($d.sha256), got $hash"
    }
}

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it: winget install JRSoftware.InnoSetup' }
Invoke-Checked $iscc @("/DAppVersion=$Version", "/O$out", (Join-Path $here 'FullLock.iss'))

$zipDir = Join-Path $out 'portable'
New-Item -ItemType Directory -Force $zipDir | Out-Null
Copy-Item (Join-Path $publish 'FullLock.exe'), (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $zipDir
Compress-Archive -Path (Join-Path $zipDir '*') -DestinationPath (Join-Path $out "FullLock-$Version-portable.zip")
Remove-Item $zipDir -Recurse -Force

Get-ChildItem $out -File | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length / 1MB) }
