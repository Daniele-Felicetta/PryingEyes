# Cooperativa online (fino a 4 giocatori)

Aggiornato il 15 novembre 2026 (protocollo `pe-coop-25`; dal 16 novembre `pe-coop-26`: il sacrario del grimorio nel piano e la fonte di danno `Magia`, vedi [arms-and-magic.md](../game/arms-and-magic.md); con la 0.7.1 `pe-coop-27`: magie e sigilli visibili agli altri, stati dei nemici decisi dall'ospite, annessi di tutti; con la 0.7.2 `pe-coop-28`: a terra e rianimazione; con la 0.8.0 `pe-coop-29`: i compagni sono i quattro sopravvissuti con l'arma in mano, vedi [characters.md](../game/characters.md); con la 0.9 `pe-coop-30`: il personaggio di ognuno, aspetto e livello, nel saluto, vedi [saves-and-characters.md](../game/saves-and-characters.md); con la 0.9.1 `pe-coop-31`: il deserto in cooperativa, vedi [the-desert.md](../game/the-desert.md); con la moto del deserto `pe-coop-32`: i compagni in sella, vedi "The motorbike" nella stessa pagina; con la moto che investe chi cammina `pe-coop-33`: la fonte di danno `Moto`, stessa pagina; con la moto su pista `pe-coop-35`: la gara in cooperativa, vedi [track-mode.md](../game/track-mode.md); con il tempo su pista `pe-coop-36`: la pioggia dell'ospite per tutti, stessa pagina; con il garage del deserto `pe-coop-37`: ognuno vede il compagno sulla moto che guida davvero, vedi "The garage and Bozzo Moto Store" nella pagina del deserto). La cooperativa e' dentro il gioco vero (La Torre): ogni giocatore
gira con la propria copia, uno **ospita** e gli altri si collegano al suo indirizzo. Rete: FishNet 4.7.3 (trasporto
Tugboat, UDP, porta **7807**), pin al commit `73f30cf2` nel manifest.

## Come si gioca

Per giocare in piu' persone sullo stesso PC (da due a quattro giocatori nello stesso gioco e nella stessa finestra, con lo schermo diviso e
i controller) non serve niente di tutto questo: menu iniziale, **Schermo condiviso**. Vedi [split-screen.md](split-screen.md). Qui sotto
si gioca tra macchine diverse.

1. Chi ospita: menu iniziale, **Cooperativa**, guarda il proprio personaggio (la riga *Il tuo personaggio*: nome e livello; si crea o si cambia da **Personaggi**) e preme **Ospita una partita**.
   Dalla 0.9.1 gli viene chiesto anche che partita ospita (*Che partita ospiti?*: **Standard** o **Il deserto**, la stessa scelta di *Gioca*; la porta si apre dopo la scelta; Indietro e B tornano qui).
   La sua partita parte subito; gli altri entrano quando vogliono (anche a piano iniziato, con un round in corso, o nel deserto) e partono dove gioca lui: chi entra non sceglie.
