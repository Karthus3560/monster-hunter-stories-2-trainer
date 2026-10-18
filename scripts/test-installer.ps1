param([string]$Compiler=$env:ISCC_PATH)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if(-not $Compiler){$Compiler=@((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')) | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1}
if(-not $Compiler){throw 'Install Inno Setup or set ISCC_PATH.'}
# A unique identity, isolated path and disabled shortcuts protect the real app and desktop.
$fixtureId='KS.Test.'+[Guid]::NewGuid().ToString('N').Substring(0,12)
$root=Join-Path $projectRoot ('artifacts/installer-tests/'+$fixtureId)
$source=Join-Path $root 'source'
$installed=Join-Path $root 'installed'
$output=Join-Path $root 'setups'
New-Item -ItemType Directory -Force -Path $source,$output | Out-Null
foreach($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','navirou-pixel-v33.ico')){Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $source}
$uninstallKey="HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${fixtureId}_is1"
$report=[Collections.Generic.List[string]]::new()
function Check([string]$Name,[bool]$Passed){if(-not $Passed){throw "FAIL $Name"};$report.Add("PASS $Name")}
function Compile([string]$Version,[string]$Content){
    [IO.File]::WriteAllText((Join-Path $source 'RidersToolkit.exe'),$Content)
    & $Compiler '/Q' "/DAppId=$fixtureId" '/DInstallerTest=1' "/DAppVersion=$Version" "/DPublishDir=$source" "/DInstallerOutput=$output" (Join-Path $projectRoot 'installer/KinshipStudio.iss')
    if($LASTEXITCODE -ne 0){throw 'Fixture installer compilation failed.'}
    return Join-Path $output "KinshipStudio-v$Version-Setup.exe"
}
function Install([string]$Setup){
    $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOLAUNCH',('/DIR="'+$installed+'"'),('/LOG="'+(Join-Path $root ('setup-'+[Guid]::NewGuid().ToString('N')+'.log'))+'"'))
    $process=Start-Process -FilePath $Setup -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    return $process.ExitCode
}
try{
    $older=Compile '3.5.0' 'version-one-fixture'
    Check 'First installation succeeds without administrator privileges' ((Install $older) -eq 0)
    Check 'One stable Windows installed-app identity is registered' (Test-Path -LiteralPath $uninstallKey)
    [IO.File]::WriteAllText((Join-Path $installed 'unrelated-user-file.txt'),'keep this')
    $newer=Compile '3.5.1' 'version-two-fixture'
    Check 'New version upgrades the existing installation' ((Install $newer) -eq 0)
    Check 'Upgrade replaces the existing executable in the same folder' ((Get-Content -LiteralPath (Join-Path $installed 'RidersToolkit.exe') -Raw) -eq 'version-two-fixture')
    $entry=Get-ItemProperty -LiteralPath $uninstallKey
    Check 'Installed-app identity is updated instead of duplicated' ($entry.DisplayVersion -eq '3.5.1' -and ([string]$entry.InstallLocation).TrimEnd('\') -eq $installed)
    Check 'Reinstalling the current version succeeds' ((Install $newer) -eq 0)
    Check 'Older installers refuse to downgrade a newer registered version' ((Install $older) -ne 0)
    Check 'Refused downgrade leaves the newer executable untouched' ((Get-Content -LiteralPath (Join-Path $installed 'RidersToolkit.exe') -Raw) -eq 'version-two-fixture')
    Check 'Upgrade and reinstall preserve unrelated user content' ((Get-Content -LiteralPath (Join-Path $installed 'unrelated-user-file.txt') -Raw) -eq 'keep this')
}
finally{
    $uninstaller=Join-Path $installed 'unins000.exe'
    if(Test-Path -LiteralPath $uninstaller){
        $resolved=[IO.Path]::GetFullPath($uninstaller)
        if(-not $resolved.StartsWith([IO.Path]::GetFullPath($root)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture uninstaller escaped its test workspace.'}
        $cleanup=Start-Process -FilePath $resolved -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
        if($cleanup.ExitCode -ne 0){throw 'Fixture uninstall failed.'}
    }
    $report | Set-Content -LiteralPath (Join-Path $root 'installer-test-report.txt')
}
Check 'Uninstallation removes only the fixture registration' (-not (Test-Path -LiteralPath $uninstallKey))
Check 'Uninstallation preserves unrelated user content' (Test-Path -LiteralPath (Join-Path $installed 'unrelated-user-file.txt'))
$report.Add("$($report.Count)/$($report.Count) passed. The real app identity, shortcuts and saves were not changed.")
$report | Set-Content -LiteralPath (Join-Path $root 'installer-test-report.txt')
$report
