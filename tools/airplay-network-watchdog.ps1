param([Parameter(Mandatory=$true)][string]$SessionId)
. "$PSScriptRoot\airplay-network-common.ps1"
try {
    if (-not(Test-AirPlayAdministrator)) { throw 'Watchdog must inherit administrative privileges.' }
    $state=Get-AirPlayNetworkState
    if (-not $state -or $state.SessionId -ne $SessionId) { throw 'Watchdog session does not match saved state.' }
    [IO.File]::WriteAllText((Join-Path $AirPlayRoot ('.cache\readiness\network-watchdog-'+$SessionId+'.ready')),'ready',[Text.UTF8Encoding]::new($false))
    while($true) {
        $state=Get-AirPlayNetworkState
        if (-not $state) { throw 'Saved recovery state disappeared unexpectedly.' }
        if ($state.SessionId -ne $SessionId -or $state.Restored) { exit 0 }
        $request=Join-Path $AirPlayRoot ('.cache\readiness\restore-network-'+$SessionId+'.request')
        if (-not(Test-NetworkProcessIdentity $state.OwnerId $state.OwnerStartUtc) -or [DateTime]::UtcNow -ge ([DateTime]$state.DeadlineUtc).ToUniversalTime() -or (Test-Path $request)) {
            Write-AirPlayNetworkLog 'WATCHDOG: test owner exited, recovery requested, or 2-hour safety limit reached; restoring only original WSL adapter.'
            Restore-AirPlayNetworkState -ExpectedSessionId $SessionId
            try { Close-OwnedNetworkTestApp $state.AppMetadata } catch { Write-AirPlayNetworkLog ('WATCHDOG APP CLEANUP: '+$_.Exception.Message) }
            [pscustomobject]@{Timestamp=(Get-Date -Format o);Status='RESTORED: administrative guardian restored original WSL state; Wi-Fi-only test ended.';
                WifiIPv4=$state.WifiIPv4;BonjourIPv4=@();Phase2='PENDING PHYSICAL IPHONE VALIDATION'} | ConvertTo-Json | Set-Content $NetworkResultPath -Encoding UTF8
            exit 0
        }
        Start-Sleep -Seconds 1
    }
} catch { Write-AirPlayNetworkLog ('WATCHDOG RESTORE ERROR: '+$_.Exception.Message+'. Saved recovery state retained; use restore-airplay-network.cmd.'); exit 1 }
