// ui.js — HUD, banner, toast, minimap, FPS.
import { $, state, player, weapon, enemies, colliders, ARENA } from './core.js';

export function updateAmmoUI() {
  $('ammo-mag').textContent = weapon.mag;
  $('ammo-reserve').textContent = '/ ' + weapon.reserve;
  $('ammo-mag').style.color = weapon.mag <= 6 ? '#ff5d5d' : '#fff';
}

export function updateBars() {
  $('health-num').textContent = Math.ceil(player.hp);
  $('health-fill').style.width = player.hp + '%';
  $('health-fill').style.background = player.hp > 50 ? 'linear-gradient(90deg,#38f0ff,#7dffcb)' : player.hp > 25 ? 'linear-gradient(90deg,#ffd34d,#ff9d3b)' : 'linear-gradient(90deg,#ff2d55,#ff2d78)';
  $('armor-fill').style.width = (player.shield * 2) + '%';
  $('armor-num').textContent = Math.ceil(player.shield);
}

export function showHitmarker(kill) {
  const h = $('hitmarker');
  h.classList.remove('show', 'kill');
  void h.offsetWidth;
  h.classList.add('show');
  if (kill) h.classList.add('kill');
}

export function showKillConfirm(txt) {
  const k = $('kill-confirm');
  k.textContent = txt;
  k.classList.remove('show'); void k.offsetWidth; k.classList.add('show');
}

export function banner(h, p) {
  const b = $('wave-banner');
  b.querySelector('h2').textContent = h;
  b.querySelector('p').textContent = p;
  b.classList.add('show');
  clearTimeout(b._to);
  b._to = setTimeout(() => b.classList.remove('show'), 2600);
}

export function toast(msg) {
  const t = $('toast');
  t.textContent = msg; t.style.opacity = 1;
  clearTimeout(t._to);
  t._to = setTimeout(() => t.style.opacity = 0, 2600);
}

// ---- minimap ----
const mm = $('minimap').getContext('2d');
function drawMinimap() {
  const S = 160, C = S / 2, scale = S / (ARENA * 2 + 10);
  mm.clearRect(0, 0, S, S);
  mm.fillStyle = 'rgba(6,12,20,0.9)';
  mm.beginPath(); mm.arc(C, C, C, 0, 7); mm.fill();
  mm.save();
  mm.beginPath(); mm.arc(C, C, C - 2, 0, 7); mm.clip();
  mm.strokeStyle = 'rgba(56,240,255,0.25)';
  colliders.forEach((c) => {
    const x = C + c.min.x * scale, z = C + c.min.z * scale;
    const w = (c.max.x - c.min.x) * scale, h = (c.max.z - c.min.z) * scale;
    if (c.max.y - c.min.y > 2.5) { mm.fillStyle = 'rgba(120,150,180,0.5)'; mm.fillRect(x, z, w, h); }
  });
  enemies.forEach((e) => {
    if (!e.alive) return;
    mm.fillStyle = e.type === 'drone' ? '#38f0ff' : '#ff2d78';
    mm.beginPath(); mm.arc(C + e.group.position.x * scale, C + e.group.position.z * scale, 3, 0, 7); mm.fill();
  });
  mm.translate(C + player.pos.x * scale, C + player.pos.z * scale);
  mm.rotate(-player.yaw + Math.PI);
  mm.fillStyle = '#fff';
  mm.beginPath(); mm.moveTo(0, -6); mm.lineTo(4, 5); mm.lineTo(-4, 5); mm.closePath(); mm.fill();
  mm.restore();
}

let fpsAcc = 0, fpsN = 0, fpsT = 0, mmT = 0;
export function tickUI(dt) {
  fpsAcc += dt; fpsN++; fpsT += dt;
  if (fpsT > 0.5) {
    $('fps-counter').textContent = Math.round(fpsN / fpsAcc) + ' FPS';
    fpsAcc = 0; fpsN = 0; fpsT = 0;
  }
  if (!state.started) return;
  mmT += dt;
  if (mmT > 0.12) { mmT = 0; drawMinimap(); }
  if (state.locked) updateBars();
}
