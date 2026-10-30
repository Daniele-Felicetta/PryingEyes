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
