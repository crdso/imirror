$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$AirPlayRoot = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$AirPlayLog = Join-Path $AirPlayRoot 'logs\airplay-readiness.log'
New-Item -ItemType Directory -Force (Join-Path $AirPlayRoot 'logs'),(Join-Path $AirPlayRoot '.cache\readiness') | Out-Null

function Write-AirPlaySetupLog {
    param([string]$Message)
    $line = "$(Get-Date -Format o) $Message"
    Add-Content -LiteralPath $AirPlayLog -Value $line -Encoding UTF8
    Write-Host $Message
}
function Test-AirPlayAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Test-AirPlayOwnedLauncher {
    param($Process,$Metadata)
    if (-not $Process -or -not $Metadata) { return $false }
    try {
        return ($Process.StartTime.ToUniversalTime().Ticks -eq ([DateTime]$Metadata.StartTimeUtc).ToUniversalTime().Ticks -and
            $Process.MainWindowTitle -eq 'iMirror')
    } catch { return $false }
}
function Get-AirPlayPaths {
    $configuration = Get-Content (Join-Path $AirPlayRoot 'airplay.json') -Raw | ConvertFrom-Json
    function Resolve-ConfiguredPath([string]$Path) {
        if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
        return [IO.Path]::GetFullPath((Join-Path $AirPlayRoot $Path))
    }
    if ($configuration.BasePort -lt 1024 -or $configuration.BasePort -gt 65533) { throw 'Invalid AirPlay BasePort.' }
    return @{ UxPlay=(Resolve-ConfiguredPath $configuration.UxPlayPath); GstBin=(Resolve-ConfiguredPath $configuration.GStreamerBinPath);
        Bonjour=(Join-Path $env:ProgramFiles 'Bonjour\mDNSResponder.exe'); Msi=(Join-Path $AirPlayRoot '.tools\downloads\Bonjour64.msi');
        ReceiverName=$configuration.ReceiverName; BasePort=[int]$configuration.BasePort }
}
function Get-AirPlayInstallerInfo {
    $path = (Get-AirPlayPaths).Msi
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    $installer=$null; $database=$null; $view=$null; $record=$null
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase($path,0)
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'ProductVersion'")
        $view.Execute(); $record=$view.Fetch()
        $version=$record.GetType().InvokeMember('StringData',[Reflection.BindingFlags]::GetProperty,$null,$record,@(1))
        return [pscustomobject]@{ Status=[string]$signature.Status; Publisher=$signature.SignerCertificate.Subject;
            Version=$version; Hash=(Get-FileHash -LiteralPath $path).Hash }
    } finally {
        foreach ($item in @($record,$view,$database,$installer)) { if ($null -ne $item) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($item) | Out-Null } }
    }
}
function Test-AirPlayX64Pe {
    param([string]$Path)
    $reader=$null
    try {
        $reader=[IO.BinaryReader]::new([IO.File]::OpenRead($Path))
        if ($reader.BaseStream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { return $false }
        $reader.BaseStream.Position=60; $offset=$reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt ($reader.BaseStream.Length - 6)) { return $false }
        $reader.BaseStream.Position=$offset
        return ($reader.ReadUInt32() -eq 0x4550 -and $reader.ReadUInt16() -eq 0x8664)
    } catch { return $false } finally { if ($reader) { $reader.Dispose() } }
}
function Get-AirPlayFirewallSpecs {
    $paths=Get-AirPlayPaths; $range="$($paths.BasePort)-$($paths.BasePort+2)"
    return @(
        @{Name='iMirror-AirPlay-TCP'; Program=$paths.UxPlay; Protocol='TCP'; Port=$range},
        @{Name='iMirror-AirPlay-UDP'; Program=$paths.UxPlay; Protocol='UDP'; Port=$range},
        @{Name='iMirror-Bonjour-mDNS'; Program=$paths.Bonjour; Protocol='UDP'; Port='5353'}
    )
}
function Test-AirPlayFirewallSpec {
    param([hashtable]$Spec)
    try {
        $rule=Get-NetFirewallRule -Name $Spec.Name -PolicyStore ActiveStore -ErrorAction Stop
        $port=$rule | Get-NetFirewallPortFilter -ErrorAction Stop
        $app=$rule | Get-NetFirewallApplicationFilter -ErrorAction Stop
        $address=$rule | Get-NetFirewallAddressFilter -ErrorAction Stop
        $protocol=[string]$port.Protocol
        $protocolOk=($protocol -eq $Spec.Protocol -or ($protocol -eq '6' -and $Spec.Protocol -eq 'TCP') -or ($protocol -eq '17' -and $Spec.Protocol -eq 'UDP'))
        return [pscustomobject]@{ Name=$Spec.Name; Readable=$true; Ready=([string]$rule.Enabled -eq 'True' -and [string]$rule.Profile -eq 'Private' -and
            [string]$rule.Direction -eq 'Inbound' -and [string]$rule.Action -eq 'Allow' -and $protocolOk -and
            (@($port.LocalPort) -join ',') -eq $Spec.Port -and [string]$app.Program -eq $Spec.Program -and
            (@($address.RemoteAddress) -join ',') -eq 'LocalSubnet'); Reason='Exact rule properties checked' }
    } catch {
        $missing=($_.CategoryInfo.Category -eq [Management.Automation.ErrorCategory]::ObjectNotFound)
        return [pscustomobject]@{ Name=$Spec.Name; Readable=$missing; Ready=$false;
            Reason=$(if($missing){'Rule not found'}else{'Firewall inspection unavailable; existing rule was not evaluated'}) }
    }
}
function Get-AirPlayBroadBonjourRules {
    $bonjour=(Get-AirPlayPaths).Bonjour
    $rules=@(Get-NetFirewallApplicationFilter -Program $bonjour -ErrorAction Stop | Get-NetFirewallRule -ErrorAction Stop |
        Where-Object { [string]$_.Direction -eq 'Inbound' -and [string]$_.Action -eq 'Allow' -and [string]$_.Enabled -eq 'True' })
    foreach ($rule in $rules) {
        $port=$rule | Get-NetFirewallPortFilter; $address=$rule | Get-NetFirewallAddressFilter
        if ([string]$rule.Profile -ne 'Private' -or (@($address.RemoteAddress) -join ',') -ne 'LocalSubnet' -or
            (@($port.LocalPort) -join ',') -ne '5353' -or [string]$port.Protocol -notin @('UDP','17')) { $rule }
    }
}
function Invoke-AirPlayNative {
    param([string]$Executable,[string[]]$Arguments,[int]$TimeoutSeconds=30,[hashtable]$Environment=@{})
    $start=[Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.Encoding]::UTF8; $start.StandardErrorEncoding=[Text.Encoding]::UTF8
    $start.WorkingDirectory=$AirPlayRoot
    if ($start.PSObject.Properties['ArgumentList']) { foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) } }
    else {
        $start.Arguments=($Arguments | ForEach-Object { '"'+[regex]::Replace([regex]::Replace($_,'(\\*)"','$1$1\"'),'(\\+)$','$1$1')+'"' }) -join ' '
    }
    foreach ($key in $Environment.Keys) { $start.EnvironmentVariables[$key]=[string]$Environment[$key] }
    $process=[Diagnostics.Process]::Start($start)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        $clock=[Diagnostics.Stopwatch]::StartNew(); $windowSeen=$false
        while (-not $process.WaitForExit(100)) {
            $process.Refresh(); if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $windowSeen=$true }
            if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
                if ($PSVersionTable.PSVersion.Major -ge 7) { $process.Kill($true) }
                else { Start-Process "$env:SystemRoot\System32\taskkill.exe" -ArgumentList @('/PID',[string]$process.Id,'/T','/F') -WindowStyle Hidden -Wait | Out-Null }
                $process.WaitForExit(); throw 'Owned diagnostic process exceeded its deadline and was stopped.'
            }
        }
        return [pscustomobject]@{ExitCode=$process.ExitCode; Stdout=$stdout.GetAwaiter().GetResult(); Stderr=$stderr.GetAwaiter().GetResult(); WindowSeen=$windowSeen}
    } finally { $process.Dispose() }
}
function Get-AirPlaySourceFingerprint {
    $files=@(Get-ChildItem (Join-Path $AirPlayRoot 'src'),(Join-Path $AirPlayRoot 'tests') -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs','.csproj','.xaml') })
    $files+=@(Get-ChildItem (Join-Path $AirPlayRoot 'tools') -Filter '*.ps1' -File)
    if (Test-Path (Join-Path $AirPlayRoot 'profiles')) { $files+=@(Get-ChildItem (Join-Path $AirPlayRoot 'profiles') -Filter '*.json' -File) }
    $files+=@(Get-Item (Join-Path $AirPlayRoot 'Directory.Build.props'),(Join-Path $AirPlayRoot 'iMirror.sln'),(Join-Path $AirPlayRoot 'airplay.json'),(Join-Path $AirPlayRoot 'global.json'))
    # Ordinal ordering is stable across .NET Framework/NLS (PS5) and .NET/ICU (PS7).
    [string[]]$fingerprintNames=@($files | ForEach-Object { $_.FullName })
    [Array]::Sort($fingerprintNames,[StringComparer]::Ordinal)
    $text=($fingerprintNames | ForEach-Object { $_.Substring($AirPlayRoot.Length)+':'+(Get-FileHash -LiteralPath $_).Hash }) -join "`n"
    $hash=[Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','') }
    finally { $hash.Dispose() }
}
