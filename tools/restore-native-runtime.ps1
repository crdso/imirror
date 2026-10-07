$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$archive = Join-Path $root '.tools/rollback/airplay-original-runtime.zip'
$manifest = Get-Content -LiteralPath (Join-Path $root '.tools/rollback/manifest.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $archive).Hash -ne $manifest.SHA256) { throw 'Rollback archive hash mismatch.' }
$destination = Join-Path $root '.tools/msys64/ucrt64'
if (-not $destination.StartsWith($root + '\')) { throw 'Outside project' }
for ($ancestor=$destination; $ancestor -ne $root; $ancestor=Split-Path -Parent $ancestor) {
    if ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse destination' }
}
foreach ($process in Get-Process uxplay,gst-launch-1.0,gst-inspect-1.0 -ErrorAction SilentlyContinue) {
    if ($process.Path -and $process.Path.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Stop the owned AirPlay receiver before restoring.' }
}
if (Get-ChildItem -LiteralPath $destination -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse in destination' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        $path = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
        if (-not $path.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive entry' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
    }
} finally { $zip.Dispose() }
Write-Host 'Original runtime DLLs/plugins restored in the same paths. UxPlay 1.73.7, configuration, network and firewall preserved.'
