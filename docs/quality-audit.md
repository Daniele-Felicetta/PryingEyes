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
  La rassegna ha preceduto la correzione ottica, verificata separatamente sotto. Restano da rifinire i materiali
  con venature del legno troppo contrastate e la leggibilità generale alle esposizioni basse.
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

## Ambiti ancora da chiudere

| Ambito | Evidenza necessaria |
| --- | --- |
| Presentazione e leggibilità | Schermate finali di menu, HUD, ambienti e nemici nei tre temi; verifica anche di risoluzioni e proporzioni diverse |
| Input e accessibilità | Tastiera/mouse, perdita del fuoco e multitouch; prova fisica su telefono e comandi equivalenti per tutte le azioni |
| Gunplay | Rassegna di armi, ADS, ricariche, cambio slot, acquisti e munizioni con la build finale |
| Interazioni | Distanza, orientamento e ostacoli: candele, acquisti, santuari, ascensore, nessun uso attraverso pareti |
| Progressione e sopravvivenza | Round, morte/respawn, perk, gocce, uscita e transizioni su più semi e temi |
| Generazione e navigazione | Generatore, raggiungibilità reale, passaggi bassi, assenza di buchi e incastri con il controller vero |
| Cooperativa | Regressioni a più processi, entrata tardiva, uscita/rientro, morte e transizioni; verifiche delle richieste all'ospite e coerenza dei piani |
| Prestazioni e audio | Misure sui tre temi e qualità di volume, spazialità e feedback durante una partita |
| Distribuzione | Build finale, guida aggiornata, avvio da cartella estratta e verifica delle piattaforme effettivamente supportate |
| Altre versioni nel repository | Valutare ruolo e stato di Three.js/Unreal rispetto alla versione distribuita, eliminando ambiguità nella documentazione |

Limiti già documentati da verificare: rete reale fuori da loopback, manichini dei compagni, fiducia nei colpi dichiarati
dagli ospitati e dispositivi mobili. Vedere anche `docs/multiplayer/README.md`.
