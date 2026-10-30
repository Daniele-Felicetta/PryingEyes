// input.js — tastiera, mouse, pointer lock, menu.
import * as THREE from 'three';
import { $, CFG, state, canvas, player, weapon, keys, applyQuality } from './core.js';
import { AudioSys } from './audio.js';
import { startReload } from './combat.js';
import { startWave } from './enemies.js';
import { toast, banner } from './ui.js';

function lock() {
  AudioSys.init();
  canvas.requestPointerLock?.();
}

export function initInput() {
  addEventListener('keydown', (e) => {
    keys[e.code] = true;
    if (e.code === 'Space') player.jumpBuf = 0.15;
    if (e.code === 'KeyR') startReload();
    if (['Space', 'Tab'].includes(e.code)) e.preventDefault();
  });
  addEventListener('keyup', (e) => { keys[e.code] = false; });
  addEventListener('mousedown', (e) => {
    if (!state.locked) return;
    if (e.button === 0) player.firing = true;
    if (e.button === 2) player.adsHeld = true;
  });
  addEventListener('mouseup', (e) => {
    if (e.button === 0) player.firing = false;
    if (e.button === 2) player.adsHeld = false;
  });
  addEventListener('contextmenu', (e) => e.preventDefault());
  document.addEventListener('mousemove', (e) => {
    if (!state.locked || !state.started) return;
    const s = 0.0021 * CFG.sens * (weapon.ads > 0.5 ? 0.6 : 1);
    player.yaw -= e.movementX * s;
    player.pitch -= e.movementY * s;
    player.pitch = Math.max(-1.45, Math.min(1.45, player.pitch));
    weapon.sway.x = THREE.MathUtils.clamp(weapon.sway.x - e.movementX * 0.0004, -0.05, 0.05);
    weapon.sway.y = THREE.MathUtils.clamp(weapon.sway.y - e.movementY * 0.0004, -0.05, 0.05);
    if (CFG.shake) player.roll = THREE.MathUtils.clamp(player.roll - e.movementX * 0.00012, -0.05, 0.05);
  });

  $('play-btn').addEventListener('click', () => {
    CFG.sens = parseFloat($('sens').value);
    CFG.baseFov = parseInt($('fov').value);
    CFG.quality = $('quality').value;
    CFG.bloom = $('opt-bloom').checked;
    CFG.shake = $('opt-shake').checked;
    applyQuality();
    lock();
  });
  $('resume-btn').addEventListener('click', lock);
  $('sens').addEventListener('input', (e) => { CFG.sens = parseFloat(e.target.value); $('sens-val').textContent = CFG.sens.toFixed(2); });
  $('fov').addEventListener('input', (e) => { CFG.baseFov = parseInt(e.target.value); $('fov-val').textContent = CFG.baseFov; });
  $('quality').addEventListener('change', (e) => { CFG.quality = e.target.value; applyQuality(); });
  $('opt-bloom').addEventListener('change', (e) => { CFG.bloom = e.target.checked; applyQuality(); });
  $('opt-shake').addEventListener('change', (e) => { CFG.shake = e.target.checked; });

  document.addEventListener('pointerlockchange', () => {
    state.locked = document.pointerLockElement === canvas;
    if (state.locked) {
      if (!state.started) {
        state.started = true;
        $('menu').classList.add('hidden');
        $('hud').classList.remove('hidden');
        startWave(1);
        banner('PRYING EYES', 'Elimina tutti gli ostili');
        toast('RMB per mirare • Shift per scattare • C per slide');
      }
      $('pause').classList.add('hidden');
    } else if (state.started && player.alive) {
      $('pause').classList.remove('hidden');
    }
  });
  canvas.addEventListener('click', () => { if (state.started && !state.locked) lock(); });
}
