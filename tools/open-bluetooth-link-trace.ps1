param([ValidateRange(1,180)][int]$Seconds = 90,
    [ValidatePattern('^iMirror\.Bluetooth\.Status\.[0-9a-f]{32}$')][string]$StopSession,
    [switch]$ContinueAfterStop, [switch]$ShowConsole, [switch]$StartIMirror)
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'trace-bluetooth-link.ps1'
$openApp = $StartIMirror -and -not (Get-Process iMirror -ErrorAction SilentlyContinue)
if ($openApp) {
    $app = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\iMirror\iMirror.exe'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw 'EXE Release ausente em dist\iMirror. Compile o release antes deste diagnóstico.' }
}
# The collector needs no console input. Hide its background window so closing
# the iMirror panel cannot accidentally close the separate diagnostic console.
# Windows UAC supplies elevation; no credentials are handled by this script.
$arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Seconds {1}' -f $script, $Seconds
if ($StopSession) { $arguments += ' -StopSession ' + $StopSession }
if ($ContinueAfterStop) { $arguments += ' -ContinueAfterStop' }
try {
    $style = if ($ShowConsole) { 'Normal' } else { 'Hidden' }
    $launchTime = Get-Date
    $collector = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle $style -PassThru
} catch {
    $errorObject = $_.Exception
    while ($errorObject.InnerException) { $errorObject = $errorObject.InnerException }
    $code = if ($errorObject -is [ComponentModel.Win32Exception]) { $errorObject.NativeErrorCode } else { $errorObject.HResult }
    Write-Error ('A coleta não iniciou; Windows/UAC código={0}. Nenhum rádio, bond ou configuração foi alterado.' -f $code)
}
if ($openApp) {
    # Capture must precede advertising: a bonded phone can reconnect immediately.
    # This parent retains the original token; only the independent collector is
    # administrative. Never start the app from inside the elevated child.
    $statePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'logs\bluetooth-link-active.json'
    $deadline = (Get-Date).AddSeconds(20)
    $ready = $false
    while ((Get-Date) -lt $deadline -and -not $collector.HasExited) {
        if (Test-Path -LiteralPath $statePath) {
            try {
                $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
                $ready = $state.Session -match '^iMirror\.Bluetooth\.Status\.[0-9a-f]{32}$' -and [datetimeoffset]$state.Started -ge [datetimeoffset]$launchTime
            } catch { $ready = $false } # concurrent first write; retry only within the fixed bound
        }
        if ($ready) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw 'A coleta não confirmou startup; iMirror não foi anunciado antes do coletor. Consulte bluetooth-link-trace.log.' }
    if (-not (Get-Process iMirror -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath $app -ArgumentList '--start-bluetooth' -WorkingDirectory (Split-Path $app -Parent) -WindowStyle Normal
    }
}
