param([switch]$FullTests,[switch]$UiTests,[switch]$Installer,[string]$SigningThumbprint=$env:KINSHIP_SIGNING_THUMBPRINT)
$ErrorActionPreference='Stop'
if($SigningThumbprint -and $SigningThumbprint -notmatch '^[0-9a-fA-F]{40}$'){throw 'Use the 40-character certificate thumbprint, not a command or certificate password.'}
$projectRoot=Split-Path -Parent $PSScriptRoot
$project=Join-Path $projectRoot 'RidersToolkit.csproj'
[xml]$metadata=Get-Content -LiteralPath $project
$version=$metadata.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'The project version must be a numeric major.minor.patch value.'}
$output=Join-Path $projectRoot 'artifacts/publish'
$artifacts=Join-Path $projectRoot 'artifacts'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
$executable=Join-Path $output 'RidersToolkit.exe'
if($SigningThumbprint){& (Join-Path $PSScriptRoot 'sign.ps1') -Path $executable -Thumbprint $SigningThumbprint}
function Invoke-Check([string[]]$CheckArguments){
    $checkProcess=Start-Process -FilePath $executable -ArgumentList $CheckArguments -WindowStyle Hidden -PassThru
    if(-not $checkProcess.WaitForExit(120000)){throw 'Checks timed out; inspect the running test process'}
    $checkProcess.Refresh()
    if($checkProcess.ExitCode -ne 0){throw 'Checks failed. See the report under local application-data/RidersToolkit.'}
}
$engineArguments=@('--self-test')
if(-not $FullTests){$engineArguments+='--portable'}
Invoke-Check $engineArguments
if($UiTests){Invoke-Check @('--ui-test')}
$storage=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RidersToolkit'
Copy-Item -LiteralPath (Join-Path $storage 'self-test-report.txt') -Destination (Join-Path $output 'engine-test-report.txt')
$uiReport=Join-Path $output 'ui-test-report.txt'
if($UiTests){Copy-Item -LiteralPath (Join-Path $storage 'ui-test-report.txt') -Destination $uiReport}
elseif(Test-Path -LiteralPath $uiReport){Remove-Item -LiteralPath $uiReport}
foreach($name in @('LICENSE','THIRD-PARTY-NOTICES.md','navirou-pixel-v33.ico')){Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/QUICKSTART.md') -Destination (Join-Path $output 'README.md')
$files=@('RidersToolkit.exe','README.md','LICENSE','THIRD-PARTY-NOTICES.md','navirou-pixel-v33.ico','engine-test-report.txt')
if($UiTests){$files+='ui-test-report.txt'}
$archive=Join-Path $artifacts "KinshipStudio-v$version-Windows-x64.zip"
Compress-Archive -LiteralPath ($files | ForEach-Object {Join-Path $output $_}) -DestinationPath $archive -Force
$checksum=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path -Leaf $archive)
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS.txt'),$checksum+[Environment]::NewLine)
if($Installer){
    $candidates=@(
        $env:ISCC_PATH,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe')
    )
    $compiler=$candidates | Where-Object {$_ -and (Test-Path -LiteralPath $_)} | Select-Object -First 1
    if(-not $compiler){throw 'Install Inno Setup 6.7+ or set ISCC_PATH before building the installer.'}
    $compilerArguments=@("/DAppVersion=$version","/DPublishDir=$output","/DInstallerOutput=$artifacts")
    if($SigningThumbprint){
        $signingScript=Join-Path $PSScriptRoot 'sign.ps1'
        $signingCommand='pwsh -NoProfile -File $q'+$signingScript+'$q -Thumbprint '+$SigningThumbprint+' -Path $f'
        $compilerArguments+='/DKinshipSigning=1'
        $compilerArguments+="/SKinshipSign=$signingCommand"
    }
    & $compiler @compilerArguments (Join-Path $projectRoot 'installer/KinshipStudio.iss')
    if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
    $setup=Join-Path $artifacts "KinshipStudio-v$version-Setup.exe"
    $setupHash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path -Leaf $setup)
    [IO.File]::AppendAllText((Join-Path $artifacts 'SHA256SUMS.txt'),$setupHash+[Environment]::NewLine)
    Write-Output $setup
}
Write-Output $archive
Write-Output $checksum
