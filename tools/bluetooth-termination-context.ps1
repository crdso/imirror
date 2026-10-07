# Context for sanitized HCI lines only. Link aliases are local to this trace;
# correlation of an OS-wide link to the iMirror GATT host is NOT established.
function Get-BluetoothTerminationContext {
    param([string]$Line, [hashtable]$Connections, [string]$SnapshotPath)
    $stamp = [regex]::Match($Line, 'controller=(\S+)')
    if (-not $stamp.Success) { return }
    $instant = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($stamp.Groups[1].Value, [ref]$instant)) { return }
    $connected = [regex]::Match($Line, '\bLE ConnectionComplete (L\d+) status=0x00\b')
    if ($connected.Success) { $Connections[$connected.Groups[1].Value] = $instant; return }
    $classic = [regex]::Match($Line, '\bClassic ConnectionComplete (L\d+) status=0x00\b')
    if ($classic.Success) { $Connections.Remove($classic.Groups[1].Value); return }
    $dropped = [regex]::Match($Line, '\bDisconnectionComplete (L\d+) status=0x00 reason=0x([0-9A-Fa-f]{2})\b')
    if (-not $dropped.Success) { return }
    $alias = $dropped.Groups[1].Value; $elapsed = 'unknown'
    if ($Connections.ContainsKey($alias)) {
        $seconds = ($instant - $Connections[$alias]).TotalSeconds
        if ($seconds -ge 0) { $elapsed = $seconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture) + 's' }
        $Connections.Remove($alias)
    }
    if ($dropped.Groups[2].Value -ne '13') { return }
    $names = @('GattSession','HidInformation','ReportMap','ProtocolMode','KeyboardCCCD','MouseCCCD','GattSessionActive','KeyboardLive','MouseLive')
    $values = @{}; foreach ($name in $names) { $values[$name] = 'unknown' }
    $freshness = 'unavailable'; $generation = 'unknown'
    try {
        if (Test-Path -LiteralPath $SnapshotPath) {
            $snapshot = [IO.File]::ReadAllText($SnapshotPath) | ConvertFrom-Json
            $at = if ($snapshot.AtUtc -is [DateTime]) { [DateTimeOffset]::new($snapshot.AtUtc) } else { [DateTimeOffset]::Parse($snapshot.AtUtc) }
            $age = ($instant - $at).TotalSeconds
            $freshness = 'stale'
            if ($age -ge -2 -and $age -le 15) {
                $freshness = 'recent'
                if (($snapshot.ProviderGeneration -is [int] -or $snapshot.ProviderGeneration -is [long]) -and $snapshot.ProviderGeneration -ge 0 -and $snapshot.ProviderGeneration -le [int]::MaxValue) { $generation = $snapshot.ProviderGeneration }
                foreach ($name in $names) { if ($snapshot.$name -is [bool]) { $values[$name] = $snapshot.$name.ToString().ToLowerInvariant() } }
            }
        }
    } catch { $freshness = 'unavailable' }
    $stages = ($names | ForEach-Object { $_ + '=' + $values[$_] }) -join '; '
    '{0} REMOTE_TERMINATION reason=0x13; link={1}; elapsedSinceLE={2}; GATT-link-correlation=unproven; stageSnapshot={3}; generation={4}; observedSinceProviderStart: {5}' -f $instant.ToString('o'), $alias, $elapsed, $freshness, $generation, $stages
}
