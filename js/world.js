// world.js — hall del GRAND HOTEL abbandonato: reception, scalone, soppalco,
// lampadari, ascensori, vetrate e gabbia di vetro per coccodrilli (angolo NW).
import * as THREE from 'three';
import { scene, gradePass, state, player, colliders, worldMeshes } from './core.js';
import { canvasTex, canvasToNormal, buildConcreteMaps, upgradeToPhotos } from './textures.js';

const HX = 35, HZ = 23; // hall: X ±35, Z ±23

let skyMat = null;
let holo = null;
let waterTex = null;
let poolMat = null;
const lampLights = [];
const crocs = [];

// ---------------- materiali ----------------
function marbleMaps() {
  const s = 512;
  const c = document.createElement('canvas'); c.width = c.height = s;
  const g = c.getContext('2d');
  const tile = s / 4;
  for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) {
    g.fillStyle = (i + j) % 2 ? '#20242c' : '#b9b4a6';
    g.fillRect(i * tile, j * tile, tile, tile);
  }
  for (let i = 0; i < 90; i++) { // venature
    g.strokeStyle = `rgba(${Math.random() < 0.5 ? '200,195,180' : '20,20,24'},${0.12 + Math.random() * 0.2})`;
    g.lineWidth = 0.8 + Math.random() * 1.5;
    g.beginPath();
    let x = Math.random() * s, y = Math.random() * s; g.moveTo(x, y);
    for (let k = 0; k < 6; k++) { x += (Math.random() - 0.5) * 90; y += (Math.random() - 0.5) * 90; g.lineTo(x, y); }
    g.stroke();
  }
  for (let i = 0; i < 2400; i++) {
    const v = 30 + Math.random() * 160 | 0;
    g.fillStyle = `rgba(${v},${v},${v},0.12)`;
    g.fillRect(Math.random() * s, Math.random() * s, 2, 2);
  }
  g.strokeStyle = 'rgba(0,0,0,0.5)'; g.lineWidth = 1;
  for (let i = 0; i < 8; i++) { // crepe
    g.beginPath(); let x = Math.random() * s, y = Math.random() * s; g.moveTo(x, y);
    for (let k = 0; k < 8; k++) { x += (Math.random() - 0.5) * 60; y += (Math.random() - 0.5) * 60; g.lineTo(x, y); }
    g.stroke();
  }
  const map = new THREE.CanvasTexture(c); map.colorSpace = THREE.SRGBColorSpace;
  map.wrapS = map.wrapT = THREE.RepeatWrapping; map.repeat.set(10, 7); map.anisotropy = 8;
  const normal = canvasToNormal(c, 0.8); normal.repeat.set(10, 7);
  return { map, normal };
}

function wallpaperMaps() {
  const s = 512;
  return {
    map: canvasTex(s, (g) => {
      g.fillStyle = '#463b30'; g.fillRect(0, 0, s, s);
      g.strokeStyle = 'rgba(190,160,100,0.35)'; g.lineWidth = 2;
      for (let y = 0; y <= s; y += 64) for (let x = 0; x <= s; x += 64) {
        const ox = ((y / 64) % 2) * 32;
        g.beginPath(); g.moveTo(x + ox, y); g.lineTo(x + ox + 32, y + 32); g.lineTo(x + ox, y + 64); g.lineTo(x + ox - 32, y + 32); g.closePath(); g.stroke();
        g.beginPath(); g.arc(x + ox, y + 32, 5, 0, 7); g.stroke();
      }
      for (let i = 0; i < 2600; i++) {
        g.fillStyle = `rgba(0,0,0,${Math.random() * 0.22})`;
        g.fillRect(Math.random() * s, Math.random() * s, 3, 3);
      }
      const gr = g.createLinearGradient(0, s * 0.7, 0, s); // umidità in basso
      gr.addColorStop(0, 'rgba(0,0,0,0)'); gr.addColorStop(1, 'rgba(10,8,5,0.75)');
      g.fillStyle = gr; g.fillRect(0, 0, s, s);
      for (let i = 0; i < 7; i++) { // intonaco scrostato
        const x = Math.random() * s, y = s * 0.55 + Math.random() * s * 0.45, r = 12 + Math.random() * 30;
        g.fillStyle = 'rgba(120,112,100,0.85)';
        g.beginPath(); g.arc(x, y, r, 0, 7); g.fill();
        g.strokeStyle = 'rgba(0,0,0,0.5)'; g.lineWidth = 2;
        g.beginPath(); g.arc(x, y, r, 0, 7); g.stroke();
      }
    }, 1),
  };
}

function carpetMaps() {
  const s = 512;
  const c = document.createElement('canvas'); c.width = c.height = s;
  const g = c.getContext('2d');
  g.fillStyle = '#5e1220'; g.fillRect(0, 0, s, s);
  g.strokeStyle = 'rgba(200,160,90,0.7)'; g.lineWidth = 6; g.strokeRect(14, 14, s - 28, s - 28);
  g.lineWidth = 2; g.strokeRect(30, 30, s - 60, s - 60);
  g.fillStyle = 'rgba(200,160,90,0.5)';
  for (let x = 50; x < s - 40; x += 40) {
    g.save(); g.translate(x, 50); g.rotate(Math.PI / 4); g.fillRect(-8, -8, 16, 16); g.restore();
    g.save(); g.translate(x, s - 50); g.rotate(Math.PI / 4); g.fillRect(-8, -8, 16, 16); g.restore();
  }
  for (let i = 0; i < 7000; i++) {
    const v = 70 + Math.random() * 60 | 0;
    g.fillStyle = `rgba(${v},${v * 0.25 | 0},${v * 0.3 | 0},0.25)`;
    g.fillRect(Math.random() * s, Math.random() * s, 2, 2);
  }
  const gr = g.createLinearGradient(0, 0, 0, s); // passaggio sbiadito
  gr.addColorStop(0.35, 'rgba(0,0,0,0)'); gr.addColorStop(0.5, 'rgba(210,180,150,0.18)'); gr.addColorStop(0.65, 'rgba(0,0,0,0)');
  g.fillStyle = gr; g.fillRect(0, 0, s, s);
  for (let i = 0; i < 8; i++) { // macchie
    const x = Math.random() * s, y = Math.random() * s, r = 10 + Math.random() * 34;
    const rg = g.createRadialGradient(x, y, 1, x, y, r);
    rg.addColorStop(0, 'rgba(8,5,5,0.55)'); rg.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = rg; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill();
  }
  const map = new THREE.CanvasTexture(c); map.colorSpace = THREE.SRGBColorSpace;
  map.wrapS = map.wrapT = THREE.RepeatWrapping; map.anisotropy = 8;
  return { map };
}