2. Gli altri: **Cooperativa**, il proprio personaggio, indirizzo dell'ospite, **Unisciti all'indirizzo**. Il menu si toglie da solo
   appena l'ospite li saluta. Se la versione del gioco non e' la stessa, l'albergo e' pieno o l'ospite non risponde, il
   menu scrive "Connessione rifiutata o senza risposta" (il motivo non viaggia in rete: l'ospite ne scrive una riga nel log).
3. Nella stessa rete basta l'indirizzo locale dell'ospite: il gioco lo scrive in alto appena si ospita e nel menu di pausa
   (Esc), oppure lo mostra `ipconfig`. La prima volta Windows chiede se
   consentire il gioco nella rete: l'ospite deve dire di si. Per giocare da Internet l'ospite deve avere la porta UDP 7807
   raggiungibile (inoltro sul router); non c'e' ancora nessun relay, quindi dietro CGNAT non e' raggiungibile. In quel caso
   funziona una VPN tra amici (Tailscale, ZeroTier, Radmin VPN: ognuno riceve un indirizzo privato, e si scrive quello).
   Il gioco non tocca router e firewall.
4. Il nome in rete e' quello del personaggio (i personaggi stanno in `characters/`, vedi [saves-and-characters.md](../game/saves-and-characters.md));
   l'ultimo indirizzo resta nelle preferenze del gioco. Non si usa mai il nome dell'utente di Windows.

**Cosa si condivide.** Un solo albergo per tutti: i nemici (li simula l'ospite, gli altri ne vedono i pupazzi),
le candele accese, le serrande e i round, l'uscita e la discesa (si scende tutti insieme), le gocce a terra (un
potenziamento raccolto vale per tutti). Ognuno ha **per conto suo** punti, armi, perk, libri, scrigno e altare, muri
d'armi, pile e torcia. Una candela da' i suoi 60 punti (e munizioni, e il bonus Fantasma) a chi l'accende; il premio di
un round va a tutti; un'uccisione paga solo chi ha sparato. Un round si annulla solo se muoiono tutti.
I compagni si vedono come sopravvissuti (Custode, Dottoressa, Reporter, Cacciatrice, con corpo, gambe che camminano e arma in mano, vedi [characters.md](../game/characters.md): dal 0.8.0 uno per slot, dal 0.9 quello del personaggio che ognuno ha scelto, mai due uguali), con la loro torcia e il nome sopra la testa (col livello, se supera 1); i nemici cacciano il piu' vicino.
In pausa il gioco non si ferma (gli altri continuano): la pausa blocca solo i tuoi comandi.

**Nel deserto** (0.9.1) un solo mondo per tutti, fatto dallo stesso seme: i nemici (ognuno vede quelli che ha entro 190 m), l'ora del giorno e il boss che cade sono di tutti; punti, armi, perk,
cattedrale, magazzino, bottino sulla sabbia e trofei restano di ognuno. Come funziona e perche' e' nella pagina del deserto, [the-desert.md](../game/the-desert.md), sezione "Co-op, the minimap and the way home".

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
- **Magie e sigilli** (0.7.1, `Coop/CoopGame.Magic.cs`, `Magia/MagicReplay.cs`). Chi lancia una magia o traccia un sigillo lo
  racconta (`SpellCast` all'ospite, che lo gira come `SpellFx` agli altri; l'ospite lo manda direttamente): nodo, mano, mira e i
  nemici colpiti in ordine (per id di rete). Chi lo riceve lo vede partire dalla mano sinistra del pupazzo di chi lancia (fuoco,
  catena di fulmini sugli stessi nemici, cono di gelo, filo di sangue, ombra, onda, Essenza), senza danni: i danni passano gia'
  da `HitClaim`. Un sigillo lascia una luce del suo colore sulla mano destra del pupazzo finche' dura e finche' ha in mano
  quell'arma (`PlayerNet.weapon`, che prima restava a 0).
- **Stati dei nemici** (fuoco, sangue, acido, stordito, lento, freddo, congelato). Li tiene l'ospite, che simula i nemici: un
  ospitato che ne mette uno su un pupazzo lo chiede con `StatusClaim` (nemico, tipo, valori, tenuti nei limiti delle magie) e
  intanto ne vede l'aspetto; il danno nel tempo va a nome suo (l'uccisione e' sua). L'ospite racconta gli stati di ogni nemico
  in un byte di `EnemyNet` (`status`) e i pupazzi li mostrano a tutti, senza danni ne' soste. Fino alla 0.7.0 stordimento,
  rallentamento e gelo di un ospitato non fermavano i nemici veri, e gli effetti sui nemici si vedevano solo su chi li causava.
- **Annessi** (sacrario, torri, cripte, giardini). Cancelli e leve sono di tutti: chi apre un annesso o tira una leva lo dice
  all'ospite (`AnnexUse`), che lo applica e lo racconta (`AnnexState`); chi entra tardi riceve gli annessi gia' aperti. La
  chiave d'oro e lo scrigno antico dentro restano di ognuno (come lo Scrigno del negozio). Fino alla 0.7.0 ognuno apriva i
  suoi: un compagno vedeva l'altro passare attraverso un muro chiuso.
- **Il deserto** (0.9.1, `CoopGame.World.cs` in fondo alla parte dei nemici, `Deserto/DesertAnchors.cs`). Il `Welcome` porta un flag col modo di gioco; l'ospitato costruisce il suo deserto dal seme
  dell'ospite (`FloorDirector.StartRun(coopDesert)`) con la stessa stretta di mano dei piani (la prima epoca: `Coop.OnFloorStart(0)`, `OnFloorBuilt()`, `FloorReady`), e riceve da
  `HostDesertClientReady` i nemici che ha vicino, l'ora e i boss gia' caduti. L'ospite fa girare i nemici attorno a tutti, e perche' camminino su sabbia, navmesh e luoghi anche lontano dal suo
  giocatore costruisce il deserto attorno a ogni compagno vivo (`DesertAnchors`). A ogni ospitato manda solo i nemici entro **190 m** da lui e li toglie oltre **230 m** (un `EnemyGone` senza morte; `known[slot]`,
  controllato ogni 0,3 s): attacchi, grida, dardi e morte vanno solo a chi ne ha il pupazzo, le pose (10 Hz) solo per quelli che conosce. L'ora (`DesertClock`: ore e giorno, non affidabile, ogni 2 s) e' dell'ospite;
  un boss caduto (`DesertBossDown`: la cella del luogo) lo dice a tutti, che mettono il trofeo nel proprio magazzino, e l'ospite tiene l'elenco per chi arriva tardi. `Enemy.dormant` viaggia nel bit
  0x80 di `EnemyNet.status`, perche' la minimappa del compagno non mostri come minaccia chi riposa. Fuori dal deserto niente di tutto questo cambia (`CoopGame.Desert` e' falso).
- **Bersagli multipli.** I nemici scelgono tra tutti i giocatori vivi dello stesso piano (`ITarget`: `LocalTarget` per
  chi gioca qui, `RemotePlayer` per i compagni; `Targets.Nearest/AnyAlive`). Se un compagno se ne va i nemici passano agli
  altri bersagli (provato: l'ospite resta solo e la caccia e il round continuano).
- **Entrate e uscite.** Chi arriva a meta' riceve tutto lo stato del piano (nemici, candele, uscita, round in corso con le
  serrande, gocce). Chi esce o cade (15 s senza pacchetti, `CoopProtocol.TimeoutSeconds`: di serie Tugboat aspetta 30
  minuti) viene tolto; se se ne va l'ospite, gli altri restano nel piano da soli e proseguono con il mondo che hanno
  (i pupazzi spariscono e il piano si ripopola).
- **In rete anche in secondo piano.** `Application.runInBackground` e' spento nel progetto; la cooperativa lo accende per tutta
  la partita (un Alt-Tab non deve bloccare gli altri).
