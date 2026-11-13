# Cooperativa online (fino a 4 giocatori)

Aggiornato il 12 novembre 2026, ramo `multiplayer`. La cooperativa e' dentro il gioco vero (La Torre): ogni giocatore
gira con la propria copia, uno **ospita** e gli altri si collegano al suo indirizzo. Rete: FishNet 4.7.3 (trasporto
Tugboat, UDP, porta **7807**), pin al commit `73f30cf2` nel manifest.

## Come si gioca

1. Chi ospita: menu iniziale, **Cooperativa**, scrive il proprio nome e preme **Ospita una partita**. La sua partita
   parte subito; gli altri entrano quando vogliono (anche a piano iniziato, con un round in corso).
2. Gli altri: **Cooperativa**, nome, indirizzo dell'ospite, **Unisciti all'indirizzo**. Il menu si toglie da solo
   appena l'ospite li saluta. Se la versione del gioco non e' la stessa, l'albergo e' pieno o l'ospite non risponde, il
   menu scrive "Connessione rifiutata o senza risposta" (il motivo non viaggia in rete: l'ospite ne scrive una riga nel log).
3. Nella stessa rete basta l'indirizzo locale dell'ospite: il gioco lo scrive in alto appena si ospita e nel menu di pausa
   (Esc), oppure lo mostra `ipconfig`. La prima volta Windows chiede se
   consentire il gioco nella rete: l'ospite deve dire di si. Per giocare da Internet l'ospite deve avere la porta UDP 7807
   raggiungibile (inoltro sul router); non c'e' ancora nessun relay, quindi dietro CGNAT non e' raggiungibile. In quel caso
   funziona una VPN tra amici (Tailscale, ZeroTier, Radmin VPN: ognuno riceve un indirizzo privato, e si scrive quello).
   Il gioco non tocca router e firewall.
4. Nomi e ultimo indirizzo restano nelle preferenze del gioco (nome vuoto: "Giocatore N"). Non si usa mai il nome
   dell'utente di Windows.

**Cosa si condivide.** Un solo albergo per tutti: i nemici (li simula l'ospite, gli altri ne vedono i pupazzi),
le candele accese, le serrande e i round, l'uscita e la discesa (si scende tutti insieme), le gocce a terra (un
potenziamento raccolto vale per tutti). Ognuno ha **per conto suo** punti, armi, perk, libri, scrigno e altare, muri
d'armi, pile e torcia. Una candela da' i suoi 60 punti (e munizioni, e il bonus Fantasma) a chi l'accende; il premio di
un round va a tutti; un'uccisione paga solo chi ha sparato. Un round si annulla solo se muoiono tutti.
I compagni si vedono come pupazzi con la loro torcia e il nome sopra la testa; i nemici cacciano il piu' vicino.
In pausa il gioco non si ferma (gli altri continuano): la pausa blocca solo i tuoi comandi.

## Come funziona

- **Ospite autorevole.** Lo slot 0 e' l'ospite (server solo di rete: non e' una connessione verso se stesso), gli altri
  occupano gli slot 1-3. L'ospite fa girare tutto il mondo (`Coop.Sim`), compresa l'IA completa dei nemici; l'ospitato non
  ha `NavMeshAgent` ne' IA: i suoi nemici sono pupazzi (`Enemy.puppet`) mossi dallo stato che arriva a 10 Hz
  (non affidabile) piu' gli eventi (attacco, lancio, grido, morte) in affidabile.
- **Piani uguali per tutti, senza spedirli.** Ogni macchina costruisce il piano da se' con
  `PlanGen.Generate(seme + n * 1013, n)`: l'ospite manda solo seme, numero di piano ed **epoca** (un contatore che cresce a ogni
  cambio di piano). Ogni messaggio del mondo porta l'epoca: se arriva troppo presto (piano non ancora costruito) si tiene da
  parte, se e' vecchio si butta. L'ospitato risponde `FloorReady` quando ha finito di costruire e solo da li' in poi riceve lo
  stato del mondo. `FloorDirector.FloorSignature()` (firma di stanze, candele e luci) finisce nel log a ogni piano e la prova
  `-cooptest` controlla che sia uguale su tutte le macchine; il gioco non la confronta da solo.
- **Colpi.** Il client spara e vede il suo effetto; manda all'ospite un `HitClaim` (nemico, danno, testa, fonte). L'ospite
  applica il danno, paga i punti al richiedente (`AwardPoints`) e gli accredita l'uccisione (`KillCredit`, che fa il suo
  `GameManager.CreditKill`: conteggio, cura, perk). Per ora l'ospite si fida dei colpi dichiarati (nessun controllo di
  distanza o cadenza).
