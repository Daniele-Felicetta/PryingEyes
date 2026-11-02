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
