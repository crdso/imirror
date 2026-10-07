param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
. "$PSScriptRoot\common.ps1"
Push-Location $ProjectRoot
try {
    $env:IMIRROR_AIRPLAY_CONFIG = Join-Path $ProjectRoot 'airplay.json'
    & "$PSScriptRoot\build.ps1" -Configuration $Configuration
    Invoke-ProjectDotNet -Arguments @('run', '--project', 'src\iMirror.App\iMirror.App.csproj', '--configuration', $Configuration, '--no-build')
} finally { Pop-Location }
