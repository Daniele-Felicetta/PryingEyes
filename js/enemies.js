// enemies.js — WASP + RAVAGER, danni, smembramento, ondate.
// (Dipendenze circolari con player.js/combat.js risolte dai live binding ESM: uso solo a runtime.)
import * as THREE from 'three';
import { $, state, scene, player, weapon, enemies, colliders, ARENA } from './core.js';
import { SPAWN_POINTS, groundHeightAt } from './world.js';
import { AudioSys } from './audio.js';
import { particles, shockwave, spawnDebris, addScorch } from './effects.js';
import { showHitmarker, showKillConfirm, updateAmmoUI, banner, toast } from './ui.js';
import { hurtPlayer } from './player.js';
import { canvasTex } from './textures.js';

function em(color, intensity, opts = {}) {
  const m = new THREE.MeshStandardMaterial({ color: 0x0a0c12, emissive: color, emissiveIntensity: intensity, roughness: 0.4, metalness: 0.6, ...opts });
  m.userData.base = intensity;
  return m;
}

const rotorTex = canvasTex(128, (g, s) => {
  g.clearRect(0, 0, s, s);
  for (let i = 0; i < 3; i++) {
    g.strokeStyle = `rgba(180,200,220,${0.5 - i * 0.14})`;
    g.lineWidth = 7 - i * 2;
    g.beginPath(); g.arc(s / 2, s / 2, s / 2 - 6 - i * 9, 0, 7); g.stroke();
  }
  g.fillStyle = 'rgba(40,45,55,0.9)';
  g.beginPath(); g.arc(s / 2, s / 2, 10, 0, 7); g.fill();
}, 1);

function makeDrone() {
  const g = new THREE.Group();
  const armor = new THREE.MeshStandardMaterial({ color: 0x39414f, roughness: 0.32, metalness: 0.92, envMapIntensity: 1.0 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x14171e, roughness: 0.6, metalness: 0.8 });
  const core = new THREE.Mesh(new THREE.IcosahedronGeometry(0.36, 1), armor);
  core.castShadow = true; g.add(core);
  const belt = new THREE.Mesh(new THREE.TorusGeometry(0.37, 0.045, 8, 28), em(0x38f0ff, 2.2));
  belt.rotation.x = Math.PI / 2; g.add(belt);
  const dome = new THREE.Mesh(new THREE.SphereGeometry(0.2, 14, 10, 0, Math.PI * 2, 0, 1.1), dark);
  dome.position.y = 0.22; g.add(dome);
  // sensore su gimbal frontale (+Z = verso il giocatore dopo lookAt)
  const gimbal = new THREE.Group(); gimbal.position.set(0, 0.02, 0.3); g.add(gimbal);
  const housing = new THREE.Mesh(new THREE.SphereGeometry(0.11, 12, 10), dark);
  gimbal.add(housing);
  const eyeMat = em(0xff2d78, 5);
  const eye = new THREE.Mesh(new THREE.SphereGeometry(0.062, 12, 10), eyeMat);
  eye.position.z = 0.07; eye.userData.head = true; gimbal.add(eye);
  const rotors = [];
  [[1, 1], [1, -1], [-1, 1], [-1, -1]].forEach(([sx, sz], i) => {
    const arm = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.05, 0.62, 8), dark);
    arm.position.set(sx * 0.32, 0.16, sz * 0.32);
    arm.rotation.z = sx * 0.9; arm.rotation.x = -sz * 0.9;
    g.add(arm);
    const motor = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, 0.1, 10), armor);
    motor.position.set(sx * 0.58, 0.34, sz * 0.58); g.add(motor);
    const disc = new THREE.Mesh(new THREE.CircleGeometry(0.3, 24),
      new THREE.MeshBasicMaterial({ map: rotorTex, transparent: true, opacity: 0.55, side: THREE.DoubleSide, depthWrite: false }));
    disc.rotation.x = -Math.PI / 2;
    disc.position.set(sx * 0.58, 0.42, sz * 0.58);
    g.add(disc); rotors.push(disc);
  });
  const sting = new THREE.Mesh(new THREE.ConeGeometry(0.07, 0.3, 8), dark);
  sting.position.y = -0.42; sting.rotation.x = Math.PI; g.add(sting);
  const belly = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.03, 0.12), em(0xff9d3b, 1.6));
  belly.position.set(0, -0.3, 0.1); g.add(belly);
  const navMat = em(0xff2222, 4);
  const nav = new THREE.Mesh(new THREE.SphereGeometry(0.03, 8, 8), navMat);
  nav.position.set(0, 0.38, 0.15); g.add(nav);
  g.userData = { rotors, eyeGimbal: gimbal, eyeMat, navMat, bank: 0, smokeT: 0 };
  return g;
}

