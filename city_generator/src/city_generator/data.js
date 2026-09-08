// Carica e normalizza manual_city.json in una forma facile da consumare:
// [{ id, color, buildings: [{ id, population }] }, ...]

import rawData from '../../spotify_city.json';

function isValidHexColor(value) {
  return typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value);
}

export function loadCityData() {
  const quartieri = [];

  for (const [quartiereId, quartiere] of Object.entries(rawData)) {
    if (!quartiere || !isValidHexColor(quartiere.color) || typeof quartiere.buildings !== 'object') {
      console.warn(`[city_generator] quartiere "${quartiereId}" ignorato: dati non validi.`);
      continue;
    }

    const buildings = Object.entries(quartiere.buildings)
      .map(([buildingId, population]) => ({ id: buildingId, population: Number(population) }))
      .filter((b) => Number.isFinite(b.population) && b.population > 0);

    if (buildings.length === 0) {
      console.warn(`[city_generator] quartiere "${quartiereId}" ignorato: nessun edificio valido.`);
      continue;
    }

    quartieri.push({ id: quartiereId, color: quartiere.color, buildings });
  }

  return quartieri;
}
