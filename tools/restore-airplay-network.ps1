param([switch]$Elevate)
. "$PSScriptRoot\airplay-network-common.ps1"
try {
    $state=Get-AirPlayNetworkState
    if (-not $state -or $state.Restored) { Write-AirPlayNetworkLog 'RESTORE: nothing pending; Wi-Fi and all adapters unchanged.'; exit 0 }
    if (-not(Test-AirPlayAdministrator)) {
        if (Test-NetworkProcessIdentity $state.OwnerId $state.OwnerStartUtc) {
            [IO.File]::WriteAllText((Join-Path $AirPlayRoot ('.cache\readiness\restore-network-'+$state.SessionId+'.request')),'restore',[Text.UTF8Encoding]::new($false))
            Write-AirPlayNetworkLog 'RESTORE: request sent to the existing administrative test process; no second elevation needed.'
            $deadline=[DateTime]::UtcNow.AddSeconds(25)
            do { Start-Sleep -Milliseconds 500; $state=Get-AirPlayNetworkState } while(-not $state.Restored -and [DateTime]::UtcNow -lt $deadline)
            if ($state.Restored) { Write-AirPlayNetworkLog 'RESTORE: completed by the test process.'; exit 0 }
        }
        if ($Elevate) {
            $runner=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            Start-Process $runner -Verb RunAs -WindowStyle Normal -ArgumentList ('-NoProfile -NoExit -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"') | Out-Null
            Write-AirPlayNetworkLog 'RESTORE: one administrative elevation requested; no retry.'; exit 0
        }
        throw 'Restore requires administrator because no live administrative guardian completed it. Use restore-airplay-network.cmd once.'
    }
    Restore-AirPlayNetworkState
    Write-AirPlayNetworkLog 'NETWORK RESTORED'
    exit 0
} catch { Write-AirPlayNetworkLog ('RESTORE BLOCKED: '+$_.Exception.Message); exit 1 }
