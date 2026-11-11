# PRYING EYES — FPS in Three.js

Bozza giocabile di FPS in prima persona. Modelli, audio ed effetti procedurali; texture foto PBR caricate da CDN con fallback procedurale se offline.

## Avvio

Gli ES Modules richiedono HTTP (non `file://`):

```powershell
cd C:\Users\danie\Desktop\pryingEyes
python -m http.server 8000
# apri http://localhost:8000
```

oppure con node:

```powershell
npx serve .
```

Poi clicca **ENTRA NELLA SIMULAZIONE** (il click serve per Pointer Lock + AudioContext).

## Controlli

| Input | Azione |
|---|---|
| WASD | movimento |
| Mouse | mira |
| LMB | fuoco (auto 700 RPM) |
| RMB | mira ADS (zoom + laser) |
| R | ricarica |
| Shift | sprint (solo avanzando) |
| C / Ctrl | abbassati |
| Sprint + C | **slide** |
| Spazio | salto (con coyote-time + jump-buffer) |
| Q / E | lean |

## Cosa include

**Movimento (priorità massima):**
- accelerazione/frizione stile Quake (ground accel 34, air control, frizione laterale)
- sprint con FOV-kick, slide con conservazione della quantità di moto, camera roll su strafe, lean Q/E
- head-bob procedurale + footstep audio, coyote-time 0.13s, jump-buffer 0.15s
- collisione AABB contro casse/muri/pilastri, eye-height interpolata in crouch

**Materiali (foto PBR reali + fallback procedurale):**
- muri in mattoni fotografici (albedo + bump + roughness + normal derivata via Sobel, scala ~0.22 m/corso)
- ponte di carico in legno fotografico, pozzanghere con normali acqua animate
- cemento v2 (giunti, crepe, olio, pneumatici + normal map), casse militari con stencil NX-07
- caricamento asincrono da CDN, fallback automatico se offline (toast "Texture HD attive" quando pronte)
- IBL + ACES + bloom + color grading + SMAA (dal pass precedente)

**Arma fotorealistica (AR-9 MALLEUS, stile BO2):** metallo usurato PBR (albedo + metalness + roughness + normal da graffi/olio), spigoli smussati, marcature stampate (calibro, selettore, seriale), otturatore che cicla a ogni colpo, contacolpi digitale, volata a stella, mani con guanti fingerless + orologio, luce dedicata al viewmodel, posa da corsa abbassata

**Nemici AAA:** drone WASP (rotori con blur, sensore su gimbal che ti segue, banking, fumo se danneggiato) e brute RAVAGER (piastre articolate, pistoni, enrage sotto il 50% con perdita dello spallaccio). Bruciature ancorate al corpo, uccisioni con smembramento fisico.

**Mappa: GRAND HOTEL CONTINENTAL abbandonato**
- hall 70×46 m: pavimento a scacchiera in marmo, carta da parati scrostata, tappeti consunti, lampadario centrale guasto che sfarfalla, luce di luna dal lucernario, vetrate con lame di luna, ragnatele e calcinacci
- reception con bacheca chiavi, salotto con divani (alcuni coperti da lenzuola), carrello portabagagli, ascensori, scalone centrale + soppalco percorribili (scalini con step-up automatico)
- **gabbia di vetro per coccodrilli nell'angolo in alto a sinistra (NW)**: vasca, isolotto, lampada riscaldante, targa, transenne — con 2 coccodrilli animati (respirano, muovono la coda, schioccano le mascelle e ti fissano se ti avvicini)

**Gameplay:**
- 2 nemici (drone volante + brute bipede) con spawn da ascensori/ingresso/soppalco, separazione, knockback, flash-hit, smembramento
- headshot x2, ondate crescenti, punteggio, scudo che si rigenera, HP, respawn
- HUD: crosshair dinamico, hitmarker, kill-confirm, minimap, FPS counter, banner ondate
- menu con sensibilità / FOV / qualità (low-high-ultra) / toggle bloom e shake

## File

- `index.html` — markup + HUD + menu + importmap (three@0.160.0 via unpkg)
- `style.css` — HUD/menu
- `js/` — gioco diviso in moduli:
  - `core.js` — renderer, scena, camera, post (bloom/grading/SMAA), stato globale (CFG, player, weapon, nemici, input)
  - `audio.js` — effetti procedurali WebAudio
  - `textures.js` — texture canvas, normal-map via Sobel, cemento/casse/muri, foto PBR da CDN con fallback
  - `world.js` — materiali, luci, cielo, arena, torri-faro, pozzanghere, volumetrici (`buildWorld`/`tickWorld`)
  - `effects.js` — particelle, traccianti, decal, bossoli, detriti, bruciature, shockwave
  - `weapon.js` — viewmodel AR-9, red-dot, mani, reload animata
  - `combat.js` — hitscan, ricarica, crosshair kick
  - `player.js` — controller Quake-style, camera, danni, respawn
  - `enemies.js` — WASP/RAVAGER, danni, smembramento, ondate
  - `ui.js` — HUD, banner, toast, minimap, FPS
  - `input.js` — tastiera/mouse, pointer lock, menu
  - `main.js` — bootstrap + game loop

## Versione Unreal Engine 5 (progetto `PryingEyes 5.8`, mappa `/Game/Maps/HotelPryingEyes`)

