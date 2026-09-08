import * as THREE from 'three';
import { loadCityData } from './data.js';
import { computeCityLayout } from './layout.js';
import { createBuildingMesh } from './buildings.js';

// Genera la scena (un THREE.Group) a partire da manual_city.json:
// data.js -> normalizza, layout.js -> posiziona, buildings.js -> crea le mesh.
export function createCityFromData() {
  const quartieriData = loadCityData();
  const { quartieri, size } = computeCityLayout(quartieriData);

  const group = new THREE.Group();
  group.name = 'city_generator';

  for (const quartiere of quartieri) {
    for (const building of quartiere.buildings) {
      group.add(createBuildingMesh(building, quartiere));
    }
  }

  group.userData.citySize = size;

  return group;
}
