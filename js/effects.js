// effects.js — particelle, traccianti, decal, bossoli, detriti, bruciature, shockwave.
import * as THREE from 'three';
import { scene, camera } from './core.js';
import { canvasTex } from './textures.js';

export class ParticlePool {
  constructor(n = 1200) {
    this.n = n; this.i = 0;
    this.pos = new Float32Array(n * 3);
    this.col = new Float32Array(n * 3);
    this.vel = new Float32Array(n * 3);
    this.life = new Float32Array(n);
    this.maxLife = new Float32Array(n);
    this.grav = new Float32Array(n);
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3));
    g.setAttribute('color', new THREE.BufferAttribute(this.col, 3));
    this.mat = new THREE.PointsMaterial({ size: 0.09, vertexColors: true, transparent: true, opacity: 0.95, depthWrite: false, blending: THREE.AdditiveBlending, sizeAttenuation: true });
    this.points = new THREE.Points(g, this.mat);
    this.points.frustumCulled = false;
    scene.add(this.points);
    for (let k = 0; k < n; k++) this.pos[k * 3 + 1] = -100;
  }
  spawn(p, v, life, color, grav = 9) {
    const k = this.i; this.i = (this.i + 1) % this.n;
    this.pos[k * 3] = p.x; this.pos[k * 3 + 1] = p.y; this.pos[k * 3 + 2] = p.z;
    this.vel[k * 3] = v.x; this.vel[k * 3 + 1] = v.y; this.vel[k * 3 + 2] = v.z;
    this.life[k] = this.maxLife[k] = life;
    this.grav[k] = grav;
    const c = new THREE.Color(color);
    this.col[k * 3] = c.r; this.col[k * 3 + 1] = c.g; this.col[k * 3 + 2] = c.b;
  }
  burst(p, count, color, speed = 6, life = 0.6, up = 2, grav = 9) {
    for (let j = 0; j < count; j++) {
      const a = Math.random() * Math.PI * 2, e = (Math.random() - 0.3) * Math.PI;
      const s = speed * (0.3 + Math.random() * 0.9);
      this.spawn(p, new THREE.Vector3(Math.cos(a) * Math.cos(e) * s, Math.sin(e) * s + up, Math.sin(a) * Math.cos(e) * s), life * (0.6 + Math.random() * 0.8), color, grav);
    }
  }
  update(dt) {
    for (let k = 0; k < this.n; k++) {
      if (this.life[k] <= 0) continue;
      this.life[k] -= dt;
      if (this.life[k] <= 0) { this.pos[k * 3 + 1] = -100; continue; }
      this.vel[k * 3 + 1] -= this.grav[k] * dt;
      this.pos[k * 3] += this.vel[k * 3] * dt;
      this.pos[k * 3 + 1] += this.vel[k * 3 + 1] * dt;
      this.pos[k * 3 + 2] += this.vel[k * 3 + 2] * dt;
      if (this.pos[k * 3 + 1] < 0.02) { this.pos[k * 3 + 1] = 0.02; this.vel[k * 3 + 1] *= -0.4; this.vel[k * 3] *= 0.7; this.vel[k * 3 + 2] *= 0.7; }
      this.col[k * 3] *= 0.985; this.col[k * 3 + 1] *= 0.985; this.col[k * 3 + 2] *= 0.985;
    }
    this.points.geometry.attributes.position.needsUpdate = true;
    this.points.geometry.attributes.color.needsUpdate = true;
  }
}
export const particles = new ParticlePool(1400);

// polvere ambientale
for (let i = 0; i < 120; i++) {
  particles.spawn(new THREE.Vector3((Math.random() - 0.5) * 80, Math.random() * 8 + 0.5, (Math.random() - 0.5) * 80),
    new THREE.Vector3((Math.random() - 0.5) * 0.3, 0.05, (Math.random() - 0.5) * 0.3), 9999, 0x1a3a4a, -0.02);
}

// ---- traccianti ----
const tracers = [];
export function spawnTracer(a, b, color = 0x9df6ff) {
  const len = a.distanceTo(b);
  if (len < 0.5) return;
  const g = new THREE.BoxGeometry(0.03, 0.03, len);
  const m = new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.9, blending: THREE.AdditiveBlending, depthWrite: false });
  const mesh = new THREE.Mesh(g, m);
  mesh.position.copy(a).lerp(b, 0.5);
  mesh.lookAt(b);
  scene.add(mesh);
  tracers.push({ mesh, life: 0.07 });
}
function updateTracers(dt) {
  for (let i = tracers.length - 1; i >= 0; i--) {
    const t = tracers[i]; t.life -= dt;
    t.mesh.material.opacity = Math.max(0, t.life / 0.07) * 0.9;
    if (t.life <= 0) { scene.remove(t.mesh); t.mesh.geometry.dispose(); t.mesh.material.dispose(); tracers.splice(i, 1); }
  }
}

// ---- fori di proiettile ----
export const decalTex = canvasTex(64, (g, s) => {
  g.clearRect(0, 0, s, s);
  const r = g.createRadialGradient(s / 2, s / 2, 2, s / 2, s / 2, s / 2);
  r.addColorStop(0, 'rgba(0,0,0,1)'); r.addColorStop(0.5, 'rgba(10,10,10,0.9)'); r.addColorStop(1, 'rgba(0,0,0,0)');
  g.fillStyle = r; g.fillRect(0, 0, s, s);
}, 1);
const decals = [];
export function spawnDecal(p, n) {
  const m = new THREE.Mesh(new THREE.PlaneGeometry(0.22, 0.22),
    new THREE.MeshBasicMaterial({ map: decalTex, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 }));
  m.position.copy(p).addScaledVector(n, 0.012);
  m.lookAt(p.clone().add(n));
  m.rotation.z = Math.random() * Math.PI;
  scene.add(m);
  decals.push(m);
  if (decals.length > 90) { const old = decals.shift(); scene.remove(old); old.geometry.dispose(); old.material.dispose(); }
}

