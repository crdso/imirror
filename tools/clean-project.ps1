param([switch]$KeepBuildOutputs)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
function Remove-GeneratedDirectory([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $resolved = (Resolve-Path -LiteralPath $path).Path.TrimEnd('\')
    if (-not $resolved.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside project: $resolved" }
    # Reject reparse points in the target or any ancestor, and never follow a junction when deleting.
    for ($ancestor = $resolved; $ancestor -ne $root; $ancestor = Split-Path -Parent $ancestor) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point: $ancestor" }
    }
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Reparse point below $resolved" }
    try { Remove-Item -LiteralPath $resolved -Recurse -Force }
    catch {
        if ($resolved -eq (Join-Path $root '.cache/temp')) {
            Write-Host 'Local compiler temporary files still in use were preserved.'
            return
        }
        throw
    }
    Write-Host "Removed generated: $($resolved.Substring($root.Length + 1))"
}
if (-not $KeepBuildOutputs) {
    foreach ($tree in @('src','tests','experiments')) {
        $base = Join-Path $root $tree
        if (-not (Test-Path -LiteralPath $base)) { continue }
        if ((Get-Item -LiteralPath $base -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse source tree: $base" }
        foreach ($directory in @(Get-ChildItem -LiteralPath $base -Directory -Recurse -Force | Where-Object Name -in @('bin','obj'))) {
            if (Get-ChildItem -LiteralPath $directory.Parent.FullName -File -Filter '*.csproj') { Remove-GeneratedDirectory $directory.FullName }
        }
    }
}
foreach ($relative in @('TestResults','artifacts','.cache/validation','.cache/publish','.cache/temp')) {
    Remove-GeneratedDirectory (Join-Path $root $relative)
}
# Only old rotating runtime logs; keep current logs, physical evidence and arbitrary user files.
$logDirectory = Join-Path $root 'logs'
if (Test-Path -LiteralPath $logDirectory) {
    if ((Get-Item -LiteralPath $logDirectory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse logs directory' }
    foreach ($file in Get-ChildItem -LiteralPath $logDirectory -File | Where-Object {
        $_.LastWriteTime -lt (Get-Date).AddDays(-14) -and $_.Name -match '^(iMirror-.*\.log(\.\d+)?|bluetooth-control\.log\.\d+|iphone-airplay-attempt\.log\.\d+)$'
    }) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse log' }
        Remove-Item -LiteralPath $file.FullName -Force
    }
}
Write-Host 'Source, .git, native runtime, dist, SDK, NuGet cache, assets and user configuration preserved.'