Stessa hall dell'hotel ricostruita in UE5 e guidata via MCP (`http://127.0.0.1:8001/mcp`, script in `hotel_build/`):
- guscio completo: pavimento, muri, colonne, soppalco con ringhiera, scalone a 12 gradini, reception, ascensori, lampadari, vetrate est
- **gabbia di vetro per coccodrilli nell'angolo NW**: vetro traslucido reale, vasca, isolotto, lampada riscaldante, transenne con corda, 2 coccodrilli modellati da primitive
- Lumen GI + hardware ray tracing (attivi nel progetto), volumetric fog, PostProcess cinematografico (bloom/vignetta/grana), lama di luna dal lucernario, RectLight alle vetrate, PlayerStart all'ingresso — premi Play per camminarci (First Person template di default)
- verificata a screenshot in editor: pavimento a scacchiera in marmo (`M_MarbleFloor` procedurale worldspace), carta da parati a strisce (`M_Wallpaper`), arma e WASP con sagoma leggibile

## Gameplay UE5 (FPS giocabile, `hotel_build/gameplay/` + `phaseG1.ps1`)

Loop completo verificato in PIE con log (`LogBlueprintUserMessages`):
- **BP_GameDirector**: ondate, spawn, HP/scudo rigenerativo, munizioni 30/120 (LMB fuoco, R ricarica), respawn protetto, punteggio/uccisioni, HUD (HP/scudo/munizioni/riserva/punti/ondata/mirino/hitmarker), banner; movimento: sprint Shift, lean Q/E, crouch C, slide sprint+C, head-bob e kick sull'arma; logica movimento in TickMove, arma in TickGun
- **BP_Enemy**: WASP volante (rotori animati + bob) / RAVAGER corazzato (spallacci, occhi rossi, enrage <50% HP con +35% velocità), inseguimento, attacco melee, `TakeHit` + punteggio (100/150)
- **BP_Gun**: viewmodel (fusto/canna/mirino/punto rosso/fiammata) che segue la camera con kick di rinculo; trace parte oltre l'arma
- **WBP_HotelHUD**: 4 valori + label + mirino, aggiornati ogni tick dal Director
- **BP_CrocAnim** (2 istanze sui coccodrilli A/B): oscillazione mascelle sinusoidale sfasata
- **BP_Impact**: flash emissivo 0.12s su ogni punto d'impatto (muri e nemici)
- bilanciamento/test: `TestAutoFire` + `TestAutoWalk` + `TestYawRate` sul Director per playtest integrale senza input (fuoco/ricarica/cammino/rotazione da dentro il gioco, log `POS`/`ONDATA`/`UCCISO`); nemici con sweep disattivato (niente incastri nei muri)
- verificato: spawn, chase, attacco, fuoco, kill, punteggio, respawn, avanzamento ONDATA 1→2, nessun errore runtime; sorgenti DSL versionati in `hotel_build/gameplay/*.lisp`, replay con `phaseG1.ps1`

## Versione Unity 6 (`unity/PryingEyes`)

Hall giocabile in prima persona, generata da `HotelBuilder`, con questi sistemi:

- **armi sui muri stile COD Zombies** (`Scripts/WallBuy.cs`; la lista e i prezzi nuovi sono nella sezione "Armi: 28 modelli" qui sotto: questo paragrafo descrive la prima versione con 4 armi). Pannello metallico, sagoma di gesso, arma 3D, nome/prezzo e luce colorata; avvicinati e premi **F** per comprare. Riacquistando la stessa arma ricarichi la riserva a metà prezzo.
- **punti come valuta** (`GameManager`): si parte da 500; +10 per colpo a segno, +50 per uccisione, +100 per headshot letale. Niente moltiplicatore per ondata. Le armi costano punti, quindi le uccisioni servono a finanziare l'arsenale.
- **quattro armi con statistiche e viewmodel distinti** (`Scripts/Weapon.cs`): cadenza, danno, caricatore, riserva, rinculo, spread, headshot multiplier, ricarica; il fucile a pompa spara 8 pallini per colpo. Il viewmodel viene ricostruito all'acquisto.
- **mirini dedicati**: red dot sull'AR-9, reflex sul VESPER-9, tacca fantasma + sfera sul BREACHER-12, ottica con torrette e lenti sul LANCE-7. In ADS l'arma si alza e centra il mirino sul punto di mira (RMB).
- **materiali PBR procedurali, nessun download** (`Scripts/GunMats.cs`): albedo usurato con graffi e chiazze d'olio, metallic/smoothness map, normal map generata via Sobel, polimero antiscivolo e legno con venature.
- **animazioni del viewmodel**: respiro, dondolio dei passi, posa di corsa, rinculo, ingresso arma all'acquisto e **ricarica animata** (arma che ruota, caricatore che esce e rientra, leva di armamento; pompa del fucile a pompa).
- HUD munizioni corretto: il riferimento all'HUD ora viene risolto a runtime, quindi colpi, ricarica e cambi d'arma aggiornano subito il contatore.
- **i monaci (maghi) attaccano da lontano** (`Scripts/Magic.cs`): se hanno linea di vista e il giocatore è tra 3 e 26 m, caricano una sfera blu sul bastone per 0,9 s e lanciano un dardo magico (8-12 danni) verso il petto; da vicino resta il colpo di bastone. Il dardo ha nucleo emissivo, anelli in orbita, luce e scia, ed esplode su muri e giocatore.
- **nemici meccanici più fotorealistici e tetri** (`Scripts/EnemyMats.cs`): materiali PBR procedurali (ferro arrugginito con graffi e unto per i brute, composito nero con pannelli e viti per i droni), normal map procedurale, piastre pettorali, spallacci, pistoni, cavi, gabbie dei rotori, pattini di atterraggio e luci di sensore/visore smorzate. Nessun asset scaricato.
- test: `-autotest -magictest` verifica i lanci a distanza e i danni; `-autotest -enemyshot` genera `hotel_build/enemies.png`.
- **divani imbottiti** (`HotelBuilder.Sofa`): base, cuscini di seduta e schienale come ellissoidi, braccioli a rullo, cuscino decorativo e gambe in legno scuro; niente più scatole rigide.
- **arma meno squadrata** (`Weapon.cs`): receiver, upper, paramano, calciatura, caricatore e impugnatura realizzati con ellissoidi e capsule; la fiammata è una stella procedurale additiva generata a runtime, non più un quadrato arancione.
- **ritmo rallentato**: walk 5.6→3.9 m/s, sprint 8.2→5.9, crouch 3.1→2.2; nemici lenti (drone 2.7-3.5, brute 1.6-2.05, monaco 1.2-1.5 m/s), attacchi con cooldown più lungo, ondate con pausa di 8 s e prevalenza di camminatori.
- **HUD**: prompt di acquisto vicino al mirino + nome arma accanto alle munizioni; punteggio e presenze restano in alto a sinistra.
- test automatici: `-autotest -camshots` produce `hotel_build/wallbuy.png` (foto dell'espositore); `-autotest -walltest` verifica prompt, acquisto, spesa punti e ricarica riserva.
- build: `BuildPlayer.Windows` (Unity 6000.6.7f1) → `unity/PryingEyes/Builds/PryingEyes.exe`.

## Armi: 28 modelli in 5 gradi di potenza (Unity)

Il gunplay e' stato rifatto da zero (`Scripts/Weapon.cs`, `Scripts/Armi/`). Le quattro armi di primitive sono sparite: ogni arma e' un modello 3D vero fatto con uno script Blender (hard-surface con smussi, guide Picatinny, asole M-LOK, mirini veri), con una texture PBR cotta da Blender (colore, metallicita'/levigatezza/occlusione, normali: usura sugli spigoli, graffi, sporco nelle cavita', puntinatura del polimero, venatura del legno). 25 armi sono "reali" e 3 sono speciali (sacra, organica, aliena) con materiali d'osso, carne ed emissione ambra.

**Controlli.** LMB fuoco, RMB mira (ADS), R ricarica, **B** cambia modo di fuoco (auto/semi/raffica dove c'e'), **1 / 2 / X / rotellina** cambiano arma (due slot), **Q / E** lean, Shift sprint (con un'ottica forte Shift tiene il fiato), F compra/accende.

**Gradi.** I = Ordinaria (grigio), II = Scelta (verde), III = Rara (azzurro), IV = Epica (viola), V = Reliquia (oro). Ai muri compaiono tre armi per piano: vicino all'arrivo la piu' modesta, lontano la migliore, e il grado massimo sale coi piani. A due slot pieni l'acquisto sostituisce l'arma in mano; ricomprando un'arma che si ha si ricarica la riserva a meta' prezzo.

| Gr. | Arma | Classe | Danno | Fuoco | Caricatore | Mirino |
|---|---|---|---|---|---|---|
| I | PIETA' P-9 (si parte con questa) | pistola | 25 | semi | 15 | ferri a tre punti |
| I | VIGILIA .45 | pistola | 38 | semi | 8 | ferri fissi |
| I | TARANTOLA | micro-mitra | 13 | auto 1150 | 32 | diaframma |
| II | CARDINALE .357 | revolver | 70, perfora | semi | 6 (tamburo) | tacca e rampa |
| II | SERAFINO-18 | pistola mitragliatrice | 16 | auto 1000 | 20 | punto rosso aperto |
| II | VESPER-9 | mitra | 20 | auto 850 | 40 | punto rosso |
| II | CIMBALO-45 | mitra | 29 | auto/semi 620 | 25 | olografico |
| II | TRINITA' | fucile a raffica | 36 | raffica da 3 | 30 | maniglia con diaframma |
| II | AR-9 MALLEUS | fucile d'assalto | 30 | auto/semi 700 | 30 | punto rosso |
| II | PREDICATORE | carabina a leva | 66, perfora | leva | 8 (tubo) | tacca a corna |
| II | ROSARIO | fucile a otturatore | 118 | otturatore | 5 | diaframma |
| II | CAPPELLANO | doppietta | 10 x 14 | 2 canne | 2 | mirino a sfera |
| II | BREACHER-12 | fucile a pompa | 8 x 15 | pompa | 8 (tubo) | mira fantasma |
| III | BORDONE | PDW bullpup | 23 | auto 900 | 50 (sopra) | anello |
| III | ARCANGELO-47 | fucile d'assalto | 42 | auto/semi 600 | 30 | ferri a tangente |
| III | ORIFIAMMA | bullpup | 32 | auto/semi 680 | 30 | ottica 1,5x |
| III | LANCE-7 | fucile di precisione | 72 | semi | 12 | cannocchiale 4x |
| III | MARTELLO-12 | fucile semiautomatico | 8 x 13 | semi | 8 (scatola) | punto rosso |
| III | SALTERIO | mitragliatrice a tamburo | 36 | auto 640 | 75 | ferri |
| III | TURIBOLO (speciale) | lanciafiamme sacro | 9 al tick, perfora 4 | auto 1100, gittata 11 m | 120 (serbatoio) | ferri |
| IV | SPORA (speciale) | lanciaspine alieno | 21 x 7 spine | semi 140 | 6 | ferri |
| IV | EREMITA .50 | pistola magnum | 125, perfora | semi | 7 | ferri regolabili |
| IV | GLORIA | fucile da battaglia | 55 | auto/semi 520 | 20 | ACOG 4x |
| IV | SENTINELLA | fucile da cecchino | 190, perfora 3 | otturatore | 5 | cannocchiale 8x |
| IV | APOCALISSE-12 | fucile a tamburo | 8 x 11 | auto 300 | 20 | olografico |
| IV | CAMPANA-249 | mitragliatrice leggera | 40 | auto 780 | 100 (nastro) | ACOG 3x |
| V | ALTARE-50 | antimateriale | 420, perfora 5 | semi | 5 | cannocchiale 10x |
| V | PUPILLA (speciale) | fucile-occhio a raggio | 90, trapassa tutto | semi 210 | 14 | anello ambra |

**Armi speciali.** Il TURIBOLO e' un turibolo-lanciafiamme (la catena dondola, il fuoco e' un getto di quad additivi che perfora i nemici in fila e ha gittata corta), la SPORA e' un lanciaspine di osso e carne che spara sette aghi verdi a ventaglio, la PUPILLA e' un fucile con un occhio al posto della bocca che emette un raggio ambra continuo che trapassa tutto. Niente bossoli, suoni sintetici a parte (`SoundGen.Special`).

Ogni arma ha numeri suoi (`Scripts/Armi/WeaponDefs.cs`): caduta del danno con la distanza, moltiplicatore alla testa, perforazione dei nemici, dispersione da fermo e in mira, quanto cresce coi colpi, in corsa, accovacciati e in aria, rinculo verticale e orizzontale e quanto in fretta la visuale rientra, tempo per mirare e per estrarre, velocita' di cammino e in mira, ricarica a caricatore pieno o vuoto, e un tipo di ricarica diverso (caricatore, pistola con carrello fermo, a canna cartuccia per cartuccia e interrompibile sparando, revolver col tamburo che si apre, doppietta che si piega, nastro).

**Gunplay.** Il rinculo sposta davvero la visuale (cresce un poco nella raffica, e' piu' dolce in mira e accovacciati) e poi rientra da solo: tirando il mouse in basso lo si compensa. La dispersione e' un cono in gradi: da fermi in mira e' minima, la prima pallottola dopo una pausa e' precisa, camminando, in corsa o in aria si allarga; il mirino a schermo si apre di conseguenza e sparisce in mira. Il modello ha inerzia (resta un po' indietro rispetto al mouse), si ritrae e si alza davanti a un muro, carrello/otturatore/pompa/leva/martello si muovono, i bossoli escono dall'espulsore e rimbalzano, ogni colpo lascia una traccia luminosa, scintille, polvere e un foro sul muro. Gli spari hanno un suono per classe (pistola, .45, revolver, magnum, mitra, fucile, AK, da battaglia, leva, cecchino, antimateriale, pompa, mitragliatrice) e le manovre hanno i loro rumori.

