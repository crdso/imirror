$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $root ('.cache/validation/ios-stable-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    & "$PSScriptRoot/verify-ios-stable-schema.ps1"
    $schema = [IO.File]::ReadAllText((Join-Path $root 'src/iMirror.Bluetooth/HidSchema.cs'))
    $damagedSchema = Join-Path $fixture 'damaged-schema.cs'
    [IO.File]::WriteAllText($damagedSchema, $schema.Replace('0x25,0x65','0x26,0x87,0x00'))
    $rejected = $false
    try { & "$PSScriptRoot/verify-ios-stable-schema.ps1" -SchemaPath $damagedSchema } catch { $rejected = $_.Exception.Message -like '*ReportMap differs*' }
    if (-not $rejected) { throw 'Changed descriptor did not fail the build guard' }
    $peripheral = [IO.File]::ReadAllText((Join-Path $root 'src/iMirror.Bluetooth/HogpPeripheral.cs'))
    $damagedGatt = Join-Path $fixture 'damaged-gatt.cs'
    [IO.File]::WriteAllText($damagedGatt, $peripheral.Replace('0x2A4A, "HID Information"','0x2A40, "HID Information"'))
    $rejected = $false
    try { & "$PSScriptRoot/verify-ios-stable-schema.ps1" -PeripheralPath $damagedGatt } catch { $rejected = $_.Exception.Message -like '*GATT definition differs*' }
    if (-not $rejected) { throw 'Changed database did not fail the build guard' }
    Write-Host 'PASS: 3 iOS Stable schema safety groups; baseline accepted, changed map/database rejected; no Bluetooth operations.'
} finally {
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    $allowed = [IO.Path]::GetFullPath((Join-Path $root '.cache/validation')) + '\'
    if (-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup outside project' }
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Fixture reparse point; no cleanup' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
