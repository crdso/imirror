param([switch]$NoExit)
. "$PSScriptRoot\airplay-setup-common.ps1"
. "$PSScriptRoot\common.ps1"
$checks=[Collections.Generic.List[object]]::new()
function Add-DoctorCheck([string]$Name,[bool]$Ready,[string]$Detail) {
    $checks.Add([pscustomobject]@{Name=$Name;Ready=$Ready;Detail=$Detail})
    Write-AirPlaySetupLog "$(if($Ready){'[OK]'}else{'[FAIL]'}) $Name — $Detail"
}
Write-AirPlaySetupLog 'iMirror AirPlay Doctor — FINAL DIAGNOSTIC START'
$paths=Get-AirPlayPaths
$environment=@{PATH=$paths.GstBin+';'+$env:PATH;GST_REGISTRY_1_0=(Join-Path $AirPlayRoot '.cache\readiness\gst-registry.bin');GST_DEBUG_NO_COLOR='1'}
$windows=[Environment]::OSVersion.Version
Add-DoctorCheck 'Windows suportado' ([Environment]::Is64BitOperatingSystem -and $windows.Major -ge 10 -and $windows.Build -ge 19041) "Windows x64 technical target, build $($windows.Build)"
try {
    $info=Get-AirPlayInstallerInfo
    Add-DoctorCheck 'Bonjour installer signature' ($info.Status -eq 'Valid' -and $info.Publisher -match 'O=Apple Inc\.') "Status=$($info.Status); Publisher=$($info.Publisher); MSI version=$($info.Version)"
} catch { Add-DoctorCheck 'Bonjour installer signature' $false 'Installer cannot be inspected; do not install it.' }
foreach ($probe in @(@{Name='UxPlay 1.73.7';Path=$paths.UxPlay;Args=@('-v');Pattern='UxPlay\s+(?:version\s+)?1\.73\.7(?:\D|$)'},
    @{Name='GStreamer 1.28.7 x64';Path=(Join-Path $paths.GstBin 'gst-inspect-1.0.exe');Args=@('--version');Pattern='GStreamer 1\.28\.7(?:\D|$)'},
    @{Name='gst-launch 1.28.7 x64';Path=(Join-Path $paths.GstBin 'gst-launch-1.0.exe');Args=@('--version');Pattern='GStreamer 1\.28\.7(?:\D|$)'})) {
    try {
        $output=Invoke-AirPlayNative -Executable $probe.Path -Arguments $probe.Args -Environment $environment
        Add-DoctorCheck $probe.Name ($output.ExitCode -eq 0 -and $output.Stdout -match $probe.Pattern -and (Test-AirPlayX64Pe $probe.Path)) 'Version command and PE x64 verified'
    } catch { Add-DoctorCheck $probe.Name $false 'Executable missing, failed to load or timed out.' }
}
foreach ($plugin in @('avdec_h264','d3d11videosink','rtph264depay','h264parse','decodebin','appsrc','videoconvert')) {
    try {
        $output=Invoke-AirPlayNative -Executable (Join-Path $paths.GstBin 'gst-inspect-1.0.exe') -Arguments @($plugin) -Environment $environment
        Add-DoctorCheck $plugin ($output.ExitCode -eq 0 -and $output.Stdout -match 'Factory Details') 'gst-inspect verified'
    } catch { Add-DoctorCheck $plugin $false 'Plugin unavailable or failed to load.' }
}
$service=Get-Service -Name 'Bonjour Service' -ErrorAction SilentlyContinue
$installed=($null -ne $service -and (Test-AirPlayX64Pe $paths.Bonjour))
Add-DoctorCheck 'Bonjour instalado' $installed $(if($installed){"x64 version=$((Get-Item $paths.Bonjour).VersionInfo.FileVersion)"}else{'Missing; administrator setup required. Prepared MSI version 3.1.0.1.'})
$running=($service -and [string]$service.Status -eq 'Running')
Add-DoctorCheck 'Bonjour Service rodando' ([bool]$running) $(if($service){[string]$service.Status}else{'Not installed'})
Add-DoctorCheck 'Bonjour Service automatic' ([bool]($service -and [string]$service.StartType -eq 'Automatic')) $(if($service){[string]$service.StartType}else{'Not installed'})
try {
    foreach ($family in @([Net.Sockets.AddressFamily]::InterNetwork,[Net.Sockets.AddressFamily]::InterNetworkV6)) {
        $socket=[Net.Sockets.Socket]::new($family,[Net.Sockets.SocketType]::Dgram,[Net.Sockets.ProtocolType]::Udp)
        try {
            $socket.SetSocketOption([Net.Sockets.SocketOptionLevel]::Socket,[Net.Sockets.SocketOptionName]::ReuseAddress,$true)
            if ($family -eq [Net.Sockets.AddressFamily]::InterNetworkV6) { $socket.DualMode=$false; $address=[Net.IPAddress]::IPv6Any } else { $address=[Net.IPAddress]::Any }
            $socket.Bind([Net.IPEndPoint]::new($address,5353))
        } finally { $socket.Dispose() }
    }
    $owners=@(Get-NetUDPEndpoint -LocalPort 5353 -ErrorAction SilentlyContinue | ForEach-Object { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName } | Sort-Object -Unique)
    Add-DoctorCheck 'UDP 5353' $true "IPv4/IPv6 shared bind succeeded; existing owners=$($owners -join ', '). Sharing alone is not a conflict."
    Add-DoctorCheck 'Bonjour mDNS socket' ($owners -contains 'mDNSResponder') 'Bonjour must own a UDP5353 endpoint; actual registration checked below.'
} catch { Add-DoctorCheck 'UDP 5353' $false 'Shared bind failed; possible conflicting exclusive listener or IP stack issue.' }
try {
    $private=@(Get-NetConnectionProfile -ErrorAction Stop | Where-Object { [string]$_.NetworkCategory -eq 'Private' })
    Add-DoctorCheck 'Private network profile' ($private.Count -gt 0) 'Rules apply only to trusted Private networks; no SSID/IP is logged.'
} catch { Add-DoctorCheck 'Private network profile' $false 'Cannot inspect network category.' }
$firewallReady=$true
$firewallUnknown=$false; $firewallMissing=$false
foreach ($spec in Get-AirPlayFirewallSpecs) {
    $state=Test-AirPlayFirewallSpec $spec; if (-not $state.Ready) { $firewallReady=$false }
    if (-not $state.Readable) { $firewallUnknown=$true } elseif (-not $state.Ready) { $firewallMissing=$true }
    Add-DoctorCheck "Firewall $($spec.Name)" $state.Ready "$($spec.Protocol) $($spec.Port), Inbound/Private/LocalSubnet; $([IO.Path]::GetFileName($spec.Program)); $($state.Reason)"
}
if ($firewallReady) {
    try { $broad=@(Get-AirPlayBroadBonjourRules); Add-DoctorCheck 'Bonjour firewall scope' ($broad.Count -eq 0) 'No broader inbound allowance for the Bonjour executable.' }
    catch { Add-DoctorCheck 'Bonjour firewall scope' $false 'Cannot inspect vendor rule scope.' }
}
$stampPath=Join-Path $AirPlayRoot '.cache\airplay-validation.json'
try {
    $stamp=Get-Content $stampPath -Raw | ConvertFrom-Json
    $current=($stamp.SourceFingerprint -eq (Get-AirPlaySourceFingerprint))
    foreach ($configuration in @('Debug','Release')) {
        $build=$stamp.$configuration
        Add-DoctorCheck "Build $configuration" ($current -and $build.BuildPassed -and $build.Errors -eq 0 -and $build.Warnings -eq 0) "Build evidence current=$current; errors=$($build.Errors); warnings=$($build.Warnings)"
        Add-DoctorCheck "Tests $configuration" ($current -and $build.TestsPassed -and $build.TestGroups -ge 13) "$($build.TestGroups)/$($build.TestGroups) groups; source fingerprint current=$current"
    }
} catch { Add-DoctorCheck 'Build and test evidence' $false 'Run tools\prepare-airplay.ps1 to build/test and record current evidence.' }
$release=Join-Path $AirPlayRoot 'src\iMirror.App\bin\Release\net10.0-windows\iMirror.dll'
Add-DoctorCheck 'iMirror Release' (Test-Path -LiteralPath $release) 'Release assembly present'
try {
    $rendererEnvironment=$environment.Clone(); $rendererEnvironment.GST_DEBUG='basesink:6'
    $render=Invoke-AirPlayNative -Executable (Join-Path $paths.GstBin 'gst-launch-1.0.exe') -Arguments @('-v','videotestsrc','num-buffers=60','!','video/x-raw,width=640,height=360,framerate=30/1','!','videoconvert','!','d3d11videosink') -Environment $rendererEnvironment -TimeoutSeconds 25
    $rendered=[regex]::Match($render.Stderr,'rendered:\s*(\d+),\s*dropped:\s*(\d+)')
    $rendererReady=($render.ExitCode -eq 0 -and $render.Stdout -match 'Got EOS' -and $render.WindowSeen -and $rendered.Success -and [int]$rendered.Groups[1].Value -eq 60)
    Add-DoctorCheck 'Local Direct3D11 renderer' $rendererReady "exit=$($render.ExitCode); window opened=$($render.WindowSeen); rendered=$($rendered.Groups[1].Value); dropped=$($rendered.Groups[2].Value); SYNTHETIC ONLY, not AirPlay."
} catch { Add-DoctorCheck 'Local Direct3D11 renderer' $false 'Synthetic renderer did not complete; no AirPlay claim.' }
$nativeResult=$null
try {
    if (@(Get-Process -Name uxplay -ErrorAction SilentlyContinue).Count) { throw 'Receiver already active; stop it in iMirror before running the controlled test.' }
    $resultFile=Join-Path $AirPlayRoot ('.cache\readiness\native-'+[DateTime]::UtcNow.Ticks+'.json')
    $harness=Join-Path $AirPlayRoot 'tests\iMirror.Phase1.Tests\bin\Release\net10.0-windows\iMirror.Phase1.Tests.dll'
    $native=Invoke-AirPlayNative -Executable $DotNetPath -Arguments @($harness,'--airplay-readiness',$AirPlayRoot,$resultFile) -TimeoutSeconds 240
    if (-not (Test-Path $resultFile)) { throw 'Readiness harness produced no result.' }
    $nativeResult=Get-Content $resultFile -Raw | ConvertFrom-Json
    if ($nativeResult.ArgumentProbe) {
        Write-AirPlaySetupLog "Native argument probe: exit=$($nativeResult.ArgumentProbe.ExitCode); timeout=$($nativeResult.ArgumentProbe.TimedOut); DNS-SD error=$($nativeResult.ArgumentProbe.DnsSdErrorCode). Exit 0 alone is not receiver readiness."
    }
    Add-DoctorCheck 'UxPlay arguments and receiver name' ([bool]$nativeResult.ArgumentsAccepted -and $nativeResult.ReceiverName -eq 'iMirror - Windows') 'Actual native startup/argument parsing; configured name=iMirror - Windows.'
    Add-DoctorCheck 'UxPlay process test' ($native.ExitCode -eq 0 -and $nativeResult.Result -eq 'PASS' -and $nativeResult.CyclesPassed -eq 5) "$($nativeResult.CyclesPassed)/5 real WPF Start/Wait/Stop cycles; $($nativeResult.Result); $($nativeResult.Reason)"
    Add-DoctorCheck 'mDNS local registration/browse' ($nativeResult.Result -eq 'PASS') $(if($nativeResult.Result -eq 'PASS'){'Local Bonjour browser observed the exact receiver name in all cycles; physical iPhone discovery remains pending.'}else{'Local registration/browse not approved: real cycles were blocked or failed. Physical iPhone discovery remains pending.'})
} catch { Add-DoctorCheck 'UxPlay process test' $false $_.Exception.Message }
$failed=@($checks | Where-Object { -not $_.Ready })
$adminRequired=(-not $installed -or -not $running -or $firewallMissing -or -not ($service -and [string]$service.StartType -eq 'Automatic'))
$result=if($failed.Count -eq 0){'READY FOR IPHONE TEST'}elseif($adminRequired){'NOT READY: ADMINISTRATIVE SETUP REQUIRED'}elseif($firewallUnknown){'NOT READY: FIREWALL INSPECTION UNAVAILABLE'}else{'NOT READY'}
Write-AirPlaySetupLog "RESULT: $result"
Write-AirPlaySetupLog 'PHASE 2: PENDING PHYSICAL IPHONE VALIDATION'
Write-AirPlaySetupLog 'Physical discovery/video/rotation/reconnection: NOT TESTED'
Write-AirPlaySetupLog 'FINAL DIAGNOSTIC END'
[pscustomobject]@{Timestamp=(Get-Date -Format o);Result=$result;AdministrativeActionRequired=$adminRequired;FirewallInspectionUnavailable=$firewallUnknown;Checks=$checks;Native=$nativeResult;
    Phase2='PENDING PHYSICAL IPHONE VALIDATION'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $AirPlayRoot 'logs\airplay-readiness.json') -Encoding UTF8
$code=if($failed.Count -eq 0){0}else{1}
if ($NoExit) { return $code }
exit $code
