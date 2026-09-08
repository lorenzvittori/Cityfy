import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { createCityFromData } from './city_generator/index.js';

const canvas = document.querySelector('#scene');

// --- Scene ---
const scene = new THREE.Scene();
scene.background = new THREE.Color('#eee9e1');
scene.fog = new THREE.Fog('#eee9e1', 40, 120);

// --- Camera (vista dall'alto, angolata) ---
const camera = new THREE.PerspectiveCamera(
  45,
  window.innerWidth / window.innerHeight,
  0.1,
  500
);
camera.position.set(28, 34, 28);

// --- Renderer ---
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

// --- Controlli camera: rotazione + zoom ---
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.06;
controls.minDistance = 15;
controls.maxDistance = 90;
controls.maxPolarAngle = Math.PI / 2.3; // evita di scendere sotto il piano
controls.target.set(0, 2, 0);

// --- Luci ---
const ambient = new THREE.AmbientLight('#ffffff', 0.7);
scene.add(ambient);

const sun = new THREE.DirectionalLight('#ffffff', 1.1);
sun.position.set(20, 30, 10);
scene.add(sun);

// --- Quartiere (generato da manual_city.json) ---
const city = createCityFromData();
scene.add(city);

// --- Terreno (dimensionato sulla città generata) ---
const GROUND_MARGIN = 20;
const citySize = city.userData.citySize;
const groundSide = Math.max(citySize.width, citySize.depth) + GROUND_MARGIN;

const groundGeo = new THREE.PlaneGeometry(groundSide, groundSide);
const groundMat = new THREE.MeshStandardMaterial({ color: '#d8d2c4' });
const ground = new THREE.Mesh(groundGeo, groundMat);
ground.rotation.x = -Math.PI / 2;
scene.add(ground);

// Adatta zoom massimo e nebbia alla dimensione reale della città, cosi'
// resta sempre inquadrabile per intero.
controls.maxDistance = Math.max(controls.maxDistance, groundSide * 1.1);
scene.fog.far = Math.max(scene.fog.far, groundSide * 1.3);

// --- Contorno bianco sull'edificio sotto il mouse ---
// Tecnica del "guscio invertito": una copia leggermente più grande della
// mesh, renderizzata solo sul retro (BackSide), fa da bordo visibile
// intorno all'edificio originale.
const outlineMaterial = new THREE.MeshBasicMaterial({ color: '#ffffff', side: THREE.BackSide });
let outlineMesh = null;
let hoveredMesh = null;

function setHovered(mesh) {
  if (mesh === hoveredMesh) return;

  if (outlineMesh) {
    scene.remove(outlineMesh);
    outlineMesh = null;
  }

  hoveredMesh = mesh;

  if (mesh) {
    outlineMesh = new THREE.Mesh(mesh.geometry, outlineMaterial);
    outlineMesh.position.copy(mesh.position);
    outlineMesh.rotation.copy(mesh.rotation);
    outlineMesh.scale.copy(mesh.scale).multiplyScalar(1.06);
    scene.add(outlineMesh);
  }
}

// --- Tooltip al passaggio del mouse su un edificio ---
const tooltip = document.querySelector('#tooltip');
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();

function onPointerMove(event) {
  pointer.x = (event.clientX / window.innerWidth) * 2 - 1;
  pointer.y = -(event.clientY / window.innerHeight) * 2 + 1;

  raycaster.setFromCamera(pointer, camera);
  const [hit] = raycaster.intersectObjects(city.children, false);

  setHovered(hit ? hit.object : null);

  if (hit) {
    const { quartiereId, buildingId } = hit.object.userData;
    tooltip.innerHTML = `<strong>${quartiereId}</strong><br>${buildingId}`;
    tooltip.style.left = `${event.clientX}px`;
    tooltip.style.top = `${event.clientY}px`;
    tooltip.hidden = false;
  } else {
    tooltip.hidden = true;
  }
}
window.addEventListener('pointermove', onPointerMove);

// --- Resize ---
function onResize() {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
}
window.addEventListener('resize', onResize);

// --- Loop ---
function animate() {
  requestAnimationFrame(animate);
  controls.update();
  renderer.render(scene, camera);
}
animate();
