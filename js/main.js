// main.js — bootstrap + game loop.
import * as THREE from 'three';
import { $, CFG, state, renderer, scene, camera, composer, bloomPass, applyQuality } from './core.js';
import { buildWorld, tickWorld } from './world.js';
import { buildWeapon } from './weapon.js';
import { updatePlayer } from './player.js';
import { updateEnemies } from './enemies.js';
import { tickEffects } from './effects.js';
import { tickCombat } from './combat.js';
import { tickUI, updateAmmoUI, updateBars } from './ui.js';
import { initInput } from './input.js';

buildWorld();
buildWeapon();
initInput();
applyQuality();
updateAmmoUI();
updateBars();
$('loading').style.display = 'none';

const clock = new THREE.Clock();

function animate() {
  requestAnimationFrame(animate);
  const dt = Math.min(clock.getDelta(), 0.033);
  state.time += dt;

  if (state.started && state.locked) {
    updatePlayer(dt);
    updateEnemies(dt);
    tickCombat(dt);
  }
  tickWorld(dt);
  tickEffects(dt);
  tickUI(dt);

  if (CFG.bloom && bloomPass.enabled) composer.render();
  else renderer.render(scene, camera);
}
animate();
