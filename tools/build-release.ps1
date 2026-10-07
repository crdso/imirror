param([switch]$FrameworkDependentOnly)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/clean-project.ps1"
. "$PSScriptRoot/common.ps1"
. "$PSScriptRoot/native-runtime.ps1"
Push-Location $ProjectRoot
try {
    & "$PSScriptRoot/test.ps1" -Configuration Release
    & "$PSScriptRoot/start-ble-hid-probe.ps1" -Build -SelfTest
    & "$PSScriptRoot/test-airplay-network-safety.ps1"
    Invoke-ProjectDotNet @('run','--project','tests/iMirror.Phase3.Tests/iMirror.Phase3.Tests.csproj','-c','Release','--no-build','--','--native-cursor')
    $stage = Join-Path $ProjectRoot '.cache/publish'
    $fdd = Join-Path $stage 'framework-dependent'
    $portable = Join-Path $stage 'portable/iMirror'
    $dist = Join-Path $ProjectRoot 'dist/iMirror'
    foreach ($path in @($stage,$dist,(Join-Path $ProjectRoot '.cache/inventory'))) { [void](Assert-NativeProjectPath $path) }
    if ((Test-Path -LiteralPath $dist) -and (Get-ChildItem -LiteralPath $dist -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) { throw 'Reparse in dist' }
    New-Item -ItemType Directory -Path (Join-Path $ProjectRoot '.cache/inventory') -Force | Out-Null
    New-Item -ItemType Directory -Path $stage,$dist -Force | Out-Null
    Invoke-ProjectDotNet @('publish','src/iMirror.App/iMirror.App.csproj','-c','Release','-p:PublishProfile=FrameworkDependent','-o',$fdd)
    # Only publish products, never source/SDK/obj/PDB/cache files.
    foreach ($file in Get-ChildItem -LiteralPath $fdd -File | Where-Object Extension -ne '.pdb') {
        Copy-Item -LiteralPath $file.FullName -Destination $dist -Force
    }
    $prefix = Join-Path $ProjectRoot '.tools/msys64/ucrt64'
    $runtime = Join-Path $dist 'runtime/airplay'
    Copy-AirPlayRuntime -Prefix $prefix -Destination $runtime | Format-Table
    $licenses = Join-Path $prefix 'share/licenses'
    if (Test-Path -LiteralPath $licenses) {
        foreach ($file in Get-ChildItem -LiteralPath $licenses -File -Recurse) {
            $target = Join-Path (Join-Path $runtime 'licenses') $file.FullName.Substring($licenses.Length + 1)
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'third_party/README.md') -Destination (Join-Path $runtime 'UXPLAY_BUILD.md') -Force
    foreach ($name in @('UxPlay-LICENSE','uxplay-1.73.7-live-logs.patch')) {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot "third_party/$name") -Destination $runtime -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $dist 'airplay.json'))) {
        [ordered]@{
            UxPlayPath='runtime/airplay/bin/uxplay.exe'; GStreamerBinPath='runtime/airplay/bin'; BonjourDirectory='runtime/airplay/bin'
            ReceiverName='iMirror - Windows'; BasePort=35000; VideoDecoder='avdec_h264'; VideoSink='d3d11videosink'
            EnableH265=$true; DetailedNegotiationLogging=$true
            AttemptLogPath='%LOCALAPPDATA%/iMirror/logs/iphone-airplay-attempt.log'
            SessionDirectory='%LOCALAPPDATA%/iMirror/airplay'
        } | ConvertTo-Json | Set-Content (Join-Path $dist 'airplay.json') -Encoding utf8
    }
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'docs/RELEASE.md') -Destination (Join-Path $dist 'LEIA-ME.md') -Force
    $zip = Join-Path $ProjectRoot 'dist/iMirror-Portable.zip'
    if ($FrameworkDependentOnly) { $zipSource = $dist }
    else {
        New-Item -ItemType Directory -Path $portable -Force | Out-Null
        Invoke-ProjectDotNet @('publish','src/iMirror.App/iMirror.App.csproj','-c','Release','-p:PublishProfile=SelfContained','-o',$portable)
        Get-ChildItem -LiteralPath $portable -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
        # Use hard links to avoid a second runtime copy while assembling the portable archive.
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $dist 'runtime') -Recurse -File) {
            $target = Join-Path $portable $file.FullName.Substring($dist.Length + 1)
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null
        }
        foreach ($name in @('airplay.json','LEIA-ME.md')) { Copy-Item -LiteralPath (Join-Path $dist $name) -Destination $portable }
        $zipSource = Split-Path -Parent $portable
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pendingZip = Join-Path $stage 'iMirror-Portable.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($zipSource, $pendingZip, [IO.Compression.CompressionLevel]::Optimal, $FrameworkDependentOnly.IsPresent)
    Move-Item -LiteralPath $pendingZip -Destination $zip -Force
    $report = [ordered]@{
        FrameworkDependentBytes=(Get-ChildItem -LiteralPath $dist -Recurse -File | Measure-Object Length -Sum).Sum
        ExeBytes=(Get-Item -LiteralPath (Join-Path $dist 'iMirror.exe')).Length
        AirPlayRuntimeBytes=(Get-ChildItem -LiteralPath $runtime -Recurse -File | Measure-Object Length -Sum).Sum
        PortableUncompressedBytes=if ($FrameworkDependentOnly) { $null } else { (Get-ChildItem -LiteralPath $portable -Recurse -File | Measure-Object Length -Sum).Sum }
        ZipBytes=(Get-Item -LiteralPath $zip).Length
        PortableSelfContained=(-not $FrameworkDependentOnly)
    }
    $report | ConvertTo-Json | Set-Content (Join-Path $ProjectRoot '.cache/inventory/release-sizes.json') -Encoding utf8
    foreach ($entry in $report.GetEnumerator()) { Write-Host "$($entry.Key): $($entry.Value)" }
    Write-Host "EXE: $dist\iMirror.exe"
    Write-Host "ZIP: $zip"
    # Discard duplicate publish staging; leave this run's bounded evidence for review.
    $resolved = (Resolve-Path -LiteralPath $stage).Path
    if (-not $resolved.StartsWith($ProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging outside project' }
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse staging' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
} finally { Pop-Location }
