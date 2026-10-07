# Isolated fixtures only: no real adapter, service, firewall or UAC calls.
. "$PSScriptRoot\airplay-network-common.ps1"
$fixture=Join-Path $AirPlayRoot ('.cache\validation\network-safety-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $fixture | Out-Null
$NetworkStatePath=Join-Path $fixture 'state.json'
$NetworkTestLog=Join-Path $fixture 'test.log'
$script:enabled=$true; $script:changes=[Collections.Generic.List[string]]::new()
$script:wifiGuid='wifi-original'; $script:failDisable=$false
function Test-AirPlayAdministrator { return $true }
function Get-NetAdapter {
    [CmdletBinding()]param([switch]$IncludeHidden)
    [pscustomobject]@{Name='Wi-Fi';InterfaceGuid=$script:wifiGuid;InterfaceIndex=16;HardwareInterface=$true;Virtual=$false;Status='Up';PhysicalMediaType='Native 802.11';AdminStatus='Up'}
    [pscustomobject]@{Name='Ethernet';InterfaceGuid='ethernet';InterfaceIndex=6;HardwareInterface=$true;Virtual=$false;Status='Disconnected';PhysicalMediaType='802.3';AdminStatus='Up'}
    [pscustomobject]@{Name='vSwitch (WSL)';InterfaceGuid='switch';InterfaceIndex=42;HardwareInterface=$false;Virtual=$true;InterfaceDescription='Hyper-V Virtual Switch Extension Adapter';Status='Up';AdminStatus='Up'}
    [pscustomobject]@{Name='vEthernet (WSL)';InterfaceGuid='wsl-original';InterfaceIndex=44;HardwareInterface=$false;Virtual=$true;InterfaceDescription='Hyper-V Virtual Ethernet Adapter';Status=$(if($script:enabled){'Up'}else{'Disabled'});AdminStatus=$(if($script:enabled){'Up'}else{'Down'})}
}
function Get-NetIPAddress {
    [CmdletBinding()]param($InterfaceIndex,$AddressFamily)
    if ($InterfaceIndex -ne 16 -or $AddressFamily -ne 'IPv4') { throw 'Fixture saw unexpected IP inspection.' }
    [pscustomobject]@{IPAddress='192.0.2.24';AddressState='Preferred'}
}
function Disable-NetAdapter {
    [CmdletBinding()]param($InputObject,[switch]$Confirm)
    if ($InputObject.InterfaceGuid -ne 'wsl-original' -or -not (Get-AirPlayNetworkState).DisableIntent) { throw 'Unsafe target or intent was not persisted BEFORE mutation.' }
    $script:changes.Add('disable:wsl-original'); $script:enabled=$false
    if ($script:failDisable) { throw 'Simulated failure after OS accepted disable.' }
}
function Enable-NetAdapter {
    [CmdletBinding()]param($InputObject,[switch]$Confirm)
    if ($InputObject.InterfaceGuid -ne 'wsl-original') { throw 'Unsafe restore target.' }
    $script:changes.Add('enable:wsl-original'); $script:enabled=$true
}
function Enter-NetworkActionLock { return [IO.MemoryStream]::new() }
function Assert-Test([bool]$Condition,[string]$Message) { if(-not $Condition){throw $Message} }
function New-FixtureState([bool]$WasEnabled=$true) {
    $script:enabled=$WasEnabled; $script:changes.Clear(); $script:wifiGuid='wifi-original'; $script:failDisable=$false
    $state=[pscustomobject]@{Schema=1;SessionId=[Guid]::NewGuid().ToString('N');AdapterName='vEthernet (WSL)';AdapterGuid='wsl-original';WasEnabled=$WasEnabled;DisableIntent=$false;Restored=$false;RestoredAt=$null;Status='PREPARING';WifiGuid='wifi-original';WifiIndex=16;WifiIPv4='192.0.2.24';Label='Conexão'}
    Save-AirPlayNetworkState $state
    return $state
}
try {
    $state=New-FixtureState
    Disable-SavedWslAdapter $state
    Assert-Test (-not $script:enabled) 'Disable did not occur.'
    Assert-Test ((Get-AirPlayNetworkState).Label -eq 'Conexão') 'State UTF8 did not round trip.'
    Restore-AirPlayNetworkState -ExpectedSessionId $state.SessionId
    Restore-AirPlayNetworkState -ExpectedSessionId $state.SessionId
    Assert-Test ($script:enabled -and ($script:changes -join ',') -eq 'disable:wsl-original,enable:wsl-original') 'Restore was not scoped/idempotent.'
    Write-Host 'PASS: active WSL restored exactly once; physical adapters and vSwitch preserved; intent saved before disable.'

    $state=New-FixtureState $false
    Disable-SavedWslAdapter $state; Restore-AirPlayNetworkState
    Assert-Test (-not $script:enabled -and $script:changes.Count -eq 0) 'Previously disabled WSL was enabled.'
    Write-Host 'PASS: previously disabled WSL remains disabled; no mutation.'

    $state=New-FixtureState; $script:failDisable=$true; $caught=$false
    try { Disable-SavedWslAdapter $state } catch {$caught=$true}
    finally { Restore-AirPlayNetworkState -ExpectedSessionId $state.SessionId }
    Assert-Test ($caught -and $script:enabled -and (Get-AirPlayNetworkState).Restored) 'Interrupted disable was not recovered.'
    Write-Host 'PASS: failure after disable retains recovery intent and restores through finally.'

    $state=New-FixtureState; $state.AdapterGuid='different-recreated-adapter'; Save-AirPlayNetworkState $state; $caught=$false
    try { Disable-SavedWslAdapter $state } catch {$caught=$true}
    Assert-Test ($caught -and $script:changes.Count -eq 0) 'Recreated adapter was modified.'
    $state=New-FixtureState; $script:wifiGuid='different-wifi'; $caught=$false
    try { Disable-SavedWslAdapter $state } catch {$caught=$true}
    Assert-Test ($caught -and $script:changes.Count -eq 0) 'Changed Wi-Fi allowed mutation.'
    $script:wifiGuid='wifi-original'; $caught=$false
    try { Restore-AirPlayNetworkState -ExpectedSessionId 'stale-session' } catch {$caught=$true}
    Assert-Test ($caught -and $script:changes.Count -eq 0) 'Stale watchdog modified a new session.'
    Write-Host 'PASS: changed adapter GUID/Wi-Fi/stale guardian blocked before mutation.'

    $state=New-FixtureState
    $hostName='fixture-pc.local.'
    $wifiRow='00:25:36.218 Add 2 16 fixture-pc.local. 192.0.2.24 120'
    $virtualRow='00:25:36.218 Add 2 44 fixture-pc.local. 198.51.100.1 120'
    $removed='00:25:37.218 Rmv 2 44 fixture-pc.local. 198.51.100.1 120'
    $evidence=[pscustomobject]@{Addresses=@(Convert-DnsAddressRows ($wifiRow+"`n"+$virtualRow) $hostName);QueryError=$false;
        Browse='00:25:36.218 Add 2 16 local. _airplay._tcp. iMirror - Windows';Resolve='iMirror - Windows can be reached at fixture-pc.local.:35000 (interface 16)'}
    Assert-Test (-not(Test-WifiOnlyService $evidence $state $hostName)) 'Dual addresses incorrectly approved.'
    $evidence.Addresses=@(Convert-DnsAddressRows ($wifiRow+"`n"+$virtualRow+"`n"+$removed) $hostName)
    Assert-Test (Test-WifiOnlyService $evidence $state $hostName) 'Removed stale address still blocked readiness.'
    $evidence.Browse+="`n00:25:36.218 Add 2 44 local. _airplay._tcp. iMirror - Windows"
    Assert-Test (-not(Test-WifiOnlyService $evidence $state $hostName)) 'Dual service interfaces incorrectly approved.'
    $evidence.Browse+="`n00:25:37.218 Rmv 2 44 local. _airplay._tcp. iMirror - Windows"
    Assert-Test (Test-WifiOnlyService $evidence $state $hostName) 'Service removal not applied.'
    $evidence.Resolve+="`niMirror - Windows can be reached at fixture-pc.local.:35000 (interface 44)"
    Assert-Test (-not(Test-WifiOnlyService $evidence $state $hostName)) 'Dual resolved interface incorrectly approved.'
    $evidence.Resolve='iMirror - Windows can be reached at fixture-pc.local.:35001 (interface 16) Flags: 1'
    $evidence.QueryError=$true
    Assert-Test (-not(Test-WifiOnlyService $evidence $state $hostName)) 'DNS-SD error incorrectly approved.'
    Write-Host 'PASS: DNS Add/Rmv, dual interfaces, exact host/port and query errors.'

    $script:queries=[Collections.Generic.List[string]]::new()
    function Invoke-AirPlayDnsSample { param([string[]]$Arguments,[int]$Seconds=4)
        $script:queries.Add(($Arguments -join ' ')); return [pscustomobject]@{Output=$wifiRow;Error=''}
    }
    $null=Get-AirPlayDnsEvidence $hostName 'FIXTURE' -AddressOnly
    Assert-Test ($script:queries.Count -eq 1 -and $script:queries[0] -eq '-G v4 fixture-pc.local.') 'Address-only query split its argument array.'
    $script:queries.Clear(); $null=Get-AirPlayDnsEvidence $hostName 'FIXTURE'
    Assert-Test ($script:queries.Count -eq 3 -and $script:queries[0] -eq '-B _airplay._tcp local.' -and $script:queries[1] -eq '-L iMirror - Windows _airplay._tcp local.') 'Required DNS queries missing.'
    Write-Host 'PASS: required DNS-SD argument boundaries preserved in Windows PowerShell.'
    Write-Host 'PASS: 6 network safety groups (fixtures only; no physical network validation).'
} catch { Write-Error $_; exit 1 }
