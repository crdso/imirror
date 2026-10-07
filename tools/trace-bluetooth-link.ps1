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
    'PASS: 12 controller fixtures; Classic/LE/enhanced LE, disconnect, encryption, authentication; keys/unsupported/truncated rejected; no native trace started.'
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
function Write-TraceLines { foreach ($line in $trace.Drain()) { $line | Add-Content -LiteralPath $path -Encoding UTF8; Write-Host $line } }
try {
    try { $ownsLease = $lease.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsLease = $true }
    if (-not $ownsLease) { throw 'Uma coleta já está ativa. Aguarde seu término; não será aberta outra sessão.' }
    # Recover ONLY the exact prior diagnostic-owned logger if its consumer was
    # terminated. The mutex prevents stopping a live cooperative collector.
    if (Test-Path -LiteralPath $statePath) {
        $previous = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($previous.Session -notmatch '^iMirror\.Bluetooth\.Status\.[0-9a-f]{32}$') { throw 'Estado de coleta inválido; nenhuma sessão foi parada.' }
        & "$env:SystemRoot\System32\logman.exe" stop $previous.Session -ets *> $null
        ('{0} RECOVERY STOP session={1}; logmanExit={2}' -f (Get-Date).ToString('o'), $previous.Session, $LASTEXITCODE) | Add-Content -LiteralPath $path -Encoding UTF8
    }
    $trace.Start()
    @{ Session = $trace.SessionName; Started = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    Write-TraceLines
    Write-Host "Coleta ativa por $Seconds segundos. Mantenha uma instância do iMirror. Faça uma tentativa no iPhone. Não altere o rádio durante a coleta."
    Write-Host 'Não salva ETL, endereços, chaves, pacotes ou teclas. Eventos abrangem todo o rádio; uma sessão não identifica automaticamente o iPhone.'
    Write-Host ('Se este processo for forçado a encerrar, pare somente sua sessão: logman stop "{0}" -ets' -f $trace.SessionName)
    $deadline = (Get-Date).AddSeconds($Seconds)
    $heartbeat = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ((Get-Date) -ge $heartbeat) { $trace.Heartbeat(); $heartbeat = (Get-Date).AddSeconds(5) }
        Write-TraceLines
    }
} catch {
    ('{0} TRACE ERROR type={1}; HRESULT=0x{2:X8}' -f (Get-Date).ToString('o'), $_.Exception.GetType().Name, $_.Exception.HResult) | Add-Content -LiteralPath $path -Encoding UTF8
    throw
} finally {
    $trace.Dispose(); Write-TraceLines
    if ($trace.StopConfirmed -and (Test-Path -LiteralPath $statePath)) {
        $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($saved.Session -eq $trace.SessionName) { Remove-Item -LiteralPath $statePath }
    }
    if ($ownsLease) { $lease.ReleaseMutex() }; $lease.Dispose()
}
