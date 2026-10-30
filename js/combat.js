// combat.js — hitscan, ricarica, crosshair kick. (Dipendenze circolari con
// player.js/enemies.js risolte dai live binding ESM: uso solo a runtime.)
import * as THREE from 'three';
import { $, CFG, state, camera, player, weapon, enemies, worldMeshes } from './core.js';
import { AudioSys } from './audio.js';
import { particles, spawnTracer, spawnDecal, ejectShell } from './effects.js';
import { damageEnemy } from './enemies.js';
import { updateAmmoUI } from './ui.js';
import { muzzleWorld } from './weapon.js';

const raycaster = new THREE.Raycaster();

export function startReload() {
  if (weapon.reloading || weapon.mag === weapon.magSize || weapon.reserve <= 0 || !player.alive) return;
  weapon.reloading = true; weapon.reloadT = 1.7;
  AudioSys.reload();
  $('reload-tip').classList.remove('hidden');
}

function finishReload() {
  const need = weapon.magSize - weapon.mag;
  const take = Math.min(need, weapon.reserve);
  weapon.mag += take; weapon.reserve -= take;
  weapon.reloading = false;
  $('reload-tip').classList.add('hidden');
  updateAmmoUI();
}

export function tickCombat(dt) {
  if (weapon.reloading) {
    weapon.reloadT -= dt;
    if (weapon.reloadT <= 0) finishReload();
  }
}

function findEnemyRoot(o) {
  while (o) {
    const f = enemies.find((e) => e.group === o);
    if (f) return f;
    o = o.parent;
  }
  return null;
}

export function shoot() {
  if (weapon.mag <= 0) { AudioSys.dry(); weapon.lastShot = state.time; startReload(); return; }
  weapon.mag--;
  weapon.lastShot = state.time;
  weapon.boltT = 1; // avvia il ciclo dell'otturatore
  weapon.recoil = Math.min(0.09, weapon.recoil + 0.028);
  player.fovKick = Math.min(3, player.fovKick + 0.35);
  if (CFG.shake) { player.shakeT = 0.09; player.shakeAmp = 0.012; }
  AudioSys.shoot();

  weapon.flash.visible = true;
  weapon.flashSide.visible = true;
  weapon.flash.rotation.z = Math.random() * Math.PI;
  const fs = 0.8 + Math.random() * 0.6;
  weapon.flash.scale.setScalar(fs);
  weapon.flashSide.scale.setScalar(fs);
  weapon.flashLight.intensity = 80;

  const spread = THREE.MathUtils.lerp(0.028, 0.004, weapon.ads) + Math.hypot(player.vel.x, player.vel.z) * 0.0016 + (player.onGround ? 0 : 0.012);
  const dir = new THREE.Vector3(0, 0, -1).applyQuaternion(camera.quaternion);
  dir.x += (Math.random() - 0.5) * 2 * spread;
  dir.y += (Math.random() - 0.5) * 2 * spread;
  dir.z += (Math.random() - 0.5) * 2 * spread;
  dir.normalize();
  raycaster.set(camera.getWorldPosition(new THREE.Vector3()), dir);
  raycaster.far = 120;

  const enemyMeshes = [];
  enemies.forEach((e) => { if (e.alive) enemyMeshes.push(e.group); });
  const hitsE = raycaster.intersectObjects(enemyMeshes, true);
  const hitsW = raycaster.intersectObjects(worldMeshes, false);
  const wallDist = hitsW.length ? hitsW[0].distance : 120;

  let end;
  if (hitsE.length && hitsE[0].distance < wallDist) {
    const h = hitsE[0];
    end = h.point;
    const enemy = findEnemyRoot(h.object);
    const headshot = h.object.userData.head === true;
    damageEnemy(enemy, headshot ? 65 : 30 + Math.random() * 8, h.point, headshot);
    spawnTracer(muzzleWorld(), end, 0xffe9a8);
  } else if (hitsW.length) {
    end = hitsW[0].point;
    const n = hitsW[0].face ? hitsW[0].face.normal.clone().transformDirection(hitsW[0].object.matrixWorld) : new THREE.Vector3(0, 1, 0);
    particles.burst(end, 10, 0xffc36b, 5, 0.4, 2.5);
    particles.burst(end, 6, 0x8a93a3, 3, 0.5, 1.5);
    spawnDecal(end, n);
    spawnTracer(muzzleWorld(), end);
  } else {
    end = camera.getWorldPosition(new THREE.Vector3()).addScaledVector(dir, 80);
    spawnTracer(muzzleWorld(), end);
  }

  // bossolo a destra
  const right = new THREE.Vector3(1, 0, 0).applyQuaternion(camera.quaternion);
  ejectShell(camera.getWorldPosition(new THREE.Vector3()).addScaledVector(dir, 0.5).add(new THREE.Vector3(0, -0.15, 0)), right);

  // fumo dalla volata
  particles.burst(muzzleWorld(), 2, 0x9aa4b2, 0.8, 0.7, 0.6, -0.5);

  // crosshair kick
  const ch = $('crosshair');
  const gap = 8 + weapon.recoil * 220 + Math.hypot(player.vel.x, player.vel.z) * 1.2;
  ch.style.setProperty('--gap', gap.toFixed(1) + 'px');
  updateAmmoUI();
}
