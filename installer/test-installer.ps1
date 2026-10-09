param([string]$SetupExe, [string]$LegacySetupExe)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SetupExe) {
    $version = [regex]::Match((Get-Content (Join-Path $workspace 'Shared\ModIdentity.cs') -Raw), 'internal const string Version = "([0-9.]+)"').Groups[1].Value
    $SetupExe = Join-Path $workspace "dist\MechaCommunityMod-$version-Setup.exe"
}
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $workspace ('output\installer-smoke-' + [guid]::NewGuid().ToString('N'))))
$fixtureGame = Join-Path $fixtureRoot 'Mechabellum'
if (-not $fixtureGame.StartsWith($workspace + '\output\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture location.' }
New-Item -ItemType Directory -Path (Join-Path $fixtureGame 'Mechabellum_Data'),(Join-Path $fixtureGame 'ProjectDatas\Replay'),(Join-Path $fixtureGame 'ProjectDatas\Stats') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fixtureGame 'Mechabellum.exe') -Value 'fixture only - never execute'
Set-Content -LiteralPath (Join-Path $fixtureGame 'GameAssembly.dll') -Value 'original fixture game'
Set-Content -LiteralPath (Join-Path $fixtureGame 'ProjectDatas\Replay\keep.grbr') -Value 'fixture replay'
Set-Content -LiteralPath (Join-Path $fixtureGame 'ProjectDatas\Stats\keep.mechstats') -Value 'fixture report'
Write-Output "Isolated fixture: $fixtureGame"

function RunInstaller([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installer process failed: $($process.ExitCode). Logs: $fixtureRoot" }
}
function Verify([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
$setup = (Resolve-Path -LiteralPath $SetupExe).Path
if ($LegacySetupExe) {
    RunInstaller (Resolve-Path -LiteralPath $LegacySetupExe).Path @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $fixtureGame + '"'),('/LOG="' + (Join-Path $fixtureRoot 'legacy-install.log') + '"'))
    $oldLauncher = Join-Path $fixtureGame 'DamageOverlay\Launcher\MechabellumModLauncher.exe'
    $legacySettings = [ordered]@{ 'Features/IndividualStats' = 'false'; 'LiveOverlay/XpFed' = 'true'; 'Statistics/TableXpFed' = 'false' }
    $legacyEncoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($legacySettings | ConvertTo-Json -Compress)))
    RunInstaller $oldLauncher @('--apply',$legacyEncoded)
}
RunInstaller $setup @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $fixtureGame + '"'),('/LOG="' + (Join-Path $fixtureRoot 'install.log') + '"'))
$cfg = Join-Path $fixtureGame 'BepInEx\config\mecha.community.mod.battle-statistics.cfg'
$loaderCfg = Join-Path $fixtureGame 'BepInEx\config\BepInEx.cfg'
$launcher = Join-Path $fixtureGame 'MechaCommunityMod\Launcher\MechaCommunityMod.Launcher.exe'
Verify (Test-Path -LiteralPath $launcher) 'Launcher missing.'
Verify (Test-Path -LiteralPath (Join-Path $fixtureGame 'BepInEx\plugins\MechaCommunityMod\BattleStatistics\MechaCommunityMod.BattleStatistics.dll')) 'Battle Statistics plugin missing.'
Verify (Test-Path -LiteralPath (Join-Path $fixtureGame 'BepInEx\unity-libs\2022.3.62.zip')) 'Unity archive missing.'
Verify (Test-Path -LiteralPath (Join-Path $fixtureGame 'MechaCommunityMod\owns-loader.txt')) 'Loader ownership missing.'
Verify ((Get-Content -LiteralPath $loaderCfg -Raw) -match 'Enabled = false') 'Console not disabled.'
if ($LegacySetupExe) {
    Verify ((Get-Content -LiteralPath $cfg -Raw) -match 'IndividualStats\s*=\s*false') 'Rename reset feature selection.'
    Verify ((Get-Content -LiteralPath $cfg -Raw) -match 'XpFed = true') 'Rename reset overlay settings.'
    Verify ((Get-Content -LiteralPath $cfg -Raw) -match 'TableXpFed = false') 'Rename reset statistics settings.'
    Verify (-not (Test-Path -LiteralPath (Join-Path $fixtureGame 'BepInEx\plugins\DamageOverlay\DamageOverlay.dll'))) 'Legacy plugin remains active.'
    Verify (-not (Test-Path -LiteralPath (Join-Path $fixtureGame 'DamageOverlay'))) 'Legacy launcher remains.'
    Write-Output 'PASS Product rename migrates choices and loader ownership, and removes duplicate plugin/launcher files.'
} else { Verify ((Get-Content -LiteralPath $cfg -Raw) -match 'IndividualStats\s*=\s*true') 'Feature defaults missing.' }
Write-Output 'PASS Fresh installer includes launcher/runtime/loader/mod/Unity libraries and disables the console.'

$settings = [ordered]@{ 'Features/IndividualStats' = 'false'; 'LiveOverlay/XpFed' = 'true'; 'Statistics/TableXpFed' = 'false' }
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($settings | ConvertTo-Json -Compress)))
RunInstaller $launcher @('--apply',$encoded)
RunInstaller $setup @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $fixtureGame + '"'),('/LOG="' + (Join-Path $fixtureRoot 'update.log') + '"'))
Verify ((Get-Content -LiteralPath $cfg -Raw) -match 'IndividualStats\s*=\s*false') 'Upgrade reset a feature.'
Verify ((Get-Content -LiteralPath $cfg -Raw) -match 'XpFed = true') 'Upgrade reset overlay settings.'
Verify ((Get-Content -LiteralPath $cfg -Raw) -match 'TableXpFed = false') 'Upgrade reset table settings.'
Write-Output 'PASS Launcher saves independent settings and an installer update preserves them.'

$uninstaller = Join-Path $fixtureGame 'MechaCommunityMod\setup\unins000.exe'
RunInstaller $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $fixtureRoot 'uninstall.log') + '"'))
Verify (-not (Test-Path -LiteralPath (Join-Path $fixtureGame 'winhttp.dll'))) 'Owned proxy remains.'
Verify (-not (Test-Path -LiteralPath (Join-Path $fixtureGame 'dotnet'))) 'Owned loader runtime remains.'
Verify (-not (Test-Path -LiteralPath (Join-Path $fixtureGame 'BepInEx'))) 'Owned BepInEx remains.'
Verify (-not (Test-Path -LiteralPath (Join-Path $fixtureGame 'MechaCommunityMod'))) 'Launcher/setup files remain.'
Verify (Test-Path -LiteralPath (Join-Path $fixtureGame 'ProjectDatas\Replay\keep.grbr')) 'Replay removed.'
Verify (Test-Path -LiteralPath (Join-Path $fixtureGame 'ProjectDatas\Stats\keep.mechstats')) 'Report removed without opt-in.'
Verify ((Get-Content -LiteralPath (Join-Path $fixtureGame 'GameAssembly.dll') -Raw).Trim() -eq 'original fixture game') 'Game file changed.'
Write-Output 'PASS Full uninstall removes owned files and preserves game files, replays and reports.'
Write-Output "Installer smoke checks passed. Logs retained in $fixtureRoot"
