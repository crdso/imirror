[CmdletBinding(SupportsShouldProcess=$true)]
param([switch]$Apply)
. "$PSScriptRoot\airplay-setup-common.ps1"
$specs=Get-AirPlayFirewallSpecs
foreach ($spec in $specs) { [pscustomobject]@{Name=$spec.Name;Program=$spec.Program;Protocol=$spec.Protocol;LocalPort=$spec.Port;Profile='Private';RemoteAddress='LocalSubnet'} }
if (-not $Apply) { Write-Host 'Preview only. Apply explicitly with -Apply as administrator.'; return }
if (-not (Test-AirPlayAdministrator)) { throw 'Administrator required for firewall. Use tools\finish-airplay-setup-admin.ps1.' }
foreach ($spec in $specs) { if (-not (Test-Path -LiteralPath $spec.Program)) { throw "Firewall executable missing: $($spec.Name)." } }
foreach ($spec in $specs) {
    $state=Test-AirPlayFirewallSpec $spec
    if ($state.Ready) { Write-AirPlaySetupLog "Firewall $($spec.Name): already correct"; continue }
    if ($PSCmdlet.ShouldProcess($spec.Name,'Ensure Inbound Allow Private LocalSubnet exact executable and ports')) {
        $parameters=@{Name=$spec.Name;Direction='Inbound';Action='Allow';Enabled='True';Program=$spec.Program;Protocol=$spec.Protocol;
            LocalPort=$spec.Port;Profile='Private';RemoteAddress='LocalSubnet';EdgeTraversalPolicy='Block';PolicyStore='PersistentStore'}
        if (Get-NetFirewallRule -Name $spec.Name -PolicyStore PersistentStore -ErrorAction SilentlyContinue) { Set-NetFirewallRule @parameters | Out-Null }
        else { New-NetFirewallRule @parameters -DisplayName $spec.Name | Out-Null }
    }
}
# The Apple installer may create broader inbound Bonjour allowances; restrict only that executable.
$broader=@(Get-AirPlayBroadBonjourRules)
foreach ($rule in $broader) {
    if ($PSCmdlet.ShouldProcess($rule.Name,'Restrict Bonjour vendor allowance to UDP5353 Private LocalSubnet')) {
        Set-NetFirewallRule -Name $rule.Name -PolicyStore PersistentStore -Profile Private -Protocol UDP -LocalPort 5353 -RemoteAddress LocalSubnet -EdgeTraversalPolicy Block | Out-Null
    }
}
if ($WhatIfPreference) { return }
foreach ($spec in $specs) {
    if (-not (Test-AirPlayFirewallSpec $spec).Ready) { throw "Firewall verification failed: $($spec.Name)." }
    Write-AirPlaySetupLog "Firewall verified $($spec.Name): $($spec.Protocol) $($spec.Port), Private/LocalSubnet, executable-specific"
}
if (@(Get-AirPlayBroadBonjourRules).Count) { throw 'Broader Bonjour inbound allowances remain (possibly controlled by policy).' }
