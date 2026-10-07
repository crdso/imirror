param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
. "$PSScriptRoot\common.ps1"
Push-Location $ProjectRoot
try {
    Invoke-ProjectDotNet -Arguments @('restore', 'iMirror.sln', '--configfile', 'NuGet.Config')
    Invoke-ProjectDotNet -Arguments @('build', 'iMirror.sln', '--configuration', $Configuration, '--no-restore')
} finally { Pop-Location }
