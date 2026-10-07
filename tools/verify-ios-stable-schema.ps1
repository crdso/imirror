param([string]$SchemaPath, [string]$PeripheralPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SchemaPath) { $SchemaPath = Join-Path $root 'src/iMirror.Bluetooth/HidSchema.cs' }
if (-not $PeripheralPath) { $PeripheralPath = Join-Path $root 'src/iMirror.Bluetooth/HogpPeripheral.cs' }
$expectedHash = '3097B7140EDA569B37EECC11502A31AF653B444FFEDB923E0CC85F3CCB5C0A5D'
$source = [IO.File]::ReadAllText($SchemaPath)
foreach ($identity in @('public const string Profile = "iOS-stable";',
    'public static Guid Uuid(ushort value) => new($"0000{value:x4}-0000-1000-8000-00805f9b34fb");',
    'public static byte[] ReportMap => (byte[])StableReportMap.Clone();')) {
    if (-not $source.Contains($identity)) { throw 'Stable schema profile/UUID/map accessor changed' }
}
$match = [regex]::Match($source, '(?s)StableReportMap\s*=\s*\[(.*?)\];')
if (-not $match.Success) { throw 'iOS Stable ReportMap not found' }
$body = [regex]::Replace($match.Groups[1].Value, '(?m)//.*$', '')
$tokens = [regex]::Matches($body, '0x[0-9A-Fa-f]+|KeyboardId|MouseId')
if ([regex]::Replace($body, '0x[0-9A-Fa-f]+|KeyboardId|MouseId|[\s,]', '').Length) { throw 'Unexpected ReportMap expression' }
foreach ($constant in @(@('KeyboardId','1'),@('MouseId','2'),@('KeyboardLength','8'),@('MouseLength','6'))) {
    if ($source -notmatch ('\b' + $constant[0] + '\s*=\s*' + $constant[1] + '\s*;')) { throw "Stable report identity changed: $($constant[0])" }
}
[byte[]]$actual = @($tokens | ForEach-Object { if ($_.Value -eq 'KeyboardId') { 1 } elseif ($_.Value -eq 'MouseId') { 2 } else { [Convert]::ToByte($_.Value.Substring(2),16) } })
$golden = [regex]::Replace([IO.File]::ReadAllText((Join-Path $root 'src/iMirror.Bluetooth/Compatibility/ios-stable-report-map.hex')), '(?m)#.*$', '').Trim()
[byte[]]$expected = @($golden -split '\s+' | ForEach-Object { [Convert]::ToByte($_,16) })
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash($actual)).Replace('-',''); $goldenHash = [BitConverter]::ToString($sha.ComputeHash($expected)).Replace('-','') } finally { $sha.Dispose() }
if ($actual.Length -ne 113 -or $hash -ne $expectedHash -or $goldenHash -ne $expectedHash -or [Convert]::ToBase64String($actual) -cne [Convert]::ToBase64String($expected)) { throw 'iOS Stable ReportMap differs from known-good commit 8262f344 (bytes/hash)' }
$peripheral = [IO.File]::ReadAllText($PeripheralPath).Replace("`r`n","`n")
if ([regex]::Matches($peripheral, 'GattServiceProvider\.CreateAsync\(HidSchema\.Uuid\(0x1812\)\)').Count -ne 2 -or
    [regex]::Matches($peripheral, 'GattServiceProvider\.CreateAsync\(HidSchema\.Uuid\(0x180F\)\)').Count -ne 1 -or
    [regex]::Matches($peripheral, 'GattServiceProvider\.CreateAsync\(').Count -ne 3) { throw 'Stable HID/BAS service identity changed' }
$start = $peripheral.IndexOf('        GattLocalService service = _provider.Service;')
$end = $peripheral.IndexOf('        _provider.AdvertisementStatusChanged += OnAdvertising;')
$methods = $peripheral.IndexOf('    private async Task<GattLocalCharacteristic> CreateAsync')
$methodsEnd = $peripheral.IndexOf('    private void TrackSession')
if ($start -lt 0 -or $end -le $start -or $methods -lt 0 -or $methodsEnd -le $methods) { throw 'Stable GATT sections not found' }
$definition = $peripheral.Substring($start,$end-$start).Trim() + "`n--- CHARACTERISTIC IMPLEMENTATION ---`n" + $peripheral.Substring($methods,$methodsEnd-$methods).Trim() + "`n"
$knownGatt = [IO.File]::ReadAllText((Join-Path $root 'src/iMirror.Bluetooth/Compatibility/ios-stable-gatt-definition.txt')).Replace("`r`n","`n")
$sha = [Security.Cryptography.SHA256]::Create()
try { $gattHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($knownGatt))).Replace('-','') } finally { $sha.Dispose() }
if ($gattHash -ne '4AB1A88E26EFEC2587A231F68B71D9953F169FA8E2EDE0329DB4DB31079AF535') { throw 'Known-good GATT baseline was changed' }
if ($definition -cne $knownGatt) { throw 'iOS Stable GATT definition differs from known-good commit 8262f344' }
Write-Host "PASS: iOS Stable schema/GATT known-good 8262f344; ReportMap length=113 SHA256=$hash"
