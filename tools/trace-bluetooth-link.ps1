param([ValidateRange(1,180)][int]$Seconds = 90, [switch]$SelfTest,
    [ValidatePattern('^iMirror\.Bluetooth\.Status\.[0-9a-f]{32}$')][string]$StopSession,
    [switch]$ContinueAfterStop)
$ErrorActionPreference = 'Stop'
if (-not ('iMirror.Diagnostics.BluetoothLinkTrace' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'BluetoothLinkTrace.cs') }
if ($SelfTest) {
    $trace = New-Object iMirror.Diagnostics.BluetoothLinkTrace
    $tests = @(
        @{ Data = [byte[]](0x05,4,0,1,0,0x13); Expected = 'DisconnectionComplete L1 status=0x00 reason=0x13 (Remote User Terminated Connection)' },
        @{ Data = [byte[]](0x08,4,0,1,0,1); Expected = 'EncryptionChange L1 status=0x00 enabled=1' },
        @{ Data = [byte[]](0x06,3,5,1,0); Expected = 'AuthenticationComplete L1 status=0x05' },
        @{ Data = [byte[]](0x30,3,6,1,0); Expected = 'EncryptionKeyRefresh L1 status=0x06' },
        @{ Data = [byte[]](0x36,7,0); Expected = 'SimplePairingComplete status=0x00' },
        @{ Data = [byte[]](0x3E,19,1,0,2,0,1,0,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF,24,0,0,0,200,0,0); Expected = 'LE ConnectionComplete L2 status=0x00 role=Peripheral interval=30ms latency=0 supervision=2000ms' },
        @{ Data = [byte[]](0x03,11,0,3,0,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF,1,1); Expected = 'Classic ConnectionComplete L3 status=0x00 linkType=1 encryption=1' },
        @{ Data = [byte[]](0x3E,31,0x0A,0,4,0,1,0,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF,1,2,3,4,5,6,7,8,9,10,11,12,24,0,0,0,200,0,0); Expected = 'LE ConnectionComplete L4 status=0x00 role=Peripheral interval=30ms latency=0 supervision=2000ms' },
        @{ Data = [byte[]](0x05,4,0,4,0,0x08); Expected = 'DisconnectionComplete L4 status=0x00 reason=0x08 (Connection Timeout)' },
        @{ Data = [byte[]](0x18,23,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF); Expected = $null }, # link key notification rejected
        @{ Data = [byte[]](0x3E,1,0x0A); Expected = $null },
        @{ Data = [byte[]](0x05,4,0); Expected = $null }
    )
    foreach ($test in $tests) { $actual = $trace.Decode($test.Data); if ($actual -ne $test.Expected) { throw ('Unexpected sanitized decode: ' + $actual) } }
    $envelopes = @(
        @{ Kind=2; Packet=[byte[]](0x05,4,0,1,0,0x13); Expected='BIP kind=2 DisconnectionComplete L1 status=0x00 reason=0x13 (Remote User Terminated Connection)' },
        @{ Kind=4; Packet=[byte[]](0x05,4,0,1,0,0x13); Expected=$null }, # H4 event constant is NOT this provider's BIP event kind
        @{ Kind=2; Packet=[byte[]](0x0E,4,1,0x0A,0x20,0); Expected='BIP kind=2 CommandComplete opcode=0x200A status=0x00' },
        @{ Kind=2; Packet=[byte[]](0x0F,4,0x0C,1,6,4); Expected='BIP kind=2 CommandStatus opcode=0x0406 status=0x0C' },
        @{ Kind=1; Packet=[byte[]](0x05,4,0,1,0,0x13); Expected=$null }, # command kind must never be interpreted as event
        @{ Kind=3; Packet=[byte[]](0x05,4,0,1,0,0x13); Expected=$null }, # event-shaped data payload rejected
        @{ Kind=7; Packet=[byte[]](0x05,4,0,1,0,0x13); Expected=$null },
        @{ Kind=2; Packet=[byte[]](0x18,8,1,2,3,4,5,6,7,8); Expected=$null }, # key material is never formatted
        @{ Kind=2; Packet=[byte[]](0x0E,4,1,0xFF,0xFF,0xAA); Expected=$null }, # unknown return bytes are not treated as status
        @{ Kind=2; Packet=[byte[]](0x05,4,0); Expected=$null },
        @{ Kind=2; Packet=[byte[]](0x05,4,0,1,0,0x13); DeclaredLength=7; Expected=$null },
        @{ Kind=2; Packet=[byte[]](0x05,4,0,1,0,0x13); Size=7; Expected=$null }
    )
    $envelopes += @(
        @{ Kind=1; Packet=[byte[]](6,0x20,15,160,0,160,0,0,0,0,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF,7,0); Expected='LE AdvertisingParameters type=0x00 connectable=True ownAddressType=0 channels=0x07 filterPolicy=0x00 intervalMinUnits=160 intervalMaxUnits=160' },
        @{ Kind=1; Packet=[byte[]](6,0x20,15,160,0,160,0,3,0,0,0xAA,0xBB,0xCC,0xDD,0xEE,0xFF,7,0); Expected='LE AdvertisingParameters type=0x03 connectable=False ownAddressType=0 channels=0x07 filterPolicy=0x00 intervalMinUnits=160 intervalMaxUnits=160' },
        @{ Kind=1; Packet=[byte[]](0x0A,0x20,1,1); Expected='LE AdvertisingEnable enabled=1' },
        @{ Kind=1; Packet=[byte[]](0x0A,0x20,1,0); Expected='LE AdvertisingEnable enabled=0' },
        @{ Kind=1; Packet=[byte[]](@(8,0x20,32,12,2,1,6,3,3,0x12,0x18,4,9,65,66,67)+(@(0)*19)); Expected='LE AdvertisingData HID1812=True BAS180F=False flags=0x06' }, # local name ABC is skipped
        @{ Kind=1; Packet=[byte[]](@(9,0x20,32,4,3,3,0x0F,0x18)+(@(0)*27)); Expected='LE ScanResponseData HID1812=False BAS180F=True flags=absent' },
        @{ Kind=1; Packet=[byte[]](@(8,0x20,32,4,5,3,0x12,0x18)+(@(0)*27)); Expected=$null }, # truncated AD structure
        @{ Kind=1; Packet=[byte[]](0xAA,0xFF,2,1,2); Expected=$null }, # arbitrary command/authentication bytes rejected
        @{ Kind=3; Packet=[byte[]](1,0x20,6,0,2,0,6,0,5,4); Expected='SMP PairingFailed reason=0x04 L1' },
        @{ Kind=3; Packet=[byte[]](1,0x20,9,0,5,0,4,0,1,0x0A,1,0,0x0F); Expected='ATT ErrorResponse request=0x0A error=0x0F L1' },
        @{ Kind=3; Packet=[byte[]](1,0x20,16,0,12,0,1,0,3,1,8,0,1,0,2,0,2,0,0,0); Expected='L2CAP ConnectionResponse result=0x0002 status=0x0000 L1' },
        @{ Kind=3; Packet=[byte[]](@(1,0x20,21,0,17,0,6,0,6)+(@(0xAA)*16)); Expected=$null }, # encryption key rejected
        @{ Kind=3; Packet=[byte[]](1,0x20,8,0,4,0,4,0,0x1B,1,0,0x42); Expected=$null }, # HID/ATT notification content rejected
        @{ Kind=3; Packet=[byte[]](1,0x10,6,0,2,0,6,0,5,4); Expected=$null }, # continuation fragment rejected, no reassembly
        @{ Kind=3; Packet=[byte[]](1,0x20,7,0,2,0,6,0,5,4); Expected=$null }, # invalid ACL length
        @{ Kind=3; Packet=[byte[]](1,0x20,6,0,2,0,5,0,5,4); Expected=$null } # other CID rejected
    )
    foreach ($test in $envelopes) {
        $bytes = [byte[]]::new(8 + $test.Packet.Length)
        $bytes[0] = 1; $bytes[1] = 2; $bytes[3] = $test.Kind
        $length = if ($test.ContainsKey('DeclaredLength')) { $test.DeclaredLength } else { $test.Packet.Length }
        [BitConverter]::GetBytes([uint32]$length).CopyTo($bytes,4)
        $test.Packet.CopyTo($bytes,8)
        $memory = [Runtime.InteropServices.Marshal]::AllocHGlobal($bytes.Length)
        try {
            [Runtime.InteropServices.Marshal]::Copy($bytes,0,$memory,$bytes.Length)
            $size = if ($test.ContainsKey('Size')) { $test.Size } else { $bytes.Length }
            $actual = (New-Object iMirror.Diagnostics.BluetoothLinkTrace).DecodeBip($memory,$size)
            if ($actual -ne $test.Expected) { throw ('Unexpected sanitized BIP decode: ' + $actual) }
        } finally {
            [Array]::Clear($bytes,0,$bytes.Length)
            [Runtime.InteropServices.Marshal]::Copy($bytes,0,$memory,$bytes.Length)
            [Runtime.InteropServices.Marshal]::FreeHGlobal($memory)
        }
    }
    'PASS: 40 controller fixtures; 12 HCI and 28 BIP envelopes; safe advertising/error metadata; keys/names/input/fragments/unknown/truncated rejected; no native trace started.'
    exit 0
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Execute este script uma vez como administrador. Ele somente observa status Bluetooth por tempo limitado.'
}
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root 'logs'
[void][IO.Directory]::CreateDirectory($directory)
$path = Join-Path $directory 'bluetooth-link-trace.log'
$statePath = Join-Path $directory 'bluetooth-link-active.json'
if ($StopSession) {
    # Only the exact diagnostic-owned session from its local log is accepted.
    # Never stop other ETW tools or change Bluetooth radio/services/bonds.
    & "$env:SystemRoot\System32\logman.exe" stop $StopSession -ets *> $null
    ('{0} RECOVERY STOP session={1}; logmanExit={2}' -f (Get-Date).ToString('o'), $StopSession, $LASTEXITCODE) | Add-Content -LiteralPath $path -Encoding UTF8
    if (-not $ContinueAfterStop) { exit $LASTEXITCODE }
}
$trace = New-Object iMirror.Diagnostics.BluetoothLinkTrace
$lease = [Threading.Mutex]::new($false, 'Local\iMirror.Bluetooth.LinkTrace')
$ownsLease = $false
$logWriter = $null
. "$PSScriptRoot/bluetooth-termination-context.ps1"
$leConnections = @{}
function Write-TraceLines {
    foreach ($line in $trace.Drain()) {
        $logWriter.WriteLine($line); Write-Host $line
        $context = Get-BluetoothTerminationContext -Line $line -Connections $leConnections -SnapshotPath (Join-Path $directory 'bluetooth-hid-stage.json')
        if ($context) { $logWriter.WriteLine($context); Write-Host $context }
    }
}
try {
    try { $ownsLease = $lease.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsLease = $true }
    if (-not $ownsLease) { throw 'Uma coleta já está ativa. Aguarde seu término; não será aberta outra sessão.' }
    # Keep one shared append handle for the entire capture. Repeated Add-Content
    # opens could fail with ERROR_SHARING_VIOLATION when diagnostics were read.
    # Readers/tailers are allowed without interrupting the native consumer.
    $logStream = [IO.FileStream]::new($path,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite)
    $logWriter = [IO.StreamWriter]::new($logStream,[Text.UTF8Encoding]::new($false))
    $logWriter.AutoFlush = $true
    # Recover ONLY the exact prior diagnostic-owned logger if its consumer was
    # terminated. The mutex prevents stopping a live cooperative collector.
    if (Test-Path -LiteralPath $statePath) {
        $previous = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($previous.Session -notmatch '^iMirror\.Bluetooth\.Status\.[0-9a-f]{32}$') { throw 'Estado de coleta inválido; nenhuma sessão foi parada.' }
        & "$env:SystemRoot\System32\logman.exe" stop $previous.Session -ets *> $null
        $logWriter.WriteLine(('{0} RECOVERY STOP session={1}; logmanExit={2}' -f (Get-Date).ToString('o'), $previous.Session, $LASTEXITCODE))
    }
    $trace.Start()
    @{ Session = $trace.SessionName; Started = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    Write-TraceLines
    Write-Host "Coleta ativa por $Seconds segundos. Mantenha uma instância do iMirror. Faça uma tentativa no iPhone. Não altere o rádio durante a coleta."
    Write-Host 'Salva somente status, metadados de anúncio e códigos de erro de segurança/GATT. Não salva ETL, nomes, endereços, chaves, pacotes ou teclas. Abrange todo o rádio; não identifica automaticamente o iPhone.'
    Write-Host ('Se este processo for forçado a encerrar, pare somente sua sessão: logman stop "{0}" -ets' -f $trace.SessionName)
    $deadline = (Get-Date).AddSeconds($Seconds)
    $heartbeat = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ((Get-Date) -ge $heartbeat) { $trace.Heartbeat(); $heartbeat = (Get-Date).AddSeconds(5) }
        Write-TraceLines
    }
} catch {
    if ($logWriter) { $logWriter.WriteLine(('{0} TRACE ERROR type={1}; HRESULT=0x{2:X8}' -f (Get-Date).ToString('o'), $_.Exception.GetType().Name, $_.Exception.HResult)) }
    throw
} finally {
    $trace.Dispose(); Write-TraceLines
    if ($trace.StopConfirmed -and (Test-Path -LiteralPath $statePath)) {
        $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($saved.Session -eq $trace.SessionName) { Remove-Item -LiteralPath $statePath }
    }
    if ($logWriter) { $logWriter.Dispose() }
    if ($ownsLease) { $lease.ReleaseMutex() }; $lease.Dispose()
}
