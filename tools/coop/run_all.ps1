# Tutte le prove della cooperativa in fila (una riga di esito per prova), con run_coop.ps1 e la build di prova.
#   .\tools\coop\run_all.ps1                  tutte (circa 12 minuti)
#   .\tools\coop\run_all.ps1 -Only a,c        solo quelle che iniziano con a e c: a griglia, b pianta ad ali, c entrata tardiva,
#                                             d uscita pulita, e processo ucciso, f esce l'ospite, g tre giocatori, h quattro giocatori,
#                                             i aspetti e livelli dei personaggi (due processi con lo stesso aspetto chiesto),
#                                             j il deserto in cooperativa (0.9.1: nemici per vicinanza, luoghi attorno al compagno, ora e boss),
#                                             k il deserto e il compagno lontano esce, l processo ucciso (l'ospite prosegue da solo),
#                                             m la moto del deserto (pe-coop-32): chi guida si vede in sella, con la sua moto, dall'altro processo, nei due versi
#                                             n la gara su pista con un compagno, sotto la pioggia dell'ospite (pe-coop-36), o la seconda gara con il compagno che se ne va, p l'ospite che esce a gara in corso
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
    i_aspetti = @{ Clients = 1; H = @('-cooptest', '-cooplook', 'custode', '-cooplevel', '7', '-seed', $S); C = @('-cooptest', '-cooplook', 'custode', '-cooplevel', '3') }
    # il deserto: l'ospite lo sceglie (come dal menu), l'ospitato non ha -deserto e parte dove gioca lui
    j_deserto = @{ Clients = 1; H = @('-cooptest', '-coopdeserto', '-deserto', '-catacombe', '-seed', $S); C = @('-cooptest', '-coopdeserto', '-catacombe') }
    # il deserto, e il compagno lontano se ne va (chiusura normale, processo ucciso): l'ospite prosegue da solo
    k_deserto_esce   = @{ Clients = 1; H = @('-cooptest', '-coopdeserto', '-coopleave', '-deserto', '-catacombe', '-seed', $S); C = @('-cooptest', '-coopdeserto', '-coopleave', '-catacombe') }
    l_deserto_ucciso = @{ Clients = 1; H = @('-cooptest', '-coopdeserto', '-coopcrash', '-deserto', '-catacombe', '-seed', $S); C = @('-cooptest', '-coopdeserto', '-coopcrash', '-catacombe') }
    # la moto del deserto: prima l'ospitato guida e l'ospite lo guarda, poi il contrario
    m_moto = @{ Clients = 1; H = @('-cooptest', '-coopdeserto', '-coopmoto', '-deserto', '-catacombe', '-seed', $S); C = @('-cooptest', '-coopdeserto', '-coopmoto', '-catacombe') }
    # la moto su pista (pe-coop-35, dal 22 novembre pe-coop-36 con il tempo nel roster; n e' sotto la pioggia dell'ospite, l'ospitato sceglie sereno): il circuito e' quello del seme 4242 (Autodromo di Montesole, 83 s a giro con il pilota automatico); l'ospite sceglie la modalita' con -pista e da' il via da solo
    # quando l'ospitato ha costruito il circuito (-pistacoopwait 1); n la gara intera, o_pista_ancora un'altra gara e l'ospitato che se ne va, p_pista_ospite l'ospite che esce a gara in corso
    n_pista        = @{ Clients = 1; H = @('-pistacooptest', '-pista', '-pistabot', '-pistalaps', '1', '-pistarivals', '3', '-pistacoopwait', '1', '-seed', '4242', '-pistacoopseed', '4242', '-pistacooplaps', '1', '-pistaaids', '1', '-pistacoopaids', '1', '-pistarain', '1', '-pistacooprain', '1'); C = @('-pistacooptest', '-pistabot', '-pistarivals', '3', '-pistacoopseed', '4242', '-pistacooplaps', '1', '-pistacoopaids', '1', '-pistacooprain', '1') }
    o_pista_ancora = @{ Clients = 1; H = @('-pistacooptest', '-pista', '-pistabot', '-pistalaps', '1', '-pistarivals', '3', '-pistacoopwait', '1', '-seed', '4242', '-pistacoopseed', '4242', '-pistacooplaps', '1', '-pistacoopscenario', 'again'); C = @('-pistacooptest', '-pistabot', '-pistarivals', '3', '-pistacoopseed', '4242', '-pistacooplaps', '1', '-pistacoopscenario', 'again') }
    p_pista_ospite = @{ Clients = 1; H = @('-pistacooptest', '-pista', '-pistabot', '-pistalaps', '1', '-pistarivals', '3', '-pistacoopwait', '1', '-seed', '4242', '-pistacoopseed', '4242', '-pistacooplaps', '1', '-pistacoopscenario', 'hostquits'); C = @('-pistacooptest', '-pistabot', '-pistarivals', '3', '-pistacoopseed', '4242', '-pistacooplaps', '1', '-pistacoopscenario', 'hostquits') }
}
# con 'powershell -File' l'elenco -Only a,c arriva come una stringa sola: la si spezza sulle virgole
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$bad = 0
$ran = 0
foreach ($k in $tests.Keys) {
    if ($Only.Count -and -not ($Only -contains $k.Substring(0, 1))) { continue }
    $t = $tests[$k]
    $ran++
    $out = & $run -LogDir $LogDir -Name $k -Clients $t.Clients -HostArgs $t.H -ClientArgs $t.C -TimeoutSec $TimeoutSec -Pattern 'RISULTATO|FALLITO|Exception|error CS'
    $code = $LASTEXITCODE
    if ($code -ne 0) { $bad++ }
    $res = @($out | Where-Object { $_ -match 'RISULTATO|FALLITO|tempo scaduto|uscite|Exception|error CS' })
    "[$k] $(if ($code -eq 0) { 'ok' } else { 'FALLITA' }): " + ($res -join ' | ')
}
if ($ran -eq 0) { 'nessuna prova selezionata (controllare -Only)'; exit 2 }
if ($bad) { "$bad prove fallite"; exit 1 }
'tutte superate'