function crocSkinMaps() {
  const s = 256;
  const c = document.createElement('canvas'); c.width = c.height = s;
  const g = c.getContext('2d');
  g.fillStyle = '#4c4a2c'; g.fillRect(0, 0, s, s);
  for (let i = 0; i < 40; i++) {
    const x = Math.random() * s, y = Math.random() * s, r = 8 + Math.random() * 26;
    const gr = g.createRadialGradient(x, y, 1, x, y, r);
    gr.addColorStop(0, 'rgba(30,32,18,0.6)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = gr; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill();
  }
  for (let y = 8; y < s; y += 14) for (let x = (y % 28 < 14 ? 8 : 15); x < s; x += 14) {
    g.fillStyle = 'rgba(140,140,100,0.5)';
    g.beginPath(); g.arc(x, y, 4, 0, 7); g.fill();
    g.fillStyle = 'rgba(20,22,12,0.6)';
    g.beginPath(); g.arc(x, y + 2, 3, 0, 7); g.fill();
  }
  const map = new THREE.CanvasTexture(c); map.colorSpace = THREE.SRGBColorSpace;
  map.wrapS = map.wrapT = THREE.RepeatWrapping;
  const normal = canvasToNormal(c, 2.4);
  return { map, normal };
}

function textPlaque(lines, w = 512, h = 256, bg = '#2a2118', fg = '#d8b96a') {
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  const g = c.getContext('2d');
  g.fillStyle = bg; g.fillRect(0, 0, w, h);
  g.strokeStyle = fg; g.lineWidth = 6; g.strokeRect(10, 10, w - 20, h - 20);
  g.fillStyle = fg; g.textAlign = 'center';
  const n = lines.length;
  lines.forEach((ln, i) => {
    g.font = `${i === 0 ? '900' : '700'} ${i === 0 ? h / (n * 2.2) : h / (n * 2.6)}px Georgia,serif`;
    g.fillText(ln, w / 2, (h / (n + 1)) * (i + 1) + h / (n * 5));
  });
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
  return t;
}

const marble = marbleMaps();
const wallFb = wallpaperMaps();
const carpetM = carpetMaps();
const conc = buildConcreteMaps();

const matFloor = new THREE.MeshStandardMaterial({ map: marble.map, normalMap: marble.normal, normalScale: new THREE.Vector2(0.4, 0.4), roughness: 0.32, metalness: 0.08, envMapIntensity: 0.9 });
const matWall = new THREE.MeshStandardMaterial({ map: wallFb.map, roughness: 0.9, metalness: 0.0, envMapIntensity: 0.25 });
const matCeil = new THREE.MeshStandardMaterial({ color: 0x17181e, roughness: 0.9, metalness: 0.1 });
const matWoodDark = new THREE.MeshStandardMaterial({ color: 0x2e2118, roughness: 0.7, metalness: 0.15 });
const matGold = new THREE.MeshStandardMaterial({ color: 0x8a6a2f, roughness: 0.35, metalness: 1.0, envMapIntensity: 1.1 });
const matMarbleCol = new THREE.MeshStandardMaterial({ map: marble.map, roughness: 0.28, metalness: 0.1, envMapIntensity: 1.0 });
const matCarpet = new THREE.MeshStandardMaterial({ map: carpetM.map, roughness: 0.98, metalness: 0.0 });
const matVelvetR = new THREE.MeshStandardMaterial({ color: 0x5a1f28, roughness: 1.0, metalness: 0.0 });
const matVelvetG = new THREE.MeshStandardMaterial({ color: 0x2a4a44, roughness: 1.0, metalness: 0.0 });
const matSheet = new THREE.MeshStandardMaterial({ color: 0xa8a498, roughness: 1.0, metalness: 0.0 });
const matLeather = new THREE.MeshStandardMaterial({ color: 0x4a3018, roughness: 0.75, metalness: 0.1 });
const matDark = new THREE.MeshStandardMaterial({ color: 0x161b26, roughness: 0.5, metalness: 0.8, envMapIntensity: 0.7 });
const matSteel = new THREE.MeshStandardMaterial({ color: 0x6a7078, roughness: 0.4, metalness: 0.9, envMapIntensity: 0.8 });
const matGlassCage = new THREE.MeshPhysicalMaterial({ color: 0xd8ece2, transparent: true, opacity: 0.10, roughness: 0.06, metalness: 0, envMapIntensity: 2.0, side: THREE.DoubleSide, depthWrite: false });
const matSand = new THREE.MeshStandardMaterial({ color: 0x9a8a5e, roughness: 1.0, metalness: 0.0 });
const matRock = new THREE.MeshStandardMaterial({ color: 0x55565a, roughness: 0.95, metalness: 0.05 });
const matPaper = new THREE.MeshStandardMaterial({ color: 0xb8b4a6, roughness: 1.0, metalness: 0.0, side: THREE.DoubleSide });
const matDeskTop = new THREE.MeshStandardMaterial({ map: marble.map, roughness: 0.25, metalness: 0.12, envMapIntensity: 1.1 });

// ---------------- helpers ----------------
function solid(mesh, collide = true) {
  mesh.updateMatrixWorld(true);
  if (collide) {
    mesh.userData.collider = new THREE.Box3().setFromObject(mesh);
    colliders.push(mesh.userData.collider);
  }
  worldMeshes.push(mesh);
  scene.add(mesh);
  return mesh;
}
function addBox(w, h, d, x, y, z, mat, collide = true, shadow = true) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
  m.position.set(x, y, z);
  m.castShadow = shadow; m.receiveShadow = true;
  return solid(m, collide);
}
function invisibleWall(x1, y1, z1, x2, y2, z2) {
  colliders.push(new THREE.Box3(new THREE.Vector3(x1, y1, z1), new THREE.Vector3(x2, y2, z2)));
}

// ---------------- luci + cielo ----------------
function setupLights() {
  scene.add(new THREE.HemisphereLight(0x4a5a78, 0x0c0a08, 0.5));
  const moon = new THREE.DirectionalLight(0xa8c4e8, 1.1); // "sole" = luna
  moon.name = 'sun';
  moon.position.set(10, 50, -20);
  moon.castShadow = true;
  moon.shadow.mapSize.set(2048, 2048);
  moon.shadow.camera.left = -45; moon.shadow.camera.right = 45;
  moon.shadow.camera.top = 45; moon.shadow.camera.bottom = -45;
  moon.shadow.camera.far = 120;
  moon.shadow.bias = -0.0004;
  scene.add(moon);

  skyMat = new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false, fog: false,
    uniforms: { sunDir: { value: new THREE.Vector3(10, 50, -20).normalize() }, uTime: { value: 0 }, uNight: { value: 1.0 } },
    vertexShader: `varying vec3 vDir; void main(){ vDir=normalize(position); gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}`,
    fragmentShader: `
      varying vec3 vDir; uniform vec3 sunDir; uniform float uTime; uniform float uNight;
      float hash21(vec2 p){ p = fract(p*vec2(123.34,456.21)); p += dot(p,p+45.32); return fract(p.x*p.y); }
      float vnoise(vec2 p){ vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
        float a=hash21(i), b=hash21(i+vec2(1.0,0.0)), c=hash21(i+vec2(0.0,1.0)), d=hash21(i+vec2(1.0,1.0));
        return mix(mix(a,b,f.x), mix(c,d,f.x), f.y); }
      void main(){
        vec3 V = normalize(vDir);
        vec3 col = mix(vec3(0.10,0.06,0.10), vec3(0.008,0.012,0.03), smoothstep(-0.1,0.6,V.y));
        float m = max(dot(V, normalize(sunDir)), 0.0);
        col += vec3(0.75,0.85,1.0)*pow(m, 1500.0)*3.0;
        col += vec3(0.35,0.45,0.65)*pow(m, 24.0)*0.22;
        vec2 sp = vec2(atan(V.x, V.z)*40.0, V.y*160.0);
        float st = step(0.9985, hash21(floor(sp)));
        col += vec3(0.8,0.85,1.0)*st*smoothstep(0.05,0.4,V.y)*0.8;
        float cl = vnoise(V.xz/(abs(V.y)+0.25)*2.0 + uTime*0.004);
        col = mix(col, vec3(0.03,0.035,0.06), smoothstep(0.45,0.75,cl)*smoothstep(0.0,0.3,V.y)*0.8);
        gl_FragColor = vec4(col, 1.0);
      }`,
  });
  scene.add(new THREE.Mesh(new THREE.SphereGeometry(320, 32, 20), skyMat));
}

