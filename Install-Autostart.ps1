param(
    [string]$UserId = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$taskName = "OSD Panel - $UserId"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -UserId "{1}"' -f $PSCommandPath, $UserId
    if ($Remove) { $arguments += ' -Remove' }
    $installer = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($installer.ExitCode -ne 0) { throw "Autostart setup failed (exit code $($installer.ExitCode))." }
    return
}
try {
    if ($Remove) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Output "Autostart removed: $taskName"
    } else {
        $exe = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'release\OsdPanel.exe')).Path
        $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path -Parent $exe)
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserId
        $trigger.Delay = 'PT10S'
        $principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType Interactive -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -StartWhenAvailable
        $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($existing -and ($existing.Actions.Execute -ne $exe -or $existing.Principal.UserId -ne $UserId)) {
            throw "An unrelated task already uses the name '$taskName'."
        }
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'OSD Panel: elevated interactive startup at user logon.' -Force | Out-Null
        $installed = Get-ScheduledTask -TaskName $taskName
        if ($installed.Principal.RunLevel -ne 'Highest' -or $installed.Principal.LogonType -ne 'Interactive') { throw 'Task verification failed.' }
        $installed | Select-Object TaskName, @{n='State';e={$_.State.ToString()}}, @{n='RunLevel';e={$_.Principal.RunLevel.ToString()}}, @{n='LogonType';e={$_.Principal.LogonType.ToString()}}, @{n='Executable';e={$_.Actions.Execute}} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification\autostart-status.json') -Encoding UTF8
        Write-Output "Elevated autostart installed: $taskName"
    }
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification\autostart-error.txt') -Encoding UTF8
    throw
}
