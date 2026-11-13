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
- Costo della misura: **4,73 µs**, media di 200 chiamate, **0 byte gestiti**, nella scena isolata di prova.
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

## Ambiti ancora da chiudere

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
