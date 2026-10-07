$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe'
$installedSdk = Get-Command dotnet -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $localSdk) {
    $DotNetPath = $localSdk
} elseif ($installedSdk) {
    $DotNetPath = $installedSdk.Source
} else {
    throw 'SDK .NET 10 não encontrado. Instale o SDK oficial: https://dotnet.microsoft.com/download/dotnet/10.0'
}
$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.cache\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $ProjectRoot '.cache\nuget'

function Invoke-ProjectDotNet {
    param([string[]]$Arguments)
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet falhou (código $LASTEXITCODE)." }
}
