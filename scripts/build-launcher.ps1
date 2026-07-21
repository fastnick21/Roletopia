param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/Roletopia.Installer/Roletopia.Installer.csproj"
$outputRoot = Join-Path $repoRoot $OutputDirectory
$publishRoot = Join-Path $outputRoot "Roletopia-Launcher"
$zipPath = Join-Path $outputRoot "Roletopia-Launcher-Windows.zip"

if (Test-Path $outputRoot) {
    Remove-Item $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishRoot | Out-Null

Write-Host "Restoring launcher..."
dotnet restore $project

Write-Host "Building launcher..."
dotnet build $project -c $Configuration --no-restore -o $publishRoot

$launcher = Join-Path $publishRoot "Roletopia-Launcher.exe"
if (-not (Test-Path $launcher)) {
    throw "Launcher executable was not produced: $launcher"
}

$readme = @"
Roletopia Launcher
==================

1. Keep all files in this folder together.
2. Run Roletopia-Launcher.exe.
3. The launcher will locate the Steam installation of Among Us.
4. Use Install / Update, then Play Roletopia.

This package contains the launcher and the repository's current bundled payload.
"@
Set-Content -Path (Join-Path $publishRoot "README.txt") -Value $readme -Encoding UTF8

Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $zipPath -Force
Write-Host "Package created: $zipPath"
