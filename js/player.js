// player.js — controller FPS Quake-style + camera + HUD vitale.
import * as THREE from 'three';
import { $, CFG, state, camera, player, weapon, keys, colliders, ARENA } from './core.js';
import { AudioSys } from './audio.js';
import { particles } from './effects.js';
import { updateWeaponPose } from './weapon.js';
import { shoot } from './combat.js';
import { updateAmmoUI, updateBars, toast, banner } from './ui.js';

export function playerHeight() { return player.crouch || player.slide ? CFG.heightCrouch : CFG.heightStand; }

// Scalino automatico: se l'ostacolo è basso (<=0.55m) e c'è spazio sopra, sali.
function tryStepUp(c, r, h) {
  const rise = c.max.y - player.pos.y;
  if (rise <= 0.02 || rise > 0.55) return false;
  if (player.vel.y < -3) return false;
  const top = new THREE.Box3(
    new THREE.Vector3(player.pos.x - r, c.max.y + 0.02, player.pos.z - r),
    new THREE.Vector3(player.pos.x + r, c.max.y + 0.02 + h, player.pos.z + r));
  for (const o of colliders) { if (o !== c && top.intersectsBox(o)) return false; }
  player.pos.y = c.max.y + 0.001;
  if (player.vel.y < 0) player.vel.y = 0;
  player.onGround = true;
  return true;
}

function collidePlayer() {
  const r = CFG.radius, h = playerHeight();
  const min = new THREE.Vector3(player.pos.x - r, player.pos.y, player.pos.z - r);
  const max = new THREE.Vector3(player.pos.x + r, player.pos.y + h, player.pos.z + r);
  const box = new THREE.Box3(min, max);
  player.onGround = false;
  for (let iter = 0; iter < 3; iter++) {
    for (const c of colliders) {
      if (!box.intersectsBox(c)) continue;
      const overlap = new THREE.Vector3();
      overlap.x = Math.min(box.max.x - c.min.x, c.max.x - box.min.x);
      overlap.y = Math.min(box.max.y - c.min.y, c.max.y - box.min.y);
      overlap.z = Math.min(box.max.z - c.min.z, c.max.z - box.min.z);
      if (overlap.x < overlap.y && overlap.x < overlap.z) {
        if (tryStepUp(c, r, h)) {
          box.min.set(player.pos.x - r, player.pos.y, player.pos.z - r);
          box.max.set(player.pos.x + r, player.pos.y + h, player.pos.z + r);
          continue;
        }
        const dir = (box.min.x + box.max.x) / 2 < (c.min.x + c.max.x) / 2 ? -1 : 1;
        player.pos.x += dir * overlap.x;
        if (dir < 0) player.vel.x = Math.min(player.vel.x, 0); else player.vel.x = Math.max(player.vel.x, 0);
      } else if (overlap.z < overlap.y) {
        if (tryStepUp(c, r, h)) {
          box.min.set(player.pos.x - r, player.pos.y, player.pos.z - r);
          box.max.set(player.pos.x + r, player.pos.y + h, player.pos.z + r);
          continue;
        }
        const dir = (box.min.z + box.max.z) / 2 < (c.min.z + c.max.z) / 2 ? -1 : 1;
        player.pos.z += dir * overlap.z;
        if (dir < 0) player.vel.z = Math.min(player.vel.z, 0); else player.vel.z = Math.max(player.vel.z, 0);
      } else {
        const centerY = (box.min.y + box.max.y) / 2, ccY = (c.min.y + c.max.y) / 2;
        if (centerY > ccY) {
          player.pos.y = c.max.y + 0.001;
          if (player.vel.y < -9) AudioSys.land(true);
          else if (player.vel.y < -3.5) AudioSys.land(false);
          player.vel.y = 0;
          player.onGround = true;
        } else {
          player.pos.y = c.min.y - h - 0.001;
          player.vel.y = Math.min(player.vel.y, 0);
        }
      }
      box.min.set(player.pos.x - r, player.pos.y, player.pos.z - r);
      box.max.set(player.pos.x + r, player.pos.y + h, player.pos.z + r);
    }
  }
  if (player.pos.y <= 0) {
    if (!player.onGround && player.vel.y < -9) AudioSys.land(true);
    else if (!player.onGround && player.vel.y < -3.5) AudioSys.land(false);
    player.pos.y = 0; player.vel.y = 0; player.onGround = true;
  }
  player.pos.x = THREE.MathUtils.clamp(player.pos.x, -ARENA, ARENA);
  player.pos.z = THREE.MathUtils.clamp(player.pos.z, -ARENA, ARENA);
}

