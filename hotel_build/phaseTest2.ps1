# Sandbox PIE test: avvia, aspetta, leggi log, ferma
. "$PSScriptRoot\ue_common.ps1"
Ue "EditorToolset.EditorAppToolset" "StartPIE" @{options = @{bSimulate = $false; playMode = "PlayMode_InViewPort"; warmupSeconds = 10 } } | Out-Null
Write-Output "PIE-STARTED"
$log = Ue "EditorToolset.LogsToolset" "GetLogEntries" @{}
$txt = ($log | Out-String)
if ($txt -match 'SANDBOX') { Write-Output "LOG-HIT-SANDBOX" } else { Write-Output "LOG-MISS" }
$txt | Select-String -Pattern 'SANDBOX|Error|Warning' | Select-Object -Last 8
Ue "EditorToolset.EditorAppToolset" "StopPIE" @{} | Out-Null
Write-Output "PIE-STOPPED"
