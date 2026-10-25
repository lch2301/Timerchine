[CmdletBinding()]
param([bool]$IncludeOnlineTools = $true, [string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$media = Join-Path $workspace 'Assets\Media'
$integrity = Get-Content -LiteralPath (Join-Path $media 'integrity.json') -Raw | ConvertFrom-Json
$tools = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'media-tools.json') -Raw | ConvertFrom-Json
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'obj'))
$temporary = [IO.Path]::GetFullPath((Join-Path $cacheRoot ('media-' + [guid]::NewGuid().ToString('N'))))
if (-not $temporary.StartsWith($cacheRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Media staging path must be inside the workspace obj directory.'
}
function Test-Original([string]$Path, [string]$Name) {
    (Test-Path -LiteralPath $Path -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $integrity.$Name)
}
try {
    foreach ($entry in $tools.PSObject.Properties) {
        if (-not $IncludeOnlineTools -and $entry.Name -ne 'ffmpeg') { continue }
        $spec = $entry.Value
        $missing = @($spec.files | Where-Object { -not (Test-Original (Join-Path $media $_) $_) })
        if ($missing.Count -eq 0) { Write-Output ('Verified existing ' + $entry.Name); continue }
        $unpacked = $null
        if (-not $SourceDirectory) {
            New-Item -ItemType Directory -Path $temporary -Force | Out-Null
            $download = Join-Path $temporary ($entry.Name + '.' + $spec.format)
            Invoke-WebRequest -Uri $spec.url -OutFile $download
            if ($spec.format -eq 'zip') {
                $unpacked = Join-Path $temporary ($entry.Name + '-expanded')
                Expand-Archive -LiteralPath $download -DestinationPath $unpacked
            }
        }
        foreach ($name in $missing) {
            if ($SourceDirectory) { $candidate = Join-Path $SourceDirectory $name }
            elseif ($spec.format -eq 'exe') { $candidate = $download }
            else {
                $archiveEntries = @(Get-ChildItem -LiteralPath $unpacked -Recurse -File -Filter $name)
                if ($archiveEntries.Count -ne 1) { throw ('Expected exactly one ' + $name + ' in archive') }
                $candidate = $archiveEntries[0].FullName
            }
            if (-not (Test-Original $candidate $name)) { throw ('SHA256 mismatch or missing original: ' + $name) }
            Copy-Item -LiteralPath $candidate -Destination (Join-Path $media $name)
            Write-Output ('Restored and verified ' + $name)
        }
    }
} finally {
    if (Test-Path -LiteralPath $temporary -PathType Container) {
        $resolved = (Resolve-Path -LiteralPath $temporary).Path
        if (-not $resolved.StartsWith($cacheRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove media staging outside workspace obj directory.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
