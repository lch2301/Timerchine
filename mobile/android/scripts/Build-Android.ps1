[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$RunTests,
    [switch]$RequireSigning
)

$ErrorActionPreference = 'Stop'
$androidProject = Split-Path -Parent $PSScriptRoot
$wrapper = Join-Path $androidProject 'gradlew.bat'
if (-not (Test-Path -LiteralPath $wrapper -PathType Leaf)) {
    throw 'Gradle Wrapper was not found in this source checkout.'
}

if ($RequireSigning) {
    if ($Configuration -ne 'Release') {
        throw '-RequireSigning is used with -Configuration Release.'
    }
    foreach ($signingName in @('TIMERCHINE_KEYSTORE', 'TIMERCHINE_KEYSTORE_PASSWORD', 'TIMERCHINE_KEY_ALIAS', 'TIMERCHINE_KEY_PASSWORD')) {
        if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($signingName))) {
            throw "The signing environment variable $signingName is required."
        }
    }
    if (-not (Test-Path -LiteralPath $env:TIMERCHINE_KEYSTORE -PathType Leaf)) {
        throw 'The configured signing keystore was not found.'
    }
}

$gradleTasks = @('--no-daemon')
if ($RunTests) {
    $gradleTasks += @('testDebugUnitTest', 'lintDebug')
}
$gradleTasks += "assemble$Configuration"

Push-Location -LiteralPath $androidProject
try {
    & $wrapper @gradleTasks
    if ($LASTEXITCODE -ne 0) {
        throw "Android build failed with exit code $LASTEXITCODE."
    }
    $outputDirectory = Join-Path $androidProject ('app\build\outputs\apk\' + $Configuration.ToLowerInvariant())
    Get-ChildItem -LiteralPath $outputDirectory -Filter '*.apk' -File | Select-Object FullName, Length
}
finally {
    Pop-Location
}
