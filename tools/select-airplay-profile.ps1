param([ValidateSet('Stable','UxPlay-iOS27')][string]$Profile='UxPlay-iOS27',[switch]$Launch)
. "$PSScriptRoot\airplay-setup-common.ps1"
$metadataPath=Join-Path $AirPlayRoot '.cache\readiness\launcher.json'
if (Test-Path $metadataPath) {
    $metadata=Get-Content $metadataPath -Raw | ConvertFrom-Json
    $existing=Get-Process -Id $metadata.Id -ErrorAction SilentlyContinue
    if (Test-AirPlayOwnedLauncher $existing $metadata) { throw 'Close iMirror before switching profiles. No process is forcibly terminated.' }
}
@{Profile=$Profile} | ConvertTo-Json | Set-Content (Join-Path $AirPlayRoot '.cache\readiness\active-airplay-profile.json') -Encoding UTF8
Write-AirPlaySetupLog "Selected AirPlay profile=$Profile. Stable executable/configuration preserved; firewall unchanged."
if ($Launch) { & "$PSScriptRoot\start-imirror.ps1" }
