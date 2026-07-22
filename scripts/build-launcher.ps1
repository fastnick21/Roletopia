param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $repoRoot "src/Roletopia.AmongUsPlugin/Roletopia.AmongUsPlugin.csproj"
$launcherProject = Join-Path $repoRoot "src/Roletopia.Installer/Roletopia.Installer.csproj"
$payloadRoot = Join-Path $repoRoot "roletopia"
$pluginPayload = Join-Path $payloadRoot "BepInEx/plugins/Roletopia"
$outputRoot = Join-Path $repoRoot $OutputDirectory
$publishRoot = Join-Path $outputRoot "Roletopia-Launcher"
$packagedPayload = Join-Path $publishRoot "payload"
$zipPath = Join-Path $outputRoot "Roletopia-Launcher-Windows.zip"
$tempRoot = Join-Path $outputRoot "temp"
$bepInExZip = Join-Path $tempRoot "BepInEx-Unity.IL2CPP-win-x86.zip"
$bepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.785%2B6abdba4.zip"

if (Test-Path $outputRoot) {
    Remove-Item $outputRoot -Recurse -Force
}

if (Test-Path $payloadRoot) {
    Remove-Item $payloadRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
New-Item -ItemType Directory -Path $pluginPayload -Force | Out-Null

Write-Host "Downloading official BepInEx 6 IL2CPP Windows x86 loader..."
Invoke-WebRequest -Uri $bepInExUrl -OutFile $bepInExZip -UseBasicParsing

if (-not (Test-Path $bepInExZip) -or (Get-Item $bepInExZip).Length -lt 1MB) {
    throw "BepInEx download was missing or unexpectedly small."
}

Write-Host "Extracting BepInEx into staging payload..."
Expand-Archive -Path $bepInExZip -DestinationPath $payloadRoot -Force

$requiredLoaderFiles = @(
    (Join-Path $payloadRoot "winhttp.dll"),
    (Join-Path $payloadRoot "doorstop_config.ini"),
    (Join-Path $payloadRoot "BepInEx/core")
)

foreach ($required in $requiredLoaderFiles) {
    if (-not (Test-Path $required)) {
        throw "Required BepInEx loader file or folder was not produced: $required"
    }
}

Write-Host "Restoring Roletopia plugin..."
dotnet restore $pluginProject

Write-Host "Building Roletopia plugin DLL..."
dotnet build $pluginProject -c $Configuration --no-restore

$pluginDll = Join-Path $pluginPayload "Roletopia.Plugin.dll"
if (-not (Test-Path $pluginDll)) {
    throw "Roletopia plugin DLL was not produced: $pluginDll"
}

Write-Host "Restoring launcher..."
dotnet restore $launcherProject

Write-Host "Building launcher without payload assemblies in compiler inputs..."
dotnet build $launcherProject -c $Configuration --no-restore -o $publishRoot

$launcher = Join-Path $publishRoot "Roletopia-Launcher.exe"
if (-not (Test-Path $launcher)) {
    throw "Launcher executable was not produced: $launcher"
}

Write-Host "Copying complete payload into packaged launcher after compilation..."
Copy-Item -Path $payloadRoot -Destination $packagedPayload -Recurse -Force

$packagedPlugin = Join-Path $packagedPayload "BepInEx/plugins/Roletopia/Roletopia.Plugin.dll"
$packagedWinHttp = Join-Path $packagedPayload "winhttp.dll"
$packagedCore = Join-Path $packagedPayload "BepInEx/core"

foreach ($required in @($packagedPlugin, $packagedWinHttp, $packagedCore)) {
    if (-not (Test-Path $required)) {
        throw "Required packaged file or folder is missing: $required"
    }
}

$readme = @"
Roletopia Launcher
==================

1. Keep all files in this folder together.
2. Run Roletopia-Launcher.exe.
3. The launcher will locate the Steam installation of Among Us.
4. Click Install / Update.
5. Click Play Roletopia.

The first modded launch may take longer while BepInEx creates its runtime files.
This package includes the Roletopia plugin and BepInEx 6 IL2CPP Windows x86 loader.
"@
Set-Content -Path (Join-Path $publishRoot "README.txt") -Value $readme -Encoding UTF8

Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $zipPath -Force
Write-Host "Package created: $zipPath"
