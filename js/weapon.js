// weapon.js — AR-9 MALLEUS fotorealistica stile BO2: metallo usurato PBR,
// marcature stampate, otturatore che cicla, contacolpi, mani con guanti fingerless.
import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { camera, weapon, player } from './core.js';
import { canvasTex, canvasToNormal } from './textures.js';

export function muzzleWorld() {
  const v = new THREE.Vector3();
  weapon.muzzle.getWorldPosition(v);
  return v;
}

// ---------- texture dedicate ----------
function wornMetalMaps() {
  const s = 512;
  const c = document.createElement('canvas'); c.width = c.height = s;
  const g = c.getContext('2d');
  g.fillStyle = '#262a33'; g.fillRect(0, 0, s, s);
  for (let i = 0; i < 6000; i++) { // grana
    const v = 28 + Math.random() * 26 | 0;
    g.fillStyle = `rgba(${v},${v + 2},${v + 5},0.5)`;
    g.fillRect(Math.random() * s, Math.random() * s, 2, 2);
  }
  const mc = document.createElement('canvas'); mc.width = mc.height = s; // metalness
  const mg = mc.getContext('2d');
  mg.fillStyle = '#8c8c8c'; mg.fillRect(0, 0, s, s);
  const rc = document.createElement('canvas'); rc.width = rc.height = s; // roughness
  const rg = rc.getContext('2d');
  rg.fillStyle = '#757575'; rg.fillRect(0, 0, s, s);
  for (let i = 0; i < 26; i++) { // chiazze d'olio (lucide, poco metalliche)
    const x = Math.random() * s, y = Math.random() * s, r = 20 + Math.random() * 70;
    let gr = g.createRadialGradient(x, y, 2, x, y, r);
    gr.addColorStop(0, 'rgba(8,9,12,0.5)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = gr; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill();
    gr = rg.createRadialGradient(x, y, 2, x, y, r);
    gr.addColorStop(0, 'rgba(40,40,40,0.8)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
    rg.fillStyle = gr; rg.beginPath(); rg.arc(x, y, r, 0, 7); rg.fill();
    gr = mg.createRadialGradient(x, y, 2, x, y, r);
    gr.addColorStop(0, 'rgba(70,70,70,0.8)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
    mg.fillStyle = gr; mg.beginPath(); mg.arc(x, y, r, 0, 7); mg.fill();
  }
  for (let i = 0; i < 130; i++) { // graffi: metallo nudo
    const x = Math.random() * s, y = Math.random() * s;
    const len = 6 + Math.random() * 46, a = Math.random() * Math.PI;
    const bright = Math.random() < 0.6;
    g.strokeStyle = bright ? `rgba(150,156,165,${0.35 + Math.random() * 0.4})` : 'rgba(10,10,12,0.6)';
    g.lineWidth = 0.8 + Math.random();
    g.beginPath(); g.moveTo(x, y); g.lineTo(x + Math.cos(a) * len, y + Math.sin(a) * len); g.stroke();
    if (bright) {
      mg.strokeStyle = 'rgba(240,240,240,0.9)'; mg.lineWidth = 1.2;
      mg.beginPath(); mg.moveTo(x, y); mg.lineTo(x + Math.cos(a) * len, y + Math.sin(a) * len); mg.stroke();
      rg.strokeStyle = 'rgba(170,170,170,0.8)'; rg.lineWidth = 1.2;
      rg.beginPath(); rg.moveTo(x, y); rg.lineTo(x + Math.cos(a) * len, y + Math.sin(a) * len); rg.stroke();
    }
  }
  for (let i = 0; i < 90; i++) { // scheggiature concentrate
    const x = Math.random() * s, y = Math.random() * s;
    g.fillStyle = `rgba(140,146,155,${0.3 + Math.random() * 0.4})`;
    g.fillRect(x, y, 1 + Math.random() * 2.5, 1 + Math.random() * 1.5);
  }
  const map = new THREE.CanvasTexture(c); map.colorSpace = THREE.SRGBColorSpace;
  const metal = new THREE.CanvasTexture(mc), rough = new THREE.CanvasTexture(rc);
  for (const t of [map, metal, rough]) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 8; }
  const normal = canvasToNormal(c, 1.1);
  return { map, metal, rough, normal };
}

function polymerMaps() {
  const s = 256;
  const c = document.createElement('canvas'); c.width = c.height = s;
  const g = c.getContext('2d');
  g.fillStyle = '#17191e'; g.fillRect(0, 0, s, s);
  for (let y = 6; y < s; y += 12) { // stippling antiscivolo
    for (let x = (y % 24 === 6 ? 6 : 12); x < s; x += 12) {
      g.fillStyle = 'rgba(60,64,72,0.8)';
      g.beginPath(); g.arc(x, y, 2.6, 0, 7); g.fill();
      g.fillStyle = 'rgba(0,0,0,0.6)';
      g.beginPath(); g.arc(x, y + 1.5, 2.2, 0, 7); g.fill();
    }
  }
  for (let i = 0; i < 1500; i++) {
    const v = 14 + Math.random() * 18 | 0;
    g.fillStyle = `rgba(${v},${v + 2},${v + 4},0.5)`;
    g.fillRect(Math.random() * s, Math.random() * s, 1.5, 1.5);
  }
  const map = new THREE.CanvasTexture(c); map.colorSpace = THREE.SRGBColorSpace;
  map.wrapS = map.wrapT = THREE.RepeatWrapping;
  const normal = canvasToNormal(c, 2.0);
  return { map, normal };
}

function rubberMaps() {
  const s = 128;
  const c = document.createElement('canvas'); c.width = c.height = s;
  const g = c.getContext('2d');
  g.fillStyle = '#101114'; g.fillRect(0, 0, s, s);
  for (let y = 0; y < s; y += 10) {
    g.fillStyle = 'rgba(0,0,0,0.8)'; g.fillRect(0, y, s, 3);
    g.fillStyle = 'rgba(70,72,80,0.5)'; g.fillRect(0, y + 3, s, 1.5);
  }
  const map = new THREE.CanvasTexture(c); map.colorSpace = THREE.SRGBColorSpace;
  map.wrapS = map.wrapT = THREE.RepeatWrapping;
  const normal = canvasToNormal(c, 2.6);
  return { map, normal };
}

function marking(text, sub, w = 0.09) {
  const c = document.createElement('canvas'); c.width = 256; c.height = 64;
  const x = c.getContext('2d');
  x.clearRect(0, 0, 256, 64);
  x.textAlign = 'center';
  x.fillStyle = 'rgba(198,203,210,0.88)';
  x.font = '700 27px monospace';
  x.fillText(text, 128, 29);
  if (sub) {
    x.font = '700 19px monospace';
    x.fillStyle = 'rgba(158,163,172,0.7)';
    x.fillText(sub, 128, 53);
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4;
  return new THREE.Mesh(new THREE.PlaneGeometry(w, w / 4),
    new THREE.MeshBasicMaterial({ map: t, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 }));
}

// contacolpi laterale (canvas aggiornato solo quando cambia il numero)
const counterC = document.createElement('canvas'); counterC.width = 96; counterC.height = 48;
const counterX = counterC.getContext('2d');
const counterTex = new THREE.CanvasTexture(counterC); counterTex.colorSpace = THREE.SRGBColorSpace;
let counterLast = -1;
function drawCounter(n) {
  counterLast = n;
  counterX.fillStyle = '#020608'; counterX.fillRect(0, 0, 96, 48);
  counterX.strokeStyle = 'rgba(56,240,255,0.55)'; counterX.lineWidth = 2; counterX.strokeRect(2, 2, 92, 44);
  counterX.fillStyle = '#54f6ff'; counterX.font = '900 32px monospace'; counterX.textAlign = 'center';
  counterX.fillText(String(n).padStart(2, '0'), 48, 36);
  counterTex.needsUpdate = true;
}

const starTex = canvasTex(128, (g, s) => {
  g.clearRect(0, 0, s, s);
  const spike = (ang, len, wid) => {
    g.save(); g.translate(s / 2, s / 2); g.rotate(ang);
    const gr = g.createLinearGradient(0, 0, len, 0);
    gr.addColorStop(0, 'rgba(255,242,205,0.95)'); gr.addColorStop(1, 'rgba(255,150,40,0)');
    g.fillStyle = gr;
    g.fillRect(0, -wid / 2, len, wid);
    g.restore();
  };
  spike(0, 62, 11); spike(Math.PI, 62, 11); spike(Math.PI / 2, 62, 8); spike(-Math.PI / 2, 62, 8);
  spike(Math.PI / 4, 34, 5); spike(-Math.PI / 4, 34, 5); spike(3 * Math.PI / 4, 34, 5); spike(-3 * Math.PI / 4, 34, 5);
  const core = g.createRadialGradient(s / 2, s / 2, 2, s / 2, s / 2, 21);
  core.addColorStop(0, 'rgba(255,255,232,1)'); core.addColorStop(1, 'rgba(255,180,80,0)');
  g.fillStyle = core; g.beginPath(); g.arc(s / 2, s / 2, 21, 0, 7); g.fill();
}, 1);

// ---------- costruzione ----------
export function buildWeapon() {
  const g = weapon.group;
  weapon.parts = {};
  const worn = wornMetalMaps();
  const poly = polymerMaps();
  const rubber = rubberMaps();

  const gunMetal = new THREE.MeshStandardMaterial({
    map: worn.map, metalnessMap: worn.metal, metalness: 1.0,
    roughnessMap: worn.rough, roughness: 1.0,
    normalMap: worn.normal, normalScale: new THREE.Vector2(0.5, 0.5),
    envMapIntensity: 1.15,
  });
  const steel = new THREE.MeshStandardMaterial({ color: 0x14161b, roughness: 0.38, metalness: 0.95, envMapIntensity: 0.9 });
  const boltSteel = new THREE.MeshStandardMaterial({ color: 0x8f959e, roughness: 0.3, metalness: 1.0, envMapIntensity: 1.2 });
  const polymer = new THREE.MeshStandardMaterial({
    map: poly.map, normalMap: poly.normal, normalScale: new THREE.Vector2(0.7, 0.7),
    roughness: 0.9, metalness: 0.05,
  });
  const rubberM = new THREE.MeshStandardMaterial({
    map: rubber.map, normalMap: rubber.normal, normalScale: new THREE.Vector2(0.8, 0.8),
    roughness: 0.95, metalness: 0.0,
  });
  const gloveM = new THREE.MeshStandardMaterial({ color: 0x35322b, roughness: 0.92, metalness: 0.0 });
  const glovePad = new THREE.MeshStandardMaterial({ color: 0x232220, roughness: 0.75, metalness: 0.1 });
  const skin = new THREE.MeshStandardMaterial({ color: 0xb98d63, roughness: 0.75, metalness: 0.0 });
  const sleeve = new THREE.MeshStandardMaterial({ color: 0x3a3a32, roughness: 0.95, metalness: 0.0 });
  const accent = new THREE.MeshStandardMaterial({ color: 0x081318, emissive: 0x38f0ff, emissiveIntensity: 2.5 });
  const glass = new THREE.MeshPhysicalMaterial({ color: 0x9fd4ff, roughness: 0.05, metalness: 0, transparent: true, opacity: 0.28, envMapIntensity: 2.2, clearcoat: 1 });

  const RB = (w, h, d, r = 0.007) => new RoundedBoxGeometry(w, h, d, 2, r);
  function P(geo, mat, x, y, z, rx = 0, ry = 0, rz = 0) {
    const m = new THREE.Mesh(geo, mat);
    m.position.set(x, y, z); m.rotation.set(rx, ry, rz);
    g.add(m); return m;
  }

  // castello smussato + slitta con 9 notches
  P(RB(0.075, 0.11, 0.42, 0.008), gunMetal, 0, 0, 0.02);
  P(new THREE.BoxGeometry(0.05, 0.018, 0.42), steel, 0, 0.064, 0.0);
  for (let i = 0; i < 9; i++) P(new THREE.BoxGeometry(0.054, 0.012, 0.018), steel, 0, 0.066, -0.17 + i * 0.042);
  // spine di smontaggio + catch + pulsante sgancio
  P(new THREE.CylinderGeometry(0.008, 0.008, 0.082, 10), steel, 0, 0.02, 0.16, 0, 0, Math.PI / 2);
  P(new THREE.CylinderGeometry(0.008, 0.008, 0.082, 10), steel, 0, -0.02, -0.12, 0, 0, Math.PI / 2);
  P(new THREE.BoxGeometry(0.012, 0.02, 0.05), steel, -0.041, 0.005, 0.05);
  P(new THREE.BoxGeometry(0.014, 0.014, 0.03), steel, 0.042, -0.045, 0.06);
  // paramano ottagonale + prese d'aria
  P(new THREE.CylinderGeometry(0.046, 0.046, 0.3, 8), gunMetal, 0, 0.005, -0.33, Math.PI / 2);
  for (let i = 0; i < 3; i++) {
    P(new THREE.BoxGeometry(0.004, 0.02, 0.05), steel, 0.045, 0.012, -0.42 + i * 0.09);
    P(new THREE.BoxGeometry(0.004, 0.02, 0.05), steel, -0.045, 0.012, -0.42 + i * 0.09);
  }
  // canna + rondella + freno di bocca con porte
  P(new THREE.CylinderGeometry(0.02, 0.02, 0.14, 12), steel, 0, 0.005, -0.54, Math.PI / 2);
  P(new THREE.CylinderGeometry(0.026, 0.026, 0.012, 12), steel, 0, 0.005, -0.605, Math.PI / 2);
  P(new THREE.CylinderGeometry(0.033, 0.033, 0.09, 6), steel, 0, 0.005, -0.655, Math.PI / 2);
  for (let i = 0; i < 3; i++) P(new THREE.BoxGeometry(0.07, 0.014, 0.02), steel, 0, 0.005, -0.685 + i * 0.03);
  P(new THREE.CylinderGeometry(0.036, 0.036, 0.015, 12), gunMetal, 0, 0.005, -0.705, Math.PI / 2);
  // red-dot: housing + vetro + punto
  P(new THREE.BoxGeometry(0.014, 0.062, 0.07), gunMetal, -0.028, 0.105, -0.06);
  P(new THREE.BoxGeometry(0.014, 0.062, 0.07), gunMetal, 0.028, 0.105, -0.06);
  P(new THREE.BoxGeometry(0.07, 0.014, 0.07), gunMetal, 0, 0.142, -0.06);
  P(new THREE.BoxGeometry(0.06, 0.014, 0.07), gunMetal, 0, 0.072, -0.06);
  P(new THREE.PlaneGeometry(0.05, 0.055), glass, 0, 0.107, -0.058);
  const rdot = new THREE.Mesh(new THREE.SphereGeometry(0.0045, 8, 8), new THREE.MeshBasicMaterial({ color: 0x53f5ff }));
  rdot.position.set(0, 0.107, -0.062); g.add(rdot);
  P(new THREE.BoxGeometry(0.02, 0.012, 0.03), steel, 0.028, 0.155, -0.06); // torretta alzo
  // mire di backup
  P(new THREE.BoxGeometry(0.01, 0.035, 0.01), steel, -0.02, 0.085, 0.14);
  P(new THREE.BoxGeometry(0.01, 0.035, 0.01), steel, 0.02, 0.085, 0.14);
  // calcio smussato + leva + calciolo gommato + guanciale
  P(RB(0.06, 0.095, 0.17, 0.01), polymer, 0, -0.012, 0.315);
  P(new THREE.BoxGeometry(0.02, 0.03, 0.06), steel, 0, -0.055, 0.27);
  P(new THREE.BoxGeometry(0.066, 0.115, 0.035), rubberM, 0, -0.012, 0.41);
  P(RB(0.055, 0.022, 0.11, 0.008), polymer, 0, 0.042, 0.32);
  // grip + grilletto ricurvo + ponticello
  P(RB(0.05, 0.13, 0.062, 0.01), polymer, 0, -0.115, 0.125, 0.32);
  P(new THREE.BoxGeometry(0.01, 0.045, 0.013), steel, 0, -0.078, 0.088, 0.35);
  P(new THREE.BoxGeometry(0.008, 0.008, 0.09), polymer, 0, -0.135, 0.06);
  // caricatore stile PMAG + baseplate + magpull
  const magG = new THREE.Group(); magG.position.set(0, -0.10, -0.01); g.add(magG);
  const magMesh = (geo, x, y, z, rx = 0) => {
    const m = new THREE.Mesh(geo, polymer); m.position.set(x, y, z); m.rotation.x = rx; magG.add(m); return m;
  };
  magMesh(RB(0.052, 0.13, 0.082, 0.006), 0, -0.05, 0, 0.12);
  magMesh(RB(0.047, 0.07, 0.075, 0.006), 0, -0.135, 0.012, 0.28);
  for (let i = 0; i < 4; i++) magMesh(new THREE.BoxGeometry(0.054, 0.009, 0.084), 0, -0.025 - i * 0.028, 0.002 - i * 0.004, 0.12);
  magMesh(new THREE.BoxGeometry(0.056, 0.02, 0.086), rubberM, 0, -0.172, 0.02, 0.28);
  magMesh(new THREE.BoxGeometry(0.02, 0.03, 0.012), rubberM, 0, -0.185, 0.045, 0.28); // magpull
  for (let i = 0; i < 3; i++) magMesh(new THREE.CylinderGeometry(0.006, 0.006, 0.054, 8), steel, 0, -0.06 - i * 0.03, -0.036, 0, 0, Math.PI / 2);
  weapon.parts.mag = magG;
  // finestra espulsione + otturatore che cicla + forward assist
  P(new THREE.BoxGeometry(0.004, 0.045, 0.11), steel, 0.0375, 0.012, 0.03);
  const bolt = P(new THREE.BoxGeometry(0.018, 0.028, 0.085), boltSteel, 0.034, 0.012, 0.03);
  bolt.userData.baseZ = 0.03;
  weapon.parts.bolt = bolt;
  P(new THREE.CylinderGeometry(0.011, 0.014, 0.03, 8), steel, 0.038, -0.01, 0.15, 0.5, 0, 0.4);
  // selettore entrambi i lati
  P(new THREE.CylinderGeometry(0.012, 0.012, 0.02, 10), steel, 0.042, -0.03, 0.10, 0, 0, Math.PI / 2);
  P(new THREE.CylinderGeometry(0.012, 0.012, 0.02, 10), steel, -0.042, -0.03, 0.10, 0, 0, Math.PI / 2);
  // leva di armamento
  const charge = P(new THREE.BoxGeometry(0.055, 0.016, 0.03), steel, 0, 0.058, 0.20);
  weapon.parts.charge = charge;
  // striscia luminosa + contacolpi
  P(new THREE.BoxGeometry(0.078, 0.012, 0.2), accent, 0, -0.032, -0.12);
  drawCounter(weapon.mag);
  const counter = new THREE.Mesh(new THREE.PlaneGeometry(0.06, 0.03),
    new THREE.MeshBasicMaterial({ map: counterTex }));
  counter.position.set(-0.0395, -0.045, -0.10); counter.rotation.y = -Math.PI / 2; g.add(counter);
  // marcature stampate
  const mk1 = marking('MALLEUS-9', '5.56 x 45 NATO', 0.11);
  mk1.position.set(-0.0385, 0.012, 0.0); mk1.rotation.y = -Math.PI / 2; g.add(mk1);
  const mk2 = marking('SAFE - SEMI - AUTO', 'SER. NX-0097', 0.1);
  mk2.position.set(0.0385, -0.052, 0.10); mk2.rotation.y = Math.PI / 2; g.add(mk2);
  const mk3 = marking('9', '', 0.03);
  mk3.position.set(0.0385, 0.045, -0.14); mk3.rotation.y = Math.PI / 2; g.add(mk3);

  // impugnatura verticale + MANO SINISTRA (guanto fingerless + orologio)
  P(RB(0.04, 0.1, 0.046, 0.008), polymer, 0, -0.075, -0.33);
  P(RB(0.052, 0.085, 0.055, 0.014), gloveM, 0.004, -0.078, -0.33);
  for (let i = 0; i < 4; i++) {
    P(new THREE.CylinderGeometry(0.011, 0.0125, 0.04, 8), gloveM, -0.024 + i * 0.015, -0.078, -0.302, 0, 0, Math.PI / 2);
    P(new THREE.CylinderGeometry(0.0095, 0.0095, 0.016, 8), skin, -0.024 + i * 0.015, -0.078, -0.326, 0, 0, Math.PI / 2);
  }
  P(new THREE.BoxGeometry(0.02, 0.05, 0.02), gloveM, 0.032, -0.06, -0.335, 0, 0, 0.5);
  P(new THREE.SphereGeometry(0.011, 8, 8), skin, 0.04, -0.045, -0.34);
  P(new THREE.BoxGeometry(0.056, 0.02, 0.058), glovePad, 0.004, -0.032, -0.33);
  P(new THREE.CylinderGeometry(0.035, 0.042, 0.14, 10), sleeve, 0.03, -0.16, -0.28, 0.5, 0, -0.5);
  // orologio tattico al polso sinistro
  P(new THREE.BoxGeometry(0.045, 0.02, 0.05), glovePad, 0.028, -0.205, -0.245, 0.5, 0, -0.5);
  const watchFace = P(new THREE.CylinderGeometry(0.02, 0.02, 0.012, 16), steel, 0.02, -0.19, -0.235, 0.5, 0, -0.5);
  watchFace.material = new THREE.MeshStandardMaterial({ color: 0x05070a, roughness: 0.15, metalness: 0.4, emissive: 0x1a3a3a, emissiveIntensity: 0.6 });
  // MANO DESTRA: palmo + dita sul grip + indice sul grilletto
  const rh = new THREE.Group(); rh.position.set(0, -0.115, 0.125); rh.rotation.x = 0.32; g.add(rh);
  const rhM = (geo, x, y, z, rx = 0, rz = 0) => { const m = new THREE.Mesh(geo, gloveM); m.position.set(x, y, z); m.rotation.set(rx, 0, rz); rh.add(m); return m; };
  rhM(RB(0.056, 0.075, 0.062, 0.014), 0, 0, 0);
  for (let i = 0; i < 3; i++) {
    rhM(new THREE.CylinderGeometry(0.011, 0.012, 0.05, 8), -0.018 + i * 0.018, -0.012, -0.04, Math.PI / 2 - 0.25);
    const tip = new THREE.Mesh(new THREE.CylinderGeometry(0.0095, 0.0095, 0.014, 8), skin);
    tip.position.set(-0.018 + i * 0.018, -0.024, -0.062); tip.rotation.x = Math.PI / 2 - 0.25; rh.add(tip);
  }
  const idx = new THREE.Mesh(new THREE.CylinderGeometry(0.010, 0.011, 0.055, 8), gloveM);
  idx.position.set(-0.012, -0.03, -0.055); idx.rotation.x = Math.PI / 2 + 0.15; rh.add(idx);
  const idxTip = new THREE.Mesh(new THREE.SphereGeometry(0.010, 8, 8), skin);
  idxTip.position.set(-0.012, -0.038, -0.082); rh.add(idxTip);
  rhM(new THREE.BoxGeometry(0.018, 0.05, 0.018), 0.032, 0.01, -0.01, 0, 0.4);
  const rpad = new THREE.Mesh(RB(0.058, 0.022, 0.06, 0.008), glovePad); rpad.position.set(0, 0.035, 0); rh.add(rpad);
  P(new THREE.CylinderGeometry(0.036, 0.043, 0.15, 10), sleeve, 0.02, -0.21, 0.22, 0.9, 0, -0.35);

  weapon.muzzle = new THREE.Object3D();
  weapon.muzzle.position.set(0, 0.005, -0.715);
  g.add(weapon.muzzle);
  // volata a stella su due piani + luce
  const flashMat = new THREE.MeshBasicMaterial({ map: starTex, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false });
  weapon.flash = new THREE.Mesh(new THREE.PlaneGeometry(0.44, 0.44), flashMat);
  weapon.flash.position.copy(weapon.muzzle.position);
  weapon.flash.visible = false;
  g.add(weapon.flash);
  const flashSide = new THREE.Mesh(new THREE.PlaneGeometry(0.44, 0.44), flashMat);
  flashSide.rotation.y = Math.PI / 2;
  flashSide.position.copy(weapon.muzzle.position);
  flashSide.visible = false;
  weapon.flashSide = flashSide;
  g.add(flashSide);
  weapon.flashLight = new THREE.PointLight(0xffb457, 0, 18, 1.8);
  weapon.flashLight.position.set(0.2, 1.4, -1);
  camera.add(weapon.flashLight);

  // luce dedicata al viewmodel (l'arma legge bene anche al buio)
  const keyL = new THREE.PointLight(0xfff0e0, 2.2, 4, 1.8);
  keyL.position.set(0.35, 0.4, 0.15);
  camera.add(keyL);
  const fillL = new THREE.PointLight(0x88aaff, 1.0, 4, 1.8);
  fillL.position.set(-0.3, 0.0, 0.35);
  camera.add(fillL);

  // laser di puntamento (visibile in ADS)
  const laserGeo = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, 0, -30)]);
  weapon.laser = new THREE.Line(laserGeo, new THREE.LineBasicMaterial({ color: 0xff2d78, transparent: true, opacity: 0.0 }));
  camera.add(weapon.laser);

  g.position.set(0.26, -0.24, -0.42);
  g.rotation.set(0, 0, 0);
  camera.add(g);
}