function makeBrute() {
  const g = new THREE.Group();
  const armor = new THREE.MeshStandardMaterial({ color: 0x4d4438, roughness: 0.52, metalness: 0.78, envMapIntensity: 0.8 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x1c1f27, roughness: 0.55, metalness: 0.85 });
  const seamMats = [];
  const B = (geo, mat, x, y, z, rx = 0) => {
    const m = new THREE.Mesh(geo, mat);
    m.position.set(x, y, z); m.rotation.x = rx; m.castShadow = true; g.add(m); return m;
  };
  const mkLeg = (sx) => {
    B(new THREE.BoxGeometry(0.3, 0.14, 0.5), dark, sx * 0.26, 0.07, -0.05);
    const shinG = new THREE.Group(); shinG.position.set(sx * 0.26, 0.14, 0); g.add(shinG);
    const shin = new THREE.Mesh(new THREE.BoxGeometry(0.24, 0.55, 0.28), armor);
    shin.position.y = 0.28; shin.castShadow = true; shinG.add(shin);
    const thighG = new THREE.Group(); thighG.position.set(sx * 0.26, 0.72, 0); g.add(thighG);
    const thigh = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.5, 0.34), armor);
    thigh.position.y = 0.25; thigh.castShadow = true; thighG.add(thigh);
    B(new THREE.CylinderGeometry(0.045, 0.045, 0.5, 8), dark, sx * 0.26, 0.42, 0.2);
    return { shinG, thighG };
  };
  const legL = mkLeg(-1), legR = mkLeg(1);
  B(new THREE.BoxGeometry(0.62, 0.3, 0.44), dark, 0, 1.1, 0);
  const beltB = new THREE.Mesh(new THREE.BoxGeometry(0.64, 0.05, 0.46), em(0x38f0ff, 1.4));
  beltB.position.set(0, 1.22, 0); g.add(beltB);
  const torsoG = new THREE.Group(); torsoG.position.set(0, 1.28, 0); g.add(torsoG);
  const tAdd = (geo, mat, x, y, z, rx = 0) => {
    const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.rotation.x = rx;
    m.castShadow = true; torsoG.add(m); return m;
  };
  tAdd(new THREE.BoxGeometry(0.7, 0.75, 0.5), dark, 0, 0.36, 0);
  tAdd(new THREE.BoxGeometry(0.78, 0.6, 0.14), armor, 0, 0.36, -0.28, 0.08);
  tAdd(new THREE.BoxGeometry(0.78, 0.6, 0.14), armor, 0, 0.36, 0.28, -0.08);
  tAdd(new THREE.BoxGeometry(0.14, 0.6, 0.56), armor, -0.4, 0.36, 0);
  tAdd(new THREE.BoxGeometry(0.14, 0.6, 0.56), armor, 0.4, 0.36, 0);
  const s1 = em(0xff7a2d, 1.8); seamMats.push(s1);
  const sm1 = new THREE.Mesh(new THREE.BoxGeometry(0.5, 0.035, 0.02), s1);
  sm1.position.set(0, 0.36, 0.36); torsoG.add(sm1);
  const padGeo = new THREE.BoxGeometry(0.34, 0.26, 0.4);
  const padL = tAdd(padGeo, armor, -0.56, 0.62, 0);
  tAdd(padGeo, armor, 0.56, 0.62, 0);
  const mkArm = (sx) => {
    const upG = new THREE.Group(); upG.position.set(sx * 0.55, 1.85, 0); g.add(upG);
    const up = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.45, 0.24), armor);
    up.position.y = -0.2; up.castShadow = true; upG.add(up);
    const foreG = new THREE.Group(); foreG.position.set(0, -0.42, 0); upG.add(foreG);
    const fore = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.4, 0.22), dark);
    fore.position.y = -0.18; fore.castShadow = true; foreG.add(fore);
    const c1 = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.24, 0.1), armor);
    c1.position.set(-0.07, -0.45, -0.05); c1.rotation.x = 0.3; foreG.add(c1);
    const c2 = c1.clone(); c2.position.x = 0.07; foreG.add(c2);
    return { upG, foreG };
  };
  const armL = mkArm(-1), armR = mkArm(1);
  const headG = new THREE.Group(); headG.position.set(0, 2.0, -0.05); g.add(headG);
  const hAdd = (geo, mat, x, y, z) => { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.castShadow = true; headG.add(m); return m; };
  const skull = hAdd(new THREE.BoxGeometry(0.4, 0.32, 0.4), dark, 0, 0.1, 0);
  skull.userData.head = true;
  const visorMat = em(0xff2d78, 4);
  const visor = new THREE.Mesh(new THREE.BoxGeometry(0.32, 0.07, 0.05), visorMat);
  visor.position.set(0, 0.12, 0.21); visor.userData.head = true; headG.add(visor);
  hAdd(new THREE.BoxGeometry(0.3, 0.1, 0.08), armor, 0, -0.06, 0.18);
  hAdd(new THREE.CylinderGeometry(0.012, 0.012, 0.35, 6), dark, 0.18, 0.35, 0.05);
  const antTip = new THREE.Mesh(new THREE.SphereGeometry(0.025, 8, 8), em(0xff2d78, 3));
  antTip.position.set(0.18, 0.53, 0.05); headG.add(antTip);
  g.userData = { legL, legR, armL, armR, torsoG, headG, visorMat, seamMats, padL, padLOn: true, atkAnim: 0, scorches: [] };
  return g;
}