- **Senza FishNet.** Il codice che nomina FishNet sta in `FishNetCoopNet.cs` (e nell'autenticatore in `Multiplayer/FishNet/`)
  dietro `PRYINGEYES_FISHNET` (solo Standalone): nella build web, o nella citta' (`CityDirector` presente), `Coop.Available` e' falso
  e ogni chiamata della facciata non fa niente.

## Dove sta il codice (`Assets/_PryingEyes/Scripts/Coop/`)

| File | Cosa fa |
| --- | --- |
| `Coop.cs` | Facciata statica: `Role` (Off/Host/Client), `Sim`, `Slot`, `Game`, avvio da menu o da riga di comando, `KeepRunning` |
| `CoopNet.cs`, `FishNetCoopNet.cs` | Trasporto astratto e realizzazione FishNet: ammissione, slot, un solo `CoopPacket` (tipo + byte) con `Broadcast` |
| `CoopWire.cs` | Tipi di messaggio (`CoopKind`), `CoopProtocol` (revisione, porta, tempi), `NetWriter`/`NetReader` (il lettore non lancia mai) |
| `CoopGame.cs` | Giocatori, piano ed epoca, saluto, stato di ciascuno, nomi |
| `CoopGame.World.cs` | Il mondo condiviso: nemici, colpi, rumore, candele, uscita, round, gocce, annunci; dalla 0.9.1 anche il deserto (nemici per vicinanza, ora, boss caduti) |
| `CoopGame.Rooms.cs` | Acquisto delle aree: richiesta del client con i punti messi da parte, convalida e risposta dell'ospite, stato delle aree |
| `CoopGame.Traps.cs` | Trappole delle soglie (`DoorTraps`): il client riserva i punti e chiede, l'ospite accende e manda lo stato |
| `CoopGame.Power.cs` | Corrente del piano (leva del quadro): il client chiede, l'ospite accende e lo dice a tutti |
| `CoopGame.Barrels.cs`, `CoopGame.Chandeliers.cs`, `CoopGame.Lamps.cs` | Botti d'olio santo, lampadari e lampade che si rompono: il colpo del client va all'ospite, che decide e racconta a tutti |
| `HUD.Coop.cs` | Pannello dei compagni nell'HUD (dalla 0.7.2 a sinistra, sotto i perk): una scheda per compagno col numero nel cerchio del suo colore, nome, vita in percentuale e barra spessa, distanza e freccia verso di lui; a terra la scheda pulsa di rosso e la barra diventa il tempo per rialzarlo; dimensione nelle impostazioni (*HUD dei compagni*, 60%-180%). Vale anche per lo schermo condiviso |
| `CoopGame.Revive.cs` | Rianimazione in rete (0.7.2): `ReviveRemote`, `ReviveClaim` all'ospite (controlla che chi rialza sia pronto e a meno di 4 m), `Revived` a chi era a terra |
| `Enemy.Net.cs` | Pupazzi dei nemici e stato di rete |
| `Targets.cs`, `RemotePlayer.cs` | Bersagli multipli e compagni (un `SurvivorBody` con torcia, nome e posa interpolata; il pupazzo di capsule resta solo se manca il modello) |
| `CoopTest.cs`, `CoopTest.Survival.cs`, `CoopTest.Desert.cs` | Prova automatica a piu' processi (solo con `-cooptest`); la seconda ha i passi del ciclo a ondate, la terza quelli del deserto (`-coopdeserto`) |
| `Deserto/DesertAnchors.cs` | I punti dei compagni attorno a cui l'ospite costruisce il deserto (sabbia, navmesh, luoghi, sassi); vuoto da soli e sugli ospitati |

L'ammissione (revisione del protocollo, capienza, tempo massimo) la fa `FishNetSeatAuthenticator`, scritto da ChatGPT nella
sua bozza (`Assets/_PryingEyes/Multiplayer/FishNet/`, con tre file di `Multiplayer/Core/`: vedi l'ultima sezione). Fuori da `Coop/` i punti toccati sono: `Enemy` (+`Stealth`, `Knife`,
`Rounds`, `EnemyTemi`: id, pupazzo, danno e rumore passano da `Coop`), `FloorDirector` (epoca, candele e uscita in rete),
`RoundDirector` (serrande e premi per giocatore), `Drops`/`DropPickup`, `Candelabro` (`litBy`), `Stealth`, `Magic`,
`PlayerController`, `LightPool` (le luci dei compagni non entrano nella firma del piano), `GameManager` (`CreditKill`) e
`GameMenu` (pannello Cooperativa).

## Protocollo

Revisione **`pe-coop-37`** (le moto del deserto, vedi [the-desert.md](../game/the-desert.md#the-garage-and-bozzo-moto-store): `PlayerNet` porta anche quale moto guida il giocatore, in quattro bit (dal 3 al 6) del secondo byte dei flag: 0 la moto di casa, per le nove della pista il loro numero piu' uno (`DesertBikeDefs.NetCode`, cosi' non cambia se il catalogo cambia ordine); chi lo riceve costruisce al compagno quel modello, con la sua posa e il suo motore (`RemotePlayer.TickBike`, `SeatAvatar`); una macchina con la revisione vecchia leggerebbe quei bit come niente e disegnerebbe la moto sbagliata, per questo non entra nella stessa partita; le moto parcheggiate non si condividono, ognuno costruisce quelle del proprio personaggio; le prove a due processi sono `m_moto` (la moto di casa) e `q_moto_comprata` (la Gigante 1300 comprata al negozio, vista col suo modello dall'altro processo)); `pe-coop-36` (il tempo della gara su pista, vedi [track-mode.md](../game/track-mode.md#the-rain): `PistaRoster` ha un byte in piu', il tempo dell'ospite (0 sereno, 1 pioggia), subito dopo l'ora; chi lo riceve rende bagnata la strada prima di fare la gara, cosi' il piano dei piloti del computer e la fisica di ogni moto sono gli stessi sulle due macchine, e accende da se' pioggia, cielo grigio e terreno bagnato; la scelta dell'amico non conta per quella partita; una macchina con la revisione vecchia leggerebbe il numero dei piloti nel byte del tempo, per questo le due versioni non entrano nella stessa partita; la prova a due processi e' `n_pista`, che ora corre sotto la pioggia dell'ospite); `pe-coop-35` (la moto su pista, vedi [track-mode.md](../game/track-mode.md): `Welcome` finisce con un secondo `Bool`, la partita dell'ospite e' una gara (il primo e' il deserto); cinque messaggi nuovi, `PistaPose` (giocatore -> ospite, non affidabile, 20 volte al secondo: epoca e la posa della propria moto, circa 35 byte) e, dall'ospite, `PistaRoster` (affidabile: epoca, seme, giri, moto, aiuti, ora e per ogni pilota id, chi e' (persona o computer), slot di rete, moto, colori, posto in griglia, ritirato, nome; si rimanda quando cambia chi c'e'), `PistaStart` (affidabile: il via), `PistaPoses` (non affidabile: le pose di tutti tranne la propria, i piloti del computer compresi, che gira l'ospite; 19 piloti stanno in 665 byte, il limite e' 900) e `PistaOver` (affidabile: per ogni pilota arrivato, ritirato, ordine, tempo e miglior giro: la classifica la dice l'ospite); un'altra gara della stessa partita e' un `FloorChange` con il seme, come un piano nuovo, e riparte dall'epoca; una macchina con la revisione vecchia non conosce ne' il flag ne' i messaggi, per questo le due versioni non entrano nella stessa partita; la prova a due processi e' `n_pista`, `o_pista_ancora` e `p_pista_ospite` di `run_all.ps1`); `pe-coop-34` (i ripari delle sale grandi sono raggruppati e motivati: una fila parallela al muro, accanto a una porta, in un angolo o al centro, da `RoomKitsCover.CoverSpot`; cambiano arredi, collisioni e navmesh di arene, stanze di guardia e boss, e il formato dei messaggi resta invariato); `pe-coop-33` (la moto che investe chi cammina: `DamageSource.Moto` e' la fonte numero 7 e `Punti.LastSource`; `HitClaim`, `EnemyDied` e `KillCredit` la portano come le altre, e una macchina con la revisione vecchia, che accetta le fonti fino alla `Magia` (6), scarterebbe in silenzio il colpo di un compagno in sella: per questo la revisione cambia e le due versioni non entrano nella stessa partita; chi investe manda un `HitClaim` per ogni corpo, l'ospite lo applica e lo paga come mezza uccisione (`Punti.Investito`, 30); il resto dei messaggi non cambia; il passo del colpo a un pupazzo di `CoopTest` usa la fonte `Moto` per il secondo dei tre colpi); `pe-coop-32` (la moto del deserto: nel secondo byte dei flag di `PlayerNet` il bit 4 e' `riding` (il giocatore e' in sella) e `PlayerNet` finisce con `heading`, un byte (`CoopProtocol.QuantYaw`) con la direzione verso cui punta la moto, che non e' quella in cui guarda il giocatore; chi la riceve fa nascere la moto sotto il pupazzo (`RemotePlayer.TickBike`), lo mette in sella e la inclina secondo quanto gira; la moto parcheggiata di un compagno non si vede, perche' ogni macchina costruisce solo quella del proprio slot); `pe-coop-31`, 0.9.1: il deserto in cooperativa: `Welcome` finisce con un `Bool`, il modo di gioco dell'ospite (deserto o piani); due messaggi nuovi dall'ospite, `DesertBossDown` (la cella del luogo, due `I32`: un boss e' caduto, per tutti) e `DesertClock` (ore `F32` e giorno `I32`, non affidabile, ogni 2 s); `EnemyNet.status` usa anche il bit `0x80` per `Enemy.dormant`; con il deserto `EnemySpawn`, `EnemyGone`, `EnemyEvent`, `EnemyState` e `Bolt` vanno solo a chi ha il nemico vicino; `pe-coop-30`, 0.9: il saluto porta il personaggio di ognuno: `Hello` = nome, aspetto (testo, vuoto nelle prove) e livello (`U16`); `Welcome` = slot, seme, piano, epoca e, per ogni giocatore presente, slot, nome, aspetto e livello, poi l'aspetto che l'ospite da' al nuovo arrivato; `PlayerJoined` = slot, nome, aspetto, livello; l'ospite tiene l'aspetto chiesto e da' il primo libero a chi ne chiede uno gia' preso, `CharacterLooks.Resolve`; `pe-coop-29`, 0.8.0: `PlayerNet` finisce con `gun`, l'indice dell'arma in mano in `WeaponDefs.All` (255 = nessuna), e un byte con `ads` e `reloading`; `PlayerNet.weapon` resta lo slot dell'arma, che serve alle magie; i sopravvissuti li usano per tenere l'arma in mano, mirare e ricaricare; `pe-coop-28`: `PlayerNet.downed` nel bit 128 dei flag e `bleed` in un byte da 1/8 di secondo, `ReviveClaim`, `Revived`; `pe-coop-27` con la 0.7.1: magie e sigilli, `StatusClaim`, stati in `EnemyNet`, annessi; `pe-coop-26` dal 16 novembre 2026: sacrario del grimorio e fonte di danno `Magia`; `CoopProtocol.Revision`; l'ammissione usa `CoopProtocol.ContentRevision`, cioe' la revisione con l'hash
del catalogo delle armi in coda): da alzare a ogni cambio di formato dei messaggi o delle regole del piano;
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
armadi con margini di 1,2 m dai muri e 1,3 m dagli arredi esistenti. Cambiano arredi, collisioni e navmesh di arene, stanze di guardia
e boss; i messaggi restano invariati. Il costo di rendering delle orde (`Enemy.Budget`) è solo locale e non cambia il protocollo.
La revisione 13 rende il corpo dei giocatori un ostacolo per l'aggiramento locale degli agenti, senza ritagliare o
ricostruire la navmesh. Vale anche per i compagni vivi nello stesso piano. I punti deboli animati restano colpibili
ma non spingono il controller; le capsule dei nemici a terra restano solide. Cambia la gestione delle collisioni
durante gli assedi, quindi tutti devono aggiornarsi; il formato dei messaggi rimane invariato.
La revisione 14 aggiunge un espositore economico rifornibile dentro ogni arena della pianta a griglia. Il controllo
del posto libero ignora giocatori e nemici, così le guardie presenti solo sull'ospite non cambiano il posizionamento.
La firma del piano comprende anche indici e posizioni delle armi a muro, ordinati in modo deterministico;
la prova cooperativa confronta quindi anche queste risorse. Il formato dei messaggi resta invariato.

La revisione 15 sostituisce la progressione a candele con il ciclo a ondate (`SurvivalMode`, il predefinito): aree da aprire coi punti e ondate
globali, senza candele e senza stealth. Tre messaggi nuovi: `BuyRoom` (giocatore -> ospite: epoca, numero della richiesta, area, prezzo messo
da parte), `RoomReply` (ospite -> giocatore: richiesta ed esito) e `RoomsState` (ospite -> tutti i pronti: le aree aperte; lo riceve anche chi
entra tardi). Chi compra mette da parte il prezzo subito (`CoopGame.RequestRoom`); l'ospite accetta solo se l'epoca e' quella giusta, il giocatore
e' pronto, il prezzo coincide con la tabella e l'acquirente sta in un'area aperta collegata, a non piu' di 4,5 m dalla porta
(`RoomAccessDoors.TryBuy`); se rifiuta il client si rimette i punti (`SettleRoom`), anche se la connessione cade o il piano cambia
(`RefundPendingRoom`). `RoundState` ha un quinto tipo (4: numero dell'ondata, in corso, secondi alla prossima, ondate superate del piano,
Processione) per chi entra a ondata in corso; gli altri tipi (inizio, stato, fine, annullata) servono anche alle ondate, che l'ospite fa
partire da solo e il cui premio e' di tutti. L'ondata si dimensiona sui giocatori collegati (16 nemici piu' 4 per ondata piu' 8 per ogni
giocatore oltre il primo, con un tetto di vivi insieme che cresce allo stesso modo).

Le revisioni 16-25 (dettagli nei commenti di `CoopWire.cs` e nei documenti in `docs/`) aggiungono: 16 l'impronta del catalogo
nell'ammissione; 17 le regole di movimento (tolto il secondo aumento di velocita' delle ondate); 18 altezze delle cattedrali e annessi;
19 varianti dell'orda; 20 trappole delle soglie; 21 equipaggiamento e prove; 22 botti d'olio santo (`BarrelGone`); 23 corrente del piano
(`UsePower`, `PowerState`); 24 lampadari che crollano (`ChandelierHit`, `ChandelierDrop`); 25 lampade che si rompono (`LampHit`, `LampBroken`).
Ogni cambio di revisione richiede la stessa build su tutte le macchine.

**The gate checks the floor.** Every player builds the floor from the seed, so a change that moves a plan of the generator (`PlanGen`) is a change of the protocol and must raise `CoopProtocol.Revision`. `python tools/studio.py test plan`, part of the CI gate, fails when it was not raised: it hashes 48 plans (six seeds, floors 1, 3, 5 and 8, the lab's default layout and the game's `waves`) and compares them with `tools/fixtures/plan-fingerprint.json`, which also records the revision the hashes were made at. After raising the revision, for any reason (a message, a weapon, a rule), run `dotnet run --project tools/planlab -c Release -- fingerprint --update tools/fixtures/plan-fingerprint.json` and commit the fixture: it refuses (`PLAN_CHANGED_WITHOUT_REVISION`) while a plan has changed and the revision has not. What it covers, and what it leaves to the co-op scenarios: [testing.md](../engineering/testing.md#24-plan-labs-net-no-unity).

| Direzione | Messaggi |
| --- | --- |
| giocatore -> ospite | `PlayerState` (~15 Hz, non affidabile), `Hello`, `FloorReady`, `UseCandle`, `BuyRoom`, `UseExit`, `HitClaim`, `NoiseEvt`, `DropClaim`, `PlayerEvent`, `UseTrap`, `BarrelHit`, `UsePower`, `ChandelierHit`, `LampHit`, `SpellCast`, `StatusClaim`, `AnnexUse`, `ReviveClaim` |
| ospite -> giocatore | `Welcome`, `PlayerJoined/Left`, `PlayersState`, `FloorChange`, `EnemySpawn/Gone/State/Event`, `AwardPoints`, `HurtPlayer`, `SedatePlayer`, `CandleLit`, `RoomsState`, `RoomReply`, `TrapsState`, `TrapReply`, `BarrelGone`, `PowerState`, `ChandelierDrop`, `LampBroken`, `ExitState`, `RoundState`, `DropSpawn/Taken`, `Announce`, `Bolt`, `KillCredit`, `Teleport`, `SpellFx`, `AnnexState`, `Revived`, `DesertBossDown`, `DesertClock` |
| entrambe | `Test` (solo `-cooptest`, altrimenti scartato) |

## Prove

Da riga di comando (lo usano le prove): `-coophost [-cooplocal]` oppure `-coopjoin <indirizzo>`, con `-coopport N` e
`-coopname Nome`. `-cooplocal` ascolta solo su loopback. Con questi flag nome e indirizzo restano in memoria e **non** toccano
le preferenze (sono condivise con la copia del gioco dell'utente).

`-cooptest` esegue uno scenario scritto a passi, con barriere tra i processi e verifiche dentro il gioco; stampa
`[PryingEyes] cooptest <ospite|ospitato>: ok/FALLITO ...` e alla fine `RISULTATO SUPERATA/FALLITA` (codice d'uscita 0/1):

| Flag | Cosa prova |
| --- | --- |
| `-cooptest` | collegamento, piano uguale (firma), pupazzi dei nemici, colpi dell'ospitato con punti solo a lui, la ricarica a vuoto fatta dalla mano sinistra su ogni processo e la Catena del Fulmine lanciata dall'ospitato su un nemico fermo dell'ospite, nato dove l'ospitato lo vede (il colpo, fonte Magia, arriva all'ospite come `HitClaim`; la magia si vede sull'ospite e sugli altri ospitati, lo stordimento arriva al nemico vero con `StatusClaim` e torna sui pupazzi nello stato del nemico), il muro del sacrario aperto dall'ospitato che si apre su tutti, ferite dall'ospite, a terra e rianimazione in rete (dalla 0.7.2: l'ospitato a terra rialzato dall'ospite con `Revived`, l'ospite a terra rialzato dall'ospitato con `ReviveClaim`, tutti che li vedono a terra e poi in piedi), area comprata dall'ospitato (e richiesta da lontano rifiutata, con i punti restituiti), ondata con nascite solo nelle aree aperte e premio per tutti, altre due ondate che aprono l'ascensore, gocce, uscita, discesa insieme e piano nuovo da capo (col ciclo a candele: candela semplice e di zona, round con serrande e premio) |
| `-cooplate` | entrata a ondata in corso: aree comprate, porte su e giu', il sacrario aperto dall'ospite prima, numero dell'ondata e nemici arrivano al nuovo (col ciclo a candele: serrande e round) |
| `-coopleave` / `-coopcrash` | uscita pulita / processo ucciso: l'ospite se ne accorge e prosegue da solo (un nemico lo caccia, poi un'ondata intera) |
| `-coophostleave` | se ne va l'ospite: l'ospitato prosegue da solo con Role spento |
| `-coopplayers N` | numero di giocatori attesi (con 3 processi) |
| `-coopdeserto` | il copione del deserto (`run_all.ps1 -Only j`, scenario `j_deserto`: l'ospite con `-deserto -catacombe`, l'ospitato senza `-deserto`, che segue l'ospite): il deserto e' costruito su tutti e due e i compagni si vedono; stesso seme e stessa ora; l'ospitato va a mezzo chilometro, accanto a un luogo, e l'ospite costruisce li' luogo, sabbia con collider e navmesh; un nemico nato vicino all'ospitato gli arriva, uno nato vicino all'ospite no (190 m); l'ospitato torna e i due si scambiano (quello di prima sparisce dalla sua vista ma resta vivo sull'ospite); un colpo mortale a un pupazzo uccide il nemico vero e paga chi ha sparato; l'ora messa dall'ospite arriva; un boss caduto sull'ospite cade anche per l'ospitato, con il trofeo nel suo magazzino; con `-coopleave` o `-coopcrash` (scenari `k_deserto_esce` e `l_deserto_ucciso`) i passi sono solo i primi tre e un ottavo, in cui l'ospitato lontano se ne va (chiusura o processo ucciso) e l'ospite prosegue da solo: nessun punto attorno a lui, un solo bersaglio per i nemici, l'elenco di quello che vedeva buttato, il luogo lontano scaricato, l'orologio che va e un nemico che gli da' la caccia; con `-cooppics` e la grafica sull'ospite, due foto del compagno sulla sabbia |
| `-cooplook ID` / `-cooplevel N` | il personaggio che il processo dichiara nel saluto (senza, le prove mandano un aspetto vuoto e livello 1): con l'ospite `custode` di livello 7 e l'ospitato `custode` di livello 3 (`run_all.ps1 -Only i`, scenario `i_aspetti`) si controlla che l'ospite tenga l'aspetto, che il secondo riceva la dottoressa, che tutti vedano gli stessi livelli e che il pupazzo di ogni compagno sia costruito con l'aspetto e il livello dichiarati |
| `-cooppics` | l'ospitato accende la torcia e guarda in basso; l'ospite controlla che il fascio scenda e, se ha la grafica (`run_coop.ps1 -HostGraphics`), fotografa il compagno di lato, da vicino e da dietro (cartella `-coopout`; senza, `persistentDataPath/coop`; con `run_coop.ps1 -HostGraphics` e' `%TEMP%\pryingeyes-coop\foto`); dalla 0.7.2 anche il pannello dei compagni nell'HUD dell'ospite: col compagno in piedi (`coop_hud.png`), a terra (`coop_hud_a_terra.png`) e ingrandito al 160% (`coop_hud_160.png`), poi lo rialza |

Con `-cooptest` le ondate partono ferme (`SurvivalMode.HoldWaves`; il copione le libera con `SurvivalMode.Hold`): le avvia il copione, perche' i passi che contano punti e nemici uno per uno non devono
trovarsele in mezzo. Il ciclo a candele si prova con `-candles` su tutti i processi (la pianta ad ali, `-oldplan`, lo implica).

Lo script `tools/coop/run_coop.ps1` avvia l'ospite e gli ospitati dalla build di verifica (`Builds/Audit`, non quella da cui gioca l'utente; senza grafica, un log per processo in
`%TEMP%\pryingeyes-coop`), li aspetta, stampa le righe che contano ed esce con 0 solo se tutti hanno superato:

```powershell
.\tools\coop\run_coop.ps1 -Clients 1 -HostArgs '-cooptest','-seed','2961' -ClientArgs '-cooptest' -TimeoutSec 420
.\tools\coop\run_coop.ps1 -Clients 2 -HostArgs '-cooptest','-coopplayers','3','-seed','1000' -ClientArgs '-cooptest','-coopplayers','3'
```

`tools/coop/run_all.ps1` le fa girare tutte in fila (griglia, ali, entrata tardiva, uscita pulita, processo ucciso, uscita
dell'ospite, tre e quattro giocatori, aspetti e livelli dei personaggi, il deserto; circa 12 minuti per i primi otto e meno di un minuto per il deserto) e dice quali sono fallite. Aggiungere `-oldplan` a entrambi i
processi prova anche la pianta ad ali. La build di prova si fa con `BuildPlayer.Torre -buildOut` come
per le altre prove (vedi `docs/engineering/building-and-shipping.md`); lo script non parte se una copia di quell'exe e' gia' aperta.

**Esiti del 12 novembre 2026** (sul protocollo `pe-coop-15`, prima delle revisioni 16-25: da ripetere), su una build **completa** (FishNet si registra da solo, senza ritocchi), tutti i processi sulla
stessa macchina (loopback): `run_all.ps1` supera tutti e otto gli scenari (griglia 26+16 verifiche, ali 28+18, entrata
tardiva, uscita pulita, processo ucciso, uscita dell'ospite, tre giocatori, quattro giocatori) con zero eccezioni nel gioco;
i nemici dell'ospitato stanno entro 0,1-0,2 m da quelli dell'ospite. Le regressioni del gioco da solo (stealth classico, round,
coltello, gocce, bot) passano come prima. Una prova delle gocce e' fallita una volta, e ha fatto trovare un difetto che c'era gia'
prima della cooperativa: i collider dei nemici volanti (occhio, falena, medusa) si infilavano nel controller del giocatore e lo
scacciavano a ogni fotogramma, a volte oltre un muro sottile o sotto il pavimento (la riga `CADUTA` nel log). Corretto in
`Enemy.IgnorePlayerBody` e coperto dalla nuova prova `-pushtest` (vedi `docs/engineering/testing.md`); le gocce e i round passano
anche sui piani 2961/3 e 3046/3, e la cooperativa (griglia, tre giocatori) dopo la correzione. La prova `-cooppics` ha mostrato un compagno con il nome leggibile sopra la testa e
la torcia accesa; ha anche fatto trovare un'inversione (il fascio del compagno saliva quando lui guardava in basso: corretta).

**Esiti del 17 novembre 2026** (albero integrato della 0.8.0, protocollo `pe-coop-29`, build di verifica, tutti i processi su loopback):
`run_all.ps1` supera tutti e otto gli scenari in 623 secondi, senza eccezioni nei log: griglia 62 verifiche sull'ospitato e 49 sull'ospite, ali
43 e 29, entrata tardiva 11 e 7, uscita pulita 4 e 8, processo ucciso 4 e 8 (il processo ucciso esce con -1, come previsto), uscita
dell'ospite 7 e 2, tre giocatori 62, 48 e 49, quattro giocatori 62, 48, 48 e 49. Sull'albero integrato non e' stata rifatta la prova con le foto
(`-cooppics`, serve la grafica) e, come sempre, nessuna prova e' stata fatta tra macchine diverse.

**Esiti del 18 novembre 2026** (0.9, protocollo `pe-coop-30`, build di verifica, tutti i processi su loopback, `run_all.ps1 -Only a,i` e poi `-Only g,h`): la griglia
supera 62 verifiche sull'ospitato e 49 sull'ospite (come prima) e la nuova prova degli aspetti, `i_aspetti`, 66 e 53, cioe' le quattro verifiche
dei personaggi in piu' su ogni processo: l'ospite chiede il Custode di livello 7, l'ospitato lo stesso aspetto a livello 3; l'ospite tiene il Custode,
l'ospitato riceve la Dottoressa, entrambi vedono gli stessi aspetti e livelli e il pupazzo di ogni compagno e' costruito cosi'. Anche tre e quattro
giocatori (`g_tre`, `h_quattro`: con loro il saluto nuovo arriva anche a chi era gia' dentro, con `PlayerJoined`) superano le stesse verifiche di
prima: tre giocatori 62, 48 e 49, quattro giocatori 62, 48, 48 e 49. Non sono stati rifatti gli scenari delle ali, dell'entrata tardiva, dell'uscita
pulita, del processo ucciso e dell'uscita dell'ospite su questo protocollo, e le prove non hanno toccato ne' la rete vera ne' i personaggi veri di chi
gioca (i processi di prova usano `characters-prova/`).

**Esiti del 19 novembre 2026** (0.9.1, protocollo `pe-coop-31`, build di verifica, tutti i processi su loopback, senza scheda grafica): `run_all.ps1 -Only a,b,c,d,e,f,g,h,i` supera tutti e nove gli scenari
standard in 737 secondi, senza eccezioni nei log e con le stesse cifre del 17 e del 18 novembre (griglia 62 verifiche sull'ospitato e 49 sull'ospite, ali 43 e 29, entrata tardiva 11 e 7, uscita pulita 4 e 8, processo
ucciso 4 e 8 con uscita -1 come previsto, uscita dell'ospite 7 e 2, tre giocatori 62, 48 e 49, quattro giocatori 62, 48, 48 e 49, aspetti 66 e 53): fuori dal deserto il protocollo 31 non cambia niente.
Nel deserto (`-Only j,k,l`, stessa build, 143 secondi): `j_deserto` 46 s, ospitato 18 verifiche su 18 e ospite 15 su 15; `k_deserto_esce` 41 s e `l_deserto_ucciso` 57 s (processo ucciso, esce con -1), ospitato 10 su 10 e ospite 16 su 16;
nessuna eccezione. Dicono che l'ospitato ha seme e ora dell'ospite; che a 499 m da casa l'ospite gli costruisce attorno luogo, sabbia con collider e navmesh (3,4 s) e gli manda solo il nemico che ha vicino (il pupazzo piu' lontano che
vede e' a 89 m) e lo scambia con quello vicino all'ospite quando torna; che un colpo mortale a un pupazzo uccide il nemico vero e paga chi ha sparato (+100 punti); che l'ora e il boss caduto arrivano a tutti, con il trofeo nel magazzino di ognuno; e che, se
l'ospitato lontano se ne va, l'ospite smette di costruire attorno a lui, scarica il luogo lontano (434 m, soglia 370 m) e prosegue da solo. Con `-cooppics` e l'ospite con la grafica (61 s) la prova ha scattato due foto: il compagno sulla sabbia a 23 m,
col nome sopra la testa e la luce della torcia, il pannello *Compagni* e il suo puntino blu sulla minimappa. **Mai provato**: tre o quattro giocatori nel deserto, tra macchine diverse, con un compagno che cammina (la foto lo mostra fermo), un ospitato che entra
a meta' partita (entra 8 s dopo l'ospite) o che muore nel deserto.

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
- Nessun relay: da Internet serve la porta UDP aperta o una VPN. Lo schermo diviso sullo stesso PC c'e' dal 15 novembre 2026; dal 16
  novembre e' un solo gioco in una sola finestra, senza rete (prima apriva una finestra per giocatore e usava questa cooperativa su
  loopback): [split-screen.md](split-screen.md), in inglese.

## La bozza di ChatGPT e perche' FishNet

La cooperativa e' partita da una bozza di ChatGPT (ramo `feature/multiplayer` in un altro worktree, importata cosi' com'era
nel commit `36e0a3a`): sessione e movimento a posti (fino a 4 posti locali per connessione) in una scena a parte
(`MultiplayerProbe.unity`), senza il gioco vero. Il lavoro rimasto in `Assets/_PryingEyes/Multiplayer/` (avatar a capsule,
posti locali, `NetworkFloorPlan`, obiettivi di prova) e `tools/multiplayer-check/` (controlli .NET e di codegen FishNet) e'
**il prototipo originale, congelato**: la cartella ha un [README](../../unity/PryingEyes/Assets/_PryingEyes/Multiplayer/README.md) che dice
che cosa il gioco ne usa e che cosa no. Il gioco usa solo l'ammissione: quattro file, `FishNetSeatAuthenticator` e i tre di
`Multiplayer/Core/` che gli servono (`AdmissionGate`, `SessionAdmission`, `SessionRoster`); il resto serve alla sola scena di prova.
`tools/multiplayer-check/` non compila piu' dal 15 novembre 2026 (manca `PlanAnnex.cs` nei due progetti che lo compilano) e non e' nel
cancello. Anche la scelta del framework e' sua:

| Framework | Punti pertinenti | Impatto sul progetto |
| --- | --- | --- |
| Photon Fusion 2 | Prediction, lag compensation, sessioni Photon | SDK con login (download 403), compatibilita' con 6000.6.7f1 non confermata |
| **FishNet** | Trasporto Tugboat e sorgenti pubblici, nessun account | Sviluppo senza fornitore; connessione diretta con host raggiungibile; relay e ricerca partite da integrare |
| Netcode for GameObjects | Stack Unity | Un solo PlayerObject per client; piu' lavoro per un FPS |

Fonti: [FishNet](https://github.com/FirstGearGames/FishNet), [release 4.7.3](https://github.com/FirstGearGames/FishNet/releases/tag/4.7.3),
[connessioni remote e port forwarding](https://fish-networking.gitbook.io/docs/tutorials/getting-started/connecting-to-remote-devices).
I prototipi Fusion precedenti restano come riferimento in `docs/multiplayer/reference/Fusion2` (fuori dagli Assets): non sono compilati,
perche' il simbolo `PRYINGEYES_FUSION2` non e' definito in nessun progetto e nessun codice li usa.
