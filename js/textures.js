// textures.js — fabbriche di texture: canvas procedurali + foto PBR da CDN con fallback.
import * as THREE from 'three';
import { renderer } from './core.js';
import { toast } from './ui.js';

export function canvasTex(size, draw, repeat = 1) {
  const c = document.createElement('canvas'); c.width = c.height = size;
  draw(c.getContext('2d'), size);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(repeat, repeat);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  return t;
}

// Normal map da qualsiasi canvas (Sobel) — usata per foto e cemento procedurale.
export function canvasToNormal(src, strength = 2) {
  const w = src.width, h = src.height;
  const sd = src.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, w, h).data;
  const nc = document.createElement('canvas'); nc.width = w; nc.height = h;
  const ng = nc.getContext('2d');
  const out = ng.createImageData(w, h);
  const lum = (x, y) => {
    const k = ((((y + h) % h) * w) + ((x + w) % w)) * 4;
    return sd[k] * 0.299 + sd[k + 1] * 0.587 + sd[k + 2] * 0.114;
  };
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const dx = (lum(x - 1, y) - lum(x + 1, y)) / 255 * strength;
    const dy = (lum(x, y - 1) - lum(x, y + 1)) / 255 * strength;
    const inv = 1 / Math.hypot(dx, dy, 1);
    const k = (y * w + x) * 4;
    out.data[k] = (-dx * inv * 0.5 + 0.5) * 255;
    out.data[k + 1] = (dy * inv * 0.5 + 0.5) * 255;
    out.data[k + 2] = (inv * 0.5 + 0.5) * 255;
    out.data[k + 3] = 255;
  }
  ng.putImageData(out, 0, 0);
  const t = new THREE.CanvasTexture(nc);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  return t;
}

function toTex(c, { srgb = true, repeat = 1 } = {}) {
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(repeat, repeat);
  if (srgb) t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  return t;
}