**Mirini.** Ferri veri (tacca e mirino, diaframma, mira fantasma, mirino a sfera, orecchie di protezione), punto rosso a tubo e aperto, olografico con cerchio e trattini, anello del PDW. Il reticolo dei punti rossi e' *collimato*: e' disegnato in funzione della direzione dello sguardo rispetto all'asse dell'ottica, quindi non ha parallasse, scivola se l'occhio esce dall'asse e da davanti non si vede. Le ottiche ingrandite (ACOG, cannocchiali) mostrano nella lente l'immagine di una seconda camera con l'ingrandimento vero, il reticolo inciso (croce duplex con punti, oppure chevron luminoso), l'ombra del cannocchiale se l'occhio e' fuori asse e il respiro del tiratore (Shift lo ferma).

**Lean vero.** Q/E inclinano il busto attorno al bacino: la testa sporge di circa 43 cm e la visuale si inclina di 19 gradi; la collisione della testa impedisce di entrare nei muri (a un muro il lean si ferma), e i nemici vedono e colpiscono il giocatore dove si trova davvero: da dietro un angolo si vede la testa ma non il bacino.

**Pipeline** (`modelli/armi/`, Blender 4.5, Python di sistema con numpy e PIL):
```powershell
blender -b -P genera_armi.py -- render <nomi> rapido lato        # anteprime in render/ (python sheet_pil.py lato <nomi> fa un foglio)
blender -b -P genera_armi.py -- bake [nomi]                      # UV, bake PBR in tex/raw, FBX in export/ (~25 s ad arma)
python pack_tex.py                                               # base.jpg, mask.png, normal.jpg in tex/
Unity -executeMethod PryingEyes.EditorTools.ArmiImport.Run       # copia FBX e texture, materiali, prefab in Resources/Armi
Unity -executeMethod PryingEyes.EditorTools.ArmiShots.Run -armi p9_pieta   # foto in mano, in mira, di lato (hotel_build/armi)
blender -b -P genera_mani2.py -- build [nomi]                      # mani su misura, un paio per arma sulla sagoma vera di impugnatura e astina: mani2/<id>.fbx (con 'misura' o 'prova' numeri e anteprime su CPU); poi si copiano gli FBX in unity/PryingEyes/Assets/_PryingEyes/Models/Mani/armi/
Unity -executeMethod PryingEyes.EditorTools.ManiImport.RunAndShots [-armi p9_pieta] [-viste mano,mira,ricarica]   # importa mani.fbx e gli FBX per arma (prefab in Resources/Mani e Resources/ManiArmi/<id>) e fotografa le viste richieste in hotel_build/armi, senza costruire il player
PryingEyes.exe -batchmode -guntest [-armi p9_pieta,ar9_malleus]            # prova in gioco di tutte le armi (o di quelle indicate): foto e numeri in hotel_build/armi_gioco (guntest.txt: cadenza misurata, rinculo massimo, dispersione)
PryingEyes.exe -batchmode -guntest -fxtest -armi tu_turibolo               # in piu' foto degli effetti a quad isolati (fiamma, raggio, traccia): fxtest0-2.png
PryingEyes.exe -batchmode -autotest -leantest -camshots                    # prova del lean nell'ascensore (con un muro a 38 cm il lean si ferma a 0,64)
python sheet_game.py hotel_build/armi lato 4                               # foglio di contatto (jpg) di una vista: mano, mira, lato, sparo, ricarica
```

