# Copies the mod source (and only the mod source) into the game's Source folder.
#
# Cities: Skylines compiles every .cs file under Mods\MyFirstMod\Source with its
# own C# 5-era compiler. Copying the whole repo drags in Tests\, Stubs\ and any
# generated obj\*.cs files, so deploy with this script instead.
#
# Usage (from the repo root, in PowerShell):
#   .\deploy.ps1
#   .\deploy.ps1 -ModDir "D:\Games\CS\Addons\Mods\MyFirstMod"

param(
    [string]$ModDir = (Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\MyFirstMod")
)

$ErrorActionPreference = "Stop"
$srcRoot = Join-Path $PSScriptRoot "MyFirstMod"
$dest = Join-Path $ModDir "Source"

if (Test-Path $dest) {
    Remove-Item -Path (Join-Path $dest "*") -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $dest | Out-Null
}

$files = Get-ChildItem -Path $srcRoot -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

foreach ($f in $files) {
    $rel = $f.FullName.Substring($srcRoot.Length).TrimStart('\', '/')
    $target = Join-Path $dest $rel
    $targetDir = Split-Path $target -Parent
    if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir | Out-Null }
    Copy-Item -Path $f.FullName -Destination $target
}

Write-Host ("Deployed {0} source files to {1}" -f $files.Count, $dest)
Write-Host "Restart Cities: Skylines to recompile the mod."
