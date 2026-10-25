[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory=$true)][ValidatePattern('^https?://[A-Za-z0-9:/._~-]+$')][string]$TimestampUrl,
    [ValidateSet('CurrentUser','LocalMachine')][string]$StoreLocation = 'CurrentUser',
    [string]$SignTool
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
if (-not $SignTool) {
    $kit = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $SignTool = Get-ChildItem -LiteralPath $kit -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $SignTool) { throw 'Install the Windows SDK code-signing tools first.' }
$signScript = Join-Path $PSScriptRoot 'Sign-PEFiles.ps1'
# Fail before creating or changing build outputs when a certificate is missing.
& $signScript -CheckCertificateOnly -CertificateThumbprint $CertificateThumbprint -StoreLocation $StoreLocation -SignTool $SignTool -TimestampUrl $TimestampUrl
$project = Join-Path $workspace 'IntervalTimer.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
$tag = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$stage = Join-Path $workspace "dist\.signing-$tag"
$media = Join-Path $stage 'media'
$payload = Join-Path $stage 'payload'
$release = Join-Path $workspace "dist\Timerchine-$version-signed-$tag"
New-Item -ItemType Directory -Path $media,$payload,$release | Out-Null
$originals = Join-Path $workspace 'Assets\Media'
$expected = Get-Content -LiteralPath (Join-Path $originals 'integrity.json') -Raw | ConvertFrom-Json
foreach ($entry in $expected.PSObject.Properties) {
    $source = Join-Path $originals $entry.Name
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Value) { throw "Original tool integrity mismatch: $($entry.Name)" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $media $entry.Name)
}
& $signScript -Directory $media -CertificateThumbprint $CertificateThumbprint -StoreLocation $StoreLocation -SignTool $SignTool -TimestampUrl $TimestampUrl
$hashes = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath $media -Filter '*.exe' -File) { $hashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
$integrity = Join-Path $media 'integrity.json'
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($integrity, ($hashes | ConvertTo-Json), $utf8)
$notice = @'
Signing supplement: tool versions, licenses and source references are unchanged.
The original upstream hashes in NOTICE/ONLINE-NOTICE refer to the upstream files.
Unsigned tool copies have an Authenticode publisher signature added for this release.
Existing valid upstream signatures are preserved. Program code is not rewritten.
Post-signing hashes and publisher information are recorded in media-signatures.json.
Signing does not complete the separate open-source/public-distribution legal review.
'@
[IO.File]::WriteAllText((Join-Path $media 'SIGNING-NOTICE.txt'), $notice, $utf8)
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet'
$env:NUGET_PACKAGES = Join-Path $workspace '.dotnet\.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$arguments = @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','--no-restore',
    '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true',
    '-p:EnableExternalLinks=true',"-p:MediaFilesDirectory=$media","-p:MediaIntegrityManifest=$integrity",
    "-p:CodeSigningThumbprint=$CertificateThumbprint","-p:CodeSigningStore=$StoreLocation", "-p:CodeSigningTool=$SignTool",
    "-p:CodeSigningTimestampUrl=$TimestampUrl","-p:CodeSigningPayloadDirectory=$payload",'-o',$release)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Signed-payload publishing failed. Do not distribute the staging output.' }
& $signScript -Directory $release -CertificateThumbprint $CertificateThumbprint -StoreLocation $StoreLocation -SignTool $SignTool -TimestampUrl $TimestampUrl
$exe = Join-Path $release 'Timerchine.exe'
$digest = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$baselinePackage = Join-Path $workspace "dist\Timerchine-$version-win-x64"
foreach ($name in 'Licenses') {
    $source = Join-Path $baselinePackage $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $release -Recurse }
}
Get-ChildItem -LiteralPath $baselinePackage -Filter '*.ko.txt' -File -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $release
}
Copy-Item -LiteralPath (Join-Path $media 'signature-report.json') -Destination (Join-Path $release 'media-signatures.json')
Copy-Item -LiteralPath (Join-Path $payload 'signature-report.json') -Destination (Join-Path $release 'bundle-signatures.json')
Copy-Item -LiteralPath (Join-Path $media 'SIGNING-NOTICE.txt') -Destination $release
Set-Content -LiteralPath (Join-Path $release 'Timerchine.exe.sha256') -Value "$digest  Timerchine.exe" -Encoding ASCII
$manifest = [ordered]@{
    product='Timerchine'; version=$version; rid='win-x64'; self_contained_single_file=$true
    authenticode_verified_on_build_pc=$true; smart_app_control_tested=$false
    user_settings_included=$false; public_license_review='REVIEW_REQUIRED'
    exe_sha256=$digest
    files=@(Get-ChildItem -LiteralPath $release -File -Recurse | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
        [ordered]@{ path=$_.FullName.Substring($release.Length+1); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $release 'manifest.json') -Encoding UTF8
$archive = $release + '.zip'
$ship = @(Get-ChildItem -LiteralPath $release | Where-Object { $_.Extension -ne '.pdb' } | Select-Object -ExpandProperty FullName)
Compress-Archive -LiteralPath $ship -DestinationPath $archive -CompressionLevel Optimal
Set-Content -LiteralPath ($archive+'.sha256') -Value "$((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($archive))" -Encoding ASCII
Write-Output "Signed EXE: $exe"
Write-Output "Signed ZIP: $archive"
Write-Output 'Local signature checks passed. Smart App Control validation on a protected PC is still required.'
