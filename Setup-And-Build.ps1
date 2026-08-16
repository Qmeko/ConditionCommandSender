$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Solution = Join-Path $Root 'ConditionCommandSender.sln'
$Artifacts = Join-Path $Root 'artifacts'
$PublishDir = Join-Path $Artifacts 'ConditionCommandSender'
$ZipPath = Join-Path $Artifacts 'ConditionCommandSender_v0.1.10.0_build.zip'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host ('=== ' + $Text + ' ===') -ForegroundColor Cyan
}

function Refresh-ProcessPath {
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = $machinePath + ';' + $userPath
}

function Find-DotNet {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $standardPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $standardPath) {
        return $standardPath
    }

    return $null
}

function Test-DotNet10([string]$DotNetPath) {
    if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
        return $false
    }

    try {
        $versions = & $DotNetPath --list-sdks 2>$null
        foreach ($version in $versions) {
            if ($version -match '^10\.') {
                return $true
            }
        }
    }
    catch {
        return $false
    }

    return $false
}

Write-Step 'ConditionCommandSender build environment'
Write-Host ('Root: ' + $Root)

Refresh-ProcessPath
$DotNet = Find-DotNet

if (-not (Test-DotNet10 $DotNet)) {
    Write-Step '.NET 10 SDK installation'

    $Winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $Winget) {
        throw 'winget was not found. Update App Installer from Microsoft Store and run Build.bat again.'
    }

    & $Winget.Source install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        throw ('winget failed to install .NET 10 SDK. Exit code: ' + $LASTEXITCODE)
    }

    Refresh-ProcessPath
    $DotNet = Find-DotNet
}

if (-not (Test-DotNet10 $DotNet)) {
    throw '.NET 10 SDK is still unavailable. Restart Windows, then run Build.bat again.'
}

Write-Step '.NET information'
& $DotNet --info
if ($LASTEXITCODE -ne 0) {
    throw ('dotnet --info failed. Exit code: ' + $LASTEXITCODE)
}

Write-Step 'NuGet restore'
& $DotNet restore $Solution
if ($LASTEXITCODE -ne 0) {
    throw ('NuGet restore failed. Exit code: ' + $LASTEXITCODE)
}

Write-Step 'Release x64 build'
& $DotNet build $Solution -c Release -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) {
    throw ('Build failed. Copy the complete error output. Exit code: ' + $LASTEXITCODE)
}

Write-Step 'Collect artifacts'
if (Test-Path $Artifacts) {
    Remove-Item $Artifacts -Recurse -Force
}
New-Item $PublishDir -ItemType Directory -Force | Out-Null

$ProjectRoot = Join-Path $Root 'ConditionCommandSender'
$PrimaryDll = Get-ChildItem $ProjectRoot -Recurse -File -Filter 'ConditionCommandSender.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $PrimaryDll) {
    Write-Host 'Searched build outputs:' -ForegroundColor Yellow
    Get-ChildItem $ProjectRoot -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'bin' -or $_.FullName -match '[\\/]bin[\\/]' } |
        ForEach-Object { Write-Host ('  ' + $_.FullName) }
    throw 'ConditionCommandSender.dll was not found after a successful build.'
}

$BuildDir = $PrimaryDll.Directory.FullName
Write-Host ('Detected build output: ' + $BuildDir)

$NamesToCopy = @(
    'ConditionCommandSender.dll',
    'ConditionCommandSender.pdb',
    'ConditionCommandSender.deps.json',
    'ConditionCommandSender.runtimeconfig.json',
    'MoonSharp.Interpreter.dll'
)

foreach ($Name in $NamesToCopy) {
    $Source = Join-Path $BuildDir $Name
    if (Test-Path $Source) {
        Copy-Item $Source -Destination $PublishDir -Force
    }
}

$Manifest = Join-Path $Root 'ConditionCommandSender\ConditionCommandSender.json'
if (-not (Test-Path $Manifest)) {
    throw ('Plugin manifest was not found: ' + $Manifest)
}

$ManifestData = Get-Content $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($ManifestData.InternalName -ne 'ConditionCommandSender') {
    throw 'Manifest InternalName must be ConditionCommandSender.'
}
if ($ManifestData.AssemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw ('Manifest AssemblyVersion must contain exactly four numeric components: ' + $ManifestData.AssemblyVersion)
}
if ([int]$ManifestData.DalamudApiLevel -ne 15) {
    throw ('Manifest DalamudApiLevel must be 15: ' + $ManifestData.DalamudApiLevel)
}
Copy-Item $Manifest -Destination $PublishDir -Force

$I18nSource = Join-Path $ProjectRoot 'Data\I18n'
$I18nDest = Join-Path $PublishDir 'Data\I18n'
if (-not (Test-Path $I18nSource)) {
    throw ('I18n files were not found: ' + $I18nSource)
}
New-Item $I18nDest -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $I18nSource '*') -Destination $I18nDest -Force
if (-not (Test-Path (Join-Path $I18nDest 'en.json')) -or -not (Test-Path (Join-Path $I18nDest 'ja.json'))) {
    throw 'Artifact folder must contain Data\I18n\en.json and Data\I18n\ja.json.'
}

$PublishedDll = Join-Path $PublishDir 'ConditionCommandSender.dll'
$PublishedManifest = Join-Path $PublishDir 'ConditionCommandSender.json'

$MoonSharpDll = Join-Path $PublishDir 'MoonSharp.Interpreter.dll'
if (-not (Test-Path $MoonSharpDll)) {
    throw 'MoonSharp.Interpreter.dll was not copied to the artifact folder. Lua cannot run without this dependency.'
}
if (-not (Test-Path $PublishedDll) -or -not (Test-Path $PublishedManifest)) {
    throw 'Artifact folder must contain both ConditionCommandSender.dll and ConditionCommandSender.json.'
}

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Step 'Completed'
Write-Host ('Build folder: ' + $PublishDir) -ForegroundColor Green
Write-Host ('ZIP: ' + $ZipPath) -ForegroundColor Green
Write-Host 'Load ConditionCommandSender.dll from Dalamud Dev Plugins.'
