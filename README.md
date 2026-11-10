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
PryingEyes.exe -batchmode -guntest [-armi p9_pieta,ar9_malleus]            # prova in gioco di tutte le armi (o di quelle indicate): foto e numeri in hotel_build/armi_gioco (guntest.txt: cadenza misurata, rinculo massimo, dispersione)
PryingEyes.exe -batchmode -guntest -fxtest -armi tu_turibolo               # in piu' foto degli effetti a quad isolati (fiamma, raggio, traccia): fxtest0-2.png
PryingEyes.exe -batchmode -autotest -leantest -camshots                    # prova del lean nell'ascensore (con un muro a 38 cm il lean si ferma a 0,64)
python sheet_game.py hotel_build/armi lato 4                               # foglio di contatto (jpg) di una vista: mano, mira, lato, sparo, ricarica
```

## La Torre: piani procedurali (Unity, scena `Torre.unity`)

Il gioco principale non e' piu' la hall con le ondate ma una discesa a piani generati a ogni partita: **un'ala d'albergo e un'ala di chiesa**, sempre tutte e due, unite da un portale e orientate in modo diverso a ogni piano (forme a L o dritte, 19-35 stanze, 25-90 m). La hall classica resta nel progetto (`Hotel.unity`, `HotelBuilder`) ma non e' piu' nella build.

**Come si gioca.** Si arriva da un ascensore (a volte in un atrio d'albergo, a volte in una sacrestia). Su ogni piano ci sono 2-4 **candelabri da accendere** (`F`), sparsi tra stanze d'albergo e di chiesa e segnalati da un alone freddo. Accenderli richiama i nemici. Quando sono tutti accesi, l'ascensore di uscita si apre e si può scendere (punti bonus). Restano le armi sui muri (3 espositori per piano), i punti come valuta e i tre nemici (monaco, abate, occhio). Se si muore si rinasce nell'ascensore di arrivo, sullo stesso piano.

**Stealth.** Le luci si affievoliscono scendendo. Monaco, abate e occhio pattugliano, indagano sui rumori e cercano nell'ultimo punto in cui ti hanno visto. Gli occhi sopra la testa indicano sospetto, ricerca e caccia. Al buio e accovacciato sei più difficile da notare. La torcia aumenta la distanza a cui ti vedono. Il battito e lo sfarfallio avvisano quando si avvicina una minaccia.

| Tasto | Azione |
|---|---|
| `T` | Accendi o spegni la torcia. Una pila dura circa 3 minuti e mezzo di luce. |
| `Y` | Sostituisci la pila quando la carica è all'80% o meno. A zero la torcia usa automaticamente una scorta, se disponibile. |
| `F` | Raccogli una pila da terra o accendi il candelabro che stai guardando. |
| `V` | Invia un ping. Per alcuni secondi mostra segnali pulsanti sulle candele ancora spente e sull'ascensore. I nemici vicini lo sentono. |

L'HUD mostra carica, scorte e attesa prima del prossimo ping. La freccia in alto indica il percorso verso l'ascensore di uscita e segnala quando è aperto.

**Stanze.** Albergo: atrio con reception, corridoio con applique, camere e suite (letto, comodini, armadio, scrivania), ristorante con cucina, sala da ballo con pianoforte, locali di servizio. Chiesa: nartece con fonti e statue, navata con panche, candelabri e lampadari a corona (capriate a vista), navate laterali con confessionali, cappelle laterali, presbiterio con altare e pala con l'Occhio d'oro, stalli del coro e organo, sacrestia, cripta con sarcofagi (volta a botte con nervature), campanile. Vetrate colorate con l'Occhio: ogni finestra e' un faro colorato in tempo reale che proietta le ombre delle panche.

**Codice** (`Assets/_PryingEyes/Scripts/Proc/`, namespace `PryingEyes.Proc`):
- `PlanTypes.cs`, `PlanGen.cs`: la pianta e' geometria intera in decimetri, senza `UnityEngine` (si genera e si prova fuori da Unity). Ala d'albergo e ala di chiesa vengono generate a parte, poi uno dei tratti di muro delle due si incolla all'altro provando le 8 trasformazioni (rotazioni e specchio) e scartando le sovrapposizioni; due stanzette-ascensore (arrivo e uscita) si attaccano ai muri esterni; le finestre e le stanze delle candele si decidono alla fine. 3.000 piani in mezzo secondo, mai falliti.
- `MeshBuilder.cs`, `FloorBuilder.cs`: muri a facce sottili (ogni stanza disegna la sua, 15 cm dentro la linea), fette verticali per porte, arcate e finestre con testa ad arco acuto, soffitti piani, a travi, a capriata o a volta (acuta o a tutto sesto) con nervature, riquadri delle aperture, vetri. Un piano da 27 stanze si costruisce in circa 15 ms.
- `RoomKits*.cs`, `PropDefs.cs`: le ricette di arredo e di luce per tipo di stanza (seme per stanza). Gli arredi sono prefab del `ProcKit`; se un modello manca compare una scatola della stessa misura. Tracciati a L riservati tra le porte e corsie libere nella navata evitano che gli arredi chiudano una stanza.
- `FloorDirector.cs` (regia del piano, orde, armi, bot), `ProcLift.cs`, `Candelabro.cs`, `Interact.cs`, `LightPool.cs` (solo le luci vicine sono accese e solo le 4 piu' vicine fanno ombra), `Connectivity.cs` (rete di sicurezza: se una stanza o l'uscita non si raggiunge a piedi si svuotano gli arredi della stanza e si ricostruisce la navmesh), `ProcAmbience.cs` (organo e riverbero lungo in chiesa, ronzio ed eco corta in albergo, rintocchi lontani, valzer del grammofono).
- Editor: `TorreBuilder.cs` (scena e kit di materiali/prefab), `ProcShots.cs` (foto di un piano in edit-mode, `-nav` disegna la navmesh), `ProcCheck.cs` (connettivita' su centinaia di piani, mappa ASCII di una stanza).

**Asset.** Texture PBR procedurali in `modelli/hotel/genera_texture_chiesa.py` (pietra di muro, lastre, pietra della cripta, mattoni delle volte, parquet, piastrelle, cemento, quattro vetrate). Modelli Blender: `modelli/chiesa/genera_chiesa.py` (panca, altare, pala, candelabro, stand di ceri, confessionale, pulpito, fonte, statua, organo, sarcofago, lampadario, stallo, inginocchiatoio) e `modelli/hotel/genera_arredi2.py` (letto, comodino, armadio, scrivania, sedia, tavolo, pianoforte, fornello, tavolo d'acciaio, scaffale). Gli FBX si copiano in `Assets/_PryingEyes/Models/{Chiesa,Arredi}` e `TorreBuilder.FillKit` ne fa i prefab.

**Comandi** (Unity 6000.6.7f1, da PowerShell con `Start-Process -Wait`):
- Provare la pianta senza Unity: `cd tools/planlab; dotnet run -c Release -- stats 3000` (o `sheet <seme> 6 file.ppm` per vedere le mappe).
- Scena + build: `-executeMethod PryingEyes.EditorTools.BuildPlayer.Torre` (niente bake, ~20 s).
- Foto di un piano: `-executeMethod PryingEyes.EditorTools.ProcShots.Run -seed 1000 -floor 1 [-rooms Navata,Camera] [-nav 1]` (uscita in `hotel_build/proc_*.png`).
- Connettivita': `-executeMethod PryingEyes.EditorTools.ProcCheck.Run -seeds 500` (riepilogo in `hotel_build/proccheck.txt`).
- Bot che attraversa i piani: `PryingEyes.exe -batchmode -nographics -autotest -procbot -procsecs 240 -seed 1000` (log in `hotel_build/proctest.txt`); con `-procshots` (senza `-nographics`) scatta foto dal punto di vista del giocatore, con HUD (`hotel_build/gioco_p*_*.png`).
- Prova stealth: `PryingEyes.exe -batchmode -autotest -stealthtest -seed 1000`. Controlla vista, udito, torcia, pile, ping e apertura dell'uscita. Foto e risultato in `hotel_build/furtivo/`.
