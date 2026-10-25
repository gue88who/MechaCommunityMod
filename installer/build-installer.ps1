param(
    [string]$LoaderZip,
    [string]$UnityLibrariesZip,
    [string]$DefaultsFile,
    [string]$Compiler,
    [string]$CoreDirectory,
    [string]$InteropDirectory,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modDirectory = Split-Path -Parent $PSScriptRoot
if (-not $DefaultsFile) { $DefaultsFile = Join-Path $PSScriptRoot 'default-settings.json' }
if (-not $UnityLibrariesZip) { $UnityLibrariesZip = 'C:\Program Files (x86)\Steam\steamapps\common\Mechabellum\BepInEx\unity-libs\2022.3.62.zip' }
if ((Get-FileHash -LiteralPath $UnityLibrariesZip -Algorithm SHA256).Hash -ne '575E7D600F69DE8200CCF4DB700B3AE6252366C22E8C3434C860E428974518D1') {
    throw 'Unity reference libraries must be the pinned official 2022.3.62.zip.'
}
if (-not $LoaderZip) { $LoaderZip = Join-Path $repoDirectory '.build-deps\bepinex.zip' }
$expectedLoaderHash = 'F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A'
if ((Get-FileHash -LiteralPath $LoaderZip -Algorithm SHA256).Hash -ne $expectedLoaderHash) {
    throw 'Loader archive must be the pinned official BepInEx Unity IL2CPP Windows x64 build 788.'
}
$plugin = Join-Path $modDirectory 'dist\MechaCommunityMod.BattleStatistics.dll'
$identitySource = Get-Content -LiteralPath (Join-Path $modDirectory 'Shared\ModIdentity.cs') -Raw
$versionMatch = [regex]::Match($identitySource, 'internal const string Version = "([0-9.]+)"')
if (-not $versionMatch.Success) { throw 'Cannot determine Mecha Community Mod version.' }
$version = $versionMatch.Groups[1].Value
& (Join-Path $modDirectory 'build.ps1') -CoreDirectory $CoreDirectory -InteropDirectory $InteropDirectory
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $plugin)) { throw 'Battle Statistics plugin build failed.' }
# A unique staging directory avoids stale payloads and needs no recursive cleanup.
$stage = Join-Path $repoDirectory ('output\mecha-community-mod-installer\' + $version + '-' + [guid]::NewGuid().ToString('N'))
$loaderDirectory = Join-Path $stage 'loader'
$payloadDirectory = Join-Path $stage 'payload'
New-Item -ItemType Directory -Path $loaderDirectory,$payloadDirectory -Force | Out-Null
Expand-Archive -LiteralPath $LoaderZip -DestinationPath $loaderDirectory
# The upstream changelog has a generic root filename; keep it with our documentation.
Move-Item -LiteralPath (Join-Path $loaderDirectory 'changelog.txt') -Destination (Join-Path $payloadDirectory 'BepInEx-changelog.txt')
Copy-Item -LiteralPath $plugin -Destination (Join-Path $payloadDirectory 'MechaCommunityMod.BattleStatistics.dll')
Copy-Item -LiteralPath (Join-Path $repoDirectory 'LICENSE') -Destination (Join-Path $payloadDirectory 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'INSTALL.txt') -Destination $payloadDirectory
Copy-Item -LiteralPath $DefaultsFile -Destination (Join-Path $payloadDirectory 'default-settings.json')
Copy-Item -LiteralPath $UnityLibrariesZip -Destination (Join-Path $payloadDirectory '2022.3.62.zip')
$launcherDirectory = Join-Path $stage 'launcher'
dotnet publish (Join-Path $modDirectory 'Launcher\Launcher.csproj') -c Release --self-contained true -r win-x64 -o $launcherDirectory --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed. Restore Launcher/Launcher.csproj first.' }
# Retain the notices distributed with each runtime package used by this publish.
$licensesDirectory = Join-Path $payloadDirectory 'licenses'
New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses\BepInEx-LICENSE.txt'),(Join-Path $PSScriptRoot 'licenses\SOURCES.txt') -Destination $licensesDirectory
Copy-Item -LiteralPath (Join-Path $modDirectory 'Launcher\Assets\README.md') -Destination (Join-Path $licensesDirectory 'Mechabellum-UI-assets.txt')
Copy-Item -LiteralPath (Join-Path $modDirectory 'Launcher\Assets\provenance.json') -Destination (Join-Path $licensesDirectory 'Mechabellum-UI-provenance.json')
Copy-Item -LiteralPath (Join-Path $modDirectory 'Launcher\Assets\Roboto-LICENSE.txt'),(Join-Path $modDirectory 'Launcher\Assets\Xolonium-LICENSE.txt'),(Join-Path $modDirectory 'Launcher\Assets\Font-NOTICES.txt') -Destination $licensesDirectory
$assets = Get-Content -LiteralPath (Join-Path $modDirectory 'Launcher\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    foreach ($package in $framework.Value.downloadDependencies) {
        if ($package.name -notmatch '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$') { continue }
        $packageVersion = [regex]::Match($package.version, '[0-9]+\.[0-9]+\.[0-9]+').Value
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $packageDirectory = Join-Path $folder ($package.name.ToLowerInvariant() + '\' + $packageVersion)
            if (-not (Test-Path -LiteralPath $packageDirectory)) { continue }
            foreach ($notice in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
                $noticePath = Join-Path $packageDirectory $notice
                if (Test-Path -LiteralPath $noticePath) {
                    Copy-Item -LiteralPath $noticePath -Destination (Join-Path $licensesDirectory ($package.name + '-' + $notice + '.txt'))
                }
            }
            break
        }
    }
}
$launcher = Join-Path $launcherDirectory 'MechaCommunityMod.Launcher.exe'
$validation = Start-Process -FilePath $launcher -ArgumentList @('--validate-defaults',('"' + $DefaultsFile + '"')) -PassThru -Wait -WindowStyle Hidden
if ($validation.ExitCode -ne 0) { throw 'Invalid default settings.' }
$defaultValues = (Get-Content -LiteralPath $DefaultsFile -Raw | ConvertFrom-Json).settings
$fairnessPath = Join-Path $stage 'feature-fairness.json'
$fairnessExport = Start-Process -FilePath $launcher -ArgumentList @('--export-fairness',('"' + $fairnessPath + '"')) -PassThru -Wait -WindowStyle Hidden
if ($fairnessExport.ExitCode -ne 0) { throw 'Could not export feature fairness ratings.' }
$fairness = Get-Content -LiteralPath $fairnessPath -Raw | ConvertFrom-Json
$defines = @('#define ModVersion "' + $version + '"')
$fairnessText = @('FAIR PLAY', '', $fairness.disclaimer, '')
foreach ($level in $fairness.levels) {
    $fairnessText += $level.title
    $fairnessText += $level.description
    $fairnessText += ''
    $defines += '#define Rating' + $level.badge + ' "' + $level.title.Replace('"','""') + '"'
    $color = $level.color.TrimStart('#')
    $defines += '#define RatingColor' + $level.badge + ' "$00' + $color.Substring(4,2) + $color.Substring(2,2) + $color.Substring(0,2) + '"'
}
$fairnessText += 'FEATURE RATINGS'
foreach ($feature in $fairness.features) {
    $value = $defaultValues.($feature.id)
    if ($null -eq $value) { $value = $feature.defaultValue }
    if ($feature.level -eq 'RED' -and $value.ToString().ToLowerInvariant() -ne 'false') {
        throw ('High-impact helpers must be disabled in distribution defaults: ' + $feature.id)
    }
    $defines += '#define Default' + $feature.key + ' "' + $value.ToString().ToLowerInvariant() + '"'
    $defines += '#define Caption' + $feature.key + ' "' + $feature.caption.Replace('"','""') + '"'
    $fairnessText += $feature.caption + ' - ' + ($fairness.levels | Where-Object badge -eq $feature.level).title
    $fairnessText += $feature.reason
    $fairnessText += ''
}
Set-Content -LiteralPath (Join-Path $payloadDirectory 'FAIRNESS.txt') -Value $fairnessText -Encoding UTF8
Set-Content -LiteralPath (Join-Path $stage 'build-values.iss') -Value $defines -Encoding UTF8
$checks = foreach ($file in Get-ChildItem -LiteralPath $loaderDirectory -Recurse -File) {
    $relative = $file.FullName.Substring($loaderDirectory.Length + 1)
    if ($relative -eq 'doorstop_config.ini') { continue }
    if ($relative.Contains("'")) { throw 'Unexpected quote in loader archive filename.' }
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    "if not FileMatches(Directory, '$relative', '$hash') then begin Result := False; Exit; end;"
}
Set-Content -LiteralPath (Join-Path $stage 'loader-checks.iss') -Value $checks -Encoding UTF8
$loaderFiles = @(Get-ChildItem -LiteralPath $loaderDirectory -Recurse -File | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($loaderDirectory.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$loaderFiles += [ordered]@{ path = 'BepInEx\unity-libs\2022.3.62.zip'; sha256 = '575E7D600F69DE8200CCF4DB700B3AE6252366C22E8C3434C860E428974518D1' }
$manifest = [ordered]@{
    modVersion = $version
    statisticsPluginVersion = $version
    statisticsPluginSha256 = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
    loaderVersion = '6.0.0-be.788+5b766a3'
    loaderUrl = 'https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip'
    loaderSha256 = $expectedLoaderHash
    unityLibrariesVersion = '2022.3.62'
    unityLibrariesSha256 = '575E7D600F69DE8200CCF4DB700B3AE6252366C22E8C3434C860E428974518D1'
    loaderFiles = $loaderFiles
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payloadDirectory 'manifest.json') -Encoding UTF8
Write-Host "Prepared installer inputs: $stage"
if ($PrepareOnly) { return }
if (-not $Compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $Compiler = $command.Source }
    else {
        $candidate = Join-Path $repoDirectory '.build-deps\inno\ISCC.exe'
        if (-not (Test-Path -LiteralPath $candidate)) { $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
        if (Test-Path -LiteralPath $candidate) { $Compiler = $candidate }
    }
}
if (-not $Compiler) { throw 'Install Inno Setup 6.7 or newer, or pass -Compiler with the path to ISCC.exe. Prepared files are retained.' }
& $Compiler '/Q' "/DBuildRoot=$stage" (Join-Path $PSScriptRoot 'MechaCommunityMod.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }
$installer = Join-Path $stage "dist\MechaCommunityMod-$version-Setup.exe"
$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($installer + '.sha256') -Value ($installerHash + '  ' + [IO.Path]::GetFileName($installer)) -Encoding ASCII
Write-Host "Installer: $installer"
$releaseDirectory = Join-Path $modDirectory 'dist'
Copy-Item -LiteralPath $installer,($installer + '.sha256') -Destination $releaseDirectory -Force
Write-Host "Release: $releaseDirectory\MechaCommunityMod-$version-Setup.exe"
