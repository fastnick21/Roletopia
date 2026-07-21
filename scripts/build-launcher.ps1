param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $repoRoot "src/Roletopia.AmongUsPlugin/Roletopia.AmongUsPlugin.csproj"
$launcherProject = Join-Path $repoRoot "src/Roletopia.Installer/Roletopia.Installer.csproj"
$pluginPayload = Join-Path $repoRoot "roletopia/BepInEx/plugins/Roletopia"
$outputRoot = Join-Path $repoRoot $OutputDirectory
$publishRoot = Join-Path $outputRoot "Roletopia-Launcher"
$zipPath = Join-Path $outputRoot "Roletopia-Launcher-Windows.zip"

if (Test-Path $outputRoot) {
    Remove-Item $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Path $pluginPayload -Force | Out-Null

Write-Host "Restoring Roletopia plugin..."
dotnet restore $pluginProject

Write-Host "Building Roletopia plugin DLLs..."
dotnet build $pluginProject -c $Configuration --no-restore

$pluginDll = Join-Path $pluginPayload "Roletopia.Plugin.dll"
if (-not (Test-Path $pluginDll)) {
    throw "Roletopia plugin DLL was not produced: $pluginDll"
}

Write-Host "Plugin produced: $pluginDll"
Get-ChildItem $pluginPayload -Filter "*.dll" | ForEach-Object {
    Write-Host "  $($_.Name)"
}

Write-Host "Restoring launcher..."
dotnet restore $launcherProject

Write-Host "Building launcher with compiled payload..."
dotnet build $launcherProject -c $Configuration --no-restore -o $publishRoot

$launcher = Join-Path $publishRoot "Roletopia-Launcher.exe"
if (-not (Test-Path $launcher)) {
    throw "Launcher executable was not produced: $launcher"
}

$packagedPlugin = Join-Path $publishRoot "payload/BepInEx/plugins/Roletopia/Roletopia.Plugin.dll"
if (-not (Test-Path $packagedPlugin)) {
    throw "Compiled plugin was not included in the launcher payload: $packagedPlugin"
}

$readme = @"
Roletopia Launcher
==================

1. Keep all files in this folder together.
2. Run Roletopia-Launcher.exe.
3. The launcher will locate the Steam installation of Among Us.
4. Use Install / Update, then Play Roletopia.

This package contains the launcher and the compiled Roletopia plugin payload.
The BepInEx IL2CPP loader files must also be present in the payload before the mod can load in Among Us.
"@
Set-Content -Path (Join-Path $publishRoot "README.txt") -Value $readme -Encoding UTF8

Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $zipPath -Force
Write-Host "Package created: $zipPath"