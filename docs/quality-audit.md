# Revisione della versione completa

Obiettivo: verificare e completare aspetto, funzionalità e gameplay del progetto. La revisione riguarda prima
la versione Unity/Torre, attualmente distribuita; le versioni Three.js e Unreal restano da valutare separatamente.
Questo documento registra prove e lavoro aperto, senza dichiarare conclusa la revisione complessiva.

## Interventi attuali

- Impostazioni: sensibilità 0,25–3×, campo visivo verticale 60–105°, oscillazione 0–100%, applicazione immediata
  e preferenze persistenti. L'oscillazione riduce bob, respiro dell'arma e variazioni decorative di FOV/roll;
  rinculo, lean e oscillazione delle ottiche conservano le rispettive regole di gioco.
- Menu: guida all'obiettivo, preparazione ai round, risorse e conseguenze della morte, accessibile anche dalla pausa;
  pannello impostazioni a due colonne, testi a capo, focus visibile, pulsanti con bordo ottone.
- Perdita del fuoco o sospensione dell'app: pausa esplicita, con ripresa volontaria. In cooperativa il mondo continua.
- Touch: mappa disponibile; il rilascio di un dito mantiene un comando ancora tenuto da un altro dito.
  La ripresa dalla pausa era già disponibile tramite il pulsante del menu.
- Cooperativa: fino a tre compagni elencati nell'HUD, vita relativa, distanza, attesa del piano e ritorno dopo la morte;
  rimozione delle righe alla disconnessione.
- Interazioni: candele, santuari e acquisti verificano anche gli ostacoli; un prompt selezionato prima che si chiuda
  una serranda non consente più l'uso. Il posizionamento dei santuari verifica il punto d'uso completo, con distanza
  e visuale effettive. La revisione cooperativa passa a `pe-coop-3` perché ciò può cambiare le posizioni generate.
- Gunplay: il Reliquiario interrompe correttamente le ricariche a cartucce; i muri vendono munizioni fino al limite
  consacrato; cambio arma, nuovo equipaggiamento e rinascita ripuliscono lo stato transitorio del tiro.
  La cadenza automatica conserva il tempo residuo tra i fotogrammi (verificata a 30/60/144 FPS).
- Movimento: scivolata attivabile con sprint+crouch; velocità che rientra a camminata/crouch quando si rilasciano
  i comandi; stick analogico che regola la velocità senza moltiplicarla accidentalmente.
- Ottiche: punto collimato, reticolo della lente e camera ingrandita usano l'asse hitscan del giocatore,
  indipendentemente da respiro, inerzia e rinculo visivo del modello. Eliminati l'origine spostata sulla lente
  e l'orientamento della camera ottica legato all'oscillazione dell'arma.

## Verifica ripetibile

Build Windows con Unity 6000.6.7f1, metodo `PryingEyes.EditorTools.BuildPlayer.Windows`, `-buildOut <cartella>`.
La cartella di verifica usata è `unity/PryingEyes/Builds/Audit`.

```powershell
python tools/check_presentation.py --shots
python tools/check_gunplay.py
python tools/check_gunplay.py --optics
dotnet run --project tools/planlab/PlanLab.csproj -c Release -- stats 1000
```

La prima prova attraversa i pulsanti veri del menu e la prima generazione del piano, cambia i cursori e verifica
la camera reale, alimenta sequenze multitouch (incluso annullamento), apre/chiude la pausa e controlla il pannello
compagni con uno stato ricevuto simulato e un server locale. Ripristina le preferenze cambiate dalla prova.
Scrive esito, log e schermate in `unity_logs/presentation`; senza `--shots` esegue solo la logica senza grafica.
La prova HUD con stato simulato non sostituisce una prova della rete a più processi.

## Risultati del 12 novembre 2026

- Build Windows di verifica: compilazione completata con zero errori (`unity_logs/audit-build.log`).
- Presentazione e interazioni: **33/33**, immagini dei cinque pannelli e dell'HUD compagni controllate;
  esito e log in `unity_logs/presentation`. Verificata la risoluzione 1280×720.
- Cooperativa reale a due processi, seme 2961: **16/16 ospite, 26/26 client**, incluso cambio piano e firma uguale
  (`unity_logs/audit-coop/coop_final_*.txt`), dopo l'aggiornamento a `pe-coop-3`.
- Santuari e perk: `-autotest -stealthtest -booktest -seed 1000` termina con **ESITO: ok**
  (`unity_logs/audit-shrines-1000.log`). La prova include prezzi, otto effetti, Consacrazione, Scrigno e morte.
- Prompt e posizionamento dei santuari nei temi teatro e sanatorio: `-booktest -shrineprompttest`,
  semi 2961 e 3046, piano 3, entrambe **SUPERATA** (`unity_logs/audit-shrines-2961.log`, `audit-shrines-3046.log`).
- Generatore puro: 1.000 piani senza fallimenti; non sostituisce la verifica fisica del controller nei piani costruiti.

Durante le verifiche è stato corretto anche il punto di osservazione della vecchia prova dell'altare:
in un caso accettava una posizione a 2,89 m dal punto d'uso, fuori dal raggio di 2,7 m. La selezione della
posizione ora controlla distanza tridimensionale e visibilità del punto d'uso reale.

### Armi, movimento e segnalazione sulle ottiche

- `gunplaytest`: **279/279** sulla build aggiornata (`unity_logs/gunplay/player.log`). Controlla i 28 prefab e i loro
  agganci, ADS, tutti i modi di fuoco, ricariche vuote/tattiche, bonus durante le ricariche a cartucce, acquisti
  consacrati, cambio slot, danno al corpo/testa, punti, muro e penetrazione. Prova anche il controller reale
  su un pavimento isolato con sprint, scivolata, crouch, camminata e mezzo stick.
- Prima delle correzioni: 32 fallimenti su munizioni/cambio arma (`unity_logs/gunplay-before.log`);
  ulteriori 9 su cadenza e movimento (`unity_logs/gunplay-motion-before/player.log`). Dopo: TARANTOLA 77 colpi
  in 4 s e MALLEUS 47 a ciascuna delle tre frequenze, contro rispettivamente circa 76,7 e 46,7 previsti.
- Rassegna visiva: generate 112 viste (mano, ADS, sparo, ricarica di ciascuna arma), tutte ispezionate nei quattro
  fogli `unity_logs/weapon-review-*.jpg`; dettaglio aggiuntivo di pistola, mitra, ottica, mitragliatrice e armi aliene.
  La rassegna ha preceduto la correzione ottica, verificata separatamente sotto. Le venature del legno troppo
  contrastate sono state rifatte il 13 novembre (vedi «Seconda passata»); resta da rifinire la leggibilità
  generale alle esposizioni basse.
- Segnalazione utente sul punto ottico: riprodotta con bersaglio a 20 m e sway. La camera della lente si discostava
  dall'asse hitscan di circa 0,37 m. `opticstest` ora supera **36/36**, su 14 mirini, di cui 6 ingranditi;
  verifica pixel del reticolo entro due pixel dall'asse, a riposo e con sway, più coerenza del raggio della camera
  ingrandita (scarto numerico inferiore a 5 mm a 20 m). Immagini e log in `unity_logs/optics`.
- Le regressioni `droptest`, `knifetest` e `booktest` superano la prova dopo le prime correzioni di munizioni
  (`unity_logs/gunplay-regression-*.log`). La presentazione resta **33/33** dopo la correzione ottica.

Le misure di cadenza usano il ciclo vero dell'arma con passi temporali controllati e riserva di prova illimitata;
non sono una misura delle prestazioni della GPU. Le prove balistiche usano collider di bersaglio controllati e il
vero percorso hitscan/danno. La verifica dei mirini richiede grafica; senza `--optics` il runner usa `-nographics`.

### Ritorno al menu e proporzioni dello schermo

- Aggiunto **Torna al menu** nella pausa, con conferma e annullamento. La conferma chiude la cooperativa,
  ripulisce la partita e ricarica la scena; la partita successiva riparte con pistola, 500 punti e zero uccisioni.
  Liberati anche audio transitorio, input touch e risorse della camera ottica.
- Menu e HUD usano una scala che conserva lo spazio previsto anche su schermi 4:3 e ultrawide;
  la minimappa segue la scala dell'HUD.
- `check_presentation.py --shots`: **61/61** a ciascuna risoluzione **800×600, 1280×720 e 2560×720**.
  Controlla anche limiti del testo, annullamento dell'uscita, due cicli di nuova partita (solo e server locale),
  assenza di duplicati, risorse ottiche e trasporto di rete dopo il ritorno al menu.
  Schermate e log in `unity_logs/presentation`, `presentation-800x600` e `presentation-2560x720`.
- Il nuovo controllo ha riprodotto una perdita del trasporto: la distruzione differita aspettava il tempo di gioco,
  fermo nel menu. La chiusura ora attende il tempo reale e libera anche l'elenco temporaneo dei prefab di rete.
  La prova che lo rilevava è passata da **58/59** a superata; aggiunti poi due controlli sulle risorse.
- Build di queste modifiche completata senza errori (`unity_logs/session-build.log`).
- Ripetuta la cooperativa reale sulla build aggiornata, seme 2961: **16/16 ospite e 26/26 client**,
  uscita regolare di entrambi i processi (`unity_logs/session-coop`).

## Seconda passata (13 novembre 2026)

### Navigazione con il controller vero

- Nuova prova `-botstrict` (`Scripts/Proc/FloorDirector.Bot.cs`) con il driver `tools/check_navigation.py`: un giocatore
  automatico cammina i piani generati con il CharacterController vero, senza teletrasporti di recupero, guarda ciò che
  vuole usare e accende le candele e prende l'ascensore con la stessa selezione del tasto F (`Interact.Current`: raggio,
  sguardo, visuale libera). Scrive una riga per piano e una per ogni INCASTRATO (fermo 4 s: collider a contatto con la
  capsula, stato del controller, tracciato e mappa a celle da 25 cm), VARCO, IRRAGGIUNGIBILE, NAVMESH, NONSELEZIONABILE,
  CADUTA, MORTE, TEMPO e SOFTLOCK; esce con 0 solo se l'ultima riga è `ESITO: ok`. Il controllo negativo `-botfreeze`
  (giocatore immobile) deve fallire, e fallisce: quattro INCASTRATO e poi SOFTLOCK.
- Primo giro, 48 semi × 6 piani (288 piani nei tre temi): tutti completati. Sei avvisi INCASTRATO erano falsi allarmi del
  rilevatore (la finestra dei 4 s non ripartiva dalla posizione giusta dopo l'accensione di una candela; il percorso si
  girava di 180° fra due strade quasi alla pari) e sono stati corretti nella prova, non nel gioco.
- Secondo giro con la prova corretta: 47 semi su 48 senza avvisi. Il caso reale è il seme 5008, piano 3 (Ristorante): la
  navmesh promette un passaggio fra una colonna e un divano che la capsula del giocatore non passa (servono 0,92 m:
  raggio 0,38 più 0,08 di margine per lato; la navmesh si accontenta di 0,8 m). Un giocatore sceglie un'altra strada,
  il bot seguiva la navmesh e restava fermo.
- Causa: `RoomKits.Free` lascia 4 cm fra arredi non collegati e il riparo di connettività controlla a 0,4 m di raggio,
  quindi restano varchi fra 0,6 e 0,95 m. Ingrossare la navmesh non è la soluzione (ProcCheck su 400 piani, riparazioni /
  oggetti rimossi / stanze svuotate / piani rotti: raggio 0,40 → 26/66/0/0; 0,42 → 37/93/0/0; 0,44 → 39/96/0/0;
  0,46 → 53/150/2/2; 0,50 → 186/591/50/44), quindi il raggio resta 0,4. Il difetto non è chiuso nel generatore:
  cambierebbe tutte le piante e la compatibilità della cooperativa.
- Nella prova: dopo due incastri nello stesso punto il bot toglie dalla navmesh l'area del varco (ostacolo che scava) e
  cerca un'altra strada come farebbe una persona. Se c'è, il piano si completa con un avviso VARCO (seme 5008 piano 3
  ora si completa); se non c'è è un vero soft-lock e la prova fallisce.
- Giro finale con il bot completo, sulla build di verifica con tutte le correzioni: 48 semi × 6 piani, 288 piani (Albergo
  180, Teatro 64, Sanatorio 44), **48/48 `ESITO: ok`**, nessun IRRAGGIUNGIBILE, NAVMESH, NONSELEZIONABILE, CADUTA, MORTE
  o SOFTLOCK. L'unico caso con avvisi è il seme 5008, piano 3: due INCASTRATO e un VARCO, piano completato. Log in
  `unity_logs/navigation_wide3` (ignorata da git; si rigenera con `python tools/check_navigation.py --seeds 5000 ...
  5047 --floors 6`). I piani sono vuoti di nemici: la prova misura la sola navigazione.

### Aspetto: legno delle armi

- In mira il calcio dell'AK usciva giallo paglierino con righe di tigre: luce dall'alto, vista radente e venature larghe e
  contrastate. Il materiale `legno` di `modelli/armi/mats.py` è stato rifatto (venature da circa 3 mm invece di 4,5, toni più
  scuri del 28%, fasce meno contrastate, superficie più ruvida) e le texture delle 8 armi che lo usano sono state
  ricalcolate e reimportate: AK Arcangelo, Predicatore, Rosario, Salterio, Vigilia .45, Cardinale .357, Cappellano, Turibolo.
  Foto prima e dopo a seme fisso (`-guntest -armi ... -seed 168272998 -floor 1`); in mano il legno resta bruno scuro,
  in mira ha venatura fine e non più a zebra. Non cambia nessuna regola di gioco.

### Testi dei pannelli d'acquisto

- Trovato con le stesse foto: nome e prezzo dell'arma sui pannelli a muro (`TextMesh`) si leggevano, specchiati e accesi
  nel buio, attraverso i muri e da dietro il pannello, perché lo shader dei font di Unity disegna senza test di profondità.
  Ora i testi si spengono quando il pannello non si vede davvero: da dietro, oltre 14 m o con un ostacolo (muro, colonna,
  porta) fra l'occhio e la fronte del pannello. A seme fisso lo stesso fotogramma mostrava il testo specchiato prima e non
  più dopo.

### Prestazioni e rassegna delle armi

- Sonda `-perftest` a 1080p sulla build di verifica (RTX 5060 Ti, piano 3, pianta a griglia): albergo 3,7 ms (268 fps),
  teatro 3,8 ms (261 fps), sanatorio 4,0 ms (253 fps); peggior p99 9,1 ms (Cripta dell'albergo, già così prima). Stessi
  valori della misura precedente alla seconda passata (269, 273 fps): nessuna regressione. Le due prime stanze del
  sanatorio sono salite a 4,9 e 6,1 ms, con la scheda condivisa con altre applicazioni; il resto è invariato.
- Rassegna completa `-guntest` (28 armi, mano, mira, sparo, ricarica, più i tre espositori) a seme fisso dopo le
  modifiche: tutte le armi si caricano, sparano e si vedono, e i testi dei pannelli si leggono ancora da davanti. La
  cadenza misurata sta entro l'8% della scheda per 27 armi su 28; il Predicatore (a leva) fa 94-97 colpi al minuto
  contro gli 85 di scheda, perché il ciclo vero è `cycle = 0,62 s` e la cifra di scheda non è usata in gioco (solo nel
  confronto del log): nessun effetto sul gioco, dato da allineare quando si rimette mano a `WeaponDefs.cs`.

## Terza passata (13 novembre 2026)

### Risorse della navigazione e varchi fra arredi

- Verificati sul remoto i commit `99444a0`, `6d49fa4` e `50ea55a`: `preview` locale e remoto coincidevano su `50ea55a`.
- Riprodotto il caso 5008/piano 3 sulla seconda passata: due incastri e un varco escluso dal bot prima di proseguire
  (`unity_logs/nav-review`). Il precedente ripiego nella prova non correggeva il percorso suggerito dal gioco.
- La navmesh ora usa esplicitamente celle da **10 cm**, mantenendo raggio 0,4 m e gradino 0,4 m. La discretizzazione
  ereditata dalle impostazioni di progetto perdeva precisione fra colonna e tavolo; aumentando la precisione il caso
  5008/piano 3 si completa senza incastri, senza modificare le ricette degli arredi o la capsula del giocatore.
  Il log diagnostico del bot ora riporta i parametri realmente usati, anziché quelli del progetto prima degli override.
- `ProcCheck.Run -seeds 400 -seed0 3000`: **400/400**, 3.960 stanze ordinarie, nessuna candela/uscita/cabina
  irraggiungibile dopo la riparazione, nessuna stanza svuotata. 14 piani riparati, 34 arredi rimossi; costruzione media
  18 ms e navmesh 26 ms (`unity_logs/nav-precision-400.log` e `.txt`). È una verifica navmesh, non del controller.
- Camminata con controller reale, semi 5000–5023, sei piani ciascuno: **144/144 piani, 24/24 partite**, nessun
  incastro né esclusione di varchi (91 Albergo, 29 Teatro, 24 Sanatorio; `unity_logs/nav-precision-wide`).
- Ripetuti sulla build finale sei piani per ciascuno dei semi 5008, 1000, 2961 e 3046: **24/24 piani**, senza avvisi,
  incluso il nuovo controllo che verifica un solo `NavMeshData` vivo dopo ogni transizione (`unity_logs/nav-final`).
- Perdita di memoria riprodotta: i dati nativi delle navmesh precedenti rimanevano vivi a ogni ricostruzione;
  tornando al menu restava anche la registrazione del piano precedente. `HotelNav` conserva e distrugge i propri
  dati, e `FloorDirector` li rilascia alla distruzione della scena. Il controllo su due ritorni al menu e nuove
  partite passa da **61/65 a 65/65** (`unity_logs/nav-resources-before`, `nav-resources-after`).
- Revisione cooperativa **`pe-coop-4`**: il formato dei messaggi non cambia, ma la navmesh più precisa può modificare
  riparazioni degli arredi e posizioni dei santuari. Tutti i partecipanti devono usare la build aggiornata.

### Segnalazione: acquisto di armi dall'altro lato del muro

- Riprodotta con pannelli sui quattro orientamenti: il retro era selezionabile e una camera dentro il collider del
  muro consentiva l'acquisto, perché il raycast non rileva il collider che contiene la sua origine. La vecchia prova
  controllava un muro interposto con la camera esterna, quindi non copriva questo caso (**69/77**, otto fallimenti;
  `unity_logs/wallbuy-before`).
- Selezione, acquisto e testo ora condividono il controllo del lato frontale del pannello e della visuale verso la sua
  faccia. Il prompt memorizzato viene comunque riverificato al momento dell'acquisto.
- Sulla build corretta: **77/77**, con grafica, incluso acquisto impedito dal muro e selezione consentita sul fronte
  nei quattro orientamenti (`unity_logs/wallbuy-after`). Build completata senza errori (`unity_logs/wallbuy-fixed-build.log`).
- Cooperativa reale a due processi su `pe-coop-4`, seme 2961: **16/16 ospite, 26/26 client**, stessa firma del piano
  dopo la discesa (`unity_logs/nav-coop`). Copiata la build finale nell'eseguibile principale in `Builds`, a gioco chiuso:
  189 file verificati con SHA-256 contro `Builds/Audit`.

## Presenze tematiche (13 novembre 2026)

- Su richiesta dell'utente, sostituiti la Falena e il Carapace con **Veglia** e **Portachiavi**: cero avvolto in un velo
  votivo strappato e casellario/reliquiario della reception con campanello e quattro chiavi snodate.
- Volumi torniti, plissettature e orli del tessuto, colature di cera, cassetti e maniglie, anelli e denti delle chiavi.
  Materiali con trama e patina, palette di cera, stoffa bruna, legno e ottone; eliminati ali viola e guscio a sei zampe.
- Animazioni proprie: movimento lento del velo, passo alternato delle quattro chiavi, inchino di attacco e collasso
  alla morte. Il Portachiavi anticipa l'attacco con il campanello, riprodotto anche sui compagni in cooperativa.
- Suoni dedicati di chiavi e respiro; il quinto round diventa **Processione dei Lumi**, con sole Veglie e rintocchi
  ascendenti. Restano i ruoli di combattimento, HP, velocità, ricompense e periodicità del round speciale.
- Indici numerici dei nemici conservati; revisione cooperativa `pe-coop-5` per mantenere nomi, modelli e segnali coerenti.
- La prova `-autotest -nofire -relictest` rende quattro angoli per modello in `unity_logs/relics`, controlla mesh finite,
  dimensione della geometria, animazione, punto debole e rilascio delle mesh generate. Le immagini sono viste del
  modello runtime, non concept art. La prova `-stealthtest -alientest` controlla anche navigazione e morte nella scena.
- Build senza errori (`unity_logs/relic-release-build.log`); rassegna modelli **24/24** (`relic-review.log`), prova nella
  scena **OK** (`relic-live.log`), potenziamenti e quinto round **ESITO: ok** (`relic-drops.log`), collisioni dei volanti
  **ESITO: ok** (`relic-push.log`: nessuno spostamento del giocatore fermo, nessuna caduta).
- Aggiornato l'eseguibile principale in `Builds` a gioco chiuso: 189 file uguali alla build di verifica (SHA-256).
- Cooperativa `pe-coop-5`, seme 2961: **16/16 ospite, 26/26 client**, uscita regolare di entrambi i processi
  (`unity_logs/relic-coop`).

## Sopravvivenza nel menu cooperativo (13 novembre 2026)

- Riprodotto un congelamento dei timer locali nel menu cooperativo: il mondo continuava, ma il ritorno anticipato
  di `PlayerController.Update` bloccava protezione di Rinascita, Velo, Occhio e sedativo. Una Rinascita seguita dal menu
  poteva quindi rendere immuni senza limite. La nuova prova falliva **82/88** (`unity_logs/life-before`).
- Separato l'avanzamento di vita ed effetti dai comandi del giocatore. In cooperativa i timer, la rigenerazione
  e il ritorno dopo la morte proseguono anche con il menu aperto; la pausa singola continua a sospenderli.
  La rinascita elimina il sedativo della vita precedente e rispetta lo stato del cursore del menu.
- Prova con grafica e menu reali: **88/88** (`unity_logs/life-after`), inclusi danno dopo la scadenza della protezione,
  morte e rinascita senza chiudere il menu, cursore utilizzabile ed effetti sospesi correttamente in singolo.
- Regressioni: armi e movimento **279/279** (`unity_logs/life-gunplay`); libri, perk, Rinascita, penalità di morte,
  Scrigno e Consacrazione **ESITO: ok** (`unity_logs/life-books.log`). Build senza errori (`life-fixed-build.log`).
- Cooperativa reale a due processi: **16/16 ospite, 26/26 client** (`unity_logs/life-coop`), con uscita regolare.
  Aggiornata anche la build principale a gioco chiuso: 189 file verificati con SHA-256 contro `Builds/Audit`.

## Fisica e azioni nel menu cooperativo (13 novembre 2026)

- Un secondo ritorno anticipato del controller congelava corpo e arma dopo l'aggiornamento dei timer di vita:
  il giocatore poteva restare sospeso in aria e la ricarica non terminava finché il menu rimaneva aperto.
  La prova riproduceva entrambi i difetti (**90/92**, `unity_logs/pause-physics-before`).
- Il controller ora continua a simulare gravità, collisioni e inerzia; l'arma porta avanti ricarica, estrazione e
  cicli già iniziati. I comandi vengono filtrati alla lettura in controller e arma: niente nuovi movimenti, salti,
  lean, mira, fuoco, cambio slot o cambio modalità dal menu. La pausa singola conserva il fermo completo del mondo.
- La prova usa anche comandi touch campionati prima di aprire il menu, due slot e un'arma con più modalità;
  verifica l'atterraggio con `CharacterController`, il caricatore riempito e l'assenza di azioni nuove nel menu.
- Prova definitiva con grafica **92/92** (`unity_logs/architecture-ui`), armi e movimento **279/279**
  (`unity_logs/pause-physics-gunplay`).

## Sale grandi e verticalità (13 novembre 2026)

- Nuova richiesta registrata integralmente in `docs/zombies-redesign.md`: spazi e verticalità, nuove forme e stanze,
  HUD, Veglia, animazioni e feedback degli attacchi. La revisione richiesta resta aperta.
- Prima passata sul generatore: moduli 12,8–17,6 m, più ambienti e sale a più moduli, più porte ad anello,
  accessi principali da 2,4–3 m, zone dei round ampliate per contenere le grandi sale.
- Nelle sale ampie, soffitti di almeno 8,2 m e percorso superiore largo 2,4 m a quota 3,6 m, con due scale da 18
  alzate di 20 cm. Il percorso a U collega le scale; porte e passaggi al piano terra restano liberi. Gli arredi
  rispettano scale e balconate, le superfici superiori sono camminabili dalla navmesh e ospitano punti di pattuglia.
- Pianificatore puro: **1.000/1.000**, circa 16–17 ambienti e 6.038 m² medi, percorso arrivo–uscita medio 142 m.
  Costruzione e navmesh: **400/400** piani senza problemi residui, 5.167 stanze ordinarie e **9.456 punti superiori
  raggiungibili**, nessuna stanza svuotata; tempi medi 43 ms costruzione e 117 ms navmesh (`architecture-400.log`).
- Camminata fisica di salita, attraversamento e discesa su due balconate nei tre temi: **21/21** per prova
  (`architecture-player.log`, `architecture-2961.log`, `architecture-3046.log`). Viste dalle due quote controllate
  in `unity_logs/architecture`. Ulteriori **12/12 piani completi** senza avvisi (`architecture-navigation`).
- Revisione cooperativa `pe-coop-6`, perché cambia il generatore. Nuovi ambienti specifici, forme curve, HUD,
  Veglia e feedback degli attacchi non sono ancora chiusi da questa prima passata.
- Regressione ottica: rimosso il legame della camera ottica con la gerarchia del modello oscillante. La misura
  di allineamento usa lo spazio camera, evitando l'errore numerico dell'inversione VP mondiale a 1 km dall'origine.
  Non è stata allargata la soglia: resta 5 mm a 20 m. **37/37**, incluso controllo negativo che rileva una camera
  spostata di 3 cm (`architecture-optics-final`); i log confrontano le due misure.
- Cooperativa sul nuovo generatore: **16/16 ospite e 26/26 client** (`architecture-coop`), con firma del piano
  uguale dopo la transizione. Build principale aggiornata a gioco chiuso, 189 file verificati con SHA-256.

## Seconda passata degli spazi: curve e nuove sale (13 novembre 2026)

- Aggiunte Rotonda, Galleria e Giardino d’inverno. Le pareti curve hanno mesh e collisioni; l’impronta condivisa
  esclude gli angoli da stanza, arredi e punti dei nemici. Finestre e acquisti rispettano i tratti rettilinei rimasti.
  La mappa usa bordi arrotondati e nomi delle nuove funzioni. La luce della Rotonda è stata rivista osservando
  il rendering reale (`unity_logs/architecture/rotonda.png`), senza modificare le preferenze del giocatore.
- **400/400** piani senza problemi residui, 5.164 stanze ordinarie più 386 segrete, nessuna candela, uscita o cabina
  irraggiungibile. Riparazioni limitate a nove arredi su due piani, nessuna stanza svuotata. Medie: costruzione
  38 ms e navmesh 107 ms (`curves-400.log`). **8.164 punti superiori raggiungibili**; **399 sale curve** senza
  angoli esclusi attraversabili dalla navmesh.
- Controller reale: salita e discesa delle balconate, parete curva che blocca visione e movimento, sedici tratti
  attorno alla Rotonda: **42/42** in albergo, teatro e sanatorio (`curves-light-player.log`, `curves-2961.log`,
  `curves-3046.log`). Teletrasporto usato solo per posizionare l’inizio delle prove, non per superarne gli ostacoli.
- **12/12 piani** completati senza avvisi con navigazione rigorosa su quattro semi (`curves-navigation`).
  Interfaccia **92/92**, comprese interazioni ostruite e acquisti sui quattro orientamenti (`curves-ui`).
- Mappa, pattuglie e passaggi bassi: `curves-griglia.txt`, **ESITO: ok**. Libri, altare, scrigno,
  bonus e morte/rinascita: `curves-libri.txt`, **ESITO: ok**, entrambi con uscita del processo 0.
- Cooperativa a due processi: **16/16 ospite, 26/26 client**, entrambi usciti con 0 in 76 s, firma del nuovo
  piano concorde dopo la transizione (`curves-coop`). Build principale aggiornata a gioco chiuso, 189 file
  verificati con SHA-256. Protocollo `pe-coop-7` per geometria e posizionamento deterministico.
  HUD, Veglia e feedback degli attacchi restano ambiti aperti; questa passata non completa l’audit generale.

## Terza passata: HUD da combattimento (13 novembre 2026)

- Dati di combattimento in sans di sistema con numeri grandi, mantenendo i titoli dell’hotel in serif.
  Vita/scudo/batterie/punti a sinistra, arma e munizioni a destra, navigazione in alto e compagni nell’angolo
  superiore destro. Sfumature scure sostengono i dati sui fondali chiari; numeri e descrizioni vanno a capo
  entro i loro spazi. I nomi delle armi adattano la dimensione quando serve.
- Vita e scudo mostrano valori numerici. Colore e vignetta della vita critica dipendono dal massimo effettivo,
  compresi i 150 punti di Pelle di pietra. Caricatore critico al 20% della capacità, anche con Consacrazione;
  avvisi distinti per ricarica in corso, caricatore vuoto con riserva e munizioni completamente esaurite.
- **157/157** verifiche di presentazione a **1280×720, 1024×768 e 2560×1080**, con schermate normali e durante
  danno/ricarica (`unity_logs/hud-alerts-*`). Inclusi tutte le armi consacrate con due slot, quattro perk,
  punteggio a sette cifre, round animato, conteggio durante rigenerazione e avvisi collegati alla ricarica reale.
  Il controllo del testo tiene conto dei pixel già scalati restituiti dal generatore per il best fit.
- Prima iterazione: un confronto rigido della vita nel copione falliva perché il personaggio rigenerava già salute;
  ora la verifica confronta il numero mostrato con la salute effettiva. La prova ultrawide ha inoltre corretto
  un doppio conteggio della scala nel verificatore; nessuna soglia di testo è stata allargata.
- Regressione delle armi sulla stessa build: **279/279** (`unity_logs/hud-gunplay`), processo terminato con 0.
- Build principale aggiornata a gioco chiuso, 189 file verificati con SHA-256.
- Questo chiude la prima revisione della gerarchia HUD richiesta. Restano le valutazioni durante combattimenti
  affollati, dispositivi reali, la riformulazione della Veglia e la revisione di animazioni e impatti dei nemici.

## Quarta passata: provenienza del danno e dardi (13 novembre 2026)

- Fino a quattro archi attorno al mirino indicano la provenienza dei colpi, si riallineano quando lo sguardo ruota
  e svaniscono in 0,85 s. Il segnale nasce solo quando il giocatore accetta il danno: nessuna falsa direzione
  durante immunità o pausa singola. `HurtPlayer` conserva la provenienza opzionale anche fra processi;
  protocollo aggiornato a `pe-coop-8`.
- Dardo con nucleo sottile, alone e scia rastremata; carica sfaccettata sul bastone. Sostituiti i dischi opachi dei
  contatti di bastone e magia con scintille e pulviscolo brevi, usando il pool degli effetti già esistente.
  Le clip di attacco restano quelle precedenti: la revisione delle animazioni non è chiusa da questo passaggio.
- Collisione continua lungo il segmento percorso, contro pareti e capsule dei giocatori: risolve il primo contatto,
  rispetta il dislivello e l’altezza accovacciata, infligge danno una volta sola. Le repliche restano soltanto grafiche.
  Rimossi collider e rigidbody ausiliari del dardo. Il raggio corporeo corrisponde ai 38 cm del controller.
- Corretti gli strati di scie, particelle e fori nel mondo: erano sullo strato 2 escluso dalla camera ottica.
  Ora restano visibili attraverso le ottiche; i bossoli fisici mantengono lo strato che esclude i raycast.
- **23/23** nella prova `-autotest -nofire -noenemies -combatfeedbacktest -combatshots` con grafica
  (`unity_logs/combat-pixels-player.log`). Include passo simulato da 10 m in un aggiornamento, parete spessa
  8 cm prima/dopo il bersaglio, colpo evitato accovacciandosi, ordine fra più giocatori, replica senza danno,
  quattro direzioni, rotazione, scadenza, immunità e maschere della camera ottica reale.
- Verifica delle particelle renderizzate su passaggio isolato: **723 pixel** sopra la soglia, oltre ai controlli di
  stato. Immagini controllate in `unity_logs/combat-feedback`: `dardo-impatto.png`, `particelle.png`.
  La scia è stata ridotta dopo la prima immagine, dove il nucleo sembrava una grande losanga piatta.
- Regressione dei nemici tematici: **temi: OK**, uscita 0 (`combat-temi.log`), inclusi sedativo, richiamo e ventaglio
  del Direttore. Cooperativa: **16/16 ospite e 27/27 client**, entrambi usciti con 0 in 76 s (`combat-coop`);
  la nuova verifica conferma che la provenienza del danno raggiunga il client attraverso la rete.
- Regressioni con gli effetti aggiornati: interfaccia **157/157** (`combat-ui`), armi **279/279**
  (`combat-gunplay`), ottiche **37/37** (`combat-optics`), tutti i processi usciti con 0.
- Build principale aggiornata a gioco chiuso; 189 file confrontati con SHA-256.

## Quinta passata: gesto e contatto della mischia (13 novembre 2026)

- Nuova clip `mischia`: 38 frame a 30 fps, contatto al frame 18 (0,6 s); Abate e Primario la rallentano a 0,85.
  Caricamento laterale del busto e del bastone, affondo a quota torace e recupero. Distingue gli incantesimi
  con stato `Incanto` e trigger `Lancia`. Gli importatori conservano questa configurazione se si ricreano i prefab.
- Pipeline: `modelli/monaco/genera_attacco.py` esporta solo lo scheletro dalla base animata; `EnemyMeleeImport`
  verifica **28/28 percorsi** compatibili su Monaco e Abate e genera `Mischia.anim`, preservando la trasformazione
  dell’armatura originale. Le mesh e le texture dei costumi non sono state rigenerate.
- Il bersaglio viene impegnato all’inizio del gesto. Direzione bloccata durante preparazione e recupero; contatto
  limitato al settore frontale, portata effettiva, dislivello e linea libera. Nessun danno anticipato o ripetuto.
  Uscita del bersaglio: il riferimento impegnato viene annullato. Colpo a vuoto: solo fruscio, senza falso impatto
  sul pavimento. Colpi ricevuti durante il gesto non desincronizzano la clip; i trigger precedenti sono ripuliti.
- **62/62** nella prova con controller e Animator reali (`melee-final-player.log`), comprese le sei varianti,
  mesh animate erette e nella scala prevista, fase visibile dell’affondo, recupero, arretramento fisico, schiena,
  parete comparsa durante la preparazione, evento di uscita e reazione al danno. Viste frontali e laterali delle
  tre fasi controllate in `unity_logs/melee`.
- Le prime immagini hanno rilevato la radice inclinata del file con sole ossa: corretta conversione degli assi e
  conservata la trasformazione dell’armatura del costume. La prima prova di arretramento teneva il bot immobile
  col blocco dello sguardo delle foto; ora usa il movimento vero. La lettura della fase avviene dopo la valutazione
  dell’Animator, evitando una posa precedente quando la prima cattura blocca a lungo il fotogramma.
- Cooperativa: **16/16 ospite e 28/28 client**, uscita 0 in 76 s (`melee-coop`), inclusa la nuova verifica della
  preparazione visibile sul client. Protocollo **`pe-coop-9`**. Nemici tematici: **temi: OK**, uscita 0 (`melee-temi`).
- La build completa ha rilevato una creazione nativa di `MaterialPropertyBlock` nel costruttore di `Weapon`;
  spostata al primo aggiornamento dell’ottica. Le build complete successive non riportano l’eccezione.
- Regressioni dopo la nuova clip e la correzione del materiale ottico: interfaccia **157/157** (`melee-ui`),
  armi **279/279** (`melee-gunplay`), ottiche **37/37** (`melee-optics`), uscite 0.
- Build principale aggiornata a gioco chiuso, 189 file verificati con SHA-256. Scene e materiali ricreati dalla
  build sono stati confrontati semanticamente: conservati gli originali, evitando modifiche dei soli identificatori.
- Resta da rifinire la torcia ravvicinata: alcune superfici dei costumi saturano nelle immagini. Restano inoltre
  Veglia e gesti dei non umanoidi, valutazione delle orde, audio ascoltato e gli altri ambiti generali sotto elencati.

## Sesta passata: luce ravvicinata e costumi (13 novembre 2026)

- Riprodotta la perdita dei dettagli chiari sui costumi: il controllo precedente misurava solo le pareti lungo
  l’asse della camera; la maschera escludeva i nemici e l’alone non veniva attenuato. La correzione arrivava inoltre
  al fotogramma successivo. Ora nove sonde sferiche seguono il fascio effettivo, includono i nemici, escludono
  portatore/arma e ignorano i contatti radenti col pavimento. Riduzione immediata, recupero graduale, cono più
  ampio da vicino; fascio e alone applicano la stessa attenuazione dopo il posizionamento della luce.
- Prova iniziale **21/31**, uscita 1 (`torch-reference-player.log`): superfici appiattite fino al 53,16% sul costume
  dell’Infermiere e 70,69% sulla parete. La soglia RGB assoluta non rilevava correttamente il problema, perché
  il grading porta il bianco visibile sotto 255: il confronto usa ora una maschera bianca nello stesso render.
- Il primo adattamento lasciava il monaco troppo scuro. Ribilanciati i soli colori diffusi dei tre tessuti, sia
  nei materiali distribuiti sia nell’importatore; nessuna soglia di leggibilità allentata. Aggiunto margine di
  potenza vicino alle superfici dopo una prova con due torce sovrapposte fallita **52/53** (`torch-final-player.log`).
- Esito finale **53/53**, uscita 0 (`torch-balanced-player.log`): modelli reali Monaco/Infermiere a 0,9, 1,8 e 8 m,
  soggetto fuori asse di 22°, parete e torcia remota reale, sovrapposizione di due fasci e recupero a distanza.
  Sui soggetti resta sotto il 4% la quota di bianco appiattito; la parete con due torce scende dal 33,11% a 0%.
  Preferenza di luminosità verificata prima/dopo: **0,091**, conservata anche su disco.
- Costo della misura: **4,73 µs**, media di 200 chiamate nella scena isolata di prova. La precedente lettura di
  **0 byte gestiti non dimostra l'assenza di allocazioni**: la controprova introdotta nella nona passata (un array
  noto di 4 KB mantenuto vivo) restituisce anch'essa zero nel player Unity usato. La suite ora segnala il contatore
  non disponibile e omette quella sola asserzione; restano 52 verifiche valide.
  Questo dato riguarda la misura di prossimità con pochi collider, non le prestazioni complessive di un’orda.
