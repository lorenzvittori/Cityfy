// Mappatura popolazione -> dimensioni dell'edificio.
// La popolazione di ogni building (1-10, vedi manual_city.json) viene
// normalizzata e poi interpolata linearmente sui range qui sotto.

export const POP_MIN = 1;
export const POP_MAX = 10;

export const MIN_HEIGHT = 1;
export const MAX_HEIGHT = 9;

export const MIN_FOOTPRINT = 1.6;
export const MAX_FOOTPRINT = 3.2;

function lerp(t, min, max) {
  return min + t * (max - min);
}

function normalizedPopulation(population) {
  const t = (population - POP_MIN) / (POP_MAX - POP_MIN);
  return Math.min(1, Math.max(0, t));
}

export function populationToHeight(population) {
  return lerp(normalizedPopulation(population), MIN_HEIGHT, MAX_HEIGHT);
}

export function populationToFootprint(population) {
  return lerp(normalizedPopulation(population), MIN_FOOTPRINT, MAX_FOOTPRINT);
}
