param([switch]$Installer,[string]$InnoCompiler)
$ErrorActionPreference='Stop'
$NativeRoot=$PSScriptRoot
$OutputRoot=Join-Path $NativeRoot 'out'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
function Check-Exit([string]$Message){if($LASTEXITCODE -ne 0){throw $Message}}
foreach($Module in @('battle','icon','bridge')){
 cmake -S (Join-Path $NativeRoot $Module) -B (Join-Path $OutputRoot $Module) -A x64
 Check-Exit "$Module configuration failed"
 cmake --build (Join-Path $OutputRoot $Module) --config Release
 Check-Exit "$Module build failed"
}
& (Join-Path $OutputRoot 'battle/Release/BattleEngineChecks.exe'); Check-Exit 'Gameplay checks failed'
& (Join-Path $OutputRoot 'battle/Release/NativeStudioChecks.exe'); Check-Exit 'Studio checks failed'
dotnet run --project (Join-Path $NativeRoot 'launcher/tests/KinshipLauncherChecks.csproj') -c Release
Check-Exit 'Launcher checks failed'
dotnet publish (Join-Path $NativeRoot 'launcher/KinshipStudioLauncher.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o (Join-Path $OutputRoot 'app')
Check-Exit 'Launcher publish failed'
$PayloadRoot=Join-Path $OutputRoot 'payload'
$ModulesRoot=Join-Path $PayloadRoot 'modules'
New-Item -ItemType Directory -Path $ModulesRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $OutputRoot 'app/KinshipStudioLauncher.exe') -Destination $PayloadRoot
foreach($Pair in @(@('battle','KinshipNativeBattleV2.dll'),@('icon','KinshipCampIcon.dll'),@('bridge','KinshipLoadBridge.dll'))){Copy-Item -LiteralPath (Join-Path $OutputRoot ($Pair[0]+'/Release/'+$Pair[1])) -Destination $ModulesRoot}
foreach($Name in @('kinship-ui-atlas.png','kinship-emblem.png')){Copy-Item -LiteralPath (Join-Path $NativeRoot ('assets/'+$Name)) -Destination $ModulesRoot}
$Manifest=Get-Content -LiteralPath (Join-Path $NativeRoot 'assets/manifest-template.json') -Raw | ConvertFrom-Json
$Hashes=[ordered]@{}
Get-ChildItem -LiteralPath $ModulesRoot -File | Where-Object Name -ne 'manifest.json' | ForEach-Object {$Hashes[$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
$Manifest.Files=$Hashes
$Manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $ModulesRoot 'manifest.json') -Encoding utf8NoBOM
Copy-Item -LiteralPath (Join-Path $NativeRoot 'USER-GUIDE.txt') -Destination (Join-Path $PayloadRoot 'Readme.txt')
$LicenseRoot=Join-Path $PayloadRoot 'licenses';New-Item -ItemType Directory -Path $LicenseRoot -Force | Out-Null
$NugetRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
foreach($Runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')){
 $RuntimeRoot=Get-ChildItem -LiteralPath (Join-Path $NugetRoot $Runtime) -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
 foreach($Name in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){if(Test-Path -LiteralPath (Join-Path $RuntimeRoot.FullName $Name)){Copy-Item -LiteralPath (Join-Path $RuntimeRoot.FullName $Name) -Destination (Join-Path $LicenseRoot ($Runtime+'-'+$Name+'.txt'))}}
}
$Files=[ordered]@{}
Get-ChildItem -LiteralPath $PayloadRoot -Recurse -File | Where-Object Name -ne 'launcher-install.json' | ForEach-Object {$Files[[IO.Path]::GetRelativePath($PayloadRoot,$_.FullName)]=(Get-FileHash -LiteralPath $_.FullName).Hash}
@{Id='KinshipStudio.NativeLauncher.E51718EA-3450-48D8-9C59-E51AE70E4D4B';Version='1.1.0';Files=$Files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PayloadRoot 'launcher-install.json') -Encoding utf8NoBOM
if($Installer){
 if(-not $InnoCompiler){$InnoCompiler=@((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')) | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1}
 if(-not $InnoCompiler){throw 'Install Inno Setup 6 or pass -InnoCompiler.'}
 & $InnoCompiler ('/DPayload='+$PayloadRoot) ('/DOutput='+(Join-Path $OutputRoot 'installer')) (Join-Path $NativeRoot 'installer/installer.iss')
 Check-Exit 'Setup compilation failed'
}
Write-Output "Ready: $PayloadRoot"
