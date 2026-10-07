param([ValidateSet('Active','Stable','UxPlay-iOS27')][string]$AirPlayProfile='Active',[switch]$StartAirPlay)
. "$PSScriptRoot\airplay-setup-common.ps1"
. "$PSScriptRoot\common.ps1"
$application=Join-Path $AirPlayRoot 'src\iMirror.App\bin\Release\net10.0-windows\iMirror.dll'
if (-not (Test-Path -LiteralPath $application)) { throw 'Release build missing. Run tools\prepare-airplay.ps1.' }
$metadataPath=Join-Path $AirPlayRoot '.cache\readiness\launcher.json'
$selectionPath=Join-Path $AirPlayRoot '.cache\readiness\active-airplay-profile.json'
if ($AirPlayProfile -eq 'Active') {
    $AirPlayProfile='Stable'
    if (Test-Path $selectionPath) { $AirPlayProfile=[string](Get-Content $selectionPath -Raw | ConvertFrom-Json).Profile }
}
if ($AirPlayProfile -notin @('Stable','UxPlay-iOS27')) { throw 'Unknown active AirPlay profile.' }
$configurationFile=if($AirPlayProfile -eq 'Stable'){Join-Path $AirPlayRoot 'airplay.json'}else{Join-Path $AirPlayRoot 'profiles\UxPlay-iOS27.json'}
if (-not (Test-Path $configurationFile)) { throw 'AirPlay profile missing.' }
if (Test-Path $metadataPath) {
    $metadata=Get-Content $metadataPath -Raw | ConvertFrom-Json
    $existing=Get-Process -Id $metadata.Id -ErrorAction SilentlyContinue
    if (Test-AirPlayOwnedLauncher $existing $metadata) {
        if ($StartAirPlay) { throw 'Close current iMirror before requesting startup AirPlay; no duplicate instance launched.' }
        if ($metadata.AirPlayProfile -ne $AirPlayProfile) { throw 'Close the current iMirror before changing its AirPlay profile.' }
        Write-AirPlaySetupLog 'iMirror is already open; no duplicate instance started.'; return
    }
}
$start=[Diagnostics.ProcessStartInfo]::new($DotNetPath)
$start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WorkingDirectory=$AirPlayRoot
$start.Arguments='"'+$application+'"'
if ($StartAirPlay) { $start.Arguments+=' --start-airplay' }
$start.EnvironmentVariables['IMIRROR_AIRPLAY_CONFIG']=$configurationFile
$start.EnvironmentVariables['IMIRROR_LOG_DIRECTORY']=Join-Path $AirPlayRoot '.cache\readiness\app-logs'
$applicationProcess=[Diagnostics.Process]::Start($start)
$clock=[Diagnostics.Stopwatch]::StartNew()
while ($clock.Elapsed.TotalSeconds -lt 15) {
    $applicationProcess.Refresh()
    if ($applicationProcess.HasExited) { throw 'iMirror exited during startup.' }
    if ($applicationProcess.MainWindowHandle -ne [IntPtr]::Zero -and $applicationProcess.MainWindowTitle -eq 'iMirror') { break }
    Start-Sleep -Milliseconds 100
}
if ($applicationProcess.MainWindowTitle -ne 'iMirror') { throw 'iMirror window startup not confirmed.' }
[pscustomobject]@{Id=$applicationProcess.Id;StartTimeUtc=$applicationProcess.StartTime.ToUniversalTime().ToString('o');AirPlayProfile=$AirPlayProfile} |
    ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding UTF8
Write-AirPlaySetupLog "iMirror Release window opened; profile=$AirPlayProfile; startup AirPlay=$StartAirPlay."