- **Bersagli multipli.** I nemici scelgono tra tutti i giocatori vivi dello stesso piano (`ITarget`: `LocalTarget` per
  chi gioca qui, `RemotePlayer` per i compagni; `Targets.Nearest/AnyAlive`). Se un compagno se ne va i nemici passano agli
  altri bersagli (provato: l'ospite resta solo e la caccia e il round continuano).
- **Entrate e uscite.** Chi arriva a meta' riceve tutto lo stato del piano (nemici, candele, uscita, round in corso con le
  serrande, gocce). Chi esce o cade (15 s senza pacchetti, `CoopProtocol.TimeoutSeconds`: di serie Tugboat aspetta 30
  minuti) viene tolto; se se ne va l'ospite, gli altri restano nel piano da soli e proseguono con il mondo che hanno
  (i pupazzi spariscono e il piano si ripopola).
- **In rete anche in secondo piano.** `Application.runInBackground` e' spento nel progetto; la cooperativa lo accende per tutta
  la partita (un Alt-Tab non deve bloccare gli altri).
- **Senza FishNet.** Il codice che nomina FishNet sta in `FishNetCoopNet.cs` dietro `PRYINGEYES_FISHNET` (solo Standalone):
  nella build web `Coop.Available` e' falso e ogni chiamata della facciata non fa niente.

## Dove sta il codice (`Assets/_PryingEyes/Scripts/Coop/`)

| File | Cosa fa |
| --- | --- |
| `Coop.cs` | Facciata statica: `Role` (Off/Host/Client), `Sim`, `Slot`, `Game`, avvio da menu o da riga di comando, `KeepRunning` |
| `CoopNet.cs`, `FishNetCoopNet.cs` | Trasporto astratto e realizzazione FishNet: ammissione, slot, un solo `CoopPacket` (tipo + byte) con `Broadcast` |
| `CoopWire.cs` | Tipi di messaggio (`CoopKind`), `CoopProtocol` (revisione, porta, tempi), `NetWriter`/`NetReader` (il lettore non lancia mai) |
| `CoopGame.cs` | Giocatori, piano ed epoca, saluto, stato di ciascuno, nomi |
| `CoopGame.World.cs` | Il mondo condiviso: nemici, colpi, rumore, candele, uscita, round, gocce, annunci |
| `Enemy.Net.cs` | Pupazzi dei nemici e stato di rete |
| `Targets.cs`, `RemotePlayer.cs` | Bersagli multipli e compagni (pupazzo con torcia, nome, posa interpolata) |
| `CoopTest.cs` | Prova automatica a piu' processi (solo con `-cooptest`) |

L'ammissione (revisione del protocollo, capienza, tempo massimo) la fa `FishNetSeatAuthenticator`, scritto da ChatGPT nella
sua bozza (`Assets/_PryingEyes/Multiplayer/FishNet/`). Fuori da `Coop/` i punti toccati sono: `Enemy` (+`Stealth`, `Knife`,
`Rounds`, `EnemyTemi`: id, pupazzo, danno e rumore passano da `Coop`), `FloorDirector` (epoca, candele e uscita in rete),
`RoundDirector` (serrande e premi per giocatore), `Drops`/`DropPickup`, `Candelabro` (`litBy`), `Stealth`, `Magic`,
`PlayerController`, `LightPool` (le luci dei compagni non entrano nella firma del piano), `GameManager` (`CreditKill`) e
`GameMenu` (pannello Cooperativa).

