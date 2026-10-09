param(
    [string]$GameDirectory = "C:\Program Files (x86)\Steam\steamapps\common\Mechabellum",
    [string]$CoreDirectory,
    [string]$InteropDirectory,
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $CoreDirectory) { $CoreDirectory = Join-Path $GameDirectory "BepInEx\core" }
if (-not $InteropDirectory) { $InteropDirectory = Join-Path $GameDirectory "BepInEx\interop" }
if (-not (Test-Path -LiteralPath (Join-Path $CoreDirectory "BepInEx.Unity.IL2CPP.dll"))) {
    throw "BepInEx 6 IL2CPP core not found. Supply -CoreDirectory or install the official loader first."
}
if (-not (Test-Path -LiteralPath (Join-Path $InteropDirectory "GRFight.dll"))) {
    throw "Game bindings not found. Launch once with BepInEx to generate BepInEx\interop, then close the game."
}

dotnet build (Join-Path $projectDirectory "Plugins\BattleStatistics\BattleStatistics.csproj") -c Release "-p:BepInExCoreDir=$CoreDirectory" "-p:GameInteropDir=$InteropDirectory"
if ($LASTEXITCODE -ne 0) { throw "Battle Statistics build failed." }

$dll = Join-Path $projectDirectory "Plugins\BattleStatistics\bin\Release\net6.0\MechaCommunityMod.BattleStatistics.dll"
$dist = Join-Path $projectDirectory "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $dist "MechaCommunityMod.BattleStatistics.dll") -Force
$hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $dist 'MechaCommunityMod.BattleStatistics.dll.sha256') -Value ($hash + '  MechaCommunityMod.BattleStatistics.dll') -Encoding ASCII
Write-Host "Built plugin: $dist\MechaCommunityMod.BattleStatistics.dll"

if ($Install) {
    if (Get-Process -Name Mechabellum -ErrorAction SilentlyContinue) {
        throw "Close Mechabellum before installing the plugin. The build is available in dist."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory "BepInEx\core\BepInEx.Unity.IL2CPP.dll"))) {
        throw "The game has no BepInEx loader. This script installs only the Battle Statistics plugin. Use the Mecha Community Mod installer to install the loader."
    }
    $destination = Join-Path $GameDirectory "BepInEx\plugins\MechaCommunityMod\BattleStatistics"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $destination "MechaCommunityMod.BattleStatistics.dll") -Force
    Write-Host "Installed: $destination\MechaCommunityMod.BattleStatistics.dll"
}
