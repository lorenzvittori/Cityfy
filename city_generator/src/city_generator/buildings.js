import * as THREE from 'three';
import { populationToHeight, populationToFootprint } from './scale.js';

// Varia leggermente il colore base del quartiere per ogni edificio, cosi'
// i palazzi si distinguono pur restando nella stessa famiglia cromatica.
function variateColor(baseHex) {
  const color = new THREE.Color(baseHex);
  const hueJitter = (Math.random() - 0.5) * 0.03;
  const satJitter = (Math.random() - 0.5) * 0.1;
  const lightJitter = (Math.random() - 0.5) * 0.16;
  color.offsetHSL(hueJitter, satJitter, lightJitter);
  return color;
}

export function createBuildingMesh(building, quartiere) {
  const height = populationToHeight(building.population);
  const footprint = populationToFootprint(building.population);

  const geometry = new THREE.BoxGeometry(footprint, height, footprint);
  const material = new THREE.MeshStandardMaterial({
    color: variateColor(quartiere.color),
    roughness: 0.9,
  });

  const mesh = new THREE.Mesh(geometry, material);
  mesh.position.set(building.x, height / 2, building.z);
  mesh.name = building.id;
  mesh.userData.quartiereId = quartiere.id;
  mesh.userData.buildingId = building.id;

  return mesh;
}
