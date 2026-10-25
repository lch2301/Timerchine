[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MediaDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$LocalOnly
)
$ErrorActionPreference = 'Stop'
$names = @('ffmpeg.exe', 'ffprobe.exe')
if (-not $LocalOnly) { $names += @('yt-dlp.exe', 'deno.exe') }
$hashes = [ordered]@{}
foreach ($name in $names) {
    $path = Join-Path $MediaDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing media tool: ' + $name) }
    $hashes[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}
# The caller chooses the manifest explicitly; no original tool or signature is modified.
[IO.File]::WriteAllText($resolvedOutput, ($hashes | ConvertTo-Json) + [Environment]::NewLine,
    (New-Object Text.UTF8Encoding($false)))
Write-Output $resolvedOutput