// ---------------- mobili ----------------
function sofa(x, z, ry, mat, sheeted = false) {
  const g = new THREE.Group();
  const add = (geo, m, px, py, pz, rx = 0) => {
    const q = new THREE.Mesh(geo, m); q.position.set(px, py, pz); q.rotation.x = rx;
    q.castShadow = q.receiveShadow = true; g.add(q); return q;
  };
  add(new THREE.BoxGeometry(3, 0.5, 1.05), mat, 0, 0.45, 0);
  add(new THREE.BoxGeometry(3, 0.85, 0.28), mat, 0, 0.9, -0.42);
  add(new THREE.BoxGeometry(0.28, 0.75, 1.05), mat, -1.5, 0.75, 0);
  add(new THREE.BoxGeometry(0.28, 0.75, 1.05), mat, 1.5, 0.75, 0);
  for (let i = -1; i <= 1; i++) add(new THREE.BoxGeometry(0.9, 0.18, 0.9), mat, i * 0.95, 0.78, 0.02, -0.06);
  add(new THREE.BoxGeometry(0.9, 0.5, 0.24), mat, -0.95, 1.15, -0.4, -0.12);
  add(new THREE.BoxGeometry(0.9, 0.5, 0.24), mat, 0.95, 1.15, -0.4, -0.12);
  if (sheeted) {
    const sh = add(new THREE.BoxGeometry(3.25, 0.14, 1.3), matSheet, 0, 1.28, -0.05, -0.04);
    sh.castShadow = true;
  }
  g.position.set(x, 0, z); g.rotation.y = ry;
  g.updateMatrixWorld(true);
  const b = new THREE.Box3().setFromObject(g);
  colliders.push(b);
  scene.add(g);
}

function painting(x, y, z, ry, seed) {
  const c = document.createElement('canvas'); c.width = 128; c.height = 96;
  const g = c.getContext('2d');
  const gr = g.createLinearGradient(0, 0, 0, 96);
  gr.addColorStop(0, '#1a2030'); gr.addColorStop(0.6, '#3a2c1c'); gr.addColorStop(1, '#0a0805');
  g.fillStyle = gr; g.fillRect(0, 0, 128, 96);
  g.fillStyle = 'rgba(220,225,235,0.85)';
  g.beginPath(); g.arc(30 + seed * 34, 26, 9, 0, 7); g.fill();
  g.fillStyle = 'rgba(5,5,8,0.9)';
  g.beginPath(); g.moveTo(0, 96); g.lineTo(0, 70);
  for (let x = 0; x <= 128; x += 16) g.lineTo(x, 66 + ((x * 7 + seed * 13) % 14));
  g.lineTo(128, 96); g.closePath(); g.fill();
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  const grp = new THREE.Group();
  const fr = new THREE.Mesh(new THREE.BoxGeometry(1.7, 1.3, 0.09), matGold);
  grp.add(fr);
  const cv = new THREE.Mesh(new THREE.PlaneGeometry(1.45, 1.05), new THREE.MeshStandardMaterial({ map: t, roughness: 0.9 }));
  cv.position.z = 0.05; grp.add(cv);
  grp.position.set(x, y, z); grp.rotation.y = ry;
  scene.add(grp);
}

function chandelier(x, z, lit, faulty = false) {
  const g = new THREE.Group();
  const stem = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 2.4, 8), matDark);
  stem.position.y = 1.2; g.add(stem);
  const bulbMat = new THREE.MeshStandardMaterial({ color: 0x111111, emissive: 0xffc98a, emissiveIntensity: lit ? 3.2 : 0.12 });
  for (const [r, y, n] of [[1.6, -1.1, 12], [1.0, -0.5, 8]]) {
    const ring = new THREE.Mesh(new THREE.TorusGeometry(r, 0.06, 8, 32), matGold);
    ring.rotation.x = Math.PI / 2; ring.position.y = y; g.add(ring);
    for (let i = 0; i < n; i++) {
      const a = (i / n) * Math.PI * 2;
      const bulb = new THREE.Mesh(new THREE.SphereGeometry(0.07, 8, 8), bulbMat);
      bulb.position.set(Math.cos(a) * r, y + 0.18, Math.sin(a) * r);
      g.add(bulb);
      const cry = new THREE.Mesh(new THREE.OctahedronGeometry(0.055),
        new THREE.MeshStandardMaterial({ color: 0xdfe8f0, roughness: 0.05, metalness: 0.1, envMapIntensity: 2.5 }));
      cry.position.set(Math.cos(a + 0.26) * r, y - 0.22, Math.sin(a + 0.26) * r);
      g.add(cry);
    }
  }
  g.position.set(x, 6.6, z);
  scene.add(g);
  if (lit) {
    const pl = new THREE.PointLight(0xffc98a, 120, 32, 1.8);
    pl.position.set(x, 6.0, z);
    pl.userData.base = 120;
    pl.userData.mode = faulty ? 'faulty' : 'steady';
    scene.add(pl);
    lampLights.push({ pl, bulbMat, mode: faulty ? 'faulty' : 'steady', base: 120 });
  }
  return g;
}

function emergencyLight(x, y, z) {
  const dome = new THREE.Mesh(new THREE.SphereGeometry(0.12, 10, 8, 0, Math.PI * 2, 0, 1.4),
    new THREE.MeshStandardMaterial({ color: 0x220000, emissive: 0xff2222, emissiveIntensity: 3 }));
  dome.position.set(x, y, z); scene.add(dome);
  const pl = new THREE.PointLight(0xff2222, 14, 13, 1.9);
  pl.position.set(x, y - 0.3, z);
  scene.add(pl);
  lampLights.push({ pl, bulbMat: dome.material, mode: 'pulse', base: 14, phase: Math.random() * 9 });
}

