# Administrative actions only: verified Bonjour installation/service and scoped firewall.
. "$PSScriptRoot\airplay-setup-common.ps1"
try {
    if (-not (Test-AirPlayAdministrator)) { throw 'Run this script once in PowerShell as administrator. No UAC retry is attempted.' }
    $paths=Get-AirPlayPaths; $info=Get-AirPlayInstallerInfo
    Write-AirPlaySetupLog "Bonjour Authenticode Status=$($info.Status)"
    Write-AirPlaySetupLog "Bonjour Publisher=$($info.Publisher)"
    Write-AirPlaySetupLog "Bonjour MSI Version=$($info.Version)"
    if ($info.Status -ne 'Valid' -or $info.Publisher -notmatch 'O=Apple Inc\.' -or
        $info.Hash -ne '46E31E284DA64D6C2D366352B8A8ABCF7DB28D3E2A870D8FCF15C4A6FE0A6DD1') { throw 'Invalid/unexpected installer; installation stopped.' }
    $service=Get-Service -Name 'Bonjour Service' -ErrorAction SilentlyContinue
    if (-not $service) {
        $installer=Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList @('/i',('"'+$paths.Msi+'"'),'/qn','/norestart') -WindowStyle Hidden -Wait -PassThru
        Write-AirPlaySetupLog "Bonjour msiexec ExitCode=$($installer.ExitCode)"
        if ($installer.ExitCode -notin @(0,3010)) { throw "Bonjour installation failed ($($installer.ExitCode)); no retry attempted." }
        if ($installer.ExitCode -eq 3010) { Write-AirPlaySetupLog 'Bonjour requested reboot; service must still validate before readiness.' }
        $service=Get-Service -Name 'Bonjour Service' -ErrorAction Stop
    } else { Write-AirPlaySetupLog 'Bonjour already installed; no reinstall.' }
    if (-not (Test-AirPlayX64Pe $paths.Bonjour)) { throw 'Installed Bonjour service executable is not x64.' }
    if ([string]$service.StartType -ne 'Automatic') { Set-Service -Name 'Bonjour Service' -StartupType Automatic }
    if ([string]$service.Status -ne 'Running') { Start-Service -Name 'Bonjour Service' }
    $service=Get-Service -Name 'Bonjour Service'; $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(20)); $service.Refresh()
    if ([string]$service.Status -ne 'Running' -or [string]$service.StartType -ne 'Automatic') { throw 'Bonjour service validation failed.' }
    Write-AirPlaySetupLog "Bonjour installed version=$((Get-Item $paths.Bonjour).VersionInfo.FileVersion); service Running/Automatic"
    & "$PSScriptRoot\configure-airplay-firewall.ps1" -Apply | Out-Null
    Write-AirPlaySetupLog 'Administrative setup complete. Run tools\prepare-airplay.ps1 as the normal user for full validation.'
    exit 0
} catch {
    Write-AirPlaySetupLog ('ADMIN SETUP FAILED: '+$_.Exception.Message)
    exit 1
}
