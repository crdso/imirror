$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/bluetooth-termination-context.ps1"
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root ('.cache/validation/termination-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
$snapshotPath = Join-Path $directory 'snapshot.json'
function Assert-Context($Condition, $Message) { if (-not $Condition) { throw $Message } }
try {
    $start = [DateTimeOffset]::Parse('2026-10-07T15:00:00Z'); $drop = $start.AddSeconds(12)
    $connections = @{}
    $snapshot = @{ AtUtc=$drop.ToString('o'); ProviderGeneration=1; GattSession=$true; HidInformation=$true; ReportMap=$false; ProtocolMode=$false; KeyboardCCCD=$false; MouseCCCD=$false; GattSessionActive=$false; KeyboardLive=$false; MouseLive=$false; IgnoredSecret='sensitive-fixture' }
    $snapshot | ConvertTo-Json | Set-Content -LiteralPath $snapshotPath -Encoding UTF8
    $null = Get-BluetoothTerminationContext "controller=$($start.ToString('o')) BIP kind=2 LE ConnectionComplete L1 status=0x00 role=Peripheral" $connections $snapshotPath
    $line = "controller=$($drop.ToString('o')) BIP kind=2 DisconnectionComplete L1 status=0x00 reason=0x13 (Remote User Terminated Connection)"
    $result = Get-BluetoothTerminationContext $line $connections $snapshotPath
    Assert-Context ($result -match 'REMOTE_TERMINATION.*elapsedSinceLE=12.000s' -and $result -match 'GattSession=true' -and $result -match 'ReportMap=false' -and $result -notmatch 'sensitive-fixture' -and $connections.Count -eq 0) 'Known LE elapsed/stages/privacy failed'
    $result = Get-BluetoothTerminationContext $line $connections $snapshotPath
    Assert-Context ($result -match 'elapsedSinceLE=unknown' -and $result -match 'correlation=unproven') 'Unknown LE link misattributed'
    $snapshot.AtUtc = $start.AddMinutes(-1).ToString('o'); $snapshot | ConvertTo-Json | Set-Content -LiteralPath $snapshotPath -Encoding UTF8
    $result = Get-BluetoothTerminationContext $line $connections $snapshotPath
    Assert-Context ($result -match 'stageSnapshot=stale' -and $result -match 'GattSession=unknown') 'Stale snapshot treated as current'
    $null = Get-BluetoothTerminationContext "controller=$($start.ToString('o')) LE ConnectionComplete L1 status=0x00 role=Peripheral" $connections $snapshotPath
    $result = Get-BluetoothTerminationContext ($line.Replace('reason=0x13','reason=0x08')) $connections $snapshotPath
    Assert-Context (-not $result -and $connections.Count -eq 0) 'Other native reason mislabeled'
    Write-Host 'PASS: 4 REMOTE_TERMINATION context groups; known/unknown elapsed, stale snapshot, privacy and native reason preserved; offline only.'
} finally {
    $resolved = (Resolve-Path -LiteralPath $directory).Path
    $allowed = [IO.Path]::GetFullPath((Join-Path $root '.cache/validation')) + '\'
    if (-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture outside workspace' }
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Fixture reparse point' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
