// core.js — stato condiviso + setup renderer/scena/post. Non importa altri moduli interni.
import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { SMAAPass } from 'three/addons/postprocessing/SMAAPass.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';

export const $ = (id) => document.getElementById(id);
export const ARENA = 44;

// ---------------- CONFIG ----------------
export const CFG = {
  sens: 1.0, baseFov: 75,
  bloom: true, shake: true, quality: 'high',
  walk: 5.6, sprint: 8.2, crouch: 3.1, accelGround: 34, accelAir: 9,
  friction: 9.5, jumpVel: 5.4, gravity: 17.5,
  eyeStand: 1.62, eyeCrouch: 1.02, radius: 0.38, heightStand: 1.75, heightCrouch: 1.15,
};
export const state = {
  started: false, locked: false, dead: false,
  score: 0, kills: 0, wave: 1, waveActive: false, waveBreak: 0,
  time: 0,
};

// ---------------- RENDERER / SCENA ----------------
export const canvas = $('game-canvas');
export const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.15;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;

export const scene = new THREE.Scene();
scene.background = new THREE.Color(0x070b14);
scene.fog = new THREE.FogExp2(0x0a0f1c, 0.016);

export const camera = new THREE.PerspectiveCamera(CFG.baseFov, innerWidth / innerHeight, 0.05, 400);
camera.rotation.order = 'YXZ';
scene.add(camera);

// ---------------- POST ----------------
export const composer = new EffectComposer(renderer);
composer.addPass(new RenderPass(scene, camera));
export const bloomPass = new UnrealBloomPass(new THREE.Vector2(innerWidth, innerHeight), 0.5, 0.7, 0.72);
composer.addPass(bloomPass);
composer.addPass(new OutputPass());

// Color grading cinematografico: vignetta + grana + aberrazione cromatica
export const GradeShader = {
  uniforms: {
    tDiffuse: { value: null },
    uTime: { value: 0 },
    uVignette: { value: 0.5 },
    uGrain: { value: 0.05 },
    uAberration: { value: 0.0016 },
    uSaturation: { value: 1.12 },
    uContrast: { value: 1.06 },
    uLift: { value: 0.012 },
  },
  vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
  fragmentShader: `
    uniform sampler2D tDiffuse;
    uniform float uTime, uVignette, uGrain, uAberration, uSaturation, uContrast, uLift;
    varying vec2 vUv;
    float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
    void main(){
      vec2 d = vUv - 0.5;
      float r2 = dot(d, d);
      float ab = uAberration * (0.5 + r2 * 4.0);
      vec3 col;
      col.r = texture2D(tDiffuse, vUv + d * ab * 2.0).r;
      col.g = texture2D(tDiffuse, vUv).g;
      col.b = texture2D(tDiffuse, vUv - d * ab * 2.0).b;
      float lum = dot(col, vec3(0.299, 0.587, 0.114));
      col = mix(vec3(lum), col, uSaturation);
      col = (col - 0.5) * uContrast + 0.5 + uLift;
      float vig = 1.0 - smoothstep(0.2, 0.75, r2 * 1.6);
      col *= mix(1.0, vig, clamp(uVignette, 0.0, 1.0));
      col += (hash(vUv * vec2(1920.0, 1080.0) + fract(uTime) * 7.13) - 0.5) * uGrain;
      gl_FragColor = vec4(col, 1.0);
    }`,
};
export const gradePass = new ShaderPass(GradeShader);
composer.addPass(gradePass);
export const smaaPass = new SMAAPass();
composer.addPass(smaaPass);

// Image-based lighting: riflessi PBR credibili su metalli e pozzanghere
{
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.06).texture;
  pmrem.dispose();
}

export function applyQuality() {
  const q = CFG.quality;
  const pr = q === 'low' ? Math.min(devicePixelRatio, 1) : q === 'high' ? Math.min(devicePixelRatio, 1.5) : Math.min(devicePixelRatio, 2);
  renderer.setPixelRatio(pr);
  composer.setPixelRatio(pr);
  renderer.setSize(innerWidth, innerHeight);
  composer.setSize(innerWidth, innerHeight);
  const sun = scene.getObjectByName('sun');
  if (sun) {
    sun.castShadow = q !== 'low';
    const s = q === 'ultra' ? 4096 : q === 'high' ? 2048 : 1024;
    if (sun.shadow.mapSize.x !== s) {
      sun.shadow.mapSize.set(s, s);
      if (sun.shadow.map) { sun.shadow.map.dispose(); sun.shadow.map = null; }
    }
  }
  bloomPass.enabled = CFG.bloom && q !== 'low';
  smaaPass.enabled = q !== 'low';
  gradePass.uniforms.uGrain.value = q === 'low' ? 0.0 : q === 'high' ? 0.05 : 0.07;
  gradePass.uniforms.uVignette.value = q === 'low' ? 0.35 : 0.5;
}

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  applyQuality();
});

// ---------------- STATO DI GIOCO ----------------
export const player = {
  pos: new THREE.Vector3(0, 0, 19), // piedi, ingresso sud dell'hotel
  vel: new THREE.Vector3(),
  yaw: 0, pitch: 0, roll: 0, // guarda a nord (-Z), verso lo scalone
  onGround: false, coyote: 0, jumpBuf: 0,
  crouch: false, sprint: false, slide: false, slideT: 0, slideDir: new THREE.Vector3(),
  eyeH: CFG.eyeStand, bobPhase: 0, stepAcc: 0,
  hp: 100, shield: 50, lastDamage: -99, alive: true, respawnT: 0,
  lean: 0, fovKick: 0, shakeT: 0, shakeAmp: 0,
  adsHeld: false, firing: false,
};

export const weapon = {
  group: new THREE.Group(),
  muzzle: null, flash: null, flashSide: null, flashLight: null, laser: null,
  parts: {},
  mag: 30, reserve: 120, magSize: 30,
  reloading: false, reloadT: 0, lastShot: 0, rpm: 700,
  ads: 0, recoil: 0,
  sway: new THREE.Vector2(), bobPhase: 0,
  sprintPose: 0, boltT: 0,
};

export const enemies = [];
export const keys = {};
export const colliders = [];   // Box3 per collisioni giocatore/nemici
export const worldMeshes = []; // mesh per raycast proiettili
