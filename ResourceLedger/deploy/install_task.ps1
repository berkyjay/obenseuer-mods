# Register the scheduled task that sends new saves to the server. Run on the game PC.
# pythonw keeps it from opening a window over the game. Each run ends itself after about 5
# minutes; the 7-minute limit stops one that gets stuck. The trigger is every minute because
# a tick is skipped while a run is active, so the next run starts within a minute of the last.
$script = Join-Path (Split-Path $PSScriptRoot) "export_ledger.py"
$python = (Get-Command pythonw).Source

$action = New-ScheduledTaskAction -Execute $python -Argument "`"$script`" --watch" -WorkingDirectory (Split-Path $script)
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1)
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 7) -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName "Obenseuer Ledger Export" -Action $action -Trigger $trigger -Settings $settings `
    -Principal $principal -Description "Sends new Obenseuer saves to the ledger site." -Force