// ---------------- coccodrilli ----------------
function makeCroc(scale = 1) {
  const skin = crocSkinMaps();
  const skinMat = new THREE.MeshStandardMaterial({ map: skin.map, normalMap: skin.normal, normalScale: new THREE.Vector2(0.8, 0.8), roughness: 0.7, metalness: 0.05, envMapIntensity: 0.6 });
  const darkMat = new THREE.MeshStandardMaterial({ color: 0x2e2f1c, roughness: 0.85 });
  const g = new THREE.Group();
  const body = new THREE.Mesh(new THREE.BoxGeometry(0.55, 0.32, 1.5), skinMat);
  body.position.y = 0.30; body.castShadow = true; g.add(body);
  for (let i = 0; i < 6; i++) { // scaglie dorsali
    const sc = new THREE.Mesh(new THREE.ConeGeometry(0.05, 0.09, 4), darkMat);
    sc.position.set((i % 2 ? 0.12 : -0.12), 0.48, -0.5 + i * 0.2);
    sc.rotation.y = Math.PI / 4; g.add(sc);
  }
  // coda in 3 segmenti
  const mkSeg = (w, h, l) => {
    const grp = new THREE.Group();
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, l), skinMat);
    m.position.z = -l / 2; m.castShadow = true; grp.add(m);
    return grp;
  };
  const tail1 = mkSeg(0.34, 0.22, 0.5); tail1.position.set(0, 0.28, -0.75); g.add(tail1);
  const tail2 = mkSeg(0.24, 0.16, 0.45); tail2.position.set(0, 0, -0.5); tail1.add(tail2);
  const tail3 = mkSeg(0.14, 0.10, 0.4); tail3.position.set(0, 0, -0.45); tail2.add(tail3);
  // zampe
  [[-0.3, 0.45], [0.3, 0.45], [-0.3, -0.45], [0.3, -0.45]].forEach(([x, z]) => {
    const leg = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.06, 0.26, 8), skinMat);
    leg.position.set(x, 0.13, z); leg.rotation.z = x > 0 ? -0.5 : 0.5; g.add(leg);
    const foot = new THREE.Mesh(new THREE.BoxGeometry(0.13, 0.08, 0.22), skinMat);
    foot.position.set(x * 1.25, 0.04, z + 0.04); g.add(foot);
  });
  // testa (+Z = fronte)
  const head = new THREE.Group(); head.position.set(0, 0.32, 0.85); g.add(head);
  const skull = new THREE.Mesh(new THREE.BoxGeometry(0.4, 0.24, 0.45), skinMat);
  skull.castShadow = true; head.add(skull);
  const snout = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.15, 0.52), skinMat);
  snout.position.set(0, -0.02, 0.44); snout.castShadow = true; head.add(snout);
  for (const sx of [-1, 1]) { // occhi
    const bump = new THREE.Mesh(new THREE.SphereGeometry(0.05, 8, 8), skinMat);
    bump.position.set(sx * 0.12, 0.15, 0.05); head.add(bump);
  }
  const eyeMat = new THREE.MeshStandardMaterial({ color: 0x201a08, emissive: 0xd8a020, emissiveIntensity: 0.9, roughness: 0.2 });
  const eyeL = new THREE.Mesh(new THREE.SphereGeometry(0.028, 8, 8), eyeMat);
  eyeL.position.set(-0.12, 0.18, 0.06); head.add(eyeL);
  const eyeR = eyeL.clone(); eyeR.position.x = 0.12; head.add(eyeR);
  for (const sx of [-1, 1]) { // narici
    const n = new THREE.Mesh(new THREE.SphereGeometry(0.018, 6, 6), darkMat);
    n.position.set(sx * 0.07, 0.07, 0.68); head.add(n);
  }
  const jawG = new THREE.Group(); jawG.position.set(0, -0.08, 0.1); head.add(jawG);
  const jaw = new THREE.Mesh(new THREE.BoxGeometry(0.27, 0.07, 0.6), darkMat);
  jaw.position.set(0, -0.03, 0.3); jawG.add(jaw);
  const toothMat = new THREE.MeshStandardMaterial({ color: 0xd8d0b8, roughness: 0.4 });
  for (let i = 0; i < 5; i++) { // denti superiori
    for (const sx of [-1, 1]) {
      const t = new THREE.Mesh(new THREE.ConeGeometry(0.014, 0.05, 6), toothMat);
      t.position.set(sx * 0.15, -0.11, 0.22 + i * 0.1);
      t.rotation.x = Math.PI; head.add(t);
    }
  }
  g.scale.setScalar(scale);
  scene.add(g);
  const c = { group: g, body, head, jawG, eyeL, eyeR, tail1, tail2, jawT: 5 + Math.random() * 9, jawA: 0, blinkT: 2 + Math.random() * 4, blink: 0, phase: Math.random() * 9 };
  crocs.push(c);
  return c;
}

// ---------------- costruzione ----------------
export const SPAWN_POINTS = [
  { x: -32, y: 0, z: 0 },    // ascensori ovest
  { x: 0, y: 0, z: 20.5 },   // ingresso sud
  { x: 30, y: 0, z: -19 },   // dietro reception
  { x: -30, y: 4.2, z: -17 },// soppalco ovest
  { x: 30, y: 4.2, z: -17 }, // soppalco est
  { x: 0, y: 0, z: -6 },     // ai piedi dello scalone
];

export function groundHeightAt(x, z) {
  if (x > -HX && x < HX && z > -HZ && z < -11) return 4.2; // soppalco
  if (x > -4.2 && x < 4.2 && z > -11.2 && z < -1.8) {       // scalone
    return THREE.MathUtils.clamp((-2 - z) / 9, 0, 1) * 4.2;
  }
  return 0;
}

export function buildWorld() {
  setupLights();
  // pavimento marmo + tappeti
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(HX * 2, HZ * 2), matFloor);
  floor.rotation.x = -Math.PI / 2;
  floor.receiveShadow = true;
  scene.add(floor);
  const carpet1 = new THREE.Mesh(new THREE.PlaneGeometry(16, 30), matCarpet);
  carpet1.rotation.x = -Math.PI / 2; carpet1.position.set(-10, 0.015, 4);
  carpet1.receiveShadow = true; scene.add(carpet1);
  const carpet2 = new THREE.Mesh(new THREE.PlaneGeometry(6, 20), matCarpet);
  carpet2.rotation.x = -Math.PI / 2; carpet2.position.set(24, 0.015, -4);
  carpet2.receiveShadow = true; scene.add(carpet2);

  // muri carta da parati + zoccolo legno
  const H = 10, T = 1.2;
  addBox(HX * 2 + 4, H, T, 0, H / 2, -HZ - 1, matWall);
  addBox(HX * 2 + 4, H, T, 0, H / 2, HZ + 1, matWall);
  addBox(T, H, HZ * 2 + 4, -HX - 1, H / 2, 0, matWall);
  addBox(T, H, HZ * 2 + 4, HX + 1, H / 2, 0, matWall);
  const wains = (w, x, z, ry) => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, 1.3, 0.12), matWoodDark);
    m.position.set(x, 0.65, z); m.rotation.y = ry; m.receiveShadow = true; scene.add(m);
  };
  wains(HX * 2, 0, -HZ + 0.65, 0); wains(HX * 2, 0, HZ - 0.65, 0);
  wains(HZ * 2, -HX + 0.65, 0, Math.PI / 2); wains(HZ * 2, HX - 0.65, 0, Math.PI / 2);

  // soffitto con lucernario (4 lastre, proiettano ombre: lama di luna)
  addBox(70, 0.5, 26, 0, 9.25, 10, matCeil);
  addBox(70, 0.5, 14, 0, 9.25, -16, matCeil);
  addBox(31, 0.5, 6, -19.5, 9.25, -6, matCeil);
  addBox(31, 0.5, 6, 19.5, 9.25, -6, matCeil);
  for (let i = -3; i <= 3; i++) { // travi a cassettoni
    const b = new THREE.Mesh(new THREE.BoxGeometry(0.35, 0.45, 26), matWoodDark);
    b.position.set(i * 9, 8.8, 10); scene.add(b);
  }
  const skyRimM = matWoodDark;
  const skyR1 = new THREE.Mesh(new THREE.BoxGeometry(8.9, 0.35, 0.35), skyRimM);
  skyR1.position.set(0, 9, -9); scene.add(skyR1);
  const skyR2 = skyR1.clone(); skyR2.position.z = -3; scene.add(skyR2);
  const skyR3 = new THREE.Mesh(new THREE.BoxGeometry(0.35, 0.35, 6.6), skyRimM);
  skyR3.position.set(-4, 9, -6); scene.add(skyR3);
  const skyR4 = skyR3.clone(); skyR4.position.x = 4; scene.add(skyR4);
  const skySpot = new THREE.SpotLight(0xbfd4ff, 900, 45, 0.42, 0.6, 1.8);
  skySpot.position.set(3, 8.8, -7); skySpot.target.position.set(-2, 0, -2);
  skySpot.castShadow = true; skySpot.shadow.mapSize.set(1024, 1024); skySpot.shadow.bias = -0.0004;
  scene.add(skySpot, skySpot.target);
  const shaftTex = canvasTex(64, (g, s) => {
    const gr = g.createLinearGradient(0, 0, 0, s);
    gr.addColorStop(0, 'rgba(200,220,255,0.55)'); gr.addColorStop(1, 'rgba(200,220,255,0)');
    g.fillStyle = gr; g.fillRect(0, 0, s, s);
  }, 1);
  const shaft = new THREE.Mesh(new THREE.ConeGeometry(4.6, 9, 20, 1, true),
    new THREE.MeshBasicMaterial({ map: shaftTex, color: 0x9fc0e8, transparent: true, opacity: 0.10, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide, fog: false }));
  shaft.position.set(0.5, 4.5, -4.5); shaft.rotation.z = 0.28; shaft.rotation.x = -0.12;
  scene.add(shaft);

  // colonne marmo
  const colG = new THREE.CylinderGeometry(0.65, 0.75, H, 14);
  for (const [x, z] of [[-12, -8], [12, -8], [-12, 8], [12, 8], [-12, -16], [12, -16]]) {
    const p = new THREE.Mesh(colG, matMarbleCol);
    p.position.set(x, H / 2, z); p.castShadow = p.receiveShadow = true;
    solid(p);
    const cap = new THREE.Mesh(new THREE.BoxGeometry(1.7, 0.3, 1.7), matGold);
    cap.position.set(x, 8.4, z); scene.add(cap);
  }

  // ---- soppalco nord + balaustra ----
  addBox(70, 0.4, 12, 0, 4.0, -17, new THREE.MeshStandardMaterial({ color: 0x4a4238, roughness: 0.8, metalness: 0.1, envMapIntensity: 0.5 }));
  addBox(31, 1.1, 0.3, -19.5, 4.75, -11, matWoodDark); // ringhiere
  addBox(31, 1.1, 0.3, 19.5, 4.75, -11, matWoodDark);
  for (let x = -34; x <= 34; x += 4) { // colonnine
    const b = new THREE.Mesh(new THREE.BoxGeometry(0.12, 1.1, 0.12), matWoodDark);
    b.position.set(x, 4.75, -11); scene.add(b);
  }
  for (const x of [-12, 12]) { // pilastri di sostegno
    const p = new THREE.Mesh(new THREE.CylinderGeometry(0.4, 0.45, 4, 12), matMarbleCol);
    p.position.set(x, 2, -11); p.castShadow = true; solid(p);
  }
  // porte del piano superiore + quadri
  for (const x of [-20, 20]) {
    addBox(2.4, 3.2, 0.3, x, 5.8, -22.8, matWoodDark);
  }

  // ---- scalone centrale ----
  for (let i = 0; i < 12; i++) {
    addBox(8, 0.35 * (i + 1), 0.75, 0, 0.35 * (i + 1) / 2, -2.375 - i * 0.75, matFloor);
  }
  for (const sx of [-4.2, 4.2]) { // balaustra inclinata (visiva)
    const rail = new THREE.Mesh(new THREE.BoxGeometry(0.25, 0.25, 10.4), matWoodDark);
    rail.position.set(sx, 2.9, -6.5); rail.rotation.x = 0.42; rail.castShadow = true; scene.add(rail);
    for (let i = 0; i < 6; i++) {
      const post = new THREE.Mesh(new THREE.BoxGeometry(0.14, 1.0, 0.14), matWoodDark);
      post.position.set(sx, 1.2 + i * 0.62, -2.8 - i * 1.5); scene.add(post);
    }
  }

  // ---- reception est ----
  addBox(8, 1.1, 1.2, 26, 0.55, 2, matWoodDark);
  addBox(8.4, 0.08, 1.5, 26, 1.14, 2, matDeskTop);
  addBox(1.2, 1.1, 4, 21.5, 0.55, 0, matWoodDark);
  const bell = new THREE.Mesh(new THREE.SphereGeometry(0.09, 12, 10), matGold);
  bell.position.set(24, 1.28, 2); scene.add(bell);
  const keyTex = (() => {
    const c = document.createElement('canvas'); c.width = 512; c.height = 192;
    const g = c.getContext('2d');
    g.fillStyle = '#241a10'; g.fillRect(0, 0, 512, 192);
    for (let r = 0; r < 5; r++) for (let q = 0; q < 12; q++) {
      const x = 24 + q * 40, y = 26 + r * 34;
      g.fillStyle = '#0a0a0a'; g.beginPath(); g.arc(x, y, 4, 0, 7); g.fill();
      if (Math.random() < 0.72) {
        g.fillStyle = '#c8a84a'; g.fillRect(x - 5, y + 4, 10, 14);
        g.fillStyle = '#111'; g.font = '9px monospace'; g.textAlign = 'center';
        g.fillText(`${r + 1}${String(q + 1).padStart(2, '0')}`, x, y + 14);
      }
    }
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
    return t;
  })();
  const keyWall = new THREE.Mesh(new THREE.PlaneGeometry(7, 2.6),
    new THREE.MeshStandardMaterial({ map: keyTex, roughness: 0.9 }));
  keyWall.position.set(34.3, 2.4, 2); keyWall.rotation.y = -Math.PI / 2;
  scene.add(keyWall);
  const bankShade = new THREE.Mesh(new THREE.CylinderGeometry(0.16, 0.22, 0.18, 12, 1, true),
    new THREE.MeshStandardMaterial({ color: 0x0a3a1a, emissive: 0x2aff66, emissiveIntensity: 1.2, side: THREE.DoubleSide }));
  bankShade.position.set(27, 1.55, 2); scene.add(bankShade);
  const bankLight = new THREE.PointLight(0x88ff99, 7, 6, 1.9);
  bankLight.position.set(27, 1.4, 2); scene.add(bankLight);
  const hotelSign = new THREE.Mesh(new THREE.PlaneGeometry(7, 1.4),
    new THREE.MeshBasicMaterial({ map: textPlaque(['GRAND HOTEL CONTINENTAL', 'RECEPTION'], 512, 128), transparent: false }));
  hotelSign.position.set(0, 6.8, -22.3);
  scene.add(hotelSign);

  // ---- salotto ovest ----
  sofa(-12, 6, 0, matVelvetR);
  sofa(-12, 12, Math.PI, matVelvetG, true);
  sofa(-18, 9, Math.PI / 2, matVelvetR);
  sofa(-6, 9, -Math.PI / 2, matVelvetG);
  addBox(2.4, 0.12, 1.2, -12, 0.5, 9, matWoodDark);
  for (const [dx, dz] of [[-1, -0.5], [1, -0.5], [-1, 0.5], [1, 0.5]]) {
    addBox(0.1, 0.45, 0.1, -12 + dx, 0.22, 9 + dz, matWoodDark);
  }
  const rug = new THREE.Mesh(new THREE.PlaneGeometry(9, 7), matCarpet);
  rug.rotation.x = -Math.PI / 2; rug.position.set(-12, 0.012, 9); rug.receiveShadow = true; scene.add(rug);
  // pianta morta + lampada rovesciata
  const pot = new THREE.Mesh(new THREE.CylinderGeometry(0.35, 0.28, 0.5, 12), new THREE.MeshStandardMaterial({ color: 0x6a3a22, roughness: 0.9 }));
  pot.position.set(-18, 0.25, 15); pot.castShadow = true; solid(pot);
  for (let i = 0; i < 6; i++) {
    const st = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.03, 1.4, 6), matWoodDark);
    st.position.set(-18 + (Math.random() - 0.5) * 0.3, 1.1, 15 + (Math.random() - 0.5) * 0.3);
    st.rotation.set((Math.random() - 0.5) * 0.7, 0, (Math.random() - 0.5) * 0.7);
    scene.add(st);
  }
  const lampBase = new THREE.Mesh(new THREE.CylinderGeometry(0.25, 0.3, 0.08, 12), matGold);
  lampBase.position.set(-5, 0.04, 13); lampBase.rotation.z = 0.2; scene.add(lampBase);
  const lampPole = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.035, 1.7, 8), matGold);
  lampPole.position.set(-4.4, 0.5, 13); lampPole.rotation.z = 1.25; scene.add(lampPole);

  // ---- carrello portabagagli + valigie ----
  const cartG = new THREE.Group();
  const brass = matGold;
  const cAdd = (geo, m, x, y, z) => { const q = new THREE.Mesh(geo, m); q.position.set(x, y, z); q.castShadow = true; cartG.add(q); return q; };
  cAdd(new THREE.BoxGeometry(1.6, 0.1, 1.0), brass, 0, 0.35, 0);
  for (const [x, z] of [[-0.7, -0.45], [0.7, -0.45], [-0.7, 0.45], [0.7, 0.45]]) {
    cAdd(new THREE.CylinderGeometry(0.09, 0.09, 0.06, 10), matDark, x, 0.09, z, 0, 0, 0).rotation.x = Math.PI / 2;
    cAdd(new THREE.CylinderGeometry(0.03, 0.03, 0.5, 8), brass, x, 0.65, z);
  }
  cAdd(new THREE.BoxGeometry(1.6, 0.06, 0.06), brass, 0, 0.9, -0.45);
  cAdd(new THREE.BoxGeometry(0.7, 0.5, 0.45), matLeather, -0.35, 0.65, 0.05);
  cAdd(new THREE.BoxGeometry(0.6, 0.4, 0.4), new THREE.MeshStandardMaterial({ color: 0x2a2018, roughness: 0.8 }), 0.4, 0.6, 0);
  cartG.position.set(6, 0, 17); cartG.rotation.y = 0.5;
  cartG.updateMatrixWorld(true);
  colliders.push(new THREE.Box3().setFromObject(cartG));
  scene.add(cartG);
  const suit = new THREE.Mesh(new THREE.BoxGeometry(0.75, 0.55, 0.3), matLeather);
  suit.position.set(9.5, 0.28, 15.5); suit.rotation.set(0, 0.7, 0.06); suit.castShadow = true;
  solid(suit);

  // ---- ascensori ovest ----
  for (const z of [-4, 0, 4]) {
    addBox(0.4, 3.4, 3.0, -34.8, 1.7, z, matWoodDark);
    addBox(0.25, 2.9, 2.3, -34.85, 1.45, z, matSteel);
    addBox(0.28, 2.9, 0.06, -34.85, 1.45, z, matDark); // fuga centrale
    const ind = new THREE.Mesh(new THREE.BoxGeometry(0.15, 0.12, 0.3),
      new THREE.MeshStandardMaterial({ color: 0x111111, emissive: z === 0 ? 0x22ff44 : 0xff2222, emissiveIntensity: 2.5 }));
    ind.position.set(-34.7, 3.15, z); scene.add(ind);
  }
  emergencyLight(-33.5, 3.6, 0);
  const dirBoard = new THREE.Mesh(new THREE.PlaneGeometry(2.2, 1.5),
    new THREE.MeshBasicMaterial({ map: textPlaque(['HALL', '◄ RETTILARIO   RECEPTION ►', '◄ ASCENSORI   PIANO I ▲'], 512, 320, '#0a0e14', '#7fd8e8'), transparent: false }));
  dirBoard.position.set(-34.3, 1.9, -8); dirBoard.rotation.y = Math.PI / 2;
  scene.add(dirBoard);

  // ---- vetrate est + tendaggi + luna ----
  const winGlowMat = new THREE.MeshStandardMaterial({ color: 0x0a1220, emissive: 0x9fc0e0, emissiveIntensity: 1.1 });
  const winDarkMat = new THREE.MeshStandardMaterial({ color: 0x05070a, roughness: 0.9 });
  for (let i = 0; i < 5; i++) {
    const z = -8 + i * 4;
    const broken = i === 2;
    addBox(0.5, 5.4, 3.2, 34.7, 3.5, z, matWoodDark);
    const pane = new THREE.Mesh(new THREE.PlaneGeometry(2.6, 4.7), broken ? winDarkMat : winGlowMat);
    pane.position.set(34.42, 3.5, z); pane.rotation.y = -Math.PI / 2; scene.add(pane);
    for (const dy of [-1.2, 0, 1.2]) {
      const mull = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.12, 2.6), matWoodDark);
      mull.position.set(34.4, 3.5 + dy, z); scene.add(mull);
    }
    const mullV = new THREE.Mesh(new THREE.BoxGeometry(0.1, 4.7, 0.12), matWoodDark);
    mullV.position.set(34.4, 3.5, z); scene.add(mullV);
    for (const sx of [-1.9, 1.9]) { // tendaggi drappeggiati
      const pg = new THREE.PlaneGeometry(1.1, 5.4, 8, 1);
      const pp = pg.attributes.position;
      for (let vi = 0; vi < pp.count; vi++) pp.setZ(vi, Math.sin(pp.getX(vi) * 9) * 0.12);
      pg.computeVertexNormals();
      const cur = new THREE.Mesh(pg, new THREE.MeshStandardMaterial({ color: 0x3d2b3a, roughness: 1 }));
      cur.position.set(34.3, 3.4, z + sx); cur.rotation.y = -Math.PI / 2; cur.castShadow = true;
      scene.add(cur);
    }
    if (broken) { // vetri rotti a terra
      for (let k = 0; k < 5; k++) {
        const sh = new THREE.Mesh(new THREE.PlaneGeometry(0.3 + Math.random() * 0.3, 0.3),
          new THREE.MeshStandardMaterial({ color: 0x9fc4d8, roughness: 0.1, metalness: 0.4, envMapIntensity: 1.6, transparent: true, opacity: 0.7, side: THREE.DoubleSide }));
        sh.rotation.x = -Math.PI / 2; sh.rotation.z = Math.random() * 3;
        sh.position.set(33.5 - Math.random() * 2, 0.02, z + (Math.random() - 0.5) * 3);
        scene.add(sh);
      }
    }
  }
  for (const wz of [-6, 2]) { // lame di luna
    const ws = new THREE.SpotLight(0x8fb4e8, 420, 34, 0.5, 0.7, 1.8);
    ws.position.set(33, 4.5, wz); ws.target.position.set(18, 0, wz + 3);
    if (wz === -6) { ws.castShadow = true; ws.shadow.mapSize.set(1024, 1024); ws.shadow.bias = -0.0004; }
    scene.add(ws, ws.target);
  }

  // ---- quadri ----
  painting(-20, 5.5, -22.3, 0, 1);
  painting(20, 5.5, -22.3, 0, 2);
  painting(-34.3, 5.5, -12, Math.PI / 2, 3);
  painting(14, 5.5, 22.3, Math.PI, 4);

  // ---- ingresso sud ----
  addBox(1.0, 4.2, 0.5, -2.2, 2.1, 22.8, matWoodDark);
  addBox(1.0, 4.2, 0.5, 2.2, 2.1, 22.8, matWoodDark);
  addBox(5.4, 0.8, 0.5, 0, 4.4, 22.8, matWoodDark);
  const doorL = new THREE.Mesh(new THREE.BoxGeometry(1.9, 4.0, 0.18), matWoodDark);
  doorL.position.set(-1.05, 2.0, 22.9); doorL.rotation.y = 0.5; doorL.castShadow = true; scene.add(doorL);
  const doorR = new THREE.Mesh(new THREE.BoxGeometry(1.9, 4.0, 0.18), matWoodDark);
  doorR.position.set(1.0, 2.0, 22.9); doorR.castShadow = true;
  solid(doorR);
  const exitSign = new THREE.Mesh(new THREE.PlaneGeometry(1.3, 0.42),
    new THREE.MeshStandardMaterial({ color: 0x220000, emissive: 0xff2222, emissiveIntensity: 3 }));
  exitSign.position.set(0, 4.0, 22.5); exitSign.rotation.y = Math.PI; scene.add(exitSign);
  emergencyLight(0, 3.4, 21.5);
  const doormat = new THREE.Mesh(new THREE.PlaneGeometry(4.5, 2),
    new THREE.MeshStandardMaterial({ map: textPlaque(['GRAND HOTEL'], 512, 128, '#3a0e14', '#a8842e'), roughness: 1 }));
  doormat.rotation.x = -Math.PI / 2; doormat.position.set(0, 0.018, 20); scene.add(doormat);

  // ---- lampadari ----
  chandelier(0, 4, true, true);   // centrale guasto (sfarfalla)
  chandelier(-22, 4, true);
  chandelier(22, 4, false);
  holo = null;

  // ---- carte, calcinacci, ragnatele ----
  for (let i = 0; i < 18; i++) {
    const p = new THREE.Mesh(new THREE.PlaneGeometry(0.3, 0.4), matPaper);
    p.rotation.x = -Math.PI / 2; p.rotation.z = Math.random() * 3;
    p.position.set((Math.random() - 0.5) * 60, 0.02, (Math.random() - 0.5) * 38);
    scene.add(p);
  }
  for (let i = 0; i < 8; i++) {
    const r = 0.15 + Math.random() * 0.3;
    const ch = new THREE.Mesh(new THREE.BoxGeometry(r, r * 0.7, r), matSheet);
    ch.position.set((Math.random() - 0.5) * 56, r * 0.3, (Math.random() - 0.5) * 34);
    ch.rotation.y = Math.random() * 3; ch.castShadow = true;
    solid(ch);
  }
  const webTex = canvasTex(128, (g, s) => {
    g.clearRect(0, 0, s, s);
    g.strokeStyle = 'rgba(220,220,220,0.4)'; g.lineWidth = 1;
    for (let i = 0; i <= 8; i++) {
      g.beginPath(); g.moveTo(0, s); g.lineTo((s / 8) * i, 0); g.stroke();
      g.beginPath(); g.moveTo(0, s); g.lineTo(s, s - (s / 8) * i); g.stroke();
    }
    for (let r = 14; r < s * 1.5; r += 14) {
      g.beginPath(); g.arc(0, s, r, -Math.PI / 2, 0); g.stroke();
    }
  }, 1);
  const webMat = new THREE.MeshBasicMaterial({ map: webTex, transparent: true, depthWrite: false, side: THREE.DoubleSide, opacity: 0.8 });
  for (const [x, y, z, ry] of [[-34, 8.6, -22, 0.8], [34, 8.6, -22, -0.8], [-34, 8.6, 22, 2.4], [34, 8.6, 10, -2.2], [-20, 8.6, -22, 0]]) {
    const wmesh = new THREE.Mesh(new THREE.PlaneGeometry(2.2, 2.2), webMat);
    wmesh.position.set(x, y, z); wmesh.rotation.y = ry;
    scene.add(wmesh);
  }

  // ================= GABBIA DEI COCCODRILLI (angolo NW) =================
  const CX = -29, CZ = -18.5; // centro gabbia
  const cBase = addBox(10, 0.5, 7, CX, 0.25, CZ, new THREE.MeshStandardMaterial({ map: conc.map, roughness: 0.9, metalness: 0.05 }));
  cBase.material.map = conc.map;
  const hzTex = canvasTex(128, (g, s) => {
    g.fillStyle = '#c8a020'; g.fillRect(0, 0, s, s);
    g.fillStyle = '#111';
    for (let i = -2; i < 6; i++) {
      g.save(); g.translate(i * 32, 0); g.rotate(Math.PI / 4);
      g.fillRect(-8, -s, 16, s * 3); g.restore();
    }
  }, 1);
  const hz = new THREE.Mesh(new THREE.BoxGeometry(10.05, 0.18, 0.1),
    new THREE.MeshStandardMaterial({ map: hzTex, roughness: 0.8 }));
  hz.position.set(CX, 0.42, CZ + 3.52); scene.add(hz);
  // acqua + isolotto + rocce
  poolMat = new THREE.MeshStandardMaterial({ color: 0x0e2a1e, metalness: 0.85, roughness: 0.15, envMapIntensity: 1.4 });
  const pool = new THREE.Mesh(new THREE.PlaneGeometry(9.4, 6.4), poolMat);
  pool.rotation.x = -Math.PI / 2; pool.position.set(CX, 0.56, CZ); scene.add(pool);
  const island = new THREE.Mesh(new THREE.SphereGeometry(1.6, 14, 10), matSand);
  island.scale.set(1.4, 0.32, 1.0); island.position.set(CX - 1, 0.55, CZ - 0.5);
  island.receiveShadow = true; scene.add(island);
  for (const [ox, oz, r] of [[2.5, 1.5, 0.5], [3.2, -1.2, 0.35], [-3.5, 1.8, 0.4]]) {
    const rock = new THREE.Mesh(new THREE.DodecahedronGeometry(r), matRock);
    rock.position.set(CX + ox, 0.6, CZ + oz); rock.rotation.set(Math.random(), Math.random(), 0);
    rock.castShadow = true; scene.add(rock);
  }
  // vetri + montanti + cornice (i vetri collidono: non si entra)
  const glassH = 2.2, glassY = 0.5 + glassH / 2;
  addBox(10, glassH, 0.06, CX, glassY, CZ - 3.5, matGlassCage);
  addBox(10, glassH, 0.06, CX, glassY, CZ + 3.5, matGlassCage);
  addBox(0.06, glassH, 7, CX - 5, glassY, CZ, matGlassCage);
  addBox(0.06, glassH, 7, CX + 5, glassY, CZ, matGlassCage);
  for (const [px, pz] of [[-5, -3.5], [0, -3.5], [5, -3.5], [-5, 3.5], [0, 3.5], [5, 3.5], [-5, 0], [5, 0]]) {
    addBox(0.14, glassH + 0.3, 0.14, CX + px, glassY, CZ + pz, matDark);
  }
  addBox(10.2, 0.12, 0.12, CX, 0.5 + glassH + 0.1, CZ - 3.5, matDark, false);
  addBox(10.2, 0.12, 0.12, CX, 0.5 + glassH + 0.1, CZ + 3.5, matDark, false);
  // lampada riscaldante
  const hpole = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.06, 3.2, 8), matDark);
  hpole.position.set(CX + 4.4, 1.6, CZ + 2.9); hpole.castShadow = true; scene.add(hpole);
  const shade = new THREE.Mesh(new THREE.ConeGeometry(0.35, 0.3, 12, 1, true),
    new THREE.MeshStandardMaterial({ color: 0x222222, metalness: 0.8, roughness: 0.4, side: THREE.DoubleSide }));
  shade.position.set(CX + 3.9, 3.1, CZ + 2.4); shade.rotation.z = 0.7; scene.add(shade);
  const heatBulb = new THREE.Mesh(new THREE.SphereGeometry(0.09, 10, 8),
    new THREE.MeshStandardMaterial({ color: 0x220000, emissive: 0xff5a2a, emissiveIntensity: 4 }));
  heatBulb.position.set(CX + 3.75, 3.0, CZ + 2.3); scene.add(heatBulb);
  const heat = new THREE.PointLight(0xff5a2a, 22, 10, 1.9);
  heat.position.set(CX + 3.7, 2.8, CZ + 2.2);
  scene.add(heat);
  lampLights.push({ pl: heat, bulbMat: heatBulb.material, mode: 'steady', base: 22 });
  // targa
  const plaque = new THREE.Mesh(new THREE.PlaneGeometry(1.6, 0.8),
    new THREE.MeshStandardMaterial({ map: textPlaque(['CROCODILI', 'NON TOCCARE IL VETRO'], 512, 256), roughness: 0.5, metalness: 0.4 }));
  plaque.position.set(CX, 1.05, CZ + 3.56); scene.add(plaque);
  // transenne con corda (collidono: barriera invisibile)
  const postG = new THREE.CylinderGeometry(0.05, 0.07, 0.95, 8);
  const ropePts = [];
  const RX1 = -34.5, RX2 = -23.5, RZ1 = -22.6, RZ2 = -13.6;
  for (let x = RX1; x <= RX2 + 0.01; x += (RX2 - RX1) / 5) { ropePts.push([x, RZ1], [x, RZ2]); }
  for (let z = RZ1 + (RZ2 - RZ1) / 3; z < RZ2 - 0.01; z += (RZ2 - RZ1) / 3) { ropePts.push([RX1, z], [RX2, z]); }
  for (const [x, z] of ropePts) {
    const post = new THREE.Mesh(postG, matGold);
    post.position.set(x, 0.475, z); post.castShadow = true; scene.add(post);
    const knob = new THREE.Mesh(new THREE.SphereGeometry(0.07, 8, 8), matGold);
    knob.position.set(x, 0.98, z); scene.add(knob);
  }
  const ropeMat = new THREE.MeshStandardMaterial({ color: 0x6e1420, roughness: 1 });
  const ropeSide = (x1, z1, x2, z2) => {
    const mid = new THREE.Vector3((x1 + x2) / 2, 0.78, (z1 + z2) / 2);
    const curve = new THREE.QuadraticBezierCurve3(new THREE.Vector3(x1, 0.92, z1), mid, new THREE.Vector3(x2, 0.92, z2));
    scene.add(new THREE.Mesh(new THREE.TubeGeometry(curve, 12, 0.03, 6), ropeMat));
  };
  ropeSide(RX1, RZ1, RX2, RZ1); ropeSide(RX1, RZ2, RX2, RZ2);
  ropeSide(RX1, RZ1, RX1, RZ2); ropeSide(RX2, RZ1, RX2, RZ2);
  invisibleWall(RX1, 0, RZ1 - 0.1, RX2, 0.9, RZ1 + 0.1);
  invisibleWall(RX1, 0, RZ2 - 0.1, RX2, 0.9, RZ2 + 0.1);
  invisibleWall(RX1 - 0.1, 0, RZ1, RX1 + 0.1, 0.9, RZ2);
  invisibleWall(RX2 - 0.1, 0, RZ1, RX2 + 0.1, 0.9, RZ2);
  // gli inquilini
  const croc1 = makeCroc(1.0);
  croc1.group.position.set(CX + 1.5, 0.42, CZ + 0.5);
  croc1.group.rotation.y = 0.7;
  const croc2 = makeCroc(0.75);
  croc2.group.position.set(CX - 1.2, 0.95, CZ - 0.6);
  croc2.group.rotation.y = -2.2;

  // foto PBR (legno soppalco + acqua piscina)
  upgradeToPhotos({}).then((r) => {
    waterTex = r.water;
    if (poolMat && waterTex) {
      poolMat.normalMap = waterTex; poolMat.normalScale.set(0.6, 0.6);
      poolMat.needsUpdate = true;
    }
  });
}

export function tickWorld(dt) {
  if (holo) { holo.rotation.y += dt * 0.8; holo.position.y = 5.6 + Math.sin(state.time * 1.5) * 0.25; }
  if (skyMat) skyMat.uniforms.uTime.value = state.time;
  gradePass.uniforms.uTime.value = state.time;
  for (const L of lampLights) {
    const { pl, bulbMat, mode, base, phase = 0 } = L;
    if (mode === 'faulty') {
      const f = Math.random() < 0.07 ? 0.12 : 0.82 + Math.random() * 0.3;
      pl.intensity = base * f;
      if (bulbMat) bulbMat.emissiveIntensity = 3.2 * f;
    } else if (mode === 'pulse') {
      const f = 0.65 + 0.35 * Math.sin(state.time * 2.4 + phase);
      pl.intensity = base * f;
      if (bulbMat) bulbMat.emissiveIntensity = 3 * f;
    } else {
      pl.intensity = base * (1 + Math.sin(state.time * 9 + phase) * 0.04);
    }
  }
  if (waterTex) { waterTex.offset.x += dt * 0.03; waterTex.offset.y += dt * 0.013; }
  // coccodrilli: respiro, coda, mascella, sguardo, ammiccamento
  for (const c of crocs) {
    const t = state.time + c.phase;
    c.tail1.rotation.y = Math.sin(t * 1.8) * 0.18;
    c.tail2.rotation.y = Math.sin(t * 1.8 - 0.7) * 0.25;
    c.body.scale.y = 1 + Math.sin(t * 1.1) * 0.025;
    c.jawT -= dt;
    if (c.jawT <= 0) { c.jawT = 6 + Math.random() * 9; c.jawA = 1; }
    c.jawA = Math.max(0, c.jawA - dt * 1.4);
    c.jawG.rotation.x = 0.45 * Math.sin(Math.PI * THREE.MathUtils.clamp(c.jawA, 0, 1));
    const dx = player.pos.x - c.group.position.x, dz = player.pos.z - c.group.position.z;
    const d = Math.hypot(dx, dz);
    let want = Math.sin(t * 0.4) * 0.12;
    if (d < 9 && d > 0.01) {
      let rel = Math.atan2(dx, dz) - c.group.rotation.y;
      while (rel > Math.PI) rel -= Math.PI * 2;
      while (rel < -Math.PI) rel += Math.PI * 2;
      want += THREE.MathUtils.clamp(rel, -0.55, 0.55) * 0.8;
    }
    c.head.rotation.y += (want - c.head.rotation.y) * Math.min(1, 3 * dt);
    c.blinkT -= dt;
    if (c.blinkT <= 0) { c.blinkT = 3 + Math.random() * 4; c.blink = 0.18; }
    c.blink = Math.max(0, c.blink - dt);
    const es = c.blink > 0 ? 0.15 : 1;
    c.eyeL.scale.y = es; c.eyeR.scale.y = es;
  }
}
