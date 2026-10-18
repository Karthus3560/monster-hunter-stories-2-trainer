param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$Thumbprint)
$ErrorActionPreference='Stop'
# Use a certificate already provisioned by its issuer. No key export, password or self-trust changes.
$certificate=Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {$_.Thumbprint -eq $Thumbprint} | Select-Object -First 1
if(-not $certificate -or -not $certificate.HasPrivateKey){throw 'The selected current-user code-signing certificate is unavailable or has no accessible private key.'}
if($certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)){throw 'The selected signing certificate is outside its validity period.'}
$signTool=$env:SIGNTOOL_PATH
if(-not $signTool){
    $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $signTool=Get-ChildItem -Path "$sdk/*/x64/signtool.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if(-not $signTool -or -not (Test-Path -LiteralPath $signTool)){throw 'Install Windows SDK signing tools, or set SIGNTOOL_PATH to the trusted SDK signtool.exe.'}
& $signTool sign /s My /sha1 $Thumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d 'Kinship Studio' $Path
if($LASTEXITCODE -ne 0){throw 'Code signing failed.'}
$signature=Get-AuthenticodeSignature -LiteralPath $Path
if($signature.Status -ne 'Valid'){throw "The signed file did not validate as trusted: $($signature.Status). Do not publish it as a trusted signed release."}
Write-Output "Trusted signature verified: $($signature.SignerCertificate.Subject)"
