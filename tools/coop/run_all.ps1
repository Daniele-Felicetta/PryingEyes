# Tutte le prove della cooperativa in fila (una riga di esito per prova), con run_coop.ps1 e la build di prova.
#   .\tools\coop\run_all.ps1                  tutte (circa 12 minuti)
#   .\tools\coop\run_all.ps1 -Only a,c        solo quelle che iniziano con a e c: a griglia, b pianta ad ali, c entrata tardiva,
#                                             d uscita pulita, e processo ucciso, f esce l'ospite, g tre giocatori, h quattro giocatori
# Esce con 0 solo se tutte sono superate.
param([string[]]$Only = @(), [int]$Seed = 2961, [int]$TimeoutSec = 420, [string]$LogDir = (Join-Path $env:TEMP 'pryingeyes-coop'))
$run = Join-Path $PSScriptRoot 'run_coop.ps1'
$S = "$Seed"
$tests = [ordered]@{
    a_griglia = @{ Clients = 1; H = @('-cooptest', '-seed', $S); C = @('-cooptest') }
    b_ali     = @{ Clients = 1; H = @('-cooptest', '-oldplan', '-seed', '1000', '-floor', '2'); C = @('-cooptest', '-oldplan') }
    c_tardi   = @{ Clients = 1; H = @('-cooptest', '-cooplate', '-seed', $S); C = @('-cooptest', '-cooplate') }
    d_esce    = @{ Clients = 1; H = @('-cooptest', '-coopleave', '-seed', $S); C = @('-cooptest', '-coopleave') }
    e_ucciso  = @{ Clients = 1; H = @('-cooptest', '-coopcrash', '-seed', $S); C = @('-cooptest', '-coopcrash') }
    f_ospite  = @{ Clients = 1; H = @('-cooptest', '-coophostleave', '-seed', $S); C = @('-cooptest', '-coophostleave') }
    g_tre     = @{ Clients = 2; H = @('-cooptest', '-coopplayers', '3', '-seed', $S); C = @('-cooptest', '-coopplayers', '3') }
    h_quattro = @{ Clients = 3; H = @('-cooptest', '-coopplayers', '4', '-seed', $S); C = @('-cooptest', '-coopplayers', '4') }
}
$bad = 0
foreach ($k in $tests.Keys) {
    if ($Only.Count -and -not ($Only -contains $k.Substring(0, 1))) { continue }
    $t = $tests[$k]
    $out = & $run -LogDir $LogDir -Name $k -Clients $t.Clients -HostArgs $t.H -ClientArgs $t.C -TimeoutSec $TimeoutSec -Pattern 'RISULTATO|FALLITO|Exception|error CS'
    $code = $LASTEXITCODE
    if ($code -ne 0) { $bad++ }
    $res = @($out | Where-Object { $_ -match 'RISULTATO|FALLITO|tempo scaduto|uscite|Exception|error CS' })
    "[$k] $(if ($code -eq 0) { 'ok' } else { 'FALLITA' }): " + ($res -join ' | ')
}
if ($bad) { "$bad prove fallite"; exit 1 }
'tutte superate'
