param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
. "$PSScriptRoot\common.ps1"
Push-Location $ProjectRoot
try {
    & "$PSScriptRoot\build.ps1" -Configuration $Configuration
    $evidence = Join-Path $ProjectRoot ('.cache\validation\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $application = Join-Path $ProjectRoot "src\iMirror.App\bin\$Configuration\net10.0-windows\iMirror.dll"
    Invoke-ProjectDotNet -Arguments @('run', '--project', 'tests\iMirror.Phase1.Tests\iMirror.Phase1.Tests.csproj',
        '--configuration', $Configuration, '--no-build', '--', $evidence, $DotNetPath, $application)
    Invoke-ProjectDotNet -Arguments @('run', '--project', 'tests\iMirror.Phase2.Tests\iMirror.Phase2.Tests.csproj',
        '--configuration', $Configuration, '--no-build', '--', $evidence, $DotNetPath, $ProjectRoot)
    Invoke-ProjectDotNet -Arguments @('run', '--project', 'tests\iMirror.Phase3.Tests\iMirror.Phase3.Tests.csproj',
        '--configuration', $Configuration, '--no-build')
    & "$PSScriptRoot\trace-bluetooth-link.ps1" -SelfTest
} finally { Pop-Location }
