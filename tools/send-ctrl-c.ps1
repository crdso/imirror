param([Parameter(Mandatory=$true)][int]$ReceiverId)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReceiverConsole {
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint pid);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList(uint[] processes, uint count);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint code, uint group);
}
'@
# Only the helper detaches. Never signal a console shared with unrelated processes.
[ReceiverConsole]::FreeConsole() | Out-Null
if (-not [ReceiverConsole]::AttachConsole([uint32]$ReceiverId)) { exit 2 }
try {
    $ids = New-Object uint32[] 64
    $count = [ReceiverConsole]::GetConsoleProcessList($ids,64)
    if ($count -eq 0 -or $count -gt 64) { exit 3 }
    for ($i=0; $i -lt $count; $i++) { if ($ids[$i] -ne $ReceiverId -and $ids[$i] -ne $PID) { exit 4 } }
    [ReceiverConsole]::SetConsoleCtrlHandler([IntPtr]::Zero,$true) | Out-Null
    if (-not [ReceiverConsole]::GenerateConsoleCtrlEvent(0,0)) { exit 5 }
} finally { [ReceiverConsole]::FreeConsole() | Out-Null }
exit 0