## La Torre: piani procedurali (Unity, scena `Torre.unity`)

Il gioco principale non e' piu' la hall con le ondate ma una discesa a piani generati a ogni partita: **un'ala d'albergo e un'ala di chiesa**, sempre tutte e due, unite da un portale e orientate in modo diverso. Un piano e' fatto di **piu' ali** di questo tipo, attaccate l'una all'altra con porte: tre ai primi due piani, quattro al terzo e quarto, cinque dal quinto in poi. Ogni ala conta circa 25 stanze (quindi circa 75 ai primi piani e circa 125 in fondo, per circa 6.000 m² di stanze in media). La hall classica resta nel progetto (`Hotel.unity`, `HotelBuilder`) ma non e' piu' nella build. **Temi.** Ogni piano ha un tema, uguale per tutte le sue ali: l'albergo con la chiesa (piani 1 e 2, poi a sorte), il **teatro** (sala con palchi e foyer, palcoscenico con camerini, uniti dall'arco scenico) e il **sanatorio** (istituto con accettazione, corsie, celle e servizi, piu' la cappella). Dal terzo piano il tema e' estratto a pari peso. Teatro e sanatorio hanno piante, materiali, luci, nebbia, suoni, arredi e modelli propri (fasi 1-3 dei temi). I modelli sono file di poltrone a ribalta, toeletta con lo specchio a lampadine, manichino da sarta e faro su treppiede per il teatro; letto di ferro, carrello, asta della flebo, sedia a rotelle, lampada scialitica e paravento per il sanatorio (`modelli/hotel/genera_arredi3.py`). Il resto degli arredi (banchi, tavoli, scaffali, poltrone dei palchi) e' ancora quello dell'albergo; i nemici di tema sono la fase 4 (vedi **Nemici di tema**).

**Teatro.** La platea ha moquetta rossa a medaglioni, file di poltrone a ribalta rivolte al palcoscenico (tre varianti: a volte con i sedili alzati o senza cuscino, e qualche fila e' sparita), lampadari a corona e un'uscita verde sopra la porta del foyer; i palchi hanno due poltrone di velluto; il foyer ha il marmo a scacchi, la biglietteria e un salotto; il ridotto un bar. Il palcoscenico ha il sipario di velluto raccolto ai lati dell'arco scenico, la ribalta di lampadine, le quinte nere, una lampada di servizio accesa al centro sotto un riflettore, il pianoforte delle prove, due fari su treppiede e l'Occhio d'oro sul fondale con un suo faro (si vede dalla sala attraverso l'arco); dietro, il corridoio delle quinte con lampadine nude, i camerini con le toelette dallo specchio a lampadine e un manichino da sarta, il deposito con altri manichini. In sottofondo un'orchestra lontana che accorda e il brusio di una sala piena, ogni tanto un applauso o il campanello di sala. La nebbia e' rossastra.

**Sanatorio.** Linoleum, vernice verde e intonaco freddo; l'accettazione con lo sportello e le sedie d'attesa, le degenze e le corsie con i letti di ferro in fila lungo i muri, le aste delle flebo e qualche paravento di tela, le celle con una branda, il refettorio con le tavolate, la sala operatoria piastrellata con il tavolo d'acciaio sotto la lampada scialitica, la lavanderia con le vasche, l'archivio a doppie file di scaffali, l'obitorio, la stanza dei medici; nei corridoi, addossati ai muri, a volte un carrello, un'asta della flebo o uno scaffale (la sedia a rotelle aspetta in accettazione: in mezzo alla corsia chiudeva la navmesh). La luce e' di tubi al neon che sfarfallano (un terzo e' morto) e la nebbia e' verdastra; in sottofondo il ronzio dei neon, ogni tanto un carrello che cigola, un monitor lontano o un portone di ferro che sbatte.

