param([switch]$NoLaunch)
. "$PSScriptRoot\airplay-setup-common.ps1"
. "$PSScriptRoot\common.ps1"
$powerShell=if($PSVersionTable.PSVersion.Major -ge 7){Join-Path $PSHOME 'pwsh.exe'}else{Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'}
$paths=Get-AirPlayPaths
Write-AirPlaySetupLog "PREPARATION START; administrator=$(Test-AirPlayAdministrator)"
try {
    $info=Get-AirPlayInstallerInfo
    Write-AirPlaySetupLog "Bonjour installer Status=$($info.Status); Publisher=$($info.Publisher); version=$($info.Version)"
    if ($info.Status -ne 'Valid' -or $info.Publisher -notmatch 'O=Apple Inc\.') { throw 'Invalid installer signature; preparation stopped.' }
    $metadataPath=Join-Path $AirPlayRoot '.cache\readiness\launcher.json'
    if (Test-Path $metadataPath) {
        $metadata=Get-Content $metadataPath -Raw | ConvertFrom-Json
        $owned=Get-Process -Id $metadata.Id -ErrorAction SilentlyContinue
        if (Test-AirPlayOwnedLauncher $owned $metadata) {
            if (-not $owned.CloseMainWindow() -or -not $owned.WaitForExit(20000)) { throw 'Previously launched iMirror did not close; no forced termination attempted.' }
            Write-AirPlaySetupLog 'Previously launched iMirror closed gracefully for validation/build.'
        }
    }
    foreach ($probe in @(@{Exe=$paths.UxPlay;Args=@('-v');Pattern='UxPlay\s+(?:version\s+)?1\.73\.7(?:\D|$)'},
        @{Exe=(Join-Path $paths.GstBin 'gst-inspect-1.0.exe');Args=@('--version');Pattern='GStreamer 1\.28\.7(?:\D|$)'})) {
        $output=Invoke-AirPlayNative -Executable $probe.Exe -Arguments $probe.Args -Environment @{PATH=$paths.GstBin+';'+$env:PATH}
        if ($output.ExitCode -ne 0 -or $output.Stdout -notmatch $probe.Pattern -or -not (Test-AirPlayX64Pe $probe.Exe)) { throw 'Native dependency/version check failed; no reinstall attempted.' }
    }
    $service=Get-Service -Name 'Bonjour Service' -ErrorAction SilentlyContinue
    $rules=@(Get-AirPlayFirewallSpecs | ForEach-Object { Test-AirPlayFirewallSpec $_ })
    $unreadableRules=@($rules | Where-Object { -not $_.Readable }).Count -gt 0
    if ($unreadableRules) { Write-AirPlaySetupLog 'Firewall inspection unavailable with current Windows permissions; existing rules were not evaluated. No firewall mutation will be attempted.' }
    $adminNeeded=(-not $service -or [string]$service.Status -ne 'Running' -or [string]$service.StartType -ne 'Automatic' -or @($rules | Where-Object { $_.Readable -and -not $_.Ready }).Count -gt 0)
    if (-not $adminNeeded -and -not $unreadableRules) { $adminNeeded=(@(Get-AirPlayBroadBonjourRules).Count -gt 0) }
    if ($adminNeeded) {
        if ((Test-AirPlayAdministrator) -and -not $unreadableRules) {
            $admin=Invoke-AirPlayNative -Executable $powerShell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',"$PSScriptRoot\finish-airplay-setup-admin.ps1") -TimeoutSeconds 180
            if ($admin.ExitCode -ne 0) { Write-AirPlaySetupLog 'Administrative setup failed; continuing independent build/tests. No retry.' }
        } else { Write-AirPlaySetupLog 'ADMINISTRATIVE ACTION REQUIRED: tools\finish-airplay-setup-admin.ps1. No installation/UAC retry attempted.' }
    } else { Write-AirPlaySetupLog 'Bonjour/service prepared; no reinstall or rule mutation. Firewall confirmation depends on inspection access.' }
    $evidence=@{SourceFingerprint=(Get-AirPlaySourceFingerprint);Timestamp=(Get-Date -Format o)}
    foreach ($configuration in @('Debug','Release')) {
        $output=Invoke-AirPlayNative -Executable $powerShell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',"$PSScriptRoot\test.ps1",'-Configuration',$configuration) -TimeoutSeconds 300
        $text=$output.Stdout+"`n"+$output.Stderr
        $text | Set-Content (Join-Path $AirPlayRoot ".cache\readiness\validation-$configuration.txt") -Encoding UTF8
        $groups=0
        foreach ($match in [regex]::Matches($text,'PASS:\s*(\d+)\s+(?:grupos de [^\r\n]+|Phase 2 groups)')) { $groups += [int]$match.Groups[1].Value }
        $zeroWarnings=($text -match '(?mi)^\s*0\s+(?:Aviso\(s\)|Warning\(s\))')
        $passed=($output.ExitCode -eq 0 -and $zeroWarnings -and $groups -ge 13)
        $evidence[$configuration]=@{BuildPassed=$passed;Warnings=$(if($zeroWarnings){0}else{-1});Errors=$(if($output.ExitCode -eq 0){0}else{-1});TestsPassed=$passed;TestGroups=$groups}
        Write-AirPlaySetupLog "Build ${configuration}: passed=$passed; warnings=$(if($zeroWarnings){0}else{'unknown/nonzero'}); test groups=$groups/13"
        if (-not $passed) { throw "Build/tests $configuration failed; see .cache\readiness\validation-$configuration.txt. No tests removed." }
    }
    $evidence | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $AirPlayRoot '.cache\airplay-validation.json') -Encoding UTF8
    $shell=New-Object -ComObject WScript.Shell
    $shortcut=$shell.CreateShortcut((Join-Path $AirPlayRoot 'iMirror.lnk'))
    $shortcut.TargetPath=$powerShell
    $shortcut.Arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$PSScriptRoot+'\start-imirror.ps1"'
    $shortcut.WorkingDirectory=$AirPlayRoot; $shortcut.WindowStyle=7
    $shortcut.IconLocation=Join-Path $AirPlayRoot 'src\iMirror.App\bin\Release\net10.0-windows\iMirror.exe'
    $shortcut.Save()
    Write-AirPlaySetupLog 'iMirror.lnk launcher prepared; no manual PowerShell required for normal app startup.'
    $doctor=Invoke-AirPlayNative -Executable $powerShell -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',"$PSScriptRoot\airplay-doctor.ps1") -TimeoutSeconds 300
    Write-Host $doctor.Stdout
    if ($doctor.ExitCode -ne 0) { Write-AirPlaySetupLog 'PREPARATION PARTIAL: Doctor reported missing requirements; app not auto-launched.'; exit 1 }
    if (-not $NoLaunch) { & "$PSScriptRoot\start-imirror.ps1" }
    Write-AirPlaySetupLog 'READY FOR IPHONE TEST; PENDING PHYSICAL IPHONE VALIDATION'
    exit 0
} catch {
    Write-AirPlaySetupLog ('PREPARATION FAILED: '+$_.Exception.Message)
    exit 1
}