## Protocollo

Revisione **`pe-coop-12`** (`CoopProtocol.Revision`): da alzare a ogni cambio di formato dei messaggi o delle regole del piano;
chi ne ha un'altra viene rifiutato all'ingresso. Un pacchetto non affidabile resta sotto 900 byte (l'MTU di Tugboat e' 1023).
La revisione 3 mantiene i messaggi della 2 ma cambia la verifica del posizionamento dei santuari: controlla la linea
fino al punto d'uso effettivo, quindi può scegliere una posizione diversa. Tutti devono usare la stessa build.
La revisione 4 aumenta la precisione della navmesh a celle da 10 cm: ciò può cambiare le riparazioni degli arredi
e la scelta di una posizione raggiungibile per i santuari. Il formato dei messaggi rimane invariato.
La revisione 5 introduce Veglia e Portachiavi, con nuovi modelli, nomi e segnali di attacco. Gli indici numerici dei nemici restano invariati.
La revisione 6 cambia scala, numero delle sale, porte e collegamenti del piano; aggiunge balconate percorribili e
scale con navigazione a più quote. I partecipanti devono usare tutti il nuovo generatore.
La revisione 7 aggiunge Rotonda, Galleria e Giardino d’inverno: cambiano impronte, arredi, collisioni e scelte
di posizionamento degli espositori. Gli indici dei tipi precedenti restano invariati.
La revisione 8 aggiunge la provenienza opzionale al messaggio `HurtPlayer`: il client mostra la direzione del
colpo comunicato dall’ospite. Cambia inoltre la collisione dei dardi: stesso percorso continuo contro muri e
capsule dei bersagli, con altezza ridotta quando sono accovacciati.
La revisione 9 introduce la nuova mischia degli umanoidi e il lancio separato degli incantesimi. `EnemyEvt.Strike`
usa il valore aggiuntivo per distinguere contatto e colpo a vuoto; preparazione e recupero proteggono la stessa clip
su ospite e client. Il bersaglio resta quello scelto all’avvio e viene rilasciato se esce dalla sessione.
La revisione 10 introduce l’affondo dei volanti: `EnemyEvt.Attack` porta nel vettore la direzione impegnata,
`Strike` annuncia contatto o colpo mancato anche per Occhio, Veglia e Medusa. Preparazione 0,55 s, contatto a
0,71 s e recupero fino a 1,13 s; il client segue la posizione dell’ospite e mostra le stesse fasi senza causare danno.
Il formato dei messaggi non cambia, ma le vecchie build non riproducono correttamente questi eventi.
La revisione 11 ingrandisce i moduli del piano, introduce atri a tre quote con ripari sulle balconate e rivede
densità/arredi, pattuglie e ondate. Le grandi arene ricevono più presenze e ingressi a gruppi, anche in quota.
I nemici ordinari privilegiano la mischia; gli umanoidi accompagnano il colpo con un passo fisico. Il formato
rimane invariato; generatore, regole e presentazione devono corrispondere su tutte le macchine.
La revisione 12 aggiunge i ripari tematici alle sale grandi (`RoomKits.ArenaCover`): file di sedie, letti, panche, banconi e
armadi a un minimo di 1,6 m dai muri e dagli altri arredi. Cambiano arredi, collisioni e navmesh di arene, stanze di guardia
e boss; i messaggi restano invariati. Il costo di rendering delle orde (`Enemy.Budget`) è solo locale e non cambia il protocollo.

