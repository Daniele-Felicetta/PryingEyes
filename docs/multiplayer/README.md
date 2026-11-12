# Multiplayer: integrazione e verifiche

Aggiornato il 12 novembre 2026. Worktree isolato:
`C:/Users/danie/.codex/worktrees/multiplayer/prying-eyes`, branch `feature/multiplayer`.
Il goal complessivo resta **incompleto**: la sessione e il movimento multiplayer sono
eseguibili; il gameplay FPS completo di Claude non è ancora integrato.

## Base di Claude e isolamento

Base Git originale e8113f2; il progetto Unity è stato aggiornato con lo snapshot di
`temi-fase2`, HEAD `0b9e9d730a559a62573e1a3eb4a816d1b953eb35`, incluse le modifiche non
committate. Sono stati acquisiti 1636 file, 1178 aggiornati nella copia isolata.
[Provenienza e hash](integration/claude-snapshot.json). È una copia di contenuto,
non un merge: la diff del worktree include anche il lavoro di Claude.

Il principale è stato soltanto letto. Nessuno stash, reset, checkout, merge, modifica
a configurazioni globali, interruzione di processi esistenti o scrittura nel principale.
Unity è stato avviato nel solo worktree dopo l'autorizzazione dell'utente.
Cache/output .NET sono in `tools/multiplayer-check`; build, log e backup dello snapshot
in `tools/unity-validation`; cache UPM in `.unity-cache`. La prima importazione aveva
solo UPM_CACHE_PATH e può avere riutilizzato metadati della cache predefinita; le
successive usano UPM_CACHE_ROOT e UPM_NPM_CACHE_PATH locali.

Non lanciare gli autotest legacy indiscriminatamente: GameManager e FloorDirector
contengono percorsi assoluti per i log nel principale. I nuovi test usano destinazioni
locali esplicite e nessun flag degli autotest legacy.

## Framework scelto

| Framework | Punti pertinenti | Impatto sul progetto |
| --- | --- | --- |
| Photon Fusion 2 | Prediction, lag compensation, sessioni Photon e input per più umani sullo stesso peer | SDK ufficiale richiede login; download tentato con esito 403; compatibilità 6000.6.7f1 non confermata dalla tabella requisiti |
| FishNet | Più oggetti posseduti per connessione, prediction, trasporto Tugboat e sorgenti pubblici | Consente sviluppo senza account vendor; connessione diretta con host raggiungibile; relay e discovery richiedono integrazione |
| Netcode for GameObjects | Stack Unity, ownership di più oggetti; un solo PlayerObject per client | Le versioni documentate distinguono anticipation da prediction completa; maggiore lavoro da valutare per FPS |

**Decisione operativa: FishNet**, host autorevole, un NetworkManager per processo,
fino a quattro posti locali per connessione. L'accesso iniziale sarà indirizzo/porta,
anche in modalità mista. Per Internet l'host deve avere UDP raggiungibile, tramite
port forwarding o hosting adatto; CGNAT/assenza di porte aperte richiedono un relay.
Questo limite non risolve ancora l'esperienza di ingresso degli amici su ogni rete.
Non sono state modificate impostazioni router o firewall.

Dipendenza fissata nel manifest locale al commit
**73f30cf2425dc808a4f463f0a233d386010810b3**, tag ufficiale **4.7.3**.
Il package.json del fornitore a quel commit dichiara 4.7.2: il pin al commit,
non il numero interno, identifica esattamente i sorgenti verificati.
Nessun sorgente del fornitore è stato modificato o vendorizzato negli Assets.

Fonti primarie:

