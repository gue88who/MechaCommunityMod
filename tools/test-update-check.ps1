$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $workspace '.build-deps\inno\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The local Inno compiler is required.' }
$fixture = Join-Path $workspace ('output\update-check-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$include = Join-Path $workspace 'installer\UpdateCheck.iss'
$script = @"
#define ModVersion "0.9.100"
[Setup]
AppName=Update check test
AppVersion=0.9.100
PrivilegesRequired=lowest
CreateAppDir=no
Uninstallable=no
OutputDir=.
OutputBaseFilename=UpdateCheckTest
[Code]
#include "$include"
function InitializeSetup: Boolean;
begin
  CheckUpdateRules;
  Result := False;
end;
"@
$scriptPath = Join-Path $fixture 'test.iss'
Set-Content -LiteralPath $scriptPath -Value $script -Encoding UTF8
& $compiler /Q $scriptPath
if ($LASTEXITCODE -ne 0) { throw 'Update-check test compilation failed.' }
$logPath = Join-Path $fixture 'test.log'
$process = Start-Process -FilePath (Join-Path $fixture 'UpdateCheckTest.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',('/LOG="' + $logPath + '"')) -WindowStyle Hidden -Wait -PassThru
if (-not (Test-Path -LiteralPath $logPath) -or -not (Select-String -LiteralPath $logPath -SimpleMatch 'PASS update version, missing-release and URL validation checks' -Quiet)) {
    throw "Update-check rules failed; see $logPath (exit $($process.ExitCode))."
}
Write-Output 'PASS installer update rules: versions, release URLs, asset names and valid/missing/invalid/mismatched checksum files.'