**Nemici di tema** (fase 4). Nei piani di teatro e sanatorio i monaci e gli abati (fuori dalla cappella) cedono il posto a quattro nemici costruiti sullo stesso scheletro e sulle stesse clip del monaco: cambiano costume, accessori, numeri e un colpo di scena ciascuno. La **Maschera** (teatro, l'usciere con la lanterna: arlecchino stinto con una maschera di porcellana che lascia scoperto solo l'occhio; 150 pf, svelta, solo mischia) al primo sguardo soffia il fischietto, una volta ogni 14 s: chi non sta gia' cacciando corre sul posto (`NoiseKind.Alarm`). Il **Direttore** (teatro, 2,4 m con il cilindro, frac gessato e diapason; 330 pf, lento) telegrafa la carica per 1,2 s e lancia un ventaglio di tre dardi, cinque da ferito. L'**Infermiere** (sanatorio, camice macchiato con la croce rossa e l'asta della flebo con la sacca che brilla di verde; 190 pf): la puntura ti addormenta le gambe (sedativo, 55% di velocita' per 2,8 s). Il **Primario** (sanatorio, grosso come l'abate: camice insanguinato, calotta, lampada scialitica come aureola, specchio frontale e divaricatore; 360 pf) quando ti vede chiama due infermieri, una volta ogni 45 s: arrivano fuori vista. Ognuno ha il suo sottofondo (carillon, tono di sala, monitor, ronzio clinico). Gli accessori sono rigidi, attaccati alle ossa `testa`, `bastone` e `petto` del monaco: `modelli/temi/genera_accessori.py` (`blender -b -P genera_accessori.py -- build` fa l'FBX; con `maschera`, `direttore`, `infermiere` o `primario` fa le anteprime su CPU in `render/`), i costumi sono la stoffa del monaco ridipinta da `genera_costumi_tex.py` (il disegno e' valutato in 3D sulla posizione di ogni pixel dell'atlante, la normal map e' quella del monaco) e `Editor/NemiciTemiImport.cs` ne fa i prefab in `Resources/` (`-executeMethod PryingEyes.EditorTools.NemiciTemiImport.RunAndShot`: foto nella hall in `hotel_build/shot_temi_*.png`).

**Come si gioca.** Si arriva da un ascensore (a volte in un atrio d'albergo, a volte in una sacrestia). Su ogni piano ci sono 4 **candelabri da accendere** (`F`) fino al secondo piano, poi 6, sparsi tra stanze d'albergo e di chiesa e segnalati da un alone freddo. Accenderli richiama i nemici. Quando sono tutti accesi, l'ascensore di uscita si apre e si può scendere (punti bonus). Le armi si comprano con i punti presso i tre espositori a parete di ogni piano. Se si muore si rinasce nell'ascensore di arrivo, sullo stesso piano.

**Stealth.** Le luci si affievoliscono scendendo. Monaco, abate e occhio pattugliano, indagano sui rumori e cercano nell'ultimo punto in cui ti hanno visto. Gli occhi sopra la testa indicano sospetto, ricerca e caccia. Al buio e accovacciato sei più difficile da notare. La torcia aumenta la distanza a cui ti vedono. Il battito e lo sfarfallio avvisano quando si avvicina una minaccia.

| Tasto | Azione |
|---|---|
| `M` | Apri o chiudi la mappa del piano esplorato. |
| `N` | Mostra o nascondi la minimappa. |
| `T` | Accendi o spegni la torcia. Una pila dura circa 3 minuti e mezzo di luce. |
| `Y` | Sostituisci la pila quando la carica è all'80% o meno. A zero la torcia usa automaticamente una scorta, se disponibile. |
| `F` | Raccogli una pila da terra o accendi il candelabro che stai guardando. |
| `V` | Invia un ping. Per alcuni secondi mostra segnali pulsanti sulle candele ancora spente e sull'ascensore. I nemici vicini lo sentono. |

La mappa rivela le stanze visitate e segna armi, candelabri e uscita incontrati da vicino. Alcuni muri nascondono passaggi bassi: tieni premuto C o Ctrl per attraversarli. I nemici infliggono il 50% di danni in pi�.

L'HUD mostra carica, scorte e attesa prima del prossimo ping. La freccia in alto indica il percorso verso l'ascensore di uscita e segnala quando è aperto.

**Luminosità e nebbia.** La luce di base è più alta e la nebbia sfuma la distanza, con toni più freddi nell'ala di chiesa. Il cursore **Luminosità** è nelle impostazioni del menu iniziale e della pausa. Regola l'esposizione subito e salva il valore per le partite successive.

**Alieni.** La **Falena** è rapida e fragile, con ali viola e occhi rosa. Dal secondo piano si possono incontrare anche la **Medusa**, più lenta e dotata di tentacoli ciano, e il **Carapace**, una creatura corazzata a sei zampe che colpisce da vicino. Tutti pattugliano e hanno un punto debole luminoso sul muso.

**Stanze.** Albergo: atrio con reception, corridoio con applique, camere e suite (letto, comodini, armadio, scrivania), ristorante con cucina, sala da ballo con pianoforte, locali di servizio. Chiesa: nartece con fonti e statue, navata con panche, candelabri e lampadari a corona (capriate a vista), navate laterali con confessionali, cappelle laterali, presbiterio con altare e pala con l'Occhio d'oro, stalli del coro e organo, sacrestia, cripta con sarcofagi (volta a botte con nervature), campanile. Vetrate colorate con l'Occhio: ogni finestra e' un faro colorato in tempo reale che proietta le ombre delle panche.

**Codice** (`Assets/_PryingEyes/Scripts/Proc/`, namespace `PryingEyes.Proc`):
- `PlanTypes.cs`, `PlanGen.cs`: la pianta e' geometria intera in decimetri, senza `UnityEngine` (si genera e si prova fuori da Unity). `PlanThemes.cs` contiene il teatro e il sanatorio: le loro ali, i portali e le funzioni delle stanze. Un'ala-coppia (albergo e chiesa) viene generata a parte e unita dal suo portale; `Compound` e `Attach` aggiungono le altre ali su un lato a caso, con una delle 8 trasformazioni (rotazioni e specchio), e aprono una porta in un tratto di muro in comune; due stanzette-ascensore (arrivo e uscita) si attaccano ai muri esterni; le finestre e le stanze delle candele si decidono alla fine. 3.000 piani in circa 3 secondi, nessuno fallito.
- `MeshBuilder.cs`, `FloorBuilder.cs`: muri a facce sottili (ogni stanza disegna la sua, 15 cm dentro la linea), fette verticali per porte, arcate e finestre con testa ad arco acuto, soffitti piani, a travi, a capriata o a volta (acuta o a tutto sesto) con nervature, riquadri delle aperture, vetri. Un piano di circa 105 stanze si costruisce in circa 210 ms, con la navmesh in circa 40 ms (media su 500 piani in `ProcCheck`).
- `RoomKits*.cs`, `PropDefs.cs`: le ricette di arredo e di luce per tipo di stanza (seme per stanza). Gli arredi sono prefab del `ProcKit`; se un modello manca compare una scatola della stessa misura. Tracciati a L riservati tra le porte e corsie libere nella navata evitano che gli arredi chiudano una stanza.
- `FloorDirector.cs` (regia del piano, orde, armi, bot), `ProcLift.cs`, `Candelabro.cs`, `Interact.cs`, `LightPool.cs` (solo le luci vicine sono accese e solo le 4 piu' vicine fanno ombra), `Connectivity.cs` (rete di sicurezza: se una stanza o l'uscita non si raggiunge a piedi si svuotano gli arredi della stanza e si ricostruisce la navmesh), `ProcAmbience.cs` (organo e riverbero lungo in chiesa, ronzio ed eco corta in albergo, rintocchi lontani, valzer del grammofono).
- Editor: `TorreBuilder.cs` (scena e kit di materiali/prefab), `ProcShots.cs` (foto di un piano in edit-mode, `-nav` disegna la navmesh), `ProcCheck.cs` (connettivita' su centinaia di piani, mappa ASCII di una stanza), `NavMap.cs` (navmesh di un piano in PPM: camminabile, componente dell'arrivo, porte che la separano dal resto).

**Asset.** Texture PBR procedurali in `modelli/hotel/genera_texture_chiesa.py` (pietra di muro, lastre, pietra della cripta, mattoni delle volte, parquet, piastrelle, cemento, quattro vetrate). Modelli Blender: `modelli/chiesa/genera_chiesa.py` (panca, altare, pala, candelabro, stand di ceri, confessionale, pulpito, fonte, statua, organo, sarcofago, lampadario, stallo, inginocchiatoio) e `modelli/hotel/genera_arredi2.py` (letto, comodino, armadio, scrivania, sedia, tavolo, pianoforte, fornello, tavolo d'acciaio, scaffale). Gli FBX si copiano in `Assets/_PryingEyes/Models/{Chiesa,Arredi}` e `TorreBuilder.FillKit` ne fa i prefab. Teatro e sanatorio: `modelli/hotel/genera_arredi3.py` (12 modelli; `blender -b -P genera_arredi3.py -- render [nomi]` fa le anteprime su CPU, senza toccare la GPU, `-- build` gli FBX in `arredi3/`) e la texture `ruggine` in `genera_texture.py` (ruolo `A_ruggine`: ferro verniciato che salta e lascia vedere la ruggine).

**Comandi** (Unity 6000.6.7f1, da PowerShell con `Start-Process -Wait`):
- Provare la pianta senza Unity: `cd tools/planlab; dotnet run -c Release -- stats 3000` (o `sheet <seme> 6 file.ppm` per vedere le mappe).
- Scena + build: `-executeMethod PryingEyes.EditorTools.BuildPlayer.Torre` (niente bake, ~20 s).
- Foto di un piano: `-executeMethod PryingEyes.EditorTools.ProcShots.Run -seed 1000 -floor 1 [-rooms Navata,Camera] [-nav 1]` (uscita in `hotel_build/proc_*.png`).
- Connettivita': `-executeMethod PryingEyes.EditorTools.ProcCheck.Run -seeds 500` (riepilogo in `hotel_build/proccheck.txt`). Navmesh di un piano: `-executeMethod PryingEyes.EditorTools.NavMap.Run -seed 3017 -floor 2 -out navmap.ppm`, con le porte che separano l'arrivo dal resto. Con `-propstats` `ProcCheck` scrive anche `hotel_build/propstats.txt`: quanti arredi di ogni tipo finiscono in quale tipo di stanza, sommati sui piani.
- Bot che attraversa i piani: `PryingEyes.exe -batchmode -nographics -autotest -procbot -procsecs 240 -seed 1000` (log in `hotel_build/proctest.txt`); con `-procshots` (senza `-nographics`) scatta foto dal punto di vista del giocatore, con HUD (`hotel_build/gioco_p*_*.png`).
- Costo di un fotogramma a 1920x1080: `PryingEyes.exe -batchmode -autotest -perftest [-perfshots] [-perftag nome] [-perfdebug] -seed 2961 -floor 3` (seme 2961 = teatro, 2995 = albergo, 3046 = sanatorio, tutti al piano 3). Senza `-nographics`: in batchmode Unity non disegna le camere da solo, quindi la sonda chiama `cam.Render()` su una RenderTexture e legge un pixel per aspettare la GPU. Misura la stanza piu' grande di ogni tipo guardando dalla soglia principale e aggiunge i risultati a `hotel_build/perf.txt` (logica, render sulla CPU, attesa della GPU, p50/p95/p99). `-perfshots` salva anche `hotel_build/perf/<etichetta>_<tema>_<stanza>.png`: la vista vera del giocatore, con nebbia, luci e post-processing del gioco; `-perfdebug` elenca le luci di ogni stanza. Prima della fase 2 dei temi, sulla RTX 5060 Ti: albergo 5,0 ms in media (caso peggiore p99 8,9 ms), sanatorio 4,4 ms, teatro 2,3 ms; con i modelli propri della fase 3, a scheda libera e su tre giri (da un giro all'altro la GPU cambia frequenza e le misure ballano del 10%): albergo 4,8-5,3 ms, sanatorio 4,9-5,2 ms (p99 peggiore 8,3), teatro 3,9-4,4 ms (p99 peggiore 5,8). Al posto del giro delle stanze, `-perfprops` fotografa da vicino ogni arredo nuovo (`-perftorch` accende la torcia, `-perfgamma 0.6` schiarisce la foto: solo la foto, non il gioco) e scrive in quali stanze compare e quanti sono. Si misura a scheda libera: la sonda scalda la GPU per 3 s (a riposo sta a ~1 GHz di 3), ma se un altro programma o il gioco aperto la usa i numeri salgono di 3-4 volte, anche per l'albergo. Controllare con `nvidia-smi` e non lanciarla mentre qualcuno gioca.
- Compilare in una cartella a parte, per provare mentre una copia del gioco e' aperta (blocca i file di `Builds/`): `-executeMethod PryingEyes.EditorTools.BuildPlayer.Torre -buildOut <cartella>` (anche `BuildPlayer.Windows`, senza rigenerare scena e kit).
- Ricompilare solo il codice (per provare la modifica a uno script senza riscrivere ~700 MB di dati e senza rigenerare scena e materiali): aggiungi `-scriptsOnly` al comando di `BuildPlayer.Torre` con `-buildOut`. Serve una build completa gia' presente nella stessa cartella (senza, ricade sulla completa); se sono cambiati modelli, texture, prefab, materiali o componenti della scena serve la build completa, cioe' senza il flag.
- Prova stealth: `PryingEyes.exe -batchmode -autotest -stealthtest -seed 1000`. Controlla vista, udito, torcia, pile, ping e apertura dell'uscita. Foto e risultato in `hotel_build/furtivo/`.
- Prova rapida di luminosità e alieni: aggiungi `-alientest` al comando precedente. Salva le foto dei tre alieni e del corridoio con diverse esposizioni.
- Prova dei nemici di tema: aggiungi `-temitest` al comando della prova stealth (`-seed 2961 -floor 3` e' un teatro, `-seed 3046 -floor 3` un sanatorio). Mette Maschera, Direttore, Infermiere e Primario in fondo alla sala piu' lunga (foto di fronte e di tre quarti con la torcia in `hotel_build/furtivo/tema_<Nome>.png` e `tema_<Nome>_3q.png`) e controlla i loro colpi di scena: fischietto della Maschera, sedativo dell'Infermiere, rinforzi del Primario, ventaglio di tre dardi del Direttore. Risultato in `hotel_build/furtivo/temi.txt`.
- Confronto visivo della chiesa dalla build: `PryingEyes.exe -batchmode -autotest -stealthtest -refshot -seed 1000`. Salva le inquadrature in `hotel_build/fotorealismo/` e verifica che le fibre della veste seguano la posa e che tutte e cinque le clip del monaco siano presenti.

## Build e controlli touch

La scena da includere è `Assets/_PryingEyes/Scenes/Torre.unity`. Unity 6000.6.7f1: **File > Build Profiles**, aggiungi la piattaforma e scegli **Switch Profile**. Installa i moduli mancanti dalla stessa versione in Unity Hub.

- Windows: Build; distribuisci tutta la cartella prodotta, esclusa la cartella `*_BackUpThisFolder_ButDontShipItWithYourGame`. Il metodo `PryingEyes.EditorTools.BuildPlayer.Windows` genera `Builds/PryingEyes.exe`.
- Web: installa Web Build Support; Build and Run per provare, oppure carica tutta la cartella su un server HTTP configurato per la compressione scelta. Il metodo `PryingEyes.EditorTools.BuildPlayer.Web` genera `Builds/Web`. I suoni lunghi sono generati durante il caricamento perché Web non supporta thread C#.
- iOS: installa iOS Build Support, scegli iOS e fai Build. Apri il progetto generato su un Mac con Xcode, imposta un bundle identifier unico, il team e la firma, poi compila e installa sull'iPhone. Per la distribuzione servono le credenziali e le autorizzazioni Apple appropriate.
- Android: installa Android Build Support con SDK, NDK e OpenJDK, poi genera un APK per una prova o un AAB firmato per Google Play.

Su iOS e Android il gioco usa il paesaggio: trascina a sinistra per muoverti, trascina nell'area libera a destra per guardare e tieni premuti i pulsanti per le azioni continue. I pulsanti coprono fuoco, mira, salto, accovacciamento, corsa, interazione, ricarica, cambio arma, modo di fuoco, torcia, pila, ping, lean e pausa. I controlli e le prestazioni devono ancora essere provati e calibrati su dispositivi reali; la versione Web non abilita questi controlli mobili.