export function updatePlayer(dt) {
  if (!player.alive) {
    player.respawnT -= dt;
    if (player.respawnT <= 0) respawn();
    return;
  }
  const f = (keys.KeyW ? 1 : 0) - (keys.KeyS ? 1 : 0);
  const s = (keys.KeyD ? 1 : 0) - (keys.KeyA ? 1 : 0);
  const wantCrouch = !!(keys.KeyC || keys.ControlLeft);
  const wantSprint = !!(keys.ShiftLeft || keys.ShiftRight) && f > 0 && !wantCrouch && !player.adsHeld;

  // slide: sprint + crouch con velocità
  const hSpeed = Math.hypot(player.vel.x, player.vel.z);
  if (!player.slide && wantSprint && wantCrouch && hSpeed > 6 && player.onGround) {
    player.slide = true; player.slideT = 0.85; player.crouch = true;
    const dir = new THREE.Vector3(player.vel.x, 0, player.vel.z).normalize();
    player.slideDir.copy(dir);
    player.fovKick = 6;
    AudioSys.burst({ dur: 0.25, freq: 500, peak: 0.2, slideTo: 120 });
  }
  if (player.slide) {
    player.slideT -= dt;
    if (!player.onGround || player.slideT <= 0 || hSpeed < 2.5 || player.jumpBuf > 0) {
      player.slide = false;
      player.crouch = wantCrouch;
    }
  } else {
    player.crouch = wantCrouch;
  }
  player.sprint = wantSprint && !player.slide && hSpeed > 1;

  // forward = (-sin yaw, 0, -cos yaw), right = (cos yaw, 0, -sin yaw)
  const sin = Math.sin(player.yaw), cos = Math.cos(player.yaw);
  const wish = new THREE.Vector3(
    -sin * f + cos * s, 0,
    -cos * f - sin * s
  );
  if (wish.lengthSq() > 1) wish.normalize();

  let maxSpeed = player.crouch && !player.slide ? CFG.crouch : player.sprint ? CFG.sprint : CFG.walk;
  if (weapon.ads > 0.5) maxSpeed *= 0.55;

  const hv = new THREE.Vector3(player.vel.x, 0, player.vel.z);
  if (player.slide) {
    const drop = hv.length() * 1.6 * dt;
    const sp = Math.max(0, hv.length() - drop * 0.4);
    const dir = hv.length() > 0.01 ? hv.normalize() : player.slideDir;
    dir.lerp(wish, 0.12 * dt * 10).normalize();
    const keep = Math.max(sp, 4.5 * (player.slideT / 0.85));
    player.vel.x = dir.x * keep; player.vel.z = dir.z * keep;
  } else if (player.onGround) {
    const speed = hv.length();
    if (wish.lengthSq() < 0.01) {
      const drop = speed * CFG.friction * dt;
      const ns = Math.max(0, speed - drop);
      if (speed > 0.01) { player.vel.x *= ns / speed; player.vel.z *= ns / speed; }
    } else {
      const cur = player.vel.x * wish.x + player.vel.z * wish.z;
      const add = maxSpeed - cur;
      if (add > 0) {
        const acc = Math.min(CFG.accelGround * dt * maxSpeed, add);
        player.vel.x += wish.x * acc;
        player.vel.z += wish.z * acc;
      }
      const lat = new THREE.Vector3(-wish.z, 0, wish.x);
      const latV = player.vel.x * lat.x + player.vel.z * lat.z;
      player.vel.x -= lat.x * latV * Math.min(1, 10 * dt);
      player.vel.z -= lat.z * latV * Math.min(1, 10 * dt);
    }
  } else {
    const cur = player.vel.x * wish.x + player.vel.z * wish.z;
    const add = maxSpeed * 0.9 - cur;
    if (add > 0 && wish.lengthSq() > 0.01) {
      const acc = Math.min(CFG.accelAir * dt * maxSpeed, add);
      player.vel.x += wish.x * acc;
      player.vel.z += wish.z * acc;
    }
    player.vel.x *= (1 - 0.08 * dt); player.vel.z *= (1 - 0.08 * dt);
  }

  // gravità + salto (coyote + buffer)
  player.vel.y -= CFG.gravity * dt;
  player.vel.y = Math.max(player.vel.y, -22);
  if (player.onGround) player.coyote = 0.13; else player.coyote -= dt;
  player.jumpBuf -= dt;
  if (player.jumpBuf > 0 && (player.onGround || player.coyote > 0)) {
    player.vel.y = CFG.jumpVel * (player.crouch ? 0.92 : 1);
    player.onGround = false; player.coyote = 0; player.jumpBuf = 0;
    player.slide = false;
    AudioSys.jump();
    particles.burst(new THREE.Vector3(player.pos.x, player.pos.y + 0.1, player.pos.z), 8, 0x5a6a7a, 2, 0.4, 1, 6);
  }

  player.pos.x += player.vel.x * dt;
  player.pos.z += player.vel.z * dt;
  player.pos.y += player.vel.y * dt;
  collidePlayer();

  // lean Q/E
  const leanT = ((keys.KeyQ ? 1 : 0) - (keys.KeyE ? 1 : 0)) * 0.16;
  player.lean += (leanT - player.lean) * Math.min(1, 12 * dt);

  // head-bob + passi
  const hs = Math.hypot(player.vel.x, player.vel.z);
  const bobAmp = (player.crouch ? 0.45 : 1) * (1 - weapon.ads * 0.8) * Math.min(1, hs / 6);
  player.bobPhase += dt * (4 + hs * 1.35) * (player.onGround ? 1 : 0.15);
  player.stepAcc += hs * dt * (player.onGround ? 1 : 0);
  if (player.stepAcc > 2.6 && hs > 1 && player.onGround) {
    player.stepAcc = 0;
    AudioSys.step(player.sprint);
  }
  const bobY = Math.sin(player.bobPhase * 2) * 0.032 * bobAmp;
  const bobX = Math.cos(player.bobPhase) * 0.026 * bobAmp;

  const targetEye = (player.crouch || player.slide ? CFG.eyeCrouch : CFG.eyeStand) + (player.slide ? -0.12 : 0);
  player.eyeH += (targetEye - player.eyeH) * Math.min(1, 11 * dt);

  // FOV dinamico
  weapon.ads += ((player.adsHeld ? 1 : 0) - weapon.ads) * Math.min(1, 13 * dt);
  const targetFov = CFG.baseFov + (player.sprint ? 7 : 0) + (player.slide ? 5 : 0) + player.fovKick - weapon.ads * (CFG.baseFov - 52);
  player.fovKick *= Math.max(0, 1 - 6 * dt);
  camera.fov += (targetFov - camera.fov) * Math.min(1, 10 * dt);
  camera.updateProjectionMatrix();

  // camera shake
  let shX = 0, shY = 0;
  if (CFG.shake && player.shakeT > 0) {
    player.shakeT -= dt;
    const k = player.shakeAmp * (player.shakeT);
    shX = (Math.random() - 0.5) * k; shY = (Math.random() - 0.5) * k;
  }
  const strafeLean = THREE.MathUtils.clamp(-(player.vel.x * Math.cos(player.yaw) - player.vel.z * Math.sin(player.yaw)) * 0.0035, -0.035, 0.035);
  camera.position.set(
    player.pos.x + bobX * Math.cos(player.yaw) + player.lean * Math.cos(player.yaw),
    player.pos.y + player.eyeH + bobY + shY,
    player.pos.z - bobX * Math.sin(player.yaw) + player.lean * -Math.sin(player.yaw)
  );
  camera.rotation.set(player.pitch + weapon.recoil * 0.35 + shX * 0.4, player.yaw, strafeLean + player.lean * 0.9 + (player.slide ? 0.09 : 0) + player.roll * 0.4);
  player.roll *= Math.max(0, 1 - 8 * dt);
  weapon.recoil *= Math.max(0, 1 - 11 * dt);
  weapon.sway.multiplyScalar(Math.max(0, 1 - 9 * dt));

  // regen scudo/vita
  if (state.time - player.lastDamage > 4 && player.shield < 50) player.shield = Math.min(50, player.shield + 14 * dt);
  if (state.time - player.lastDamage > 7 && player.hp < 100) player.hp = Math.min(100, player.hp + 6 * dt);

  updateWeaponPose(dt, bobX, bobY, hs);

  // fuoco automatico
  const interval = 60 / weapon.rpm;
  if (player.firing && !weapon.reloading && state.time - weapon.lastShot >= interval && state.locked && !state.dead) {
    shoot();
  }
  if (weapon.flash.visible && state.time - weapon.lastShot > 0.055) {
    weapon.flash.visible = false;
    if (weapon.flashSide) weapon.flashSide.visible = false;
    weapon.flashLight.intensity = 0;
  }
  if (weapon.laser) {
    weapon.laser.material.opacity += ((weapon.ads > 0.7 ? 0.25 : 0) - weapon.laser.material.opacity) * 10 * dt;
  }
}

export function hurtPlayer(dmg) {
  if (!player.alive || !state.started) return;
  player.lastDamage = state.time;
  const absorbed = Math.min(player.shield, dmg * 0.6);
  player.shield -= absorbed;
  player.hp -= (dmg - absorbed);
  AudioSys.hurt();
  $('damage-vignette').style.opacity = 0.9;
  setTimeout(() => $('damage-vignette').style.opacity = 0, 180);
  if (CFG.shake) { player.shakeT = 0.25; player.shakeAmp = 0.05; }
  updateBars();
  if (player.hp <= 0) {
    player.hp = 0; player.alive = false; player.respawnT = 2.5;
    banner('SEI MORTO', 'Respawn in corso…');
    document.exitPointerLock?.();
  }
}

export function respawn() {
  player.alive = true; player.hp = 100; player.shield = 50;
  player.pos.set(0, 0, 19); player.vel.set(0, 0, 0);
  player.yaw = 0; player.pitch = 0;
  weapon.mag = weapon.magSize; weapon.reserve = 120; weapon.reloading = false;
  updateBars(); updateAmmoUI();
  toast('Respawnato');
}
