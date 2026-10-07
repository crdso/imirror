$ErrorActionPreference = 'Stop'
$NativePackagingRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
function Assert-NativeProjectPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $full.StartsWith($NativePackagingRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside project: $full" }
    for ($ancestor=$full; $ancestor -ne $NativePackagingRoot; $ancestor=Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse path: $ancestor" }
    }
    return $full
}
# PE imports, including delay imports; Windows system/API-set DLLs remain supplied by Windows.
if (-not ('iMirrorPackaging.PeImports' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
namespace iMirrorPackaging {
 public static class PeImports {
  public static string[] Read(string path) {
   byte[] b=File.ReadAllBytes(path); int pe=BitConverter.ToInt32(b,60);
   if(BitConverter.ToUInt16(b,pe+4)!=0x8664) throw new InvalidDataException("Runtime must be x64: "+path);
   int count=BitConverter.ToUInt16(b,pe+6), size=BitConverter.ToUInt16(b,pe+20), opt=pe+24;
   int dir=opt+(BitConverter.ToUInt16(b,opt)==0x20b?112:96), sections=opt+size;
   Func<uint,int> offset=rva=> { for(int i=0;i<count;i++) {
    int s=sections+40*i; uint start=BitConverter.ToUInt32(b,s+12);
    uint length=Math.Max(BitConverter.ToUInt32(b,s+8),BitConverter.ToUInt32(b,s+16));
    if(rva>=start && rva<start+length) return checked((int)(rva-start+BitConverter.ToUInt32(b,s+20)));
   } return checked((int)rva); };
   var result=new List<string>();
   foreach(int index in new[]{1,13}) {
    uint rva=BitConverter.ToUInt32(b,dir+8*index); if(rva==0) continue;
    int pos=offset(rva), stride=index==1?20:32;
    for(;pos+stride<=b.Length;pos+=stride) {
     bool end=true; for(int j=0;j<stride;j++) if(b[pos+j]!=0) {end=false;break;} if(end) break;
     uint name=BitConverter.ToUInt32(b,pos+(index==1?12:4)); if(name==0) continue;
     if(index==13 && (BitConverter.ToUInt32(b,pos)&1)==0) {
      ulong image=BitConverter.ToUInt16(b,opt)==0x20b?BitConverter.ToUInt64(b,opt+24):BitConverter.ToUInt32(b,opt+28);
      name=checked((uint)(name-image));
     }
     int start=offset(name), finish=start; while(finish<b.Length && b[finish]!=0) finish++;
     result.Add(System.Text.Encoding.ASCII.GetString(b,start,finish-start));
    }
   } return result.ToArray();
  }
 }
}
'@
}

function Get-AirPlayRuntimeFiles {
    param([Parameter(Mandatory)][string]$Prefix, [switch]$IncludeTestEncoders)
    $Prefix = (Resolve-Path -LiteralPath (Assert-NativeProjectPath $Prefix)).Path
    $bin = Join-Path $Prefix 'bin'
    $inspect = Join-Path $bin 'gst-inspect-1.0.exe'
    $elements = @('appsrc','h264parse','h265parse','avdec_h264','avdec_h265','d3d11videosink',
        'decodebin','rtph264depay','rtph265depay','videoconvert','videoscale','videotestsrc',
        'filesrc','queue','typefind','typefindfunctions','autodetect','fakesink','identity','capsfilter')
    if ($IncludeTestEncoders) { $elements += @('openh264enc','x265enc') }
    $queue = [Collections.Generic.Queue[string]]::new()
    foreach ($name in @('uxplay.exe','gst-inspect-1.0.exe','gst-launch-1.0.exe','dns-sd.exe','dnssd.dll')) {
        $queue.Enqueue((Join-Path $bin $name))
    }
    $queue.Enqueue((Join-Path $Prefix 'libexec/gstreamer-1.0/gst-plugin-scanner.exe'))
    $saved = @{}
    foreach ($name in @('PATH','GST_PLUGIN_SYSTEM_PATH_1_0','GST_PLUGIN_PATH_1_0','GST_PLUGIN_SCANNER_1_0','GST_REGISTRY_1_0')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
    try {
        $env:PATH = "$bin;$env:SystemRoot\System32;$env:SystemRoot"
        $env:GST_PLUGIN_SYSTEM_PATH_1_0 = Join-Path $Prefix 'lib/gstreamer-1.0'
        $env:GST_PLUGIN_PATH_1_0 = ''
        $env:GST_PLUGIN_SCANNER_1_0 = Join-Path $Prefix 'libexec/gstreamer-1.0/gst-plugin-scanner.exe'
        $registryDirectory = Assert-NativeProjectPath (Join-Path $NativePackagingRoot '.cache/runtime-discovery')
        New-Item -ItemType Directory -Path $registryDirectory -Force | Out-Null
        $env:GST_REGISTRY_1_0 = Join-Path $registryDirectory 'registry.bin'
        $version = & (Join-Path $bin 'uxplay.exe') -v 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $version -notmatch 'UxPlay version 1\.73\.7(?:\D|$)') { throw 'Expected UxPlay 1.73.7' }
        $version = & $inspect --version 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $version -notmatch 'GStreamer 1\.28\.7(?:\D|$)') { throw 'Expected GStreamer 1.28.7' }
        foreach ($element in $elements) {
            $output = & $inspect $element 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw "gst-inspect $element failed: $output" }
            $match = [regex]::Match($output, '(?m)^\s*Filename\s+(.+\.dll)\s*$')
            if (-not $match.Success) { throw "Plugin file not identified for $element" }
            $queue.Enqueue($match.Groups[1].Value.Trim())
        }
    } finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $system = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    while ($queue.Count) {
        $file = $queue.Dequeue()
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing runtime file: $file" }
        $file = (Resolve-Path -LiteralPath (Assert-NativeProjectPath $file)).Path
        if (-not $file.StartsWith($Prefix + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime outside prefix' }
        if (-not $seen.Add($file)) { continue }
        foreach ($dependency in [iMirrorPackaging.PeImports]::Read($file)) {
            $local = Join-Path $bin $dependency
            if (Test-Path -LiteralPath $local) { $queue.Enqueue($local) }
            elseif ($dependency -match '^(api-ms-|ext-ms-)' -or (Test-Path -LiteralPath (Join-Path $env:SystemRoot "System32/$dependency"))) {
                [void]$system.Add($dependency)
            } else { throw "Unresolved dependency $dependency of $file" }
        }
    }
    [pscustomobject]@{ Files=@($seen | Sort-Object); Elements=$elements; SystemDlls=@($system | Sort-Object) }
}

function Copy-AirPlayRuntime {
    param([Parameter(Mandatory)][string]$Prefix, [Parameter(Mandatory)][string]$Destination)
    $Prefix = (Resolve-Path -LiteralPath $Prefix).Path
    $Destination = Assert-NativeProjectPath $Destination
    $manifest = Get-AirPlayRuntimeFiles -Prefix $Prefix
    $entries = foreach ($file in $manifest.Files) {
        $relative = $file.Substring($Prefix.Length + 1)
        $target = Assert-NativeProjectPath (Join-Path $Destination $relative)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file -Destination $target -Force
        [pscustomobject]@{ Path=$relative; Bytes=(Get-Item -LiteralPath $file).Length; SHA256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    }
    [pscustomobject]@{ UxPlay='1.73.7'; GStreamer='1.28.7'; Files=@($entries); Elements=$manifest.Elements; SystemDlls=$manifest.SystemDlls } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Destination 'runtime-manifest.json') -Encoding utf8
    $entries | Measure-Object Bytes -Sum | Select-Object Count,Sum
}
