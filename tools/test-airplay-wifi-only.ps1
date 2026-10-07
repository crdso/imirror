param([switch]$PreflightOnly,[switch]$Elevate)
. "$PSScriptRoot\airplay-network-common.ps1"
$testLock=$null; $mustRestore=$false; $appMetadata=$null; $outcome='BLOCKED: not started'; $wifi=$null; $dns=$null; $sessionId=$null
function Save-WifiTestResult([string]$Status) {
    [pscustomobject]@{Timestamp=(Get-Date -Format o);Status=$Status;WifiIPv4=$(if($wifi){$wifi.IPv4}else{$null});
        BonjourIPv4=@($(if($dns){$dns.Addresses | ForEach-Object {$_.Address}}));Phase2='PENDING PHYSICAL IPHONE VALIDATION'} |
        ConvertTo-Json -Depth 4 | Set-Content $NetworkResultPath -Encoding UTF8
}
try {
    Write-AirPlayNetworkLog "WIFI-ONLY TEST START; administrator=$(Test-AirPlayAdministrator); preflight=$PreflightOnly"
    $existingState=Get-AirPlayNetworkState
    if (-not $PreflightOnly -and $existingState -and -not $existingState.Restored -and (Test-NetworkProcessIdentity $existingState.OwnerId $existingState.OwnerStartUtc)) {
        Write-AirPlayNetworkLog ('Existing Wi-Fi-only test: '+$existingState.Status+'. Nothing changed; use its terminal or restore-airplay-network.cmd.'); exit 0
    }
    if (-not $PreflightOnly -and -not(Test-AirPlayAdministrator)) {
        if ($Elevate) {
            $runner=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            Start-Process $runner -Verb RunAs -WindowStyle Normal -ArgumentList ('-NoProfile -NoExit -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"') | Out-Null
            $outcome='BLOCKED: administrative elevation pending; one UAC request, no retry.'
            Save-WifiTestResult $outcome; Write-AirPlayNetworkLog $outcome; exit 0
        }
        throw 'Administrator required. Double-click tools\test-airplay-wifi-only.cmd once and accept Windows elevation.'
    }
    $wifi=Get-WifiOnlyAdapter; $wsl=Get-ExactWslAdapter
    $hostname=[Net.Dns]::GetHostName()+'.local.'
    Write-AirPlayNetworkLog "Detected physical Wi-Fi: $($wifi.Adapter.Name), interface=$($wifi.Adapter.InterfaceIndex), IPv4=$($wifi.IPv4); Bonjour hostname=$hostname"
    $before=Get-AirPlayNetworkSnapshot
    Write-AirPlayNetworkLog ('INTERFACES BEFORE / Get-NetIPConfiguration / Get-NetIPInterface: '+($before | ConvertTo-Json -Depth 6 -Compress))
    $dns=Get-AirPlayDnsEvidence $hostname 'BEFORE'
    if ($PreflightOnly) {
        $outcome='PREFLIGHT ONLY: no adapter/service/firewall change; administrator required for Wi-Fi-only test.'
        Save-WifiTestResult $outcome; Write-AirPlayNetworkLog $outcome; exit 0
    }
    try { $testLock=[IO.File]::Open((Join-Path $AirPlayRoot '.cache\readiness\network-test.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
    catch [IO.IOException] { Write-AirPlayNetworkLog 'Existing Wi-Fi-only test is active; nothing changed. Use its terminal or restore shortcut.'; exit 0 }
    $previous=Get-AirPlayNetworkState
    if ($previous -and -not $previous.Restored) {
        if (Test-NetworkProcessIdentity $previous.OwnerId $previous.OwnerStartUtc) { throw 'Another saved test is active; no state overwrite.' }
        Restore-AirPlayNetworkState
        $wsl=Get-ExactWslAdapter # Snapshot again after recovering an interrupted older test.
    }
    $metadataPath=Join-Path $AirPlayRoot '.cache\readiness\launcher.json'
    if (Test-Path $metadataPath) { Close-OwnedNetworkTestApp (Get-Content $metadataPath -Raw | ConvertFrom-Json) }
    if (@(Get-Process -Name uxplay -ErrorAction SilentlyContinue).Count) { throw 'An unrelated receiver is running; no global process kill attempted.' }
    $profile=Get-Content (Join-Path $AirPlayRoot 'profiles\UxPlay-iOS27.json') -Raw | ConvertFrom-Json
    if (-not $profile.EnableH265 -or $profile.BasePort -ne 35000 -or $profile.VideoSink -ne 'd3d11videosink' -or $profile.ReceiverName -ne 'iMirror - Windows') { throw 'Current iOS27 profile changed unexpectedly; test stopped.' }
    $owner=Get-Process -Id $PID
    $state=[pscustomobject]@{Schema=1;SessionId=[Guid]::NewGuid().ToString('N');OwnerId=$PID;OwnerStartUtc=$owner.StartTime.ToUniversalTime().ToString('o');AppMetadata=$null;
        AdapterName='vEthernet (WSL)';AdapterGuid=[string]$wsl.InterfaceGuid;AdapterIndex=$wsl.InterfaceIndex;OriginalAdminStatus=[string]$wsl.AdminStatus;
        OriginalOperationalStatus=[string]$wsl.Status;WasEnabled=(Test-AdapterAdministrativelyEnabled $wsl);DisableIntent=$false;Restored=$false;RestoredAt=$null;
        WifiGuid=[string]$wifi.Adapter.InterfaceGuid;WifiIndex=[int]$wifi.Adapter.InterfaceIndex;WifiIPv4=$wifi.IPv4;Status='PREPARING';
        DeadlineUtc=[DateTime]::UtcNow.AddHours(2).ToString('o');Before=$before}
    $sessionId=$state.SessionId
    Save-AirPlayNetworkState $state; $mustRestore=$true
    $runner=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $watchdog=Start-Process $runner -WindowStyle Hidden -PassThru -ArgumentList ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$PSScriptRoot+'\airplay-network-watchdog.ps1" -SessionId '+$state.SessionId)
    $guardianReady=Join-Path $AirPlayRoot ('.cache\readiness\network-watchdog-'+$state.SessionId+'.ready')
    $deadline=[DateTime]::UtcNow.AddSeconds(10)
    while(-not(Test-Path $guardianReady) -and -not $watchdog.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not(Test-Path $guardianReady)) { throw 'Administrative recovery watchdog did not start; WSL will not be disabled.' }
    $actionLock=Enter-NetworkActionLock
    try {
        Assert-WifiUntouched $state
        $state=Get-AirPlayNetworkState
        if ($state.Restored) { throw 'Test was already cancelled by recovery request.' }
        if ($state.SessionId -ne $sessionId) { throw 'Network test session changed; no adapter mutation.' }
        Disable-SavedWslAdapter $state
    } finally { $actionLock.Dispose() }
    Start-Sleep -Seconds 5
    Assert-WifiUntouched $state
    Write-AirPlayNetworkLog ('INTERFACES AFTER: '+((Get-AirPlayNetworkSnapshot) | ConvertTo-Json -Depth 6 -Compress))
    $dns=Get-AirPlayDnsEvidence $hostname 'AFTER DISABLE' -AddressOnly
    if (-not(Test-WifiOnlyDns $dns $state) -and @($dns.Addresses | Where-Object {$_.Address -ne $state.WifiIPv4}).Count -gt 0) {
        Write-AirPlayNetworkLog 'Bonjour still returned non-Wi-Fi IPv4 after WSL disable; one controlled Bonjour Service restart to clear stale advertisement/cache.'
        Restart-Service -Name 'Bonjour Service' -ErrorAction Stop
        $bonjour=Get-Service -Name 'Bonjour Service'; $bonjour.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(20))
        Start-Sleep -Seconds 5
        $dns=Get-AirPlayDnsEvidence $hostname 'AFTER BONJOUR RESTART' -AddressOnly
    }
    if (-not(Test-WifiOnlyDns $dns $state)) { throw 'Bonjour hostname did not resolve exclusively to Wi-Fi IPv4/interface; no READY claim.' }
    & "$PSScriptRoot\start-imirror.ps1" -AirPlayProfile UxPlay-iOS27 -StartAirPlay
    $appMetadata=Get-Content $metadataPath -Raw | ConvertFrom-Json
    $actionLock=Enter-NetworkActionLock
    try { $state=Get-AirPlayNetworkState; if($state.Restored -or $state.SessionId -ne $sessionId){throw 'Network restored/session changed during startup.'}; $state.AppMetadata=$appMetadata; Save-AirPlayNetworkState $state }
    finally { $actionLock.Dispose() }
    $deadline=[DateTime]::UtcNow.AddSeconds(25); $announced=$false
    do {
        if (-not(Test-NetworkProcessIdentity $appMetadata.Id $appMetadata.StartTimeUtc)) { throw 'iMirror exited during startup.' }
        $appLog=Get-ChildItem (Join-Path $AirPlayRoot '.cache\readiness\app-logs') -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($appLog -and $appLog.LastWriteTimeUtc -ge ([DateTime]$appMetadata.StartTimeUtc).ToUniversalTime()) {
            $appText=Get-Content $appLog.FullName -Raw -Encoding UTF8
            $announced=$appText -match 'Aguardando iPhone\.\.\.' -and $appText -match [regex]::Escape('UxPlay-iOS27')
        }
        if (-not $announced) { Start-Sleep -Milliseconds 250 }
    } while(-not $announced -and [DateTime]::UtcNow -lt $deadline)
    if (-not $announced) { throw 'iMirror did not reach waiting-for-iPhone state.' }
    $receivers=@(Get-CimInstance Win32_Process -Filter "Name='uxplay.exe'" -ErrorAction Stop)
    if ($receivers.Count -ne 1 -or $receivers[0].ParentProcessId -ne $appMetadata.Id) { throw 'Expected exactly one receiver owned by this iMirror.' }
    $dns=Get-AirPlayDnsEvidence $hostname 'FINAL'
    if (-not(Test-WifiOnlyService $dns $state $hostname)) { throw 'Final browse/resolve/address validation failed; test will restore WSL.' }
    Assert-WifiUntouched $state
    $argumentLine=Get-Content (Join-Path $AirPlayRoot 'logs\iphone-airplay-attempt.log') | Where-Object {$_ -match 'Arguments JSON='} | Select-Object -Last 1
    Write-AirPlayNetworkLog ('UxPlay version=1.73.7; owned PID='+$receivers[0].ProcessId+'; '+$argumentLine)
    $actionLock=Enter-NetworkActionLock
    try { $state=Get-AirPlayNetworkState; if($state.Restored -or $state.SessionId -ne $sessionId){throw 'Network was restored/session changed before readiness.'}; $state.Status='READY FOR IPHONE TEST - WIFI ONLY'; Save-AirPlayNetworkState $state }
    finally { $actionLock.Dispose() }
    $outcome='READY FOR IPHONE TEST - WIFI ONLY'; Save-WifiTestResult $outcome; Write-AirPlayNetworkLog $outcome
    Write-Host 'Teste o iPhone agora. Mantenha este terminal aberto. Fechar iMirror ou encerrar este terminal restaura o WSL.'
    Write-Host ('Restaurar com um clique: '+$PSScriptRoot+'\restore-airplay-network.cmd')
    Write-Host 'Limite de seguranca: 2 horas. Wi-Fi, Ethernet fisica e firewall nao foram alterados.'
    $nextNetworkCheck=[DateTime]::UtcNow
    while(Test-NetworkProcessIdentity $appMetadata.Id $appMetadata.StartTimeUtc) {
        $current=Get-AirPlayNetworkState
        if ($current.Restored) { break }
        if (Test-Path (Join-Path $AirPlayRoot ('.cache\readiness\restore-network-'+$state.SessionId+'.request'))) { break }
        if ([DateTime]::UtcNow -ge ([DateTime]$state.DeadlineUtc).ToUniversalTime()) { break }
        if ([DateTime]::UtcNow -ge $nextNetworkCheck) {
            Assert-WifiUntouched $state
            if (Test-AdapterAdministrativelyEnabled (Get-ExactWslAdapter $state.AdapterGuid)) { throw 'WSL was re-enabled during test; Wi-Fi-only condition no longer holds.' }
            $activeReceivers=@(Get-CimInstance Win32_Process -Filter "Name='uxplay.exe'" -ErrorAction Stop)
            if($activeReceivers.Count -ne 1 -or $activeReceivers[0].ProcessId -ne $receivers[0].ProcessId -or $activeReceivers[0].ParentProcessId -ne $appMetadata.Id) { throw 'Original receiver stopped or extra receiver started; ending Wi-Fi-only test.' }
            $nextNetworkCheck=[DateTime]::UtcNow.AddSeconds(10)
        }
        Start-Sleep -Seconds 1
    }
} catch {
    $outcome='BLOCKED: '+$_.Exception.Message
    Write-AirPlayNetworkLog $outcome
    if($wifi) { Write-AirPlayNetworkLog ('INTERFACES AT BLOCK: '+((Get-AirPlayNetworkSnapshot) | ConvertTo-Json -Depth 6 -Compress)) }
    Save-WifiTestResult $outcome
} finally {
    if ($mustRestore) {
        try { Close-OwnedNetworkTestApp $appMetadata }
        catch { Write-AirPlayNetworkLog ('APP CLEANUP: '+$_.Exception.Message) }
        try {
            Restore-AirPlayNetworkState -ExpectedSessionId $sessionId
            Write-AirPlayNetworkLog 'TEST FINISHED; original WSL administrative state restored; PHASE 2: PENDING PHYSICAL IPHONE VALIDATION.'
            Write-AirPlayNetworkLog ('INTERFACES RESTORED: '+((Get-AirPlayNetworkSnapshot) | ConvertTo-Json -Depth 6 -Compress))
            if ($outcome -eq 'READY FOR IPHONE TEST - WIFI ONLY') { $outcome='RESTORED: test server stopped; physical validation remains pending.' }
            Save-WifiTestResult $outcome
        }
        catch { Write-AirPlayNetworkLog ('RESTORE REQUIRED: '+$_.Exception.Message+' Use tools\restore-airplay-network.cmd.'); $outcome='BLOCKED: automatic restore failed; saved state retained'; Save-WifiTestResult $outcome }
    }
    if ($testLock) { $testLock.Dispose() }
}
if ($outcome -like 'BLOCKED:*') { exit 1 }
exit 0
