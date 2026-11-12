param(
    [ValidateRange(1,4)][int]$HostSeats = 2,
    [ValidateRange(1,4)][int]$ClientSeats = 2,
    [ValidateRange(1024,65535)][int]$Port = 17807
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $root 'tools/unity-validation'
$player = Join-Path $output 'player/PryingEyesMultiplayer.exe'
if (!(Test-Path -LiteralPath $player)) { throw 'Build the isolated multiplayer player first.' }
$label = "network-$HostSeats-$ClientSeats"
$processes = @()
try {
    foreach ($role in @('host','client')) {
        $seats = if ($role -eq 'host') { $HostSeats } else { $ClientSeats }
        $report = Join-Path $output "$label-$role.json"
        if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
        $log = Join-Path $output "$label-$role.log"
        $arguments = @('-batchmode','-nographics','-job-worker-count','2',
            '-logFile',('"'+$log+'"'),'-mpSmokeNetwork',$role,
            '-mpSeats',$seats,'-mpTotalSeats',($HostSeats+$ClientSeats),
            '-mpPort',$Port,'-mpReport',('"'+$report+'"'))
        $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $processes += $process
        Write-Output "$role PID $($process.Id), $seats local seats"
        if ($role -eq 'host') { Start-Sleep -Seconds 2 }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(210)
    while (@($processes | Where-Object { !$_.HasExited }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Network smoke processes timed out.' }
        Start-Sleep -Seconds 1
    }
    foreach ($role in @('host','client')) {
        $report = Join-Path $output "$label-$role.json"
        if (!(Test-Path -LiteralPath $report)) { throw "Missing $role report; inspect $label-$role.log" }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        $result | ConvertTo-Json -Compress | Write-Output
        if (!$result.passed) { throw "$role failed: $($result.error)" }
    }
    foreach ($process in $processes) {
        if ($process.ExitCode -ne 0) { throw "Player $($process.Id) exited with $($process.ExitCode)" }
    }
} finally {
    # Only handles created by this invocation, never an existing Unity/player process.
    foreach ($process in $processes) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    }
}
