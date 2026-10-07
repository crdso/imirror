param([ValidateRange(1,180)][int]$Seconds = 90,
    [ValidatePattern('^iMirror\.Bluetooth\.Status\.[0-9a-f]{32}$')][string]$StopSession,
    [switch]$ContinueAfterStop, [switch]$ShowConsole, [switch]$StartIMirror)
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'trace-bluetooth-link.ps1'
if ($StartIMirror -and -not (Get-Process iMirror -ErrorAction SilentlyContinue)) {
    $app = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\iMirror\iMirror.exe'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw 'EXE Release ausente em dist\iMirror. Compile o release antes deste diagnóstico.' }
    # Launch from the original user token BEFORE UAC. The app/renderer should
    # not inherit the administrative token of the independent ETW collector.
    Start-Process -FilePath $app -ArgumentList '--start-bluetooth' -WorkingDirectory (Split-Path $app -Parent) -WindowStyle Normal
}
# The collector needs no console input. Hide its background window so closing
# the iMirror panel cannot accidentally close the separate diagnostic console.
# Windows UAC supplies elevation; no credentials are handled by this script.
$arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Seconds {1}' -f $script, $Seconds
if ($StopSession) { $arguments += ' -StopSession ' + $StopSession }
if ($ContinueAfterStop) { $arguments += ' -ContinueAfterStop' }
try {
    $style = if ($ShowConsole) { 'Normal' } else { 'Hidden' }
    Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle $style
} catch {
    $errorObject = $_.Exception
    while ($errorObject.InnerException) { $errorObject = $errorObject.InnerException }
    $code = if ($errorObject -is [ComponentModel.Win32Exception]) { $errorObject.NativeErrorCode } else { $errorObject.HResult }
    Write-Error ('A coleta não iniciou; Windows/UAC código={0}. Nenhum rádio, bond ou configuração foi alterado.' -f $code)
}
