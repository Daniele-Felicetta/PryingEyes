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
