param([switch]$FullTests,[switch]$UiTests)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$project=Join-Path $projectRoot 'RidersToolkit.csproj'
[xml]$metadata=Get-Content -LiteralPath $project
$version=[string]$metadata.Project.PropertyGroup.Version
$output=Join-Path $projectRoot 'artifacts/publish'
$artifacts=Join-Path $projectRoot 'artifacts'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
$executable=Join-Path $output 'RidersToolkit.exe'
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
Write-Output $archive
Write-Output $checksum
