[CmdletBinding()]
param([string]$Executable)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $Executable) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $workspace 'IntervalTimer.csproj')
    $version = [string]$project.Project.PropertyGroup.Version
    $Executable = Join-Path $workspace "dist\Timerchine-$version-win-x64\Timerchine.exe"
}
$files = @($Executable) + @(Get-ChildItem -LiteralPath (Join-Path $workspace 'Assets\Media') -Filter '*.exe' -File | Select-Object -ExpandProperty FullName)
$signatures = @($files | ForEach-Object {
    $signature = Get-AuthenticodeSignature -LiteralPath $_
    [ordered]@{
        file=$_; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
        status=$signature.Status.ToString()
        signer=if($signature.SignerCertificate){$signature.SignerCertificate.Subject}else{$null}
    }
})
$certificates = @(Get-ChildItem 'Cert:\CurrentUser\My','Cert:\LocalMachine\My' -CodeSigningCert -ErrorAction SilentlyContinue)
$report = [ordered]@{
    captured_at=(Get-Date).ToString('o')
    certificate_count=$certificates.Count
    unsigned_or_untrusted_binaries=@($signatures | Where-Object { $_.status -ne 'Valid' }).Count
    recipient_smart_app_control_result='NOT_TESTED'
    signatures=$signatures
}
$destination = Join-Path $workspace 'Audit\distribution-readiness.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $destination -Encoding UTF8
Write-Output $destination