- [FishNet: repository e installazione UPM](https://github.com/FirstGearGames/FishNet)
- [FishNet: release e supporto delle API Unity 6.5](https://github.com/FirstGearGames/FishNet/releases/tag/4.7.3)
- [FishNet: connessioni e insieme degli oggetti posseduti](https://fish-networking.gitbook.io/docs/guides/features/server-and-client-identification/networkconnections)
- [FishNet: prediction](https://fish-networking.gitbook.io/docs/guides/features/prediction)
- [FishNet: connessione remota e port forwarding](https://fish-networking.gitbook.io/docs/tutorials/getting-started/connecting-to-remote-devices)
- [Licenza FishNet alla revisione fissata](https://github.com/FirstGearGames/FishNet/blob/73f30cf2425dc808a4f463f0a233d386010810b3/LICENSE.md)
- [Fusion: più umani per peer](https://doc.photonengine.com/fusion/v2/manual/input/player-input)
- [Fusion: SDK e requisiti](https://doc.photonengine.com/fusion/v2/getting-started/sdk-download)
- [NGO: PlayerObject e ownership, versione documentata](https://mp-docs.dl.it.unity3d.com/netcode/1.10.0/basics/networkobject/)
- [NGO: anticipation, versione documentata](https://mp-docs.dl.it.unity3d.com/netcode/2.2.0/advanced-topics/client-anticipation/)

Le pagine NGO sono versionate; il confronto non è un benchmark né una verifica
delle funzionalità di ogni release futura.

## Moduli

Tutti gli adapter e i nuovi asset sono sotto `Assets/_PryingEyes/Multiplayer`.

| Modulo | Responsabilità |
| --- | --- |
| Core | Identità peer/posto, capacità per umani, admission con scadenza, ownership, readiness per epoch, input sanitizzato e layout 1–4 |
| NetworkFloorPlan | PlanGen reale di Claude, scelta esplicita griglia/legacy preservando UseGrid globale, digest schema 2 con ruoli stanza, candidati spawn raggiungibili |
| LocalSeatInput/View | Input per posto, Submit per adapter esterni, camera/HUD per avatar e un solo listener locale |
| FishNetSeatAuthenticator | Compatibilità di protocollo/contenuti e conteggio posti prima dell'ammissione; non autenticazione account |
| FishNetSessionLauncher | Local/Host/Join/Stop, Tugboat UDP, un avatar posseduto per ogni posto, cleanup della connessione |
| FishNetSeatAvatar | Movimento sul server, sequenza/epoch e timeout input; pose replicate e interpolate |
| FishNetWorldState | Seed/piano/modalità/digest persistenti, barriera caricamento e scadenze; evento ServerInput sul tick autorevole |
| FishNetProceduralFloor | FloorBuilder reale, collider prima dell'ack, spawn controllati fisicamente, rimozione geometria precedente |
| FishNetTowerObjectives | Candele e ascensore di Claude, intenzione Use validata per distanza/sguardo/linea libera sul server; snapshot persistente per i client |

Il protocollo d'ammissione del player corrente usa `0b9e9d7-d28c34dcfb80-mp-schema3`.
Il digest del piano resta schema 2: include la modalità del generatore e Room.type.
Cambiare il token quando cambia lo schema dei messaggi o il contenuto incompatibile.

## Avvio manuale

Aprire **solo** la scena `MultiplayerProbe.unity` sotto `Multiplayer/GeneratedProbe*`.
Il player Windows di verifica è `tools/unity-validation/player/PryingEyesMultiplayer.exe`.
Il pannello F1 permette Local, Host e Join, numero posti locali e indirizzo/porta.
La porta UDP predefinita è 7807; Local ascolta su loopback, Host sulle interfacce esterne.
Nessuna regola router/firewall è creata automaticamente.

| Posto | Movimento | Rotazione | Fuoco (intenzione) | Interagisci |
| --- | --- | --- | --- | --- |
| 1 | WASD | Q/E | Space | Tab |
| 2 | Frecce | Home/End | RightCtrl | RightShift |
| 3 | IJKL | U/O | Return | RightAlt |
| 4 | TFGH | R/Y | Backspace | LeftAlt |

Per ora il fuoco è soltanto un evento, senza armi/danni integrati. Accendere le candele
sblocca l'ascensore; una richiesta valida presso l'uscita porta tutti al piano dopo.
Il pulsante host per il piano successivo resta un comando di debug.
Più tastiere non sono dispositivi distinti: questa è una mappa condivisa per la prova.
Gamepad, mouse look completo, touch e prestazioni grafiche richiedono ancora lavoro.

La creazione è esplicita tramite `Prying Eyes/Multiplayer/Create isolated probe`.
Il guard richiede un vero linked worktree sul branch feature/multiplayer e rifiuta
il principale. Il kit è copiato dalla scena Torre in anteprima, usando i riferimenti
materiali/arredi già creati da Claude. Non viene chiamato TorreBuilder.FillKit, che
rigenererebbe risorse e salverebbe altri asset. Non vengono modificate Build Settings.

## Verifiche eseguite e riproduzione

- 260 asserzioni .NET: admission/timeout/replay, capacità, posti misti, ownership,
  barriera, viewport, griglia e legacy su seed/piani diversi, digest e spawn.
- Compilazione esterna di gameplay Claude, adapter ed Editor contro DLL Unity reali.
- Codegen managed FishNet: 2 RPC con reader/writer/logic, 5 inizializzazioni SyncVar,
  6 coppie di serializer. 12 roundtrip reali con confronto di tutti i campi/array.
- Unity 6000.6.7f1: importazione e compilazione native, scena/prefab creati, build Windows.
- Play Mode: 66 assert, sessioni locali 1–4, ownership, camera/rect/listener, input,
  cambio piano, arresto e pulizia. Questa prova precede il kit e il modulo obiettivi.
- Due player Windows distinti, loopback UDP: 1+1 e 2+2 con input remoto e cambio piano.
- Modulo obiettivi: prova 2+2 con accensione da un posto remoto, replica delle candele,
  uscita sbloccata e discesa al piano 3. Il test posiziona i giocatori sul server per
  raggiungere gli oggetti; l'interazione percorre il normale input di rete.

Gli esiti più recenti e i log restano sotto `tools/unity-validation`. Le prove native
sono headless: non dimostrano qualità visiva, frame rate, gioco su Internet o latenza reale.
Il report ILPP ha runtimeVerified=false perché descrive solo il controllo managed;
i report dei player coprono separatamente i percorsi runtime indicati sopra.

Dalla directory `tools/multiplayer-check`, con SDK fissato da global.json:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
dotnet run --project MultiplayerCheck.csproj --disable-build-servers
dotnet build fishnet-compile/Editor/Editor.csproj --disable-build-servers -m:1
./Run-NetworkSmoke.ps1 -HostSeats 2 -ClientSeats 2 -Port 17807
```

Prima delle prove native serve la build del player. Nel solo worktree autorizzato,
Unity batch può invocare `PryingEyes.Multiplayer.FishNetBackend.EditorTools.BuildMultiplayerPlayer.Windows`
con `-quit`. Il metodo usa una lista scene esplicita, aggiorna soltanto il probe e i
suoi prefab e scrive nell'output isolato. La prova locale usa `RunMultiplayerSmoke.Local`
con `-mpSmokeLocal -mpReport <percorso-assoluto>` **senza -quit**: termina da sola.
Il runner di rete avvia soltanto i due player creati dalla propria invocazione e li
chiude in caso di timeout; non cerca né interrompe altre istanze Unity.

## Integrazione gameplay e gate aperti

| Parte | Stato / lavoro rimanente |
| --- | --- |
| Movimento | Capsule autorevoli, ancora senza il controller FPS completo, prediction e lag compensation |
| Armi, salute e inventario | Collegare simulazione e stato per attore; nessuna fiducia in ammo/hit/cooldown dichiarati dal client |
| Punteggio e perk | Estrarre lo stato globale di GameManager.I e Perks per giocatore; libri/scrigno/altare restano disattivati nel probe |
| Round e nemici | Simulazione server con più bersagli, stato persistente e presentazione sui client |
| Obiettivi | Candele/uscita integrate; restano round associati alle candele e ricompense per giocatore |
| Generazione | PlanGen/FloorBuilder e kit reali; navmesh e Connectivity.Repair non ancora integrate |
| Sessioni | Aggiungere prove native late join/reconnect, rifiuti, timeout, capacità e perdita/latency; i controlli Core non sostituiscono queste prove |
| Internet | Prova tra reti distinte; integrazione relay per CGNAT o host non raggiungibile; ingresso tra amici ancora per indirizzo/porta |
| Presentazione | Verifica grafica delle viewport/HUD, rendering corpo locale e materiali, dispositivi fisici e piattaforme distinte |

Per una nuova meccanica, il modulo gameplay conserva stato e regole. L'adapter può
sottoscrivere WorldState.ServerInput (intent già autenticato e sanitizzato) e replicare
uno snapshot persistente tramite i SyncType di FishNet. Niente RPC aggiuntive per ogni
muro/arredo statico e nessun riferimento a Camera.main per selezionare il giocatore.
Gli ID degli interattivi sono legati a epoch e ordine deterministico del piano;
non usare GetInstanceID. Il digest certifica il Plan, non tutti i collider o effetti.
Le riparazioni geometriche devono essere deterministiche o decise e replicate dall'host.

Il principale può continuare a evolvere: prima di un'integrazione finale va acquisito
un nuovo snapshot controllato, confrontando i punti toccati; non sovrascrivere il suo
worktree con questa copia. I prototipi Fusion precedenti restano solo come riferimento
in `docs/multiplayer/reference/Fusion2`, fuori dagli Assets e dalla compilazione.