# UIA probe: find a control of OWN test-app PID by AutomationId, read its properties (-Mode info) and optionally Invoke it (-Mode invoke).
# Accessibility-API path, no key or mouse injection. Never use it on the user's own Pd (only a PID started by the test).
# Usage: powershell.exe -NoProfile -ExecutionPolicy Bypass -File uia_invoke.ps1 -OwnPid <test app PID> -Id <AutomationId> [-Mode info|invoke]
# Output: one JSON line {found, name, enabled, keyboardFocusable, controlType, hasInvoke, invoked, error}
# NOTE: keep this file ASCII only - Windows PowerShell 5.1 reads BOM-less UTF-8 as ANSI and Korean comment bytes can swallow the line break before param().
param([Parameter(Mandatory = $true)][int]$OwnPid, [Parameter(Mandatory = $true)][string]$Id, [ValidateSet('info', 'invoke')][string]$Mode = 'info')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$pidCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$OwnPid)
$idCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, [string]$Id)
$cond = [System.Windows.Automation.AndCondition]::new($pidCond, $idCond)
$el = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
$out = [ordered]@{ found = ($null -ne $el); pid = $OwnPid; id = $Id; mode = $Mode }
if ($el) {
    $out.name = $el.Current.Name
    $out.enabled = $el.Current.IsEnabled
    $out.keyboardFocusable = $el.Current.IsKeyboardFocusable
    $out.controlType = $el.Current.ControlType.ProgrammaticName
    $pat = $null
    $out.hasInvoke = $el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)
    if ($Mode -eq 'invoke') {
        $out.invoked = $false
        try { $pat.Invoke(); $out.invoked = $true } catch { $out.error = $_.Exception.GetType().Name + ': ' + $_.Exception.Message }
    }
}
$out | ConvertTo-Json -Compress
