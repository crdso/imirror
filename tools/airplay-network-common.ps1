. "$PSScriptRoot\airplay-setup-common.ps1"
$NetworkTestLog=Join-Path $AirPlayRoot 'logs\airplay-wifi-only-test.log'
$NetworkStatePath=Join-Path $AirPlayRoot '.cache\readiness\airplay-network-state.json'
$NetworkResultPath=Join-Path $AirPlayRoot 'logs\airplay-wifi-only-result.json'
function Write-AirPlayNetworkLog([string]$Message) {
    $line="$(Get-Date -Format o) $Message"
    for($logAttempt=0;$logAttempt -lt 5;$logAttempt++) {
        try { Add-Content -LiteralPath $NetworkTestLog -Encoding UTF8 -Value $line; break }
        catch [IO.IOException] { if($logAttempt -eq 4){throw}; Start-Sleep -Milliseconds 100 }
    }
    Write-Host $Message
}
function Test-NetworkProcessIdentity($ProcessId,$StartTimeUtc) {
    try {
        $tracked=Get-Process -Id $ProcessId -ErrorAction Stop
        return ($tracked.StartTime.ToUniversalTime().Ticks -eq ([DateTime]$StartTimeUtc).ToUniversalTime().Ticks)
    } catch { return $false }
}
function Save-AirPlayNetworkState($State) {
    $stateTemporary=$NetworkStatePath+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    [IO.File]::WriteAllText($stateTemporary,($State | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    if (Test-Path $NetworkStatePath) { [IO.File]::Replace($stateTemporary,$NetworkStatePath,$NetworkStatePath+'.previous') }
    else { [IO.File]::Move($stateTemporary,$NetworkStatePath) }
}
function Get-AirPlayNetworkState {
    if (Test-Path $NetworkStatePath) { return (Get-Content $NetworkStatePath -Raw -Encoding UTF8 | ConvertFrom-Json) }
    return $null
}
function Get-WifiOnlyAdapter {
    $adapters=@(Get-NetAdapter -IncludeHidden -ErrorAction Stop | Where-Object {
        $_.HardwareInterface -and [string]$_.Status -eq 'Up' -and
        ([string]$_.PhysicalMediaType -eq 'Native 802.11' -or [string]$_.MediaType -eq 'Native 802.11')
    })
    if ($adapters.Count -ne 1) { throw 'Expected exactly one active physical Wi-Fi adapter; no network change attempted.' }
    $addresses=@(Get-NetIPAddress -InterfaceIndex $adapters[0].InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop |
        Where-Object { [string]$_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' })
    if ($addresses.Count -ne 1) { throw 'Expected exactly one usable Wi-Fi IPv4 address.' }
    return [pscustomobject]@{Adapter=$adapters[0];IPv4=[string]$addresses[0].IPAddress}
}
function Get-ExactWslAdapter([string]$Guid) {
    $adapters=@(Get-NetAdapter -IncludeHidden -ErrorAction Stop | Where-Object {
        $_.Name -eq 'vEthernet (WSL)' -and (-not $Guid -or [string]$_.InterfaceGuid -eq $Guid)
    })
    if ($adapters.Count -ne 1 -or $adapters[0].HardwareInterface -or -not $adapters[0].Virtual -or
        $adapters[0].InterfaceDescription -notmatch '^Hyper-V Virtual Ethernet Adapter') {
        throw 'Original vEthernet (WSL) adapter cannot be identified safely; no other adapter will be changed.'
    }
    return $adapters[0]
}
function Test-AdapterAdministrativelyEnabled($Adapter) {
    return ([string]$Adapter.AdminStatus -in @('Up','1'))
}
function Get-AirPlayNetworkSnapshot {
    $adapters=@(Get-NetAdapter -IncludeHidden -ErrorAction Stop | ForEach-Object {
        $kind=if($_.HardwareInterface){'Physical'}elseif($_.Name -eq 'vEthernet (WSL)'){'WSL'}elseif($_.InterfaceDescription -match 'Docker'){'Docker'}elseif($_.InterfaceDescription -match 'Hyper-V'){'Hyper-V'}elseif(($_.Name+' '+$_.InterfaceDescription) -match '(VPN|WireGuard|TAP-|Tailscale|ZeroTier|OpenVPN)'){'VPN candidate'}else{'Other virtual/system'}
        [pscustomobject]@{Name=$_.Name;Index=$_.InterfaceIndex;Status=[string]$_.Status;AdminStatus=[string]$_.AdminStatus;Kind=$kind}
    })
    $config=@(Get-NetIPConfiguration -ErrorAction Stop | Select-Object InterfaceAlias,InterfaceIndex,@{Name='IPv4';Expression={@($_.IPv4Address.IPAddress)}})
    $interfaces=@(Get-NetIPInterface -AddressFamily IPv4 -ErrorAction Stop | Select-Object InterfaceAlias,InterfaceIndex,@{Name='State';Expression={[string]$_.ConnectionState}},InterfaceMetric,@{Name='Dhcp';Expression={[string]$_.Dhcp}})
    return [pscustomobject]@{Adapters=$adapters;IPConfiguration=$config;IPInterfaces=$interfaces}
}
function Assert-WifiUntouched($State) {
    $wifi=Get-WifiOnlyAdapter
    if ([string]$wifi.Adapter.InterfaceGuid -ne $State.WifiGuid -or $wifi.IPv4 -ne $State.WifiIPv4) {
        throw 'Wi-Fi changed during test; ending the test and restoring only the saved WSL adapter.'
    }
}
function Enter-NetworkActionLock {
    $deadline=[DateTime]::UtcNow.AddSeconds(10)
    do {
        try { return [IO.File]::Open((Join-Path $AirPlayRoot '.cache\readiness\network-action.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
        catch [IO.IOException] { Start-Sleep -Milliseconds 100 }
    } while([DateTime]::UtcNow -lt $deadline)
    throw 'Network action already in progress; no adapter change attempted.'
}
function Restore-AirPlayNetworkState([string]$ExpectedSessionId) {
    if (-not (Test-AirPlayAdministrator)) { throw 'Administrator required to restore the saved adapter state.' }
    $actionLock=Enter-NetworkActionLock
    try {
        $state=Get-AirPlayNetworkState
        if (-not $state) { Write-AirPlayNetworkLog 'RESTORE: no state was saved; no network change.'; return }
        if ($ExpectedSessionId -and $state.SessionId -ne $ExpectedSessionId) { throw 'Recovery session changed; no adapter mutation.' }
        if ($state.Schema -ne 1 -or $state.AdapterName -ne 'vEthernet (WSL)' -or -not $state.AdapterGuid) { throw 'Invalid saved state; no network change.' }
        if ($state.Restored) { Write-AirPlayNetworkLog 'RESTORE: original state already restored; no network change.'; return }
        $adapter=Get-ExactWslAdapter $state.AdapterGuid
        if ($state.WasEnabled -and $state.DisableIntent -and -not (Test-AdapterAdministrativelyEnabled $adapter)) {
            Enable-NetAdapter -InputObject $adapter -Confirm:$false -ErrorAction Stop
            $deadline=[DateTime]::UtcNow.AddSeconds(20)
            do { Start-Sleep -Milliseconds 250; $adapter=Get-ExactWslAdapter $state.AdapterGuid } while(-not(Test-AdapterAdministrativelyEnabled $adapter) -and [DateTime]::UtcNow -lt $deadline)
            if (-not(Test-AdapterAdministrativelyEnabled $adapter)) { throw 'Original WSL adapter did not re-enable; saved recovery state retained.' }
            Write-AirPlayNetworkLog 'RESTORE: original vEthernet (WSL) re-enabled. Wi-Fi/Ethernet/firewall untouched.'
        } elseif (-not $state.WasEnabled) { Write-AirPlayNetworkLog 'RESTORE: WSL was disabled before test; it was not enabled.' }
        else { Write-AirPlayNetworkLog 'RESTORE: WSL already has original administrative state; no mutation.' }
        $state.Restored=$true; $state.RestoredAt=(Get-Date -Format o); $state.Status='RESTORED'
        Save-AirPlayNetworkState $state
    } finally { $actionLock.Dispose() }
}
function Disable-SavedWslAdapter($State) {
    # Caller holds the action lock. Recovery intent must reach disk before the OS change.
    if (-not(Test-AirPlayAdministrator)) { throw 'Administrator required to disable WSL.' }
    Assert-WifiUntouched $State
    $adapter=Get-ExactWslAdapter $State.AdapterGuid
    if (-not $State.WasEnabled) { Write-AirPlayNetworkLog 'WSL was already disabled; its original state will be retained.'; return }
    $State.DisableIntent=$true; Save-AirPlayNetworkState $State
    Disable-NetAdapter -InputObject $adapter -Confirm:$false -ErrorAction Stop
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    do { Start-Sleep -Milliseconds 250; $adapter=Get-ExactWslAdapter $State.AdapterGuid } while((Test-AdapterAdministrativelyEnabled $adapter) -and [DateTime]::UtcNow -lt $deadline)
    if (Test-AdapterAdministrativelyEnabled $adapter) { throw 'WSL adapter did not disable.' }
    Write-AirPlayNetworkLog 'Temporarily disabled ONLY original vEthernet (WSL); no Wi-Fi/Ethernet/firewall mutation.'
}
function Convert-DnsAddressRows([string]$Output,[string]$Hostname) {
    $records=@{}
    foreach($line in ($Output -split "`n")) {
        $match=[regex]::Match($line,'\b(Add|Rmv)\s+\S+\s+(\d+)\s+(\S+)\s+(\d{1,3}(?:\.\d{1,3}){3})\s+\d+\s*$')
        if (-not $match.Success -or $match.Groups[3].Value.TrimEnd('.') -ine $Hostname.TrimEnd('.')) { continue }
        $key=$match.Groups[2].Value+'|'+$match.Groups[4].Value
        if ($match.Groups[1].Value -eq 'Rmv') { $records.Remove($key) }
        else { $records[$key]=[pscustomobject]@{Interface=[int]$match.Groups[2].Value;Address=$match.Groups[4].Value} }
    }
    return @($records.Values)
}
function Invoke-AirPlayDnsSample {
    param([string[]]$Arguments,[int]$Seconds=4)
    $paths=Get-AirPlayPaths
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $paths.GstBin 'dns-sd.exe'))
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.Encoding]::UTF8; $start.StandardErrorEncoding=[Text.Encoding]::UTF8
    if ($start.PSObject.Properties['ArgumentList']) { foreach($argument in $Arguments){$start.ArgumentList.Add($argument)} }
    else { $start.Arguments=($Arguments | ForEach-Object {'"'+$_+'"'}) -join ' ' }
    $start.EnvironmentVariables['PATH']=$paths.GstBin+';'+$env:PATH
    $process=[Diagnostics.Process]::Start($start)
    try {
        $output=$process.StandardOutput.ReadToEndAsync(); $errorOutput=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($Seconds*1000)) { $process.Kill(); $process.WaitForExit() }
        return [pscustomobject]@{Output=$output.GetAwaiter().GetResult();Error=$errorOutput.GetAwaiter().GetResult()}
    } finally { $process.Dispose() }
}
function Get-AirPlayDnsEvidence([string]$Hostname,[string]$Stage,[switch]$AddressOnly) {
    $result=[ordered]@{Addresses=@();Browse='';Resolve='';QueryError=$false}
    $arguments=[Collections.Generic.List[string[]]]::new()
    if (-not $AddressOnly) {
        $arguments.Add(@('-B','_airplay._tcp','local.'))
        $arguments.Add(@('-L','iMirror - Windows','_airplay._tcp','local.'))
    }
    $arguments.Add(@('-G','v4',$Hostname))
    foreach($dnsArguments in $arguments) {
        $sample=Invoke-AirPlayDnsSample -Arguments $dnsArguments
        Write-AirPlayNetworkLog "$Stage dns-sd $($dnsArguments -join ' ')"
        $safeLines=@($sample.Output -split "`n" | Where-Object {
            ($_ -match '(\bAdd\b|\bRmv\b|can be reached at)' -and ($_ -match [regex]::Escape($Hostname.TrimEnd('.')) -or $_.TrimEnd().EndsWith('iMirror - Windows'))) -and
            $_ -notmatch '(deviceid=|\bpk=|\bpi=|features=)'
        })
        foreach($safeLine in $safeLines) { Write-AirPlayNetworkLog $safeLine.Trim() }
        if ($sample.Error) { $result.QueryError=$true; Write-AirPlayNetworkLog 'dns-sd stderr received; operation cannot be approved solely from this sample.' }
        if ($dnsArguments[0] -eq '-G') { $result.Addresses=@(Convert-DnsAddressRows $sample.Output $Hostname) }
        if ($dnsArguments[0] -eq '-B') { $result.Browse=$safeLines -join "`n" }
        if ($dnsArguments[0] -eq '-L') { $result.Resolve=$safeLines -join "`n" }
    }
    return [pscustomobject]$result
}
function Test-WifiOnlyDns($Evidence,$State) {
    return (-not $Evidence.QueryError -and $Evidence.Addresses.Count -gt 0 -and @($Evidence.Addresses | Where-Object {$_.Address -ne $State.WifiIPv4 -or $_.Interface -ne $State.WifiIndex}).Count -eq 0)
}
function Test-WifiOnlyService($Evidence,$State,[string]$Hostname) {
    $resolutions=@([regex]::Matches($Evidence.Resolve,'can be reached at (\S+):(\d+) \(interface (\d+)\)'))
    $resolved=($resolutions.Count -gt 0 -and @($resolutions | Where-Object {
        $_.Groups[1].Value.TrimEnd('.') -ine $Hostname.TrimEnd('.') -or [int]$_.Groups[2].Value -notin @(35000,35001,35002) -or [int]$_.Groups[3].Value -ne $State.WifiIndex
    }).Count -eq 0)
    $records=@{}
    foreach($line in ($Evidence.Browse -split "`n")) {
        $match=[regex]::Match($line,'\b(Add|Rmv)\s+\S+\s+(\d+)\s+local\.\s+_airplay\._tcp\.\s+iMirror - Windows\s*$')
        if (-not $match.Success) { continue }
        $key=$match.Groups[2].Value
        if ($match.Groups[1].Value -eq 'Rmv') { $records.Remove($key) } else { $records[$key]=$true }
    }
    $browsed=($records.Count -gt 0 -and @($records.Keys | Where-Object {[int]$_ -ne $State.WifiIndex}).Count -eq 0)
    return ((Test-WifiOnlyDns $Evidence $State) -and $resolved -and $browsed)
}
function Close-OwnedNetworkTestApp($Metadata) {
    if (-not $Metadata) { return }
    $owned=Get-Process -Id $Metadata.Id -ErrorAction SilentlyContinue
    if (Test-AirPlayOwnedLauncher $owned $Metadata) {
        if (-not $owned.CloseMainWindow() -or -not $owned.WaitForExit(20000)) { throw 'Owned iMirror did not close gracefully; no global process kill attempted.' }
    }
}
