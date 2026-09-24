# Installs the mod into the game's Mods folder.
#
# Default (WO-33): build MyFirstMod.dll against your installed game and copy ONLY
# the DLL, removing any old Source folder. That is exactly what the Workshop
# ships, so the game never compiles our source.
#
# -FromSource: the old path, for machines without a .NET SDK. Copies only the
# mod's .cs files into Mods\MyFirstMod\Source for the game's own compiler.
# Never copy the whole repo there: Tests\, Stubs\ and obj\*.cs break that build.
#
# Usage (from the repo root, in PowerShell):
#   .\deploy.ps1
#   .\deploy.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Cities_Skylines"
#   .\deploy.ps1 -FromSource

param(
    [string]$ModDir = (Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\MyFirstMod"),
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines",
    [switch]$FromSource
)

$ErrorActionPreference = "Stop"
$srcRoot = Join-Path $PSScriptRoot "MyFirstMod"
$sourceDest = Join-Path $ModDir "Source"
$dllDest = Join-Path $ModDir "MyFirstMod.dll"

if (-not (Test-Path $ModDir)) { New-Item -ItemType Directory -Path $ModDir | Out-Null }

if ($FromSource) {
    # A leftover DLL would load alongside the compiled source.
    if (Test-Path $dllDest) { Remove-Item $dllDest -Force }
    if (Test-Path $sourceDest) {
        Remove-Item -Path (Join-Path $sourceDest "*") -Recurse -Force
    } else {
        New-Item -ItemType Directory -Path $sourceDest | Out-Null
    }

    $files = Get-ChildItem -Path $srcRoot -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

    foreach ($f in $files) {
        $rel = $f.FullName.Substring($srcRoot.Length).TrimStart('\', '/')
        $target = Join-Path $sourceDest $rel
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir | Out-Null }
        Copy-Item -Path $f.FullName -Destination $target
    }

    Write-Host ("Deployed {0} source files to {1}" -f $files.Count, $sourceDest)
    Write-Host "Restart Cities: Skylines to recompile the mod."
    return
}

$managed = Join-Path $GameDir "Cities_Data\Managed"
if (-not (Test-Path (Join-Path $managed "ICities.dll"))) {
    throw "Game assemblies not found in '$managed'. Pass -GameDir, or use -FromSource."
}

dotnet build (Join-Path $srcRoot "MyFirstMod.csproj") --configuration Release "-p:ManagedDir=$managed" "-p:DeployToGame=false"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# The game compiles any Source folder it finds; with the DLL present too the mod
# would load twice.
if (Test-Path $sourceDest) { Remove-Item -Path $sourceDest -Recurse -Force }

Copy-Item -Path (Join-Path $srcRoot "bin\Release\net35\MyFirstMod.dll") -Destination $dllDest -Force
Write-Host ("Deployed MyFirstMod.dll to {0}" -f $ModDir)
Write-Host "Restart Cities: Skylines. Check Debug Output for the '[MyFirstMod] SELF-CHECK' line."