// ---- bossoli ----
const shells = [];
const shellGeo = new THREE.BoxGeometry(0.03, 0.03, 0.07);
const shellMat = new THREE.MeshStandardMaterial({ color: 0xd8a833, metalness: 1, roughness: 0.3 });
export function ejectShell(origin, right) {
  const m = new THREE.Mesh(shellGeo, shellMat);
  m.position.copy(origin);
  scene.add(m);
  shells.push({ m, v: new THREE.Vector3().copy(right).multiplyScalar(1.6 + Math.random()).add(new THREE.Vector3(0, 2.2 + Math.random(), 0)), rot: new THREE.Vector3(Math.random() * 12, Math.random() * 12, 0), life: 1.4 });
}
function updateShells(dt) {
  for (let i = shells.length - 1; i >= 0; i--) {
    const s = shells[i]; s.life -= dt;
    s.v.y -= 9.5 * dt;
    s.m.position.addScaledVector(s.v, dt);
    s.m.rotation.x += s.rot.x * dt; s.m.rotation.y += s.rot.y * dt;
    if (s.m.position.y < 0.03) { s.m.position.y = 0.03; s.v.y *= -0.4; s.v.x *= 0.6; s.v.z *= 0.6; }
    if (s.life <= 0) { scene.remove(s.m); shells.splice(i, 1); }
  }
}

// ---- detriti da smembramento (geometria condivisa, mai dispose) ----
const debris = [];
const debrisGeo = new THREE.BoxGeometry(1, 1, 1);
export function spawnDebris(p, mat, size, v) {
  const m = new THREE.Mesh(debrisGeo, mat);
  m.scale.setScalar(size);
  m.position.copy(p);
  m.rotation.set(Math.random() * 3, Math.random() * 3, 0);
  m.castShadow = true;
  scene.add(m);
  debris.push({
    m, v: v.clone(),
    rv: new THREE.Vector3((Math.random() - 0.5) * 12, (Math.random() - 0.5) * 12, (Math.random() - 0.5) * 12),
    life: 2.4, size,
  });
}
function updateDebris(dt) {
  for (let i = debris.length - 1; i >= 0; i--) {
    const d = debris[i]; d.life -= dt;
    d.v.y -= 12 * dt;
    d.m.position.addScaledVector(d.v, dt);
    d.m.rotation.x += d.rv.x * dt; d.m.rotation.y += d.rv.y * dt;
    if (d.m.position.y < d.size / 2) {
      d.m.position.y = d.size / 2;
      d.v.y *= -0.35; d.v.x *= 0.6; d.v.z *= 0.6; d.rv.multiplyScalar(0.7);
    }
    if (d.life < 0.5) d.m.scale.setScalar(Math.max(0.001, d.size * d.life / 0.5));
    if (d.life <= 0) { scene.remove(d.m); debris.splice(i, 1); }
  }
}

// ---- bruciature ancorate ai nemici ----
const scorchGeo = new THREE.PlaneGeometry(0.2, 0.2);
const scorchMat = new THREE.MeshBasicMaterial({ map: decalTex, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2, opacity: 0.92 });
export function addScorch(e, p) {
  const ud = e.group.userData;
  ud.scorches = ud.scorches || [];
  e.group.updateMatrixWorld(true);
  const m = new THREE.Mesh(scorchGeo, scorchMat);
  m.position.copy(e.group.worldToLocal(p.clone()));
  e.group.add(m);
  const n = camera.getWorldDirection(new THREE.Vector3());
  m.lookAt(p.clone().sub(n)); // piano rivolto alla camera
  m.rotation.z = Math.random() * Math.PI;
  m.scale.setScalar(0.8 + Math.random() * 0.9);
  ud.scorches.push(m);
  if (ud.scorches.length > 6) { const o = ud.scorches.shift(); e.group.remove(o); }
}

// ---- shockwave ----
const waves = [];
export function shockwave(p, color = 0x38f0ff, maxR = 4) {
  const m = new THREE.Mesh(new THREE.TorusGeometry(0.3, 0.05, 8, 40),
    new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.9, blending: THREE.AdditiveBlending, depthWrite: false }));
  m.rotation.x = Math.PI / 2; m.position.copy(p); m.position.y = Math.max(0.3, p.y);
  scene.add(m);
  waves.push({ m, t: 0, maxR });
}
function updateWaves(dt) {
  for (let i = waves.length - 1; i >= 0; i--) {
    const w = waves[i]; w.t += dt * 3.2;
    const r = 0.3 + w.t * w.maxR;
    w.m.scale.set(r, r, 1);
    w.m.material.opacity = Math.max(0, 0.9 - w.t);
    if (w.t >= 1) { scene.remove(w.m); w.m.geometry.dispose(); w.m.material.dispose(); waves.splice(i, 1); }
  }
}

export function tickEffects(dt) {
  particles.update(dt);
  updateTracers(dt);
  updateShells(dt);
  updateDebris(dt);
  updateWaves(dt);
}