function spawnEnemy(type, pos) {
  const group = type === 'drone' ? makeDrone() : makeBrute();
  group.position.copy(pos);
  scene.add(group);
  const e = {
    type, group, alive: true,
    hp: type === 'drone' ? 60 : 130,
    maxHp: type === 'drone' ? 60 : 130,
    speed: type === 'drone' ? 4.2 + Math.random() * 1.5 : 2.6 + Math.random(),
    atkCd: 0, flash: 0, bobR: Math.random() * 10,
    vel: new THREE.Vector3(),
  };
  enemies.push(e);
  return e;
}

export function damageEnemy(e, dmg, point, headshot) {
  if (!e || !e.alive) return;
  e.hp -= dmg;
  e.flash = 1;
  AudioSys.hit();
  const oil = e.type === 'drone' ? 0xff5d5d : 0x8a1408;
  particles.burst(point, headshot ? 14 : 9, headshot ? 0xffe27a : oil, 5, 0.5, 2);
  particles.burst(point, 5, 0xffc36b, 6, 0.3, 2);
  addScorch(e, point);
  showHitmarker(false);
  const kb = player.pos.clone().sub(e.group.position).setY(0).normalize().multiplyScalar(-(e.type === 'drone' ? 2.2 : 0.9));
  e.vel.add(kb);
  if (e.hp <= 0) killEnemy(e, headshot);
  else {
    e.group.position.add(kb.clone().multiplyScalar(0.03));
    const ud = e.group.userData;
    if (e.type === 'brute' && e.hp < e.maxHp * 0.5 && ud.padLOn) {
      ud.padLOn = false;
      ud.torsoG.remove(ud.padL);
      const wp = new THREE.Vector3();
      ud.padL.getWorldPosition(wp);
      spawnDebris(wp, ud.padL.material, 0.3, new THREE.Vector3((Math.random() - 0.5) * 6, 4, (Math.random() - 0.5) * 6));
      particles.burst(wp, 10, 0xff7a2d, 4, 0.5, 2);
    }
  }
}

export function killEnemy(e, headshot) {
  e.alive = false;
  state.kills++;
  const pts = (e.type === 'drone' ? 100 : 150) + (headshot ? 50 : 0) + state.wave * 10;
  state.score += pts;
  AudioSys.kill();
  AudioSys.explosion();
  const p = e.group.position.clone(); p.y += e.type === 'drone' ? 0.5 : 1.2;
  const mats = [];
  e.group.traverse((o) => {
    if (o.isMesh && o.material && !mats.includes(o.material)) mats.push(o.material);
  });
  const fallback = mats[0];
  for (let i = 0; i < 6; i++) {
    spawnDebris(p, mats.length ? mats[i % mats.length] : fallback,
      0.12 + Math.random() * 0.22,
      new THREE.Vector3((Math.random() - 0.5) * 9, 3 + Math.random() * 5, (Math.random() - 0.5) * 9));
  }
  particles.burst(p, 26, 0xff8c3b, 8, 0.8, 3);
  particles.burst(p, 14, e.type === 'drone' ? 0x38f0ff : 0x8a1408, 6, 0.7, 2);
  shockwave(e.group.position.clone(), e.type === 'drone' ? 0x38f0ff : 0xff2d78, 3);
  showHitmarker(true);
  showKillConfirm(headshot ? 'HEADSHOT +' + ((e.type === 'drone' ? 100 : 150) + 50) : '+' + pts);
  $('kills').textContent = state.kills;
  $('score').textContent = state.score;
  scene.remove(e.group);
  enemies.splice(enemies.indexOf(e), 1);
}