- Regressione della mischia con grafica: **62/62 in ciascuno dei tre temi**, uscite 0 (`torch-melee-albergo`,
  `torch-melee-teatro`, `torch-melee-sanatorio`). Controllate le immagini frontali e laterali, compresi i costumi
  nelle stanze effettive; iride e tessuto restano riconoscibili. Build completa riuscita senza errori.
- La modifica riguarda la presentazione della luce: il segnale acceso/direzione usato dalla percezione e il
  formato dei messaggi restano invariati. Protocollo **`pe-coop-9`**.
- Cooperativa a due processi: **16/16 ospite e 28/28 client**, uscite 0 in 75 s (`torch-coop`). Interfaccia e
  interazioni: **157/157**, uscita 0 (`torch-ui`), comprese le protezioni contro acquisti attraverso i muri.
- Build principale aggiornata a gioco chiuso: **189 file verificati con SHA-256**. Archiviate le rigenerazioni
  della scena e dei materiali delle armi; confronto semantico con gli originali riuscito prima della pulizia.

## Settima passata: Veglia e replica della mischia (13 novembre 2026)

- Riformulata la Veglia come effigie votiva sospesa: maschera con orbita realmente scavata, colature di cera,
  aureola metallica, tre ceri e pannelli di tessuto con normali morbide. Superata la sagoma cilindrica con teli
  rigidi. Preparazione con inclinazione del corpo, dilatazione dell’iride e allungamento delle fiamme; il punto
  debole segue la maschera. Fiamme a due materiali emissivi e spegnimento alla morte, senza luci aggiuntive.
- Le prime immagini hanno rilevato normali nulle sulla maschera e sul velo: il prodotto vettoriale fra differenze
  molto piccole finiva sotto la soglia di `Vector3.normalized`. Normalizzati prima i due vettori tangenti. Un secondo
  problema interessava le facce degli orli da 3 mm: corretto `MeshBuilder.Quad` usando la lunghezza effettiva,
  dopo il controllo dei degeneri. `QuadW` sceglie gli UV dalla direzione senza azzerare il vettore. La verifica
  delle normali ha fallito **49/52** prima della correzione degli orli (`vigil-fixed-player.log`).
- Trovato un errore nella replica: `strikeAt` diminuiva due volte, `atkLock` non diminuiva. Riprodotti preparazione
  troppo breve e recupero che non terminava (`vigil-puppet-red-player.log`). Ora ogni timer avanza una sola volta;
  la nuova prova usa il pupazzo con eventi reali, attende le tre fasi e verifica la durata. Mischia con grafica:
  **65/65**, uscita 0 (`vigil-puppet-final-player.log`). La prova iniziale senza foto segnalava anche sei confronti
  delle pose; la verifica completa delle pose è quella con `-meleeshots`, senza allentarne le soglie.
- Verifica finale dei modelli: **54/54 in ciascuno dei tre temi**, uscite 0 (`vigil-release-albergo`,
  `vigil-release-teatro`, `vigil-release-sanatorio`). Comprende vertici finiti, normali valide, budget geometrico,
  posa di preparazione, allineamento del punto debole, recupero, spegnimento e rilascio di mesh/materiali.
  Veglia **14.507 vertici**, Portachiavi **5.411**; entrambi sotto il limite della prova di 18.000. Viste a quattro
  angoli e immagini nell’ambiente reale con torcia e luminosità dell’utente. Sono prove di presentazione delle
  pose, non una verifica nuova del danno dei volanti; quella rimane da completare.
- Regressione dei percorsi fisici con la correzione di `MeshBuilder`: **42/42**, uscita 0 (`vigil-architecture`),
  comprese salita/discesa delle balconate e circolazione della Rotonda. Build completa e successiva build del
  codice riuscite. Resta da misurare il costo della nuova Veglia durante le orde.
- Cooperativa a due processi: **16/16 ospite, 28/28 client**, uscite 0 in 76 s (`vigil-coop`). Nessun cambio al
  formato di rete o alle regole del piano: protocollo **`pe-coop-9`**. Build principale aggiornata a gioco chiuso,
  **189 file verificati con SHA-256**. Scena/materiali rigenerati confrontati semanticamente e archiviati prima
  di ripristinare gli originali, senza includere cambi dei soli identificatori.

## Ottava passata: affondo e contatto dei volanti (13 novembre 2026)

- Riprodotto il comportamento precedente con controller reale: **37/68**, uscita 1 (`flying-red-player.log`).
  Occhio, Veglia e Medusa continuavano a inseguire durante la preparazione; schiena e schivata laterale potevano
  ancora subire danno, mancava il recupero e l’uscita del bersaglio non annullava quel contatto.
- Nuovo gesto: preparazione 0,55 s, contatto a 0,71 s, recupero fino a 1,13 s. Direzione e bersaglio impegnati;
  corsa fino a 85 cm con SphereCast dell’intero passo, senza scorrere sul muro durante l’affondo. Corsa ridotta
  entro 75 cm dalla destinazione iniziale per evitare il superamento del bersaglio quando è già vicinissimo.
  Raggio di danno 1,2 m in 3D, cono frontale e linea libera verificati al contatto. Avvicinamento a circa 1,55 m
  tra gli attacchi, anziché puntare a sovrapporsi alla testa del giocatore.
- Animazioni procedurali guidate dalla stessa fase su ospite e replica: Veglia arretra/inclina/rilascia la posa,
  Medusa contrae campana e tentacoli; Occhio usa il gesto degli occhi e lo scatto reale. Gli effetti di contatto
  comuni, anche degli umanoidi, vengono posti sulla superficie alta rivolta al nemico invece che dentro il corpo
  del giocatore, fuori dalla visuale. Nessun effetto di impatto su un colpo mancato.
- **117/117 in ciascuno dei tre temi**, uscite 0 (`flying-albergo`, `flying-teatro`, `flying-sanatorio`): tre specie,
  nessun inseguimento durante preparazione/recupero, affondo fisico, bersaglio fermo e molto vicino, direzione
  del danno, particelle nel campo visivo, schiena, schivata laterale e arretramento col controller, parete che blocca
  danno e corpo, bersaglio uscito, replica con direzione impegnata, tempi corretti e nessun danno autonomo.
- Regressioni dopo il nuovo contatto comune: mischia degli umanoidi **65/65** con grafica (`flying-melee-regression`),
  dardi/feedback **23/23** (`flying-fx-regression`), uscite 0. Le immagini dei tre temi sono archiviate nei rispettivi
  percorsi sotto `unity_logs`, comprese preparazione e contatto.
- Protocollo **`pe-coop-10`**: invariato il formato dei messaggi; l’evento `Attack` dei volanti porta la direzione
  impegnata e `Strike` ne annuncia anche il contatto/mancato contatto. Le build precedenti hanno tempi e uso
  degli eventi diversi, quindi l’ammissione evita di mescolarle.
- Prova di spinta con dieci Veglie attive per 14 s: uscita 0 (`flying-push-player.log`), spostamento massimo
  del giocatore **0,00 m**, quota minima **0,05 m**, distanza minima dei volanti **0,75 m**, **zero cadute**.
  È una verifica di collisioni e stabilità del controller, non una misura delle prestazioni di rendering dell’orda.
- Cooperativa con nuova fase dedicata all’affondo: **17/17 ospite e 32/32 client**, uscite 0 in **84 s**
  (`flying-coop`). Il client osserva preparazione e spostamento del nemico appena annunciato, riceve il danno
  dall’ospite e ne mostra la provenienza. Build principale aggiornata a gioco chiuso: **189 file verificati SHA-256**.

## Nona passata: verifica indipendente (13 novembre 2026)

La nona passata (`a675b19`: atri a tre quote, nemici a gruppi, attacchi senza anelli) è stata consegnata con il limite
d'uso esaurito. Prima di aggiungere altro è stata rieseguita per intero sul codice committato, con la build di verifica
(`Builds/Audit`), in modalità senza finestre e senza input iniettato:

