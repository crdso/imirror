param([switch]$KeepExtracted)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
. "$PSScriptRoot/native-runtime.ps1"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not ('iMirrorReleaseSmoke.Native' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace iMirrorReleaseSmoke {
 public static class Native {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct Monitor { public int Size; public Rect Display,Work; public int Flags; }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct FileInfo {
   public IntPtr Icon; public int Index; public uint Attributes;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string Type;
  }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
  [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr h,ref Monitor m);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string p,uint a,out FileInfo f,uint size,uint flags);
  [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
 }
}
'@
}
function Assert-BlueIcon([IntPtr]$Handle) {
    if ($Handle -eq [IntPtr]::Zero) { throw 'Icon missing' }
    $borrowed = [Drawing.Icon]::FromHandle($Handle)
    $bitmap = $borrowed.ToBitmap()
    try {
        $blue = 0
        for ($y=0; $y -lt $bitmap.Height; $y++) {
            for ($x=0; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x,$y)
                if ($pixel.A -gt 100 -and $pixel.B -gt ($pixel.R + 30)) { $blue++ }
            }
        }
        if ($blue -lt 5) { throw 'Expected blue branding pixels' }
    } finally { $bitmap.Dispose(); $borrowed.Dispose() }
}
function Assert-ShellIcon([string]$Path) {
    $info = [iMirrorReleaseSmoke.Native+FileInfo]::new()
    [void][iMirrorReleaseSmoke.Native]::SHGetFileInfo($Path,0,[ref]$info,[Runtime.InteropServices.Marshal]::SizeOf($info),0x101)
    try { Assert-BlueIcon $info.Icon } finally { if ($info.Icon -ne [IntPtr]::Zero) { [void][iMirrorReleaseSmoke.Native]::DestroyIcon($info.Icon) } }
}
function Test-Executable([string]$Path,[string]$Label) {
    Assert-ShellIcon $Path
    $start = [Diagnostics.ProcessStartInfo]::new($Path)
    $start.WorkingDirectory = Split-Path -Parent $Path
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Normal
    $start.EnvironmentVariables['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
    foreach ($name in @($start.EnvironmentVariables.Keys | Where-Object { $_ -like 'DOTNET_ROOT*' })) { $start.EnvironmentVariables.Remove($name) }
    $start.EnvironmentVariables['IMIRROR_LOG_DIRECTORY'] = Join-Path $smoke "logs-$Label"
    $process = [Diagnostics.Process]::Start($start)
    try {
        for ($i=0; $i -lt 150; $i++) {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
            if ($process.HasExited) { throw "$Label exited early: $($process.ExitCode)" }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -eq 'iMirror') { break }
        }
        $handle = $process.MainWindowHandle
        if ($handle -eq [IntPtr]::Zero -or $process.MainWindowTitle -ne 'iMirror') { throw "$Label main window missing; observed title=$($process.MainWindowTitle)" }
        Start-Sleep -Milliseconds 800
        foreach ($size in @(0,1)) {
            $icon = [IntPtr]::Zero
            [void][iMirrorReleaseSmoke.Native]::SendMessageTimeout($handle,0x7F,[IntPtr]$size,[IntPtr]::Zero,2,500,[ref]$icon)
            Assert-BlueIcon $icon
        }
        $rect = [iMirrorReleaseSmoke.Native+Rect]::new()
        [void][iMirrorReleaseSmoke.Native]::GetWindowRect($handle,[ref]$rect)
        $monitor = [iMirrorReleaseSmoke.Native+Monitor]::new()
        $monitor.Size = [Runtime.InteropServices.Marshal]::SizeOf($monitor)
        [void][iMirrorReleaseSmoke.Native]::GetMonitorInfo([iMirrorReleaseSmoke.Native]::MonitorFromWindow($handle,2),[ref]$monitor)
        $work = $monitor.Work
        $width = $rect.Right-$rect.Left; $height = $rect.Bottom-$rect.Top
        if ($rect.Left -lt $work.Left -or $rect.Top -lt $work.Top -or $rect.Right -gt $work.Right -or $rect.Bottom -gt $work.Bottom -or $width -gt (($work.Right-$work.Left)*.85+2) -or $height -gt (($work.Bottom-$work.Top)*.85+2)) { throw "$Label window outside expected WorkArea" }
        Write-Host "PASS: $Label direct EXE; embedded blue SMALL/BIG/Shell icons; window ${width}x${height} within WorkArea"
        if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw "$Label graceful exit failed" }
    } finally {
        if (-not $process.HasExited) { [void]$process.CloseMainWindow(); if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() } }
        $process.Dispose()
    }
}
$smoke = Assert-NativeProjectPath (Join-Path $ProjectRoot '.cache/release-branding-smoke')
if (Test-Path -LiteralPath $smoke) { throw 'Smoke directory exists; inspect/remove previous evidence first' }
New-Item -ItemType Directory -Path $smoke | Out-Null
try {
    $zip = Join-Path $ProjectRoot 'dist/iMirror-Portable.zip'
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $smoke $entry.FullName))
            if (-not $target.StartsWith($smoke+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive entry' }
            if ($entry.FullName -match '(^|[/\\])(obj|\.tools|\.git|\.cache|src)([/\\]|$)') { throw 'Unexpected development files in package' }
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip,$smoke)
    $portable = Join-Path $smoke 'iMirror'
    foreach ($root in @((Join-Path $ProjectRoot 'dist/iMirror'),$portable)) {
        $runtime = Join-Path $root 'runtime/airplay'
        $manifest = Get-Content (Join-Path $runtime 'runtime-manifest.json') -Raw | ConvertFrom-Json
        foreach ($file in $manifest.Files) {
            if ((Get-FileHash -LiteralPath (Join-Path $runtime $file.Path) -Algorithm SHA256).Hash -ne $file.SHA256) { throw "Native hash differs: $($file.Path)" }
        }
        if ($manifest.UxPlay -ne '1.73.7' -or $manifest.GStreamer -ne '1.28.7') { throw 'Unexpected native versions' }
        $options = Get-Content (Join-Path $root 'airplay.json') -Raw | ConvertFrom-Json
        if ($options.VideoSink -ne 'd3d11videosink' -or -not $options.EnableH265 -or $options.BasePort -ne 35000) { throw 'AirPlay configuration changed' }
        Write-Host "PASS: runtime $($manifest.Files.Count) native SHA256 files, versions, HEVC and original renderer/port"
    }
    Test-Executable (Join-Path $ProjectRoot 'dist/iMirror/iMirror.exe') 'FDD'
    Test-Executable (Join-Path $portable 'iMirror.exe') 'Portable'
    $shell = New-Object -ComObject WScript.Shell
    $linkPath = Join-Path $smoke 'iMirror.lnk'
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = Join-Path $ProjectRoot 'dist/iMirror/iMirror.exe'
    $link.IconLocation = $link.TargetPath + ',0'
    $link.Save()
    Assert-ShellIcon $linkPath
    Write-Host 'PASS: shortcut Shell icon uses blue application branding; temporary local shortcut only'
} finally {
    if (-not $KeepExtracted) {
        $resolved = (Resolve-Path -LiteralPath (Assert-NativeProjectPath $smoke)).Path
        if (Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse in smoke directory; no cleanup' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
