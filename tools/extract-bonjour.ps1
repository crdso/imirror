param([string]$Installer = "$PSScriptRoot\..\.tools\downloads\Bonjour64.msi")
$ErrorActionPreference = 'Stop'
# Read-only extraction: no Windows Installer actions or service installation.
Add-Type @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class BonjourCab {
 [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiOpenDatabase(string path, IntPtr mode, out uint db);
 [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiDatabaseOpenView(uint db, string query, out uint view);
 [DllImport("msi.dll")] static extern uint MsiViewExecute(uint view, uint record);
 [DllImport("msi.dll")] static extern uint MsiViewFetch(uint view, out uint record);
 [DllImport("msi.dll")] static extern uint MsiRecordReadStream(uint record, uint field, byte[] data, ref uint length);
 [DllImport("msi.dll")] static extern uint MsiCloseHandle(uint handle);
 static void Check(uint code) { if(code!=0) throw new IOException("MSI read error: "+code); }
 public static void Extract(string path, string target) {
  uint db=0, view=0, record=0;
  try {
   Check(MsiOpenDatabase(path, IntPtr.Zero, out db));
   Check(MsiDatabaseOpenView(db, "SELECT `Data` FROM `_Streams` WHERE `Name`='Bonjour.cab'", out view));
   Check(MsiViewExecute(view, 0)); Check(MsiViewFetch(view, out record));
   using(var output=File.Create(target)) {
    byte[] bytes=new byte[65536]; uint length;
    do { length=(uint)bytes.Length; Check(MsiRecordReadStream(record,1,bytes,ref length)); output.Write(bytes,0,(int)length); } while(length!=0);
   }
  } finally { if(record!=0) MsiCloseHandle(record); if(view!=0) MsiCloseHandle(view); if(db!=0) MsiCloseHandle(db); }
 }
}
'@
$destination = [IO.Path]::GetFullPath("$PSScriptRoot\..\.tools\bonjour-cab")
New-Item -ItemType Directory -Force $destination | Out-Null
[BonjourCab]::Extract([IO.Path]::GetFullPath($Installer), "$destination\Bonjour.cab")
& "$env:SystemRoot\System32\expand.exe" '-F:*' "$destination\Bonjour.cab" $destination | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not extract Bonjour CAB.' }
Copy-Item -LiteralPath "$destination\dnssd64.dll" -Destination "$PSScriptRoot\..\.tools\msys64\ucrt64\bin\dnssd.dll"
Copy-Item -LiteralPath "$destination\dns_sd64.exe" -Destination "$PSScriptRoot\..\.tools\msys64\ucrt64\bin\dns-sd.exe"