export function updateWeaponPose(dt, bobX, bobY, hs) {
  const g = weapon.group;
  const ads = weapon.ads;
  const hipPos = new THREE.Vector3(0.26, -0.245, -0.42);
  const adsPos = new THREE.Vector3(0, -0.104, -0.30);
  const p = hipPos.lerp(adsPos, ads);
  const swayAmp = (1 - ads * 0.85);
  const bobA = Math.min(1, hs / 6) * (1 - ads * 0.85);
  const bY = Math.sin(player.bobPhase * 2) * 0.012 * bobA;
  const bX = Math.cos(player.bobPhase) * 0.010 * bobA;
  g.position.set(
    p.x + weapon.sway.x * swayAmp + bX,
    p.y + weapon.sway.y * swayAmp + bY - weapon.recoil * 0.06,
    p.z + weapon.recoil * 0.09
  );
  g.rotation.set(weapon.recoil * 1.4 + bY * 2, weapon.sway.x * 2.2 * swayAmp, weapon.sway.y * 1.2);

  // posa da corsa stile CoD: arma abbassata e inclinata
  const sprintT = (player.sprint && !player.slide && weapon.ads < 0.15) ? 1 : 0;
  weapon.sprintPose += (sprintT - weapon.sprintPose) * Math.min(1, 8 * dt);
  const sp = weapon.sprintPose;
  g.rotation.x += -0.55 * sp;
  g.rotation.z += 0.28 * sp;
  g.position.y += -0.085 * sp;
  g.position.x += 0.035 * sp;

  // otturatore: ciclo avanti-indietro a ogni colpo (~80 ms)
  weapon.boltT = Math.max(0, weapon.boltT - dt * 11);
  if (weapon.parts.bolt) {
    const ph = THREE.MathUtils.clamp(1 - weapon.boltT, 0, 1);
    weapon.parts.bolt.position.z = weapon.parts.bolt.userData.baseZ - 0.05 * Math.sin(ph * Math.PI);
  }

  // contacolpi
  if (counterLast !== weapon.mag) drawCounter(weapon.mag);

  if (weapon.reloading) {
    const t = THREE.MathUtils.clamp(1 - weapon.reloadT / 1.7, 0, 1);
    const w = (a, b) => { const x = THREE.MathUtils.clamp((t - a) / (b - a), 0, 1); return x * x * (3 - 2 * x); };
    const tilt = Math.sin(Math.min(1, t * 1.12) * Math.PI);
    g.rotation.x += -0.5 * tilt;
    g.rotation.z += 0.38 * w(0, 0.25) * (1 - w(0.6, 0.88));
    g.position.y += -0.09 * tilt;
    g.position.x += -0.055 * w(0.05, 0.3) * (1 - w(0.65, 0.92));
    const drop = w(0.08, 0.35), up = w(0.42, 0.68);
    const mg = weapon.parts.mag;
    if (mg) {
      mg.position.y = -0.10 - 0.34 * drop + 0.34 * up;
      mg.rotation.x = 0.12 + 0.55 * drop * (1 - up);
      if (up > 0.85 && up < 1) mg.position.y += Math.sin(up * 40) * 0.004;
    }
    const ch = w(0.75, 0.85) - w(0.88, 0.97);
    if (weapon.parts.charge) weapon.parts.charge.position.z = 0.20 + 0.075 * ch;
  }
  const s = 1 - ads * 0.12;
  g.scale.set(s, s, s);
}