export function updateEnemies(dt) {
  const pp = new THREE.Vector3(player.pos.x, player.pos.y + player.eyeH, player.pos.z);
  for (let i = enemies.length - 1; i >= 0; i--) {
    const e = enemies[i];
    if (!e.alive) { // sicurezza: i morti vengono rimossi subito in killEnemy
      scene.remove(e.group);
      enemies.splice(i, 1);
      continue;
    }
    e.flash = Math.max(0, e.flash - dt * 5);
    e.group.traverse((o) => {
      if (o.isMesh && o.material.emissive && o.userData.head !== true) {
        o.material.emissiveIntensity = (o.material.userData?.base ?? 1) + e.flash * 3;
      }
    });
    const gp = e.group.position;
    const toP = new THREE.Vector3().subVectors(pp, gp); toP.y = 0;
    const dist = toP.length();
    if (dist > 0.01) toP.normalize();
    const sep = new THREE.Vector3();
    enemies.forEach((o) => {
      if (o === e || !o.alive) return;
      const d = new THREE.Vector3().subVectors(gp, o.group.position); d.y = 0;
      const l = d.length();
      if (l < 2 && l > 0.01) sep.addScaledVector(d.normalize(), (2 - l) * 1.5);
    });
    e.atkCd -= dt;
    if (e.type === 'drone') updateDrone(e, dt, pp, gp, toP, dist, sep);
    else updateBrute(e, dt, pp, gp, toP, dist, sep);
    // collisione mondo (XZ push-out)
    for (const c of colliders) {
      const p = e.group.position;
      const r = 0.5, h = e.type === 'drone' ? 1 : 2.1;
      if (p.x + r > c.min.x && p.x - r < c.max.x && p.z + r > c.min.z && p.z - r < c.max.z && p.y < c.max.y && p.y + h > c.min.y) {
        const dx1 = c.max.x - (p.x - r), dx2 = (p.x + r) - c.min.x;
        const dz1 = c.max.z - (p.z - r), dz2 = (p.z + r) - c.min.z;
        const m = Math.min(dx1, dx2, dz1, dz2);
        if (m === dx1) p.x = c.max.x + r; else if (m === dx2) p.x = c.min.x - r;
        else if (m === dz1) p.z = c.max.z + r; else p.z = c.min.z - r;
      }
    }
    e.group.position.x = THREE.MathUtils.clamp(e.group.position.x, -ARENA, ARENA);
    e.group.position.z = THREE.MathUtils.clamp(e.group.position.z, -ARENA, ARENA);
  }
  const alive = enemies.filter((e) => e.alive).length;
  $('enemies-left').textContent = alive;
  if (state.waveActive && alive === 0) {
    state.waveActive = false;
    state.waveBreak = 5;
    player.shield = 50;
    weapon.reserve = Math.min(240, weapon.reserve + 60);
    updateAmmoUI();
    toast(`Ondata ${state.wave} completata! +60 munizioni, scudo ripristinato`);
  }
  if (!state.waveActive) {
    state.waveBreak -= dt;
    if (state.waveBreak <= 0) startWave(state.wave + 1);
  }
}

function updateDrone(e, dt, pp, gp, toP, dist, sep) {
  const ud = e.group.userData;
  const damaged = e.hp < e.maxHp * 0.5;
  const targetY = 2.4 + Math.sin(state.time * 2 + e.bobR) * 0.5;
  const desired = toP.clone().multiplyScalar(e.speed * (damaged ? 0.8 : 1)).add(sep);
  e.vel.lerp(desired, Math.min(1, 3 * dt));
  gp.addScaledVector(e.vel, dt);
  gp.y += (targetY - gp.y) * Math.min(1, 3 * dt);
  const prevYaw = e.group.rotation.y;
  e.group.lookAt(pp.x, gp.y, pp.z);
  let dyaw = e.group.rotation.y - prevYaw;
  if (dyaw > Math.PI) dyaw -= Math.PI * 2;
  if (dyaw < -Math.PI) dyaw += Math.PI * 2;
  ud.rotors.forEach((r, i) => { r.rotation.z += dt * (28 + i * 4) * (i % 2 ? 1 : -1); });
  const targetBank = THREE.MathUtils.clamp(-dyaw * 10, -0.45, 0.45);
  ud.bank += (targetBank - ud.bank) * Math.min(1, 5 * dt);
  e.group.rotation.z = ud.bank + (damaged ? Math.sin(state.time * 13 + e.bobR) * 0.07 : 0);
  e.group.rotation.x = THREE.MathUtils.clamp(e.vel.length() * 0.03, 0, 0.25);
  if (ud.eyeGimbal) ud.eyeGimbal.lookAt(pp);
  ud.navMat.emissiveIntensity = (Math.sin(state.time * 6 + e.bobR) > 0) ? 4 : 0.2;
  if (damaged) {
    ud.smokeT -= dt;
    if (ud.smokeT <= 0) {
      ud.smokeT = 0.09;
      particles.burst(gp.clone().add(new THREE.Vector3(0, 0.3, 0)), 2, 0x555560, 1, 0.8, 1.5, 1.5);
    }
  }
  if (dist < 2.6 && e.atkCd <= 0 && player.alive) {
    e.atkCd = 1.1;
    hurtPlayer(8 + Math.random() * 6);
    particles.burst(pp, 8, 0xff2d78, 4, 0.4, 1);
  }
}

