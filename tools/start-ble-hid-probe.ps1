param(
    [switch]$Build,
    [switch]$SelfTest,
    [ValidateRange(1,1800)][int]$WaitBluetoothSeconds = 900
)
. "$PSScriptRoot\common.ps1"
$probeProject = Join-Path $ProjectRoot 'experiments\BleHidProbe\BleHidProbe.csproj'
$probeConfig = Join-Path $ProjectRoot 'experiments\BleHidProbe\NuGet.Config'
$probeAssembly = Join-Path $ProjectRoot 'experiments\BleHidProbe\bin\Release\net10.0-windows10.0.19041.0\iMirror.BleHidProbe.dll'
$probeLog = Join-Path $ProjectRoot 'logs\ble-hid-probe.log'
Push-Location $ProjectRoot
try {
    if ($Build -or -not (Test-Path -LiteralPath $probeAssembly)) {
        Invoke-ProjectDotNet -Arguments @('restore',$probeProject,'--configfile',$probeConfig)
        Invoke-ProjectDotNet -Arguments @('build',$probeProject,'-c','Release','--no-restore')
    }
    if ($SelfTest) {
        Invoke-ProjectDotNet -Arguments @($probeAssembly,'--self-test')
    } else {
        $Host.UI.RawUI.WindowTitle = 'iMirror - BLE HID Probe (isolado)'
        Write-Host 'Probe isolado. AirPlay permanece intacto. Bluetooth deve estar ligado no Windows.'
        Write-Host 'Logs: logs\ble-hid-probe.log. Use exit ou Ctrl+C para encerrar com cleanup.'
        & $DotNetPath $probeAssembly --log $probeLog --wait-bluetooth $WaitBluetoothSeconds
        if ($LASTEXITCODE -ne 0) { throw "Probe terminou com código $LASTEXITCODE. Consulte $probeLog" }
    }
} finally { Pop-Location }