| Direzione | Messaggi |
| --- | --- |
| giocatore -> ospite | `PlayerState` (~15 Hz, non affidabile), `Hello`, `FloorReady`, `UseCandle`, `UseExit`, `HitClaim`, `NoiseEvt`, `DropClaim`, `PlayerEvent` |
| ospite -> giocatore | `Welcome`, `PlayerJoined/Left`, `PlayersState`, `FloorChange`, `EnemySpawn/Gone/State/Event`, `AwardPoints`, `HurtPlayer`, `SedatePlayer`, `CandleLit`, `ExitState`, `RoundState`, `DropSpawn/Taken`, `Announce`, `Bolt`, `KillCredit`, `Teleport` |
| entrambe | `Test` (solo `-cooptest`, altrimenti scartato) |

## Prove

Da riga di comando (lo usano le prove): `-coophost [-cooplocal]` oppure `-coopjoin <indirizzo>`, con `-coopport N` e
`-coopname Nome`. `-cooplocal` ascolta solo su loopback. Con questi flag nome e indirizzo restano in memoria e **non** toccano
le preferenze (sono condivise con la copia del gioco dell'utente).

`-cooptest` esegue uno scenario scritto a passi, con barriere tra i processi e verifiche dentro il gioco; stampa
`[PryingEyes] cooptest <ospite|ospitato>: ok/FALLITO ...` e alla fine `RISULTATO SUPERATA/FALLITA` (codice d'uscita 0/1):

| Flag | Cosa prova |
| --- | --- |
| `-cooptest` | collegamento, piano uguale (firma), pupazzi dei nemici, colpi dell'ospitato con punti solo a lui, ferite dall'ospite, candela semplice e di zona accesa dall'ospitato, round con serrande e premio, gocce, uscita, discesa insieme e piano nuovo |
| `-cooplate` | entrata a round in corso: serrande, numero del round e nemici arrivano al nuovo |
| `-coopleave` / `-coopcrash` | uscita pulita / processo ucciso: l'ospite se ne accorge e prosegue da solo |
| `-coophostleave` | se ne va l'ospite: l'ospitato prosegue da solo con Role spento |
| `-coopplayers N` | numero di giocatori attesi (con 3 processi) |
| `-cooppics` | l'ospitato accende la torcia e guarda in basso; l'ospite controlla che il fascio scenda e, se ha la grafica (`run_coop.ps1 -HostGraphics`), fotografa il compagno di lato, da vicino e da dietro (cartella `-coopout`, di serie `%TEMP%\pryingeyes-coop\foto`) |

Lo script `tools/coop/run_coop.ps1` avvia l'ospite e gli ospitati dalla build di verifica (`Builds/Audit`, non quella da cui gioca l'utente; senza grafica, un log per processo in
`%TEMP%\pryingeyes-coop`), li aspetta, stampa le righe che contano ed esce con 0 solo se tutti hanno superato:

```powershell
.\tools\coop\run_coop.ps1 -Clients 1 -HostArgs '-cooptest','-seed','2961' -ClientArgs '-cooptest' -TimeoutSec 420
.\tools\coop\run_coop.ps1 -Clients 2 -HostArgs '-cooptest','-coopplayers','3','-seed','1000' -ClientArgs '-cooptest','-coopplayers','3'
```

`tools/coop/run_all.ps1` le fa girare tutte in fila (griglia, ali, entrata tardiva, uscita pulita, processo ucciso, uscita
dell'ospite, tre e quattro giocatori; circa 12 minuti) e dice quali sono fallite. Aggiungere `-oldplan` a entrambi i
processi prova anche la pianta ad ali. La build di prova si fa con `BuildPlayer.Torre -buildOut` come
per le altre prove (vedi il README principale); lo script non parte se una copia di quell'exe e' gia' aperta.

**Esiti del 12 novembre 2026**, su una build **completa** (FishNet si registra da solo, senza ritocchi), tutti i processi sulla
stessa macchina (loopback): `run_all.ps1` supera tutti e otto gli scenari (griglia 26+16 verifiche, ali 28+18, entrata
tardiva, uscita pulita, processo ucciso, uscita dell'ospite, tre giocatori, quattro giocatori) con zero eccezioni nel gioco;
i nemici dell'ospitato stanno entro 0,1-0,2 m da quelli dell'ospite. Le regressioni del gioco da solo (stealth classico, round,
coltello, gocce, bot) passano come prima. Una prova delle gocce e' fallita una volta, e ha fatto trovare un difetto che c'era gia'
prima della cooperativa: i collider dei nemici volanti (occhio, falena, medusa) si infilavano nel controller del giocatore e lo
scacciavano a ogni fotogramma, a volte oltre un muro sottile o sotto il pavimento (la riga `CADUTA` nel log). Corretto in
`Enemy.IgnorePlayerBody` e coperto dalla nuova prova `-pushtest` (vedi il README principale); le gocce e i round passano
anche sui piani 2961/3 e 3046/3, e la cooperativa (griglia, tre giocatori) dopo la correzione. La prova `-cooppics` ha mostrato un compagno con il nome leggibile sopra la testa e
la torcia accesa; ha anche fatto trovare un'inversione (il fascio del compagno saliva quando lui guardava in basso: corretta).

## Cosa non e' ancora verificato (e limiti)

- **Gioco tra macchine e reti diverse**: tutte le prove sono su loopback, quindi latenza, perdita di pacchetti e porta aperta
  sul router non sono mai stati provati. E' la prima cosa da provare con un amico.
- Rientro nello stesso slot dopo un'uscita (l'ospite riassegna il primo slot libero; la prova di entrata tardiva c'e', quella di
  rientro no).
- **I compagni sono manichini semplici** (capsule e sfere, colore diverso per slot): al buio si vedono quasi solo il nome e il fascio
  della torcia. L'HUD ora elenca i compagni con vita relativa, distanza e stato di attesa/rinascita
  (`Scripts/Coop/HUD.Coop.cs`, verificato da `-uitest`). Un modello vero, chiaramente diverso da un nemico, e' da fare.
- Sicurezza: l'ospite si fida dei colpi dichiarati (il danno e' solo limitato tra 0 e 100000, senza controllo di distanza o
  cadenza) e delle candele che un ospitato dice di accendere; le gocce invece le controlla (entro 4,5 m). Nessuna cifratura.
  E' pensata per giocare tra amici.
- Nessuno schermo diviso (un giocatore per macchina) e nessun relay: da Internet serve la porta UDP aperta o una VPN.

## La bozza di ChatGPT e perche' FishNet

La cooperativa e' partita da una bozza di ChatGPT (ramo `feature/multiplayer` in un altro worktree, importata cosi' com'era
nel commit `36e0a3a`): sessione e movimento a posti (fino a 4 posti locali per connessione) in una scena a parte
(`MultiplayerProbe.unity`), senza il gioco vero. Il lavoro rimasto in `Assets/_PryingEyes/Multiplayer/` (avatar a capsule,
posti locali, `NetworkFloorPlan`, obiettivi di prova) e `tools/multiplayer-check/` (controlli .NET e di codegen FishNet) e'
**il prototipo originale**, non usato dal gioco tranne l'autenticatore. Anche la scelta del framework e' sua:

| Framework | Punti pertinenti | Impatto sul progetto |
| --- | --- | --- |
| Photon Fusion 2 | Prediction, lag compensation, sessioni Photon | SDK con login (download 403), compatibilita' con 6000.6.7f1 non confermata |
| **FishNet** | Trasporto Tugboat e sorgenti pubblici, nessun account | Sviluppo senza fornitore; connessione diretta con host raggiungibile; relay e ricerca partite da integrare |
| Netcode for GameObjects | Stack Unity | Un solo PlayerObject per client; piu' lavoro per un FPS |

Fonti: [FishNet](https://github.com/FirstGearGames/FishNet), [release 4.7.3](https://github.com/FirstGearGames/FishNet/releases/tag/4.7.3),
[connessioni remote e port forwarding](https://fish-networking.gitbook.io/docs/tutorials/getting-started/connecting-to-remote-devices).
I prototipi Fusion precedenti restano come riferimento in `docs/multiplayer/reference/Fusion2` (fuori dagli Assets).