function updateBrute(e, dt, pp, gp, toP, dist, sep) {
  const ud = e.group.userData;
  const enraged = e.hp < e.maxHp * 0.5;
  const spd = e.speed * (enraged ? 1.25 : 1);
  const desired = toP.clone().multiplyScalar(dist > 1.7 ? spd : 0).add(sep);
  e.vel.lerp(desired, Math.min(1, 4 * dt));
  gp.addScaledVector(e.vel, dt);
  // quota terreno: soppalco, scalone o lobby (resta sotto il soppalco se ci passa sotto)
  const gy = groundHeightAt(gp.x, gp.z);
  if (gy < gp.y - 0.6) gp.y = Math.max(gy, gp.y - 9 * dt); // cade dal bordo
  else if (Math.abs(gp.y - gy) < 1.5) gp.y += (gy - gp.y) * Math.min(1, 10 * dt);
  e.group.lookAt(pp.x, gp.y, pp.z);
  ud.atkAnim = Math.max(0, (ud.atkAnim || 0) - dt * 3);
  const w = state.time * (enraged ? 9 : 7) + e.bobR;
  const swing = Math.min(1, e.vel.length() / 2);
  ud.legL.thighG.rotation.x = Math.sin(w) * 0.55 * swing;
  ud.legR.thighG.rotation.x = -Math.sin(w) * 0.55 * swing;
  ud.legL.shinG.rotation.x = Math.max(0, -Math.sin(w)) * 0.5 * swing;
  ud.legR.shinG.rotation.x = Math.max(0, Math.sin(w)) * 0.5 * swing;
  const atk = ud.atkAnim;
  ud.armL.upG.rotation.x = -Math.sin(w) * 0.5 * swing - atk * 1.4;
  ud.armR.upG.rotation.x = Math.sin(w) * 0.5 * swing - atk * 1.4;
  ud.armL.foreG.rotation.x = -0.3 - atk * 0.6;
  ud.armR.foreG.rotation.x = -0.3 - atk * 0.6;
  ud.torsoG.rotation.y = Math.sin(w) * 0.06 * swing;
  ud.torsoG.position.y = 1.28 + Math.abs(Math.sin(w)) * 0.04 * swing;
  if (ud.headG) ud.headG.lookAt(pp.x, pp.y, pp.z);
  if (enraged) ud.visorMat.emissiveIntensity = 3 + Math.sin(state.time * 14) * 2;
  if (dist < 2.2 && e.atkCd <= 0 && player.alive) {
    e.atkCd = 1.3;
    ud.atkAnim = 1;
    hurtPlayer((enraged ? 18 : 14) + Math.random() * 8);
    e.group.position.addScaledVector(toP, 0.3);
  }
}

export function startWave(n) {
  if (state.waveActive) return;
  state.wave = n;
  state.waveActive = true;
  $('wave').textContent = n;
  const count = 3 + n * 2;
  for (let k = 0; k < count; k++) {
    const type = Math.random() < Math.min(0.65, 0.35 + n * 0.06) ? 'drone' : 'brute';
    // brute solo a terra, droni ovunque; mai addosso al giocatore
    const pts = type === 'brute' ? SPAWN_POINTS.filter((p) => p.y < 1) : SPAWN_POINTS;
    for (let j = 0; j < pts.length; j++) {
      const sp = pts[(k + j) % pts.length];
      if (Math.hypot(sp.x - player.pos.x, sp.z - player.pos.z) < 10) continue;
      const px = sp.x + (Math.random() - 0.5) * 3, pz = sp.z + (Math.random() - 0.5) * 3;
      spawnEnemy(type, new THREE.Vector3(px, type === 'drone' ? Math.max(3, sp.y + 1) : sp.y, pz));
      break;
    }
  }
  banner(`ONDATA ${n}`, `${count} ostili in arrivo`);
  AudioSys.wave();
}
