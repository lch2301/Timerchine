[CmdletBinding()]
param(
    [string]$Directory,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [ValidateSet('CurrentUser','LocalMachine')][string]$StoreLocation = 'CurrentUser',
    [Parameter(Mandatory=$true)][string]$SignTool,
    [Parameter(Mandatory=$true)][ValidatePattern('^https?://[A-Za-z0-9:/._~-]+$')][string]$TimestampUrl,
    [switch]$CheckCertificateOnly
)
$ErrorActionPreference = 'Stop'
$certificate = Get-Item -LiteralPath "Cert:\$StoreLocation\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (-not $certificate -or -not $certificate.HasPrivateKey) { throw 'No usable code-signing certificate/private key was found. Obtain a public-trust code-signing certificate first.' }
if ($certificate.Subject -eq $certificate.Issuer) { throw 'A self-signed certificate is not suitable for public Smart App Control distribution.' }
if ($certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'The signing certificate is outside its validity period.' }
if (-not ($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) { throw 'The certificate does not have the code-signing EKU.' }
if (-not (Test-Path -LiteralPath $SignTool -PathType Leaf)) { throw 'SignTool was not found.' }
if ($CheckCertificateOnly) { return }
if (-not $Directory) { throw 'A staging directory is required.' }
$resolved = (Resolve-Path -LiteralPath $Directory).Path
$workspace = Split-Path -Parent $PSScriptRoot
$allowed = [IO.Path]::GetFullPath((Join-Path $workspace 'dist')) + [IO.Path]::DirectorySeparatorChar
if (-not ($resolved + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Signing must operate on copies under this workspace dist directory.' }
$report = @()
foreach ($file in Get-ChildItem -LiteralPath $resolved -File -Recurse | Where-Object { $_.Extension -in '.exe','.dll' }) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    $originalHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $signedHere = $false
    if ($signature.Status -eq 'NotSigned') {
        $arguments = @('sign','/sha1',$CertificateThumbprint,'/s','My','/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256','/d','Timerchine')
        if ($StoreLocation -eq 'LocalMachine') { $arguments += '/sm' }
        & $SignTool @arguments $file.FullName
        if ($LASTEXITCODE -ne 0) { throw "Code signing failed: $($file.FullName)" }
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        $signedHere = $true
    }
    if ($signature.Status -ne 'Valid') { throw "Signature is not trusted/valid on this PC: $($file.FullName) [$($signature.Status)]" }
    if ($signedHere -and -not $signature.TimeStamperCertificate) { throw "A trusted timestamp is missing: $($file.FullName)" }
    & $SignTool verify /pa /all $file.FullName
    if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed: $($file.FullName)" }
    $report += [pscustomobject]@{
        File = $file.FullName.Substring($resolved.Length).TrimStart('\')
        Status = $signature.Status.ToString()
        OriginalSHA256 = $originalHash
        SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        SignedInThisRun = $signedHere
        Signer = $signature.SignerCertificate.Subject
        Timestamped = [bool]$signature.TimeStamperCertificate
    }
}
ConvertTo-Json -InputObject @($report) -Depth 5 | Set-Content -LiteralPath (Join-Path $resolved 'signature-report.json') -Encoding UTF8
