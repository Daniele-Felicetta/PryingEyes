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

- **armi sui muri stile COD Zombies** (`Scripts/WallBuy.cs`): VESPER-9 (SMG) 500 punti, BREACHER-12 (pompa) 1200, LANCE-7 (DMR semiauto) 1800. Pannello metallico, sagoma di gesso, arma 3D, nome/prezzo e luce colorata; avvicinati e premi **F** per comprare. Riacquistando la stessa arma ricarichi la riserva a metà prezzo.
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

## La Torre: piani procedurali (Unity, scena `Torre.unity`)

Il gioco principale non e' piu' la hall con le ondate ma una discesa a piani generati a ogni partita: **un'ala d'albergo e un'ala di chiesa**, sempre tutte e due, unite da un portale e orientate in modo diverso a ogni piano (forme a L o dritte, 19-35 stanze, 25-90 m). La hall classica resta nel progetto (`Hotel.unity`, `HotelBuilder`) ma non e' piu' nella build.

**Come si gioca.** Si arriva da un ascensore (a volte in un atrio d'albergo, a volte in una sacrestia). Su ogni piano ci sono 2-4 **candelabri da accendere** (`F`), sparsi tra stanze d'albergo e di chiesa e segnalati da un alone freddo; a ogni candela arriva un'orda, quando sono tutte accese l'ascensore di uscita si apre e si scende (punti bonus). Restano le armi sui muri (3 espositori per piano, scelti per distanza dall'arrivo), i punti come valuta, i tre nemici (monaco, abate, occhio) e un rivolo di presenze a intervalli. Se si muore si rinasce nell'ascensore di arrivo, il piano resta com'e'.

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
