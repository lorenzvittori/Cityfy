// Layout "a scaffale" (shelf packing), puro - nessuna dipendenza da three.js.
//
// 1. Ogni quartiere viene disposto in una mini-griglia interna auto-adattiva
//    (cols = ceil(sqrt(n)), rows = ceil(n/cols)) -> bounding box del quartiere.
// 2. I quartieri vengono accodati in righe separate da un corridoio stradale;
//    quando una riga supera MAX_ROW_WIDTH si va a capo (nuovo corridoio).
// 3. Il risultato finale viene centrato sull'origine.

import { MAX_FOOTPRINT } from './scale.js';

const LOT_GAP = 1; // spazio tra edifici dentro lo stesso quartiere
const MAIN_ROAD_WIDTH = 5; // corridoio tra quartieri e tra righe
const MAX_ROW_WIDTH = 60; // larghezza oltre cui si va a capo

// Passo fisso tra i "lotti" interni di un quartiere: basato sul footprint
// massimo possibile, così nessun edificio (qualunque sia la sua popolazione)
// puo' mai sconfinare nel lotto vicino.
const LOT_PITCH = MAX_FOOTPRINT + LOT_GAP;

function computeQuartiereGrid(quartiere) {
  const n = quartiere.buildings.length;
  const cols = Math.ceil(Math.sqrt(n));
  const rows = Math.ceil(n / cols);

  return {
    ...quartiere,
    cols,
    rows,
    width: cols * LOT_PITCH,
    depth: rows * LOT_PITCH,
  };
}

function packIntoRows(quartieri) {
  const rows = [];
  let current = { items: [], width: 0, depth: 0 };

  for (const q of quartieri) {
    const isFirstInRow = current.items.length === 0;
    const widthWithQ = current.width + (isFirstInRow ? 0 : MAIN_ROAD_WIDTH) + q.width;

    if (!isFirstInRow && widthWithQ > MAX_ROW_WIDTH) {
      rows.push(current);
      current = { items: [], width: 0, depth: 0 };
    }

    const gap = current.items.length === 0 ? 0 : MAIN_ROAD_WIDTH;
    current.width += gap + q.width;
    current.depth = Math.max(current.depth, q.depth);
    current.items.push(q);
  }

  if (current.items.length > 0) rows.push(current);
  return rows;
}

export function computeCityLayout(quartieriData) {
  const sized = quartieriData.map(computeQuartiereGrid);
  const rows = packIntoRows(sized);

  // Posiziona ogni quartiere (angolo in alto a sinistra del suo blocco)
  // scorrendo riga per riga.
  const positioned = [];
  let cursorZ = 0;
  let cityWidth = 0;

  for (const row of rows) {
    let cursorX = 0;
    for (const q of row.items) {
      positioned.push({ ...q, blockX: cursorX, blockZ: cursorZ });
      cursorX += q.width + MAIN_ROAD_WIDTH;
    }
    cityWidth = Math.max(cityWidth, cursorX - MAIN_ROAD_WIDTH);
    cursorZ += row.depth + MAIN_ROAD_WIDTH;
  }

  const cityDepth = Math.max(0, cursorZ - MAIN_ROAD_WIDTH);
  const offsetX = cityWidth / 2;
  const offsetZ = cityDepth / 2;

  const quartieri = positioned.map((q) => {
    const blockCenterX = q.blockX + q.width / 2 - offsetX;
    const blockCenterZ = q.blockZ + q.depth / 2 - offsetZ;

    const gridOffsetX = ((q.cols - 1) * LOT_PITCH) / 2;
    const gridOffsetZ = ((q.rows - 1) * LOT_PITCH) / 2;

    const buildings = q.buildings.map((b, i) => {
      const col = i % q.cols;
      const row = Math.floor(i / q.cols);
      return {
        ...b,
        x: blockCenterX + col * LOT_PITCH - gridOffsetX,
        z: blockCenterZ + row * LOT_PITCH - gridOffsetZ,
      };
    });

    return {
      id: q.id,
      color: q.color,
      x: blockCenterX,
      z: blockCenterZ,
      width: q.width,
      depth: q.depth,
      buildings,
    };
  });

  return { quartieri, size: { width: cityWidth, depth: cityDepth } };
}