- mischia degli umanoidi **71/71**, creature procedurali (Veglia, Portachiavi) **63/63**, volanti **117/117**,
  dardi e feedback **23/23**, torcia **52/52**;
- architettura **58/58** (albergo), **74/74** (teatro), **74/74** (sanatorio); interfaccia **157/157** a 1280×720,
  800×600 e 2560×720; armi **279/279**; round furtivo: esito ok;
- cooperativa a due processi **17/17 ospite e 32/32 ospitato**; bot rigoroso della navigazione **2/2 piani**, zero avvisi.

Nessuna correzione è stata necessaria. Il costo di rendering delle orde, rimasto aperto, è la passata successiva.

## Decima passata: costo di rendering delle orde (13 novembre 2026)

Misura con la sonda `-perftest -perfhorde` (1920×1080 in una `RenderTexture`, `camera.Render()`, RTX 5060 Ti, piano 1
seme 1000, nemici in fila davanti al giocatore; millisecondi per fotogramma, medie su 120 fotogrammi dopo 25 di riscaldamento). A stanza vuota il
fotogramma costa **3,2 ms**.

| Caso | Prima (costo pieno) | Dopo |
| --- | --- | --- |
| 24 umanoidi (Monaco, Abate, Maschera, Direttore, Infermiere, Primario) | **113,4 ms** (82 ms in un'altra corsa) | **9,4 ms** |
| 16 misti (Monaco, Abate, Portachiavi, Veglia) | **28,9 ms** | **8,5 ms** |
| 16 Veglie, 32 Veglie | 3,9 ms, 5,8 ms | invariate |

Causa: un umanoide ha 110–170 mila vertici (le fibre sfilacciate della tunica sono 62 mila da sole), 24 corpi sono
**3,7 milioni di vertici** da ricalcolare con la pelle a ogni fotogramma e, per ogni luce con ombra, da ridisegnare.
I LOD automatici di Unity non aiutano: i `SkinnedMeshRenderer` restano al livello 0 e, anche forzando il livello,
accorciano solo gli indici mentre il buffer dei vertici resta intero (misurato: livello 4 forzato = 17,8 ms, contro
9,0 ms con la mesh compatta dello stesso livello).

Tre leve, tutte in `Enemy.Budget.cs` e ricalcolate da `LightPool.Tick` cinque volte al secondo:

1. **Budget delle ombre**: fanno ombra solo i corpi più vicini a chi guarda, entro 14 m e fino a un peso massimo
   (un umanoide pieno = 1, un occhio volante ≈ un settimo; budget 4,5), con isteresi di 1,5 m. Un'ombra in più non
   si paga mai su un corpo lontano.
2. **Politica del dettaglio**: la mesh di ogni parte scende con la distanza (soglie 3, 5, 8, 12, 17 m, accorciate
   fino a un terzo quando in scena ci sono molti corpi pieni, isteresi di 0,6 m); le fibre sfilacciate spariscono oltre
   4,5 m e la sottoveste e il metallo oltre 8 m. In mischia il corpo è sempre intero.
3. **Scala di mesh compatte** (`BodyLodLibrary`, `Resources/BodyLods.asset`, 25 MB binari): per ognuno dei LOD
   automatici l'editor (`BodyLodBuilder`) estrae una mesh a sé, con solo i vertici di quel livello e gli stessi pesi e
   pose di legatura; a runtime si cambia `sharedMesh`, quindi la pelle elabora davvero meno vertici (1,03 milioni
   invece di 3,73 alla nascita di 24 corpi). I `localBounds` originali vengono ripristinati dopo il cambio.

Se i modelli vengono rifatti, la scala va rigenerata (`Unity.exe -batchmode -projectPath … -executeMethod
PryingEyes.EditorTools.BodyLodBuilder.Run -quit`): la build avvisa (`[BodyLod] AVVISO`) quando il numero di vertici o di
ossa non corrisponde più, e nel frattempo i corpi ricadono sui LOD automatici senza guasti. Le foto della sonda
(`24-umanoidi.png`) mostrano costumi, accessori e aureole intatti alle distanze di gioco. La sonda accetta ora
qualsiasi scenario in `-hordeonly` (conteggio, tipo e modificatori nel nome: `-lodN`, `-scalaN`, `-nofili`, `-parti`,
`-bone1`, `-tutte-ombre`, `-senza-ombre`, `-dettaglio-pieno`, `-ferme`) e scrive nella colonna `con_ombra` quanti corpi
fanno ombra.

Regressioni con la build che contiene la scala, tutte con gli stessi conteggi della nona passata: **71/71** mischia,
**63/63** creature procedurali, **117/117** volanti, **23/23** dardi, **52/52** torcia, **58/58, 74/74, 74/74**
architettura, round furtivo ok, **157/157** interfaccia in tre risoluzioni, **279/279** armi, cooperativa **17/17 e
32/32**, bot rigoroso **2/2 piani**. La prima nascita di un umanoide nel processo, che carica anche la libreria, è passata da 57 a 71 ms (una volta sola);
le altre costano in media circa 1 ms per portare il corpo al dettaglio della sua distanza.

## Undicesima passata: ripari nelle sale (13 novembre 2026)

Richiesta rimasta aperta nel filo di Codex: «le stanze sono più grosse ma sembrano troppo vuote». Prima di cambiare
qualcosa le sale sono state misurate: il flag `-roomcensus` di `ProcCheck` scrive per ogni stanza `CENSUS` (oggetti,
area coperta da arredi solidi) e, per le sale trattate dalla passata nuova, `COVER` (obiettivo, prima, dopo, tentativi).
Su 90 piani (seme 3000, 1427 stanze, 1201 sopra i 200 m²) le 585 sale grandi che la passata nuova tratta (sopra i 150 m²,
senza corridoi e stanze con impianto proprio) avevano arredi solidi sul **3,7%** del pavimento (stanze di guardia 3,1%,
arene 4,7%, boss 4,1%); il 64% delle sale sopra i 200 m² stava sotto il 4%, 33 erano senza alcun oggetto, 115 Gallerie
su 201 sotto il 3%.

Cause: le ricette mettono arredi a muro e qualche colonna, e in tre sale grandi su quattro (quelle con balconata)
`RoomKits.Dress` riservava a terra tutta la fascia sotto i solai: 4,2 m per lato (6,8 nell'atrio a tre quote) lungo
tutta la sala, più la testata. Con i tracciati davanti alle porte non restava quasi pavimento libero. Una prima versione
della passata nuova, che teneva 1,6 m anche dai tracciati riservati, non piazzava quasi nulla (il 99% dei tentativi
bloccato): ora `Ctx.footprints` separa gli ingombri veri degli arredi dai tracciati riservati.

Cosa è cambiato:

- **`RoomKitsCover.cs` (`ArenaCover`)**, ultima ricetta dopo colonne, panche e candelabro: porta arene (11%), boss (10%) e
  guardie (9%) verso un obiettivo di copertura con pezzi del tema (divani, armadi, banconi, file di sedie, letti,
  paraventi, panche, sarcofagi...), soli o in fila di fino a tre come una barricata, a 1,3 m dagli altri arredi e a 1,2 m
  dai muri (corsie larghe quanto servono a navmesh e capsula). Salta corridoi, ascensori, rotonda, navate, platea, corsie e
  palchi, che hanno un impianto proprio, e le stanze speciali. Ha un generatore a parte, quindi non sposta i numeri
  che le ricette successive (luci, nascite) traggono da quello della stanza.
- **`ReserveBalconies`**: a terra restano riservate solo le scale (da x0+2 per 7,2 m più mezzo metro) e un metro attorno a
  ogni colonna dei solai. Il pavimento sotto i solai (3,3 m liberi) è `Ctx.underSlab`: vietato alle altre ricette
  (`Free`), aperto ai soli ripari alti al massimo 3 m.
- **`-nocover`** riporta generatore e riserve a com'erano (confronti a parità di seme). Protocollo cooperativo **`pe-coop-12`**.

Risultato sugli stessi 90 piani: copertura **3,7% → 7,0%** nelle sale trattate (8948 pezzi, circa 15 a sala, 6293 sotto i
solai): sale basse 4,8 → 7,7%, con balconata 3,0 → 6,3%, atri a tre quote 4,5 → 7,8%; arene 4,7 → 7,8%, boss 4,1 → 7,6%,
guardie 3,1 → 6,5%. Sopra i 200 m²: 441 sale su 1201 sotto il 4% (prima 765), zero sale senza oggetti (prima 33), una
sola Galleria sotto il 3% (prima 115). Gli obiettivi 9–11% non vengono raggiunti ovunque: la ricerca si ferma a 400
tentativi e lo spazio libero con i corridoi larghi 1,2–1,3 m non basta. Non è stato abbassato il margine, perché con i
varchi sotto 1,2 m la navmesh chiude la strada (vedi la nota sul raggio della navmesh nelle passate precedenti).

Verifica: `ProcCheck` su 400 piani (seme 3000, `-fast`): 8 piani con problemi prima della riparazione, **0 rotti dopo**,
0 stanze svuotate (55 arredi tolti dai varchi). Build di verifica (`Builds/Audit`, solo script) senza finestre e senza
input iniettato: mischia **71/71**, creature **63/63**, volanti **117/117**, dardi **23/23**, torcia **52/52**,
architettura **58/58, 74/74, 74/74**, round furtivo ok, interfaccia **157/157** in tre risoluzioni, armi **279/279**,
cooperativa **17/17 ospite e 32/32 ospitato** con `pe-coop-12`, e il bot rigoroso della navigazione su **6 semi × 5 piani =
30 piani, 6/6 completati**. Foto delle sale (`unity_logs/architecture`, scattate dalla suite di architettura): file di
panche nella chiesa, tavoli lunghi nel refettorio, vista dalla balconata alta sul pavimento arredato.

Costo dei pezzi in più, con la sonda `-perftest` (1080p in una `RenderTexture`, RTX 5060 Ti, stessi piani con e senza
`-nocover`): albergo (seme 6034, 15 stanze) **3,5 → 3,7 ms** medi, peggior p99 6,3 → 6,4 ms; teatro (seme 6000) **3,4 → 3,5 ms**,
peggior p99 5,5 → 5,9 ms. Circa 0,1–0,2 ms per fotogramma, nessuna sala fuori budget.

Il bot ha trovato due cose, nessuna dovuta ai ripari (le ho riprodotte con `-nocover`):

1. **Un difetto del bot** (corretto): alla fine del piano 5 del seme 1000 (sanatorio) il bot risultava isolato («uscita
   irraggiungibile»). Dopo l'ultima candela la strada per l'uscita tornava indietro e la logica «tira dritto per un secondo» al bivio, nata per
   evitare lo sfarfallio tra due strade alla pari, lo portava nel vicolo oltre la candela: tra candela e muro la navmesh non passa
   (agente 0,8 m), il controller sì, quindi il bot entrava e non poteva più uscire. Ora al cambio di bersaglio quello stato si azzera. Il vicolo è
   comunque uno spazio che i nemici (navmesh) non raggiungono: non è un blocco per il giocatore.
2. **Un avviso noto, non causato dai ripari**: seme 2961 piano 2 (albergo, navata con le panche), uno o due `INCASTRATO` e piano completato
   con avviso, uguale con e senza ripari. È la classe «la navmesh promette un varco che la capsula non passa»
   della terza passata (fessure tra 0,6 e 0,95 m tra arredi vicini); la regola di generazione che la chiuderebbe
   cambierebbe tutte le piante e resta da decidere.

Il bot stampa ora, quando la navmesh non collega partenza e bersaglio, dove finisce il percorso parziale e in che stanza, la
raggiungibilità di candele, partenza e uscita e una mappa a componenti (`a` dal giocatore, `M` dalla partenza) di 16 × 28 m.
`tools/coop/run_coop.ps1` usa di serie la build di verifica (`Builds/Audit`) e non quella da cui gioca l'utente.

## Dodicesima passata: distinguere impatto fisico e magico (13 novembre 2026)

Il contatto della mischia usava ancora `Fx.EnemyImpact`, con nove scintille additive colorate come l'incantesimo.
La nuova verifica ha riprodotto il difetto nei sei umanoidi: **80/86**, con le sole sei asserzioni sull'impatto
fisico fallite nella corsa con immagini (`unity_logs/physical-melee-red-shots`).

`Fx.MeleeImpact` usa invece sei piccoli frammenti opachi e due sbuffi di polvere di tessuto, senza emissione,
luci, anelli o materiali nuovi. Durano 0,12–0,28 s e riutilizzano lo stesso pool degli altri impatti.
`Enemy.PlayMeleeContact`, comune a ospite e replica, sceglie questo effetto per gli attacchi a terra, compreso il
Portachiavi. Occhio, Veglia e Medusa conservano il contatto magico. Restano invariati danni, tempi e messaggi di rete.

La suite controlla assenza di scintille additive, presenza nel campo visivo, colpi schivati senza falso impatto,
stesso effetto sulla replica, nessun danno autonomo della replica e scomparsa di tutti gli elementi dopo 0,35 s.
È inoltre stata corretta la modalità senza immagini della suite: nel player batch le ossa con
`CullUpdateTransforms` potevano restare ferme senza fotogrammi presentati. Una piccola RenderTexture aggiornata
durante la prova mantiene effettiva la verifica delle pose, senza forzare l'Animator né modificarne il culling.

Verifiche (`unity_logs/physical-melee-final`): mischia **90/90** nei tre temi e **90/90** anche senza salvare immagini;
volanti **117/117**, dardi/feedback **22/22** (senza la fotografia aggiuntiva della modalità `-feedbackshots`).
Le schermate di contatto mostrano ancora un possibile difetto distinto sul Monaco dopo un colpo ricevuto:
il costume appare completamente bianco. È da diagnosticare il ripristino dell'emissione nei fotogrammi lunghi;
questa passata non dichiara conclusa la qualità delle animazioni o dell'illuminazione in combattimento.

## Tredicesima passata: ripristino del costume dopo i colpi (13 novembre 2026)

Il Monaco completamente bianco nelle immagini della passata precedente non era solo una questione di esposizione.
Il timer del lampo veniva prima decrementato, poi il materiale veniva aggiornato soltanto se il timer era ancora
positivo. Un fotogramma di almeno 0,2 s lasciava quindi l'emissione bianca per sempre; anche con fotogrammi brevi
l'ultimo colore restava parzialmente schiarito e sovrascriveva l'emissione originale con un valore generico.

Riproduzione nel player reale: `-autotest -nofire -noenemies -meleetest -hitflashtest`.
Con il vecchio avanzamento estratto senza cambiarlo: **122/212**, uscita 1 (`unity_logs/hitflash-red`). Tutti e dieci
i tipi di nemico fallivano il ripristino dopo 0,25 s, quello a riposo e quello dopo colpi ripetuti con passi di 16 ms,
nelle tre vie: danno sull'ospite, evento Hit ricevuto e danno previsto sul pupazzo. I materiali del vicino restavano
corretti: esclusa la condivisione accidentale come causa di questo difetto.

Ora ogni nemico conserva l'emissione iniziale dei propri materiali. `BeginHitFlash` è comune alle tre vie;
`AdvanceHitFlash`, usato da Update, scrive anche il fotogramma conclusivo e torna esattamente ai colori iniziali,
senza lavoro per materiale quando il lampo è spento e senza allocazioni aggiunte per colpo.
La stessa prova passa **212/212**, uscita 0 (`unity_logs/hitflash-final/flash`). La suite esercita direttamente
l'avanzamento usato nel gioco con tempi controllati, senza modificare il tempo globale o simulare materiali fittizi.

Regressioni con grafica: mischia **90/90**, creature procedurali **63/63**, volanti **117/117**. Cooperativa a due
processi: **17/17 ospite e 32/32 client**, uscite 0 in 84 s (`unity_logs/hitflash-coop`), protocollo invariato.
Verificata anche l'immagine del contatto
del Monaco dopo un danno reale: il costume torna scuro con i dettagli del tessuto, invece della sagoma bianca
(`unity_logs/hitflash-final/Monaco_after.png`). La schermata precedente archiviata del sanatorio appartiene a un
altro piano e non è un confronto quantitativo della luminosità. Restano da rifinire gesto e leggibilità
complessiva durante una partita: questa correzione chiude il lampo persistente, non l'intero lavoro sugli attacchi.

## Quattordicesima passata: presa del bastone durante l'affondo (13 novembre 2026)

L'arma era animata indipendentemente dal corpo e il braccio sinistro la seguiva con IK. La traiettoria dell'affondo
portava però la presa oltre la lunghezza del braccio: nello scheletro sorgente la mano si staccava fino a **7,306 cm**
al fotogramma 21,25. La nuova verifica del generatore fallisce prima dell'esportazione; misura 153 pose, ogni quarto
di fotogramma, e rifiuta una distanza mano/presa sopra 3 mm. Comando ripetibile:
`blender --background --factory-startup --python-exit-code 1 --python modelli/monaco/genera_attacco.py -- check`.

Difetto confermato anche sul player e sui modelli importati (`unity_logs/grip-red`): mischia **102/108**, uscita 1,
con i soli sei controlli della presa al contatto falliti. Scarti al contatto **2,44–7,86 cm** nei sei umanoidi;
preparazione e recupero conservavano la presa.

La traslazione del bastone nei fotogrammi 18 e 21 è ora più vicina al corpo di 12–13 cm. La rotazione dell'arma,
l'avvolgimento del busto, il passo fisico e i tempi di contatto e recupero sono conservati. La presa resta raggiungibile
anche tra le chiavi, senza allungare il braccio o cambiare il modello: errore massimo nello scheletro **0,005 mm**.
Il controllo impedisce future esportazioni con una presa irraggiungibile. Rigenerati `mischia.fbx` e `Mischia.anim`;
importazione **28/28 percorsi compatibili** su Monaco e Abate, durata invariata **1,267 s**.

Build completa e prove con grafica nei tre temi (`unity_logs/grip-final`): mischia **108/108** per tema, compresi
preparazione, contatto e recupero su tutti e sei i costumi. Lo scarto massimo osservato nei tre temi è **4,3 mm**
(l'interpolazione del player non coincide con il campionamento di Blender), sotto la soglia di 15 mm nel mondo.
Superata anche la regressione dei lampi **212/212**. Immagine laterale dell'Abate controllata: arma nella mano durante
l'affondo, senza perdita della figura eretta. Cooperativa: **17/17 ospite e 32/32 client**, uscite 0 in 84 s
(`unity_logs/grip-coop`); nessun cambio alle regole o al protocollo. Resta aperta la valutazione del ritmo e della
varietà dei gesti in partita.

## Quindicesima passata: inseguimenti verticali e spinta della folla (13 novembre 2026)

La raggiungibilità della navmesh e la camminata del giocatore non provavano ancora il comportamento dei nemici
in gruppo. Aggiunta `-architecturetest -architecturepursuit`: otto agenti reali, con sei tipi di corpo, inseguono
il giocatore da 7,2 m a terra e viceversa. Si controllano navmesh attiva, passaggio alla quota intermedia, assenza di
salti verticali e arrivo con preparazione della mischia. Nella prova di percorso si rimuove chi è arrivato e prepara
il colpo, liberando la prima fila: un bersaglio immortale non deve trasformare la coda dietro gli attaccanti in un
falso errore di navigazione. La variante `-pursuitpressure` tiene invece tutti vivi per 120 s per direzione e misura
lo spostamento del giocatore fermo. Nessun Warp o recupero degli agenti durante le due prove.

La prima verifica dei soli arrivi dava 54/54 ma non misurava lo spostamento del bersaglio: non era sufficiente.
La verifica estesa ha rilevato spostamenti di **1–3 m**, in un caso con discesa involontaria del giocatore dalla
balconata. In `unity_logs/pursuit-isolate` e `pursuit-obstacle` sono conservati i confronti che isolano le cause:
disabilitare solo il passo della mischia o solo le collisioni con i punti deboli non risolveva la spinta.
Rilevata una sovrapposizione di **22 cm** con la capsula di un Monaco, e di **3,6 cm** con il nucleo del Portachiavi.
Gli agenti aggiravano gli altri agenti ma non il giocatore; un punto debole animato poteva inoltre avanzare nel
controller. I valori delle singole corse variano con il passo temporale: non sono misure di prestazioni.

Correzione in due parti:

- `PlayerAvoidance`: capsula per l'aggiramento degli agenti sul giocatore locale e sui compagni vivi nello stesso
  piano. Si adatta all'altezza del corpo, si spegne alla morte o quando il compagno non è presente, non ritaglia
  la navmesh e non aggiunge collider o ostacoli ai colpi.
- I punti deboli animati non collidono con il CharacterController, ma restano visibili ai colpi. I corpi a terra
  restano solidi; le coppie di collisione vengono ripristinate anche dopo Teleport/SetSpawn. Stessa gestione per
  nemici reali e repliche. Protocollo **pe-coop-13**: formato dei messaggi invariato, comportamento fisico aggiornato.

Verifica finale (`unity_logs/pursuit-final`), senza modifiche alle collisioni da parte della suite:

| Tema | Percorso, salita e discesa | Assedio, 120 s per quota | Spostamento massimo del giocatore |
| --- | --- | --- | --- |
| Albergo | 72/72 | 40/40 | 0,00 m |
| Teatro | 72/72 | 40/40 | 0,09 m |
| Sanatorio | 72/72 | 40/40 | 0,13 m |

Regressione della mischia **120/120** (`pursuit-final/melee`),
compresi i sei controlli con camminata reale contro un corpo fermo, ancora solido, e i sei controlli sui punti
deboli dopo il riavvio del controller. La prova del corpo disabilita l'agente: altrimenti l'IA può cedere il passo
e una camminata oltre il punto iniziale non dimostra l'attraversamento del collider.

Regressioni (`unity_logs/pursuit-regressions`): armi e movimento **279/279**, interfaccia **157/157**, potenziamenti
e Processione dei Lumi **ESITO: ok**. Cooperativa a due processi **17/17 ospite e 32/32 client** in 84 s
(`pursuit-coop`); a quattro processi **17/17 ospite, 32/32 primo client, 22/22 ciascuno degli altri due** in 93 s
(`pursuit-coop-four`), tutte le uscite 0. La suite cooperativa verifica sessione, round, danno e transizioni;
non sostituisce una prova di assedio su rete reale con persone che si muovono.

Corretto anche l'obiettivo del round speciale, che mostrava ancora «NOTTE DELLE FALENE»: ora usa «PROCESSIONE DEI LUMI»,
coerente con creature, striscione e premio. Restano da verificare bilanciamento delle orde con armi e munizioni reali,
ritmo, varietà delle animazioni e leggibilità in movimento; gli agenti di questa suite non simulano una partita completa.

## Sedicesima passata: munizioni dentro le arene (13 novembre 2026)

Le tre armi a muro erano nella partenza e nel negozio (con ripiego nelle stanze di guardia), mentre il round sigilla
la stanza-arena. Mancava quindi una fonte acquistabile di munizioni durante il combattimento. Riproduzione nel
player: `-architecturetest -architecturesupply`, seme 1000 piano 1, **2/4**, con entrambe le arene senza espositore
(`unity_logs/supply-red`). I potenziamenti casuali e il premio di fine round non garantiscono una risorsa prima
di finire le munizioni.

Ora ogni stanza `Candela` della pianta a griglia riceve una TARANTOLA (500 punti), con rifornimento da 250 punti.
Riusa integralmente geometria, interazione F, controllo di ostacoli e tariffe di `WallBuy`: non introduce munizioni
gratuite né ricariche automatiche del caricatore. Le armi di partenza e del negozio rimangono. Il posto libero viene
controllato contro l'ambiente, escludendo giocatori e nemici: le guardie presenti solo sull'ospite potevano altrimenti
influenzare il risultato di `TryWallSpot`. La firma cooperativa comprende ora anche numero, indici e posizioni degli
espositori, ordinati prima del calcolo. Protocollo **pe-coop-14**.

Verifica su **60 piani / 150 arene**, dieci piani per ciascun livello da 1 a 6: tutte le arene possiedono un espositore
raggiungibile dalla candela con le soglie effettivamente sigillate; acquisto con i 500 punti iniziali, rifornimento
con F, addebito esatto e nessun addebito a riserva piena (`unity_logs/supply-seeds/summary.json`).
Le sei arene dei tre temi passano inoltre con grafica e fotografie; con la misura dell'intera ondata sono **24/24**
per tema (`unity_logs/supply-budget`). Immagini in `unity_logs/supply`.

La misura delle ondate registra la salute effettiva alla nascita e calcola il minimo di colpi al corpo a bruciapelo,
senza errori di mira, caduta del danno o bonus. Non è una prova di sopravvivenza: libera i nemici già registrati per
non fermare il conteggio al limite dei vivi. Nell'albergo, seme 1000 piano 1, le due ondate campionate di 18 e 15
nemici richiedono rispettivamente **140 e 116 colpi di pistola**, contro **105 iniziali**. Con la TARANTOLA sono
**279 e 231**, contro **224 iniziali**, ma la riserva acquistabile arriva a **288** e la pistola resta nel primo slot.
Il punto di rifornimento è dunque una scelta durante il combattimento, non una dichiarazione che qualunque precisione
o arma basti senza ricaricare. Restano da provare in partita pressione, tempi, guadagni e progressione oltre i primi round.

Cooperativa con la nuova firma degli espositori: a due processi **17/17 ospite e 32/32 client** in 84 s;
a quattro **17/17 ospite, 32/32 primo client, 22/22 ciascuno degli altri due** in 94 s, tutte le uscite 0
(`unity_logs/supply-coop-two` e `supply-coop-four`). Verificati anche i passaggi tra piani con le stesse armi
e posizioni generate su tutte le copie.

## Diciassettesima passata: ciclo a ondate (13 novembre 2026)

Chiusura della conversione richiesta dall'utente il 13 novembre: via candele e stealth, aree da aprire coi punti, meno sale più grandi,
ondate automatiche, nemici più veloci. Il lavoro era a metà nell'albero (non committato); ora `SurvivalMode` è il ciclo predefinito e quello
a candele resta dormiente con `-candles`. Descrizione delle regole in `docs/zombies-redesign.md`, «Diciassettesima passata». Tutte le prove
sono sulla build di verifica `Builds/Audit` (solo script).

| Prova | Esito |
| --- | --- |
| Compilazione Unity | 0 errori |
| `planlab survival 1000` (senza Unity) | 1000 piani validi: 8,0 aree principali in media, 925 m² l'una, 2598 atri a tre quote, 6996 acquisti e snapshot validi; al massimo 13 porte e 12 coppie di stanze su 29 aree della navmesh; ascensore di uscita a 3,5 porte in media (massimo 8), 2.908 punti per arrivarci (massimo 9.500) |
| `-autotest -survivaltest -seed 1000` | **80/80**: prima ondata dopo 8 s, nascite solo nelle aree aperte (anche quella appena comprata), mai a meno di 3 m, tutti all'attacco, tetto dei vivi, premio una volta sola, Processione alla quinta, tre ondate aprono l'ascensore, la morte annulla l'ondata e il suo numero torna indietro |
| `-architecturetest` / `-architecturepursuit` / `-architectureaccess` | **74/74**, **72/72**, **20/20** (porte col controller vero: collider, navmesh dei nemici, selezione con `F`, punti insufficienti, acquisto una volta sola, attraversamento; più il controllo di vista da entrambi i lati di tutte le porte) |
| Camminata rigorosa (`tools/check_navigation.py --floors 3`, sei semi: 1000, 2961, 3046, 777, 114, 4242) | **6/6 esecuzioni, 18 piani** (albergo, teatro, sanatorio), nessun avviso; il bot compra le porte da solo con `F` |
| Cooperativa `pe-coop-15`, 8 scenari a più processi | ospite + client a due: 37/37 e 50/50; ali con ciclo a candele 19/19 e 34/34; entrata tardiva 6/6 e 10/10; uscita pulita 8/8 e 4/4; processo ucciso 8/8 e 4/4; uscita dell'ospite 2/2 e 7/7; a tre 37/37, 50/50, 39/39; a quattro 37/37, 50/50, 39/39, 39/39. Uscite 0, tranne il processo ucciso apposta nello scenario del crash |
| Vecchio ciclo a candele, una volta sola | `-architecturesupply` 22/22 e le suite stealth, round, coltello, potenziamenti, libri, griglia, buchi e spinta passano ancora sul ciclo a candele; non fanno parte dell'accettazione del ciclo nuovo |

### Difetti trovati e corretti

- **La stanza Segreta non si poteva comprare.** Si entra da un passaggio basso (architrave a 1,3 m) e il punto che si guarda per comprare
  la sua porta (`PaidRoomDoor.Point`) stava proprio a 1,3 m: la linea di vista sbatteva nel muro (`Guscio` della stanza), quindi né chi
  gioca né il bot potevano selezionarla. Lo ha trovato la camminata rigorosa (prima porta di ogni piano `NONSELEZIONABILE`, ora con la
  diagnostica `ostacolo=` che nomina il collider). Il punto ora è `min(1,3; architrave - 0,35)`. Prova di regressione in
  `-architectureaccess`: ogni porta, da tutti e due i lati a 1,5 m e in piedi, deve vedersi; senza la correzione fallisce con 4 lati nascosti
  (i due passaggi bassi fra le stanze 2 e 5 del seme 1000), con la correzione 0 su 9 porte.
- **Controllo sbagliato nella prova delle ondate.** Il premio atteso non contava le uccisioni, che pagano sempre 60 punti ciascuna: il gioco
  era giusto, la prova no (ad esempio +3680 invece di +200). Ora attende premio più uccisioni.
- **Cooperativa, entrata tardiva.** Il confronto fra i nemici dell'ospite e i pupazzi del nuovo arrivato usava un rapporto già vecchio
  (i nemici inseguono e si muovono): lontani fino a 16,6 m. Ora i processi si sincronizzano con una barriera prima del rapporto.
- **Menu.** La riga dei comandi parlava ancora di presa alle spalle e di Velo; ora dice coltello, torcia, pila, `V` per le porte da
  comprare e mappa.

### Cosa non è stato rigirato

In questa chiusura non sono state rigirate la prova dell'interfaccia a tre risoluzioni e quelle delle armi (`check_presentation.py`,
`check_gunplay.py`; le ultime esecuzioni sono della passata precedente), né la cooperativa dopo l'ultima correzione del punto delle porte
(cambia solo i passaggi bassi); le prestazioni non sono state rimisurate. Nessuna partita giocata da una persona: ritmo,
prezzi, numero di nemici e difficoltà delle ondate non sono tarati. La rete resta provata solo su loopback.

## Diciottesima passata: fondamento grafico, spazio colore Linear (13 novembre 2026)

Richiesta dell'utente: avvicinare grafica e animazioni a un gioco tripla A senza rifare gli asset e, visto che vuole aggiungere cose nuove,
decidere prima la base dell'immagine, così le novità nascono già con la luce giusta (ordine concordato: fondamento, novità, lucidatura).
Questa passata è solo il fondamento: nessun asset nuovo, nessuna meccanica toccata. Prove sulla build di verifica `Builds/Audit`.

### Cosa è cambiato

- **Spazio colore Gamma → Linear** (`ProjectSettings.asset`) e **grading di URP da LDR a HDR** (`HotelURP.asset`, tonemapper ACES invariato).
  Luci e nebbia si sommano e sfumano come in natura e le pozze di luce calda staccano dall'ambiente freddo. Le texture create a runtime
  e i 7 shader nostri non hanno richiesto modifiche (nel codice non c'erano conversioni di gamma).
- **Look dei temi in un file solo** (`Scripts/Proc/FloorDirector.Look.cs`): nebbia, luce ambiente, esposizione, split toning, bloom,
  vignetta e grana di albergo, chiesa, teatro e sanatorio sono la tabella `ThemeLook` (prima erano numeri sparsi in `ApplyLook`).
  `LightDim`, la forza delle luci del piano, va da 1,20 a 0,77 (era da 0,50 a 0,32): in Linear i lampadari non si alzano più da soli
  con la gamma.
- **Esposizione**: `GameSettings.ProcExposure` +0,70 EV (era +0,60 in Gamma) più un valore per tema (albergo e chiesa −0,10,
  teatro −0,50, sanatorio −0,30: marmo e piastrelle chiare bruciavano le pozze di luce). Il cursore della luminosità dell'utente
  (±1,3 EV) resta sopra e non cambia.
- **Nebbia**: il colore vale la metà di prima. La nebbia non è illuminata da niente e l'esposizione la schiarisce come il resto:
  con il colore vecchio era 10-20 volte più chiara delle pareti illuminate dal solo ambiente e le stanze diventavano un latte piatto
  (il sanatorio un acquario verde). Con la metà le zone lontane sfumano nel buio e le luci vicine restano nette.
- **Flag di taratura** (senza ricostruire): `-lookexp EV`, `-lookamb x`, `-lookfog x` (densità), `-lookfogc x` (luminosità del colore
  della nebbia), `-lookdim x`, `-looksat d`, `-lookcon d`, `-lookbloom x`. Nuovo anche `-espositori` per `-guntest -armi ...`
  (fotografa i pannelli d'acquisto senza passare in rassegna tutte le armi).

### Misure

Foto 1920×1080 della sonda `-perftest -perfshots` (piano 3, semi 2995 albergo, 2961 teatro, 3046 sanatorio), luminanza sRGB di tutti i
pixel, media / mediana sulle foto di ogni tema. Il primo passaggio a Linear senza ritarare dava metà della luce di prima (+1 EV vale circa
il doppio di luminanza); i valori sopra sono il risultato di tre giri di varianti (esposizione, colore della nebbia, ambiente, luci).

| Tema | Prima (Gamma) | Dopo (Linear) |
| --- | --- | --- |
| Albergo (6 foto) | 0,050 / 0,033 | 0,092 / 0,049 |
| Teatro (5 foto) | 0,041 / 0,019 | 0,082 / 0,035 |
| Sanatorio (5 foto) | 0,057 / 0,038 | 0,104 / 0,057 |

- Torcia (`-torchreview`): **52/52 prima e dopo**. Infermiere a 0,9 m: pixel bruciati 2,92% → 2,09%, luminanza 0,431 → 0,433;
  Monaco a 0,9 m: luminanza 0,131 → 0,236 (la tonaca si legge di più); due torce sul muro 0,811 → 0,791, 0% bruciati.
- Fotogramma a 1080p: 3,4-3,7 ms in media sui tre temi (prima 3,2-3,7): nessun costo misurabile. Nessuna eccezione nei log.
- Armi (`-guntest` su VESPER-9, SENTINELLA, TURIBOLO, PUPILLA con `-fxtest` e `-espositori`): cadenza e rinculo uguali alla scheda;
  fiammate, tracce, mirino a lente, pannelli d'acquisto e HUD guardati nelle foto e a posto sotto Linear.

### Prova a tempo del GI in tempo reale (Surface Cache di URP): non riuscita, parcheggiata

Con il define `SURFACE_CACHE`, lo static batching spento e il feature nel renderer la build parte (0 errori, circa 4 MB di shader in più),
ma a ogni fotogramma il feature lancia `InvalidOperationException: Not allowed to change buffer target on mesh '…' (isReadable is
false …)` e il render graph si azzera: **tutti i 109 FBX del progetto sono importati non leggibili** e il feature li vuole leggibili
dalla CPU. Le foto della sonda con e senza GI sono identiche (luminanza 0,091 contro 0,092), il fotogramma sale comunque da 3,4-3,7 a
4,5-5,5 ms e `-torchreview` fallisce **15/52** (le 12 misure leggono 0 pixel: l'eccezione azzera il render graph e la camera di prova
resta vuota). Per riprovare servono: Read/Write su tutti i modelli
(più RAM, riimportazione), la misura del costo (la scena oggi costa 3,5 ms) e un'opzione di qualità, perché i giocatori della cooperativa
possono avere GPU più deboli. `Editor/SurfaceCacheTrial.cs` (`Enable`, `EnableAndBuild`, `Disable`) aggiunge e toglie il feature; il
define e lo static batching (`m_BuildTargetBatching`) vanno messi a mano in `ProjectSettings.asset`, e il define scrive anche nel
volume di default e nelle impostazioni globali di URP (da annullare con `git checkout`). Nel progetto il define non c'è.

### Cosa non è stato verificato

- Menu, impostazioni e schermate di presentazione sotto Linear: non ho rilanciato `check_presentation.py` né `-uitest` (modificano le
  preferenze del gioco se interrotti). Le sfumature semitrasparenti dell'interfaccia si fondono ora in lineare e potrebbero
  apparire un po' diverse; l'HUD in partita (foto delle armi) è a posto.
- La scena classica `Hotel` (`-oldplan`) ha lightmap e una sonda di riflessione cotte in Gamma: non sono state ricotte.
- Nessuna partita giocata da una persona: le tarature sono sui numeri e sulle foto a seme fisso, non sulla sensazione di gioco.
  Il cursore della luminosità serve proprio a questo.

## Ambiti ancora da chiudere

**Direzione dell'utente (13 novembre):** la progressione a candele e lo stealth sono stati sostituiti da ondate automatiche,
aree a pagamento, meno sale più grandi e nemici più veloci (diciassettesima passata). Le prove storiche delle candele non sono
criteri di accettazione del ciclo nuovo. Regole in `docs/zombies-redesign.md`.

| Ambito | Evidenza necessaria |
| --- | --- |
| Presentazione e leggibilità | Schermate finali di menu, HUD, ambienti e nemici nei tre temi; verifica anche di risoluzioni e proporzioni diverse |
| Input e accessibilità | Tastiera/mouse, perdita del fuoco e multitouch; prova fisica su telefono e comandi equivalenti per tutte le azioni |
| Gunplay | Rassegna di armi, ADS, ricariche, cambio slot, acquisti e munizioni con la build finale |
| Interazioni | Distanza, orientamento e ostacoli: candele, acquisti, santuari, ascensore, nessun uso attraverso pareti |
| Progressione e sopravvivenza | Round, morte/respawn, perk, gocce, uscita e transizioni su più semi e temi |
| Generazione e navigazione | Camminata con controller vero e confronto del varco noto (vedi seconda e terza passata); estendere la verifica a più semi e alle partite con nemici presenti |
| Cooperativa | Regressioni a più processi, entrata tardiva, uscita/rientro, morte e transizioni; verifiche delle richieste all'ospite e coerenza dei piani |
| Prestazioni e audio | Prestazioni misurate sui tre temi (vedi sopra); resta la qualità di volume, spazialità e feedback durante una partita, che non si misura senza ascoltare |
| Distribuzione | Build finale, guida aggiornata, avvio da cartella estratta e verifica delle piattaforme effettivamente supportate |
| Altre versioni nel repository | Valutare ruolo e stato di Three.js/Unreal rispetto alla versione distribuita, eliminando ambiguità nella documentazione |

Limiti già documentati da verificare: rete reale fuori da loopback, manichini dei compagni, fiducia nei colpi dichiarati
dagli ospitati e dispositivi mobili. Vedere anche `docs/multiplayer/README.md`.


### 14 novembre: Torre Zombies e aggiornamento della copia Prova

La Torre mantiene il ciclo predefinito senza candele interattive o stealth: 7–9 aree principali,
porte a pagamento e ondate automatiche. La copia Prova era precedente alla build Audit del 14 novembre.
Ora aggiornata da una nuova build completa, poi ricompilata per le regole definitive.

La velocità dei nemici a terra cresce da ×1,6 alla prima ondata a ×2,2 alla decima e resta stabile;
i volanti mantengono ×1,15. Tolto il secondo incremento di velocità da RoundDirector.Scale nel ciclo
survival. Il protocollo passa a pe-coop-17 per evitare sessioni con regole di movimento differenti.

Verifiche locali (log in unity_logs, non versionati):

- PlanLab survival: 1.000 piani validi, 8,0 aree medie, 925 m² per area e 2.598 atri a tre quote.
- zombies-final-survival.log: 86/86; incluse velocità reali dei monaci senza doppio moltiplicatore.
- zombies-final-access.log: 20/20 sul seme 2961; porte fisiche, pagamento, navigazione e assenza di stealth/candele interattive.
- zombies-coop-fixed: host 38/38, client 50/50, entrambi usciti con 0, seme 1000.
- La prima prova cooperativa sul seme 1000 era 37/37 host e 49/50 client: PlaceNear usava AllAreas,
  consentendo un nemico di verifica dietro una porta chiusa. Ora richiede EnemyMask e un percorso completo
  fino al compagno. Lo stesso scenario ripetuto verifica anche lo spostamento del pupazzo (0,7 m).
- zombies-coop-fix-build.log: build riuscita, zero errori.
- Copiati in Builds/Prova e confrontati per SHA256 tutti i 192 file; ricevuta in build-info.json nella stessa cartella.

Le uccisioni nelle prove delle ondate sono automatiche: verificano regole e sincronizzazione, non il bilanciamento
di una partita giocata con le armi. Il nuovo laboratorio dell’officina urbana resta separato da questo aggiornamento.


## Weapon-state extraction: verified integration

Full pipeline job: `unity_logs/studio/20261114T123907Z-990ed0ba/report.json`.
The new package is compiled into the game and the full job passes 1,454 state
checks, 297/297 native gunplay checks and 67/67 city checks. It publishes and
verifies 193 files in `Builds/CityPreview`, including `Production.Weapons.dll`.
The independent Unity consumer passes 12 checks, with an interactive Fire/Reload/Reset
demo in the generated RoomDemo scene (`unity_logs/weapon-state-independent`).

The Torre build also passes 161/161 presentation checks (`unity_logs/weapon-state-ui`),
including reload HUD state, ammo exhaustion, inventory changes and a reload that
continues inside the cooperative menu. Preferences are restored by the existing
presentation test. This preserves the visible integration in addition to the pure
state comparison. Full logs are local evidence, not versioned artifacts.

Torre cooperative regression: host **38/38**, client **50/50**, both exit 0
in 103 seconds (`unity_logs/weapon-state-coop`). The Torre package DLL matches
the one verified in the city. Published **193 files** to `Builds/Prova`, all
compared by SHA256; provenance in `build-info.json` and
`unity_logs/weapon-state-torre-publish.json`. No network message format or balance
rule changes were introduced. Build-generated scene IDs and numerically identical
material rewrites are archived locally rather than included in the refactor.


## Persistent Studio queue: lifecycle verification

The coordinator now persists requests in SQLite, pins submitted source snapshots,
serializes the project resource across report stores, and supports foreground or
background draining, status and cancellation. Generic queue/process modules have
no Unity content dependency; the Studio adapter supplies validated operations.

`unity_logs/queue-tests.log`: **14 tests passed**, including actual worker/child/
grandchild processes, cancellation while waiting, queued and running cancellation,
timeout cleanup, worker crash and resume, recovery after a completed report, PID
creation-token distinction, stale-source refusal and CLI request validation.
An unrelated process stays alive during cancellation. The strengthened cross-store
resource-lock check also passes (`queue-source-lock-tests.log`).

`unity_logs/queue-final/acceptance.json`: actual background worker **7276** completes
job `20261114T131100Z-6f348fa8` (1,454 state checks) before starting
`20261114T131101Z-8775dbf4` (Unity workshop preview). Verified timestamps, worker
identity, source hashes, existing artifacts, 1280×900 preview and reproduction
commands. Each operation has its own common report. Job IDs and worker identity
are evidence for this run, not values assumed for future runs.

This adds queue/cancellation behavior, not a parallel asset-generation pool. The
worker drains available jobs and exits; it is not a permanent service. Interrupted
jobs are not silently retried. Direct Editor usage is refused rather than closed.
Commands, source pinning and cancellation limits are in `production-studio.md`.


Final queued full-production proof: `unity_logs/queue-production/20261114T131900Z-5d41b9d7/report.json`.
The background worker completes **1,454 state checks**, **297/297 native gunplay**,
**68/68 city/room checks**, and a fresh-session import check of **7 materials**.
All **193 files** published to CityPreview are verified. The source snapshots match
the implementation at verification. The extra city check covers emissive keyword
and intensity. Before the material fix, fresh-session verification failed with
`ROOM_EMISSION_IMPORT: paper` (`unity_logs/queue-material-stored-red.log`); the
corrected flags and saved keywords survive the new session. The final queue
regression suite remains **14/14** (`unity_logs/queue-tests-final.log`).


## Production gallery and declared dependency graph

The read-only portable gallery now collects the 30 catalog weapons, furnished room,
job images, measurements, failures and reproduction commands. A/B comparisons use
common views where possible. Current catalog values are labeled separately from
historical job evidence; unknown source provenance stays unverified. Mixed direct
and queued report date formats no longer reorder history incorrectly.

CLI and gallery share dependency traversal. Local Python recipe imports are read
without execution, and JSON pointer queries scope room palette changes. The graph
rejects cycles, missing content nodes and paths outside its root; a copied module
also runs in an isolated Python process without the game catalogs installed. It
does not yet describe all Unity GUID links or dynamic imports.

Local checks: **17 Studio/gallery tests**, **6 graph tests**, plus a fresh workshop
preview and a second Unity material-import session. Browser verification covers
desktop and 390-pixel layouts, loaded comparison images, search/filter behavior,
report links, historical/unverified provenance and material impacts. A separate
synthetic failed-check fixture displays HTML-like error text literally, without
creating an image element. Details and evidence paths are in `production-studio.md`.

Remaining objective includes material/lighting and performance laboratories,
parallel independent generation, broader catalogs and dependencies, further weapon
integration separation, and the urban survival level's progression and art work.


## Urban wave scheduling package

The city now consumes the engine-free `Production.Survival` package. Wave budget,
spawn pacing and a population limit are separated from city enemy selection,
difficulty, rewards and locations. Its clock advances with caller-supplied time;
failed creation preserves budget and a capped population cannot bank spawn bursts.
The independent Unity consumer uses the same package in an interactive simulation.

The city recipe configures 12/16/20 enemies for the first three waves, a total cap
of 96, a concurrent cap of 24, 0.65-second entry spacing, ten-second intermissions,
immediate pursuit and progressive health/speed multipliers. Native evidence at
`studio/20261114T141116Z-c42694a8` passes **71 city**, **297 gunplay**, **1,454 weapon
state**, **24 wave state** and **7 imported-material** checks. **194 CityPreview
files** were compared by SHA256. All 246 recorded sources remain unchanged. The
independent Unity consumer passes **14 checks**; final Python suites pass **17 + 7**.
The full report and the corrected first-run expectation are documented in
`production-studio.md`. The gallery exposes the wave-state measurements and impact
queries connect the shared wave package to city rules and the district.

The crowd screenshot still shows primitive enemy bodies and generic buildings.
This verifies population and pacing, not finished art or final gameplay balance.
Urban paid access, vertical interiors, enemy presentation and further framework
work remain part of the active objective. This change updates CityPreview, not the
Torre/Prova copy.