// Cemento industriale: chiazze, giunti con bevel, crepe, olio, pneumatici.
export function buildConcreteMaps() {
  const c = document.createElement('canvas'); c.width = c.height = 1024;
  const g = c.getContext('2d');
  g.fillStyle = '#2b2e34'; g.fillRect(0, 0, 1024, 1024);
  for (let i = 0; i < 60; i++) {
    const x = Math.random() * 1024, y = Math.random() * 1024, r = 40 + Math.random() * 160;
    const v = 30 + Math.random() * 30 | 0;
    const gr = g.createRadialGradient(x, y, 5, x, y, r);
    gr.addColorStop(0, `rgba(${v},${v + 2},${v + 6},0.25)`); gr.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = gr; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill();
  }
  for (let i = 0; i < 14000; i++) {
    const v = 20 + Math.random() * 55 | 0;
    g.fillStyle = `rgba(${v},${v},${v + 4},0.5)`;
    g.fillRect(Math.random() * 1024, Math.random() * 1024, 2, 2);
  }
  g.strokeStyle = 'rgba(0,0,0,0.6)'; g.lineWidth = 4;
  for (let i = 0; i <= 4; i++) {
    g.beginPath(); g.moveTo(i * 256, 0); g.lineTo(i * 256, 1024); g.stroke();
    g.beginPath(); g.moveTo(0, i * 256); g.lineTo(1024, i * 256); g.stroke();
  }
  g.strokeStyle = 'rgba(255,255,255,0.10)'; g.lineWidth = 1.5;
  for (let i = 0; i <= 4; i++) {
    g.beginPath(); g.moveTo(i * 256 + 3, 0); g.lineTo(i * 256 + 3, 1024); g.stroke();
    g.beginPath(); g.moveTo(0, i * 256 + 3); g.lineTo(1024, i * 256 + 3); g.stroke();
  }
  g.strokeStyle = 'rgba(0,0,0,0.55)'; g.lineWidth = 1.5;
  for (let i = 0; i < 9; i++) {
    g.beginPath(); let x = Math.random() * 1024, y = Math.random() * 1024; g.moveTo(x, y);
    for (let k = 0; k < 14; k++) { x += (Math.random() - 0.5) * 70; y += (Math.random() - 0.5) * 70; g.lineTo(x, y); }
    g.stroke();
  }
  for (let i = 0; i < 12; i++) {
    const x = Math.random() * 1024, y = Math.random() * 1024, r = 15 + Math.random() * 50;
    const gr = g.createRadialGradient(x, y, 2, x, y, r);
    gr.addColorStop(0, 'rgba(5,5,8,0.5)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = gr; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill();
  }
  g.strokeStyle = 'rgba(10,10,12,0.28)'; g.lineWidth = 14;
  for (let i = 0; i < 3; i++) {
    const x = Math.random() * 1024, y = Math.random() * 1024;
    g.beginPath(); g.moveTo(x, y);
    g.bezierCurveTo(x + 100, y - 80, x + 300, y + 120, x + 500, y); g.stroke();
  }
  const rc = document.createElement('canvas'); rc.width = rc.height = 256;
  const rg = rc.getContext('2d');
  rg.fillStyle = '#b8b8b8'; rg.fillRect(0, 0, 256, 256);
  for (let i = 0; i < 14; i++) {
    const x = Math.random() * 256, y = Math.random() * 256, r = 10 + Math.random() * 40;
    const gr = rg.createRadialGradient(x, y, 1, x, y, r);
    gr.addColorStop(0, 'rgba(60,60,60,0.5)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
    rg.fillStyle = gr; rg.beginPath(); rg.arc(x, y, r, 0, 7); rg.fill();
  }
  const map = toTex(c, { repeat: 14 });
  const normal = canvasToNormal(c, 2.2); normal.repeat.set(14, 14);
  const rough = toTex(rc, { srgb: false, repeat: 14 });
  return { map, normal, rough };
}

// Casse in metallo militare: scheggiature, ruggine, rivetti, stencil.
export function buildCrateMaps() {
  const c = document.createElement('canvas'); c.width = c.height = 512;
  const g = c.getContext('2d');
  g.fillStyle = '#414a55'; g.fillRect(0, 0, 512, 512);
  for (let i = 0; i < 5000; i++) {
    const v = 45 + Math.random() * 45 | 0;
    g.fillStyle = `rgba(${v},${v + 4},${v + 9},0.4)`;
    g.fillRect(Math.random() * 512, Math.random() * 512, 2, 2);
  }
  for (let i = 0; i < 420; i++) {
    g.fillStyle = `rgba(190,200,210,${0.25 + Math.random() * 0.4})`;
    g.fillRect(Math.random() * 512, Math.random() * 512, 1 + Math.random() * 3, 1 + Math.random() * 2);
  }
  for (let i = 0; i < 260; i++) {
    g.fillStyle = `rgba(12,10,8,${0.2 + Math.random() * 0.35})`;
    g.beginPath(); g.arc(Math.random() * 512, Math.random() * 512, 1 + Math.random() * 4, 0, 7); g.fill();
  }
  g.strokeStyle = 'rgba(0,0,0,0.55)'; g.lineWidth = 5; g.strokeRect(8, 8, 496, 496);
  g.strokeStyle = 'rgba(255,255,255,0.12)'; g.lineWidth = 2; g.strokeRect(16, 16, 480, 480);
  g.fillStyle = 'rgba(10,10,12,0.9)';
  for (let x = 30; x < 512; x += 46) { g.beginPath(); g.arc(x, 26, 5, 0, 7); g.fill(); g.beginPath(); g.arc(x, 486, 5, 0, 7); g.fill(); }
  g.save(); g.translate(256, 270); g.font = '900 64px monospace'; g.textAlign = 'center';
  g.fillStyle = 'rgba(205,220,230,0.5)'; g.fillText('NX-07', 0, 0); g.restore();
  g.save(); g.translate(60, 452); g.rotate(-0.06); g.font = '700 30px monospace';
  g.fillStyle = 'rgba(255,200,60,0.65)'; g.fillText('▲ MUNIZIONI', 0, 0); g.restore();
  const map = toTex(c);
  const normal = canvasToNormal(c, 2.4);
  normal.repeat.set(1, 1);
  return { map, normal };
}

// Muri di fallback (se le foto non caricano): pannelli + strisce neon dipinte.
export function buildWallMaps() {
  const s = 512;
  const map = canvasTex(s, (g) => {
    g.fillStyle = '#2b303c'; g.fillRect(0, 0, s, s);
    g.fillStyle = '#22262f';
    for (let y = 0; y < s; y += 64) g.fillRect(0, y, s, 4);
    for (let i = 0; i < 4000; i++) {
      g.fillStyle = `rgba(0,0,0,${Math.random() * 0.25})`;
      g.fillRect(Math.random() * s, Math.random() * s, 3, 3);
    }
    g.fillStyle = 'rgba(56,240,255,0.85)'; g.fillRect(0, s - 26, s, 8);
    g.fillStyle = 'rgba(255,45,120,0.7)'; g.fillRect(0, 12, s, 4);
  }, 6);
  const c = document.createElement('canvas'); c.width = c.height = 256;
  const g2 = c.getContext('2d');
  g2.drawImage(map.image, 0, 0, 256, 256);
  const normal = canvasToNormal(c, 1.1);
  normal.repeat.set(6, 2);
  return { map, normal };
}

// ---- Foto PBR reali da CDN (mattoni, legno, normali acqua) ----
const TEX_BASE = 'https://cdn.jsdelivr.net/gh/mrdoob/three.js@r160/examples/textures/';
const texLoader = new THREE.TextureLoader();
texLoader.setCrossOrigin('anonymous');
const maxAniso = Math.min(8, renderer.capabilities.getMaxAnisotropy());

function photo(url, srgb, rx, ry) {
  return texLoader.loadAsync(TEX_BASE + url).then((t) => {
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.repeat.set(rx, ry); t.anisotropy = maxAniso;
    if (srgb) t.colorSpace = THREE.SRGBColorSpace;
    return t;
  });
}

function photoNormal(img, strength, rx, ry) {
  const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
  c.getContext('2d').drawImage(img, 0, 0);
  const t = canvasToNormal(c, strength); t.repeat.set(rx, ry);
  return t;
}

// Applica le foto ai materiali del mondo; in caso di errore resta il fallback.
export async function upgradeToPhotos(mats) {
  try {
    const timeout = new Promise((_, rej) => setTimeout(() => rej(new Error('timeout')), 12000));
    const [brickD, brickB, brickR, woodD, woodR, water] = await Promise.race([Promise.all([
      photo('brick_diffuse.jpg', true, 28, 4),
      photo('brick_bump.jpg', false, 28, 4),
      photo('brick_roughness.jpg', false, 28, 4),
      photo('hardwood2_diffuse.jpg', true, 3, 3),
      photo('hardwood2_roughness.jpg', false, 3, 3),
      photo('waternormals.jpg', false, 3, 3),
    ]), timeout]);
    const { matWall, matDeck, puddleMat } = mats;
    if (matWall) {
      matWall.map = brickD; matWall.bumpMap = brickB; matWall.bumpScale = 1.4;
      matWall.roughnessMap = brickR; matWall.roughness = 1; matWall.metalness = 0.06;
      matWall.normalMap = photoNormal(brickD.image, 2.0, 28, 4);
      matWall.normalScale.set(0.8, 0.8);
      matWall.color.set(0xbfc8d4);
      matWall.needsUpdate = true;
    }
    if (matDeck) {
      matDeck.map = woodD; matDeck.roughnessMap = woodR; matDeck.roughness = 1;
      matDeck.bumpMap = woodR; matDeck.bumpScale = 0.5;
      matDeck.needsUpdate = true;
    }
    if (puddleMat) {
      puddleMat.normalMap = water; puddleMat.normalScale.set(0.5, 0.5);
      puddleMat.needsUpdate = true;
    }
    toast('Texture HD attive (foto PBR)');
    return { water };
  } catch (err) { return { water: null }; }
}
