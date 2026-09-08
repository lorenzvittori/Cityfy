# Quartiere 3D

Visualizzazione minimal di un quartiere composto da palazzi (parallelepipedi),
con telecamera dall'alto interagibile (rotazione + zoom).

Stack: **Vite** + **Three.js** (vanilla JS, nessun framework).

## Struttura del repo

```
quartiere-3d/
├── index.html               # entry point HTML
├── package.json
├── vite.config.js
├── .gitignore
├── README.md
├── manual_city.json          # dati di input: quartieri, edifici, popolazione, colore
└── src/
    ├── main.js               # scena, camera, luci, controlli, render loop, hover
    ├── style.css              # stile base (canvas fullscreen, tooltip)
    └── city_generator/         # genera la città a partire da manual_city.json
        ├── data.js            # carica e valida manual_city.json
        ├── layout.js           # posiziona quartieri ed edifici (nessuna dipendenza da three.js)
        ├── scale.js            # mappa la popolazione di un edificio ad altezza/footprint
        ├── buildings.js        # crea le mesh degli edifici
        └── index.js            # orchestratore: createCityFromData() -> THREE.Group
```

La città non è generata a caso: viene costruita leggendo `manual_city.json`
(vedi sezione [Personalizzare la città](#personalizzare-la-città) più sotto).

## Avvio in locale

```bash
npm install
npm run dev
```

Poi apri l'URL mostrato in console (di solito `http://localhost:5173`).

## Build di produzione

```bash
npm run build
npm run preview
```

I file pronti per il deploy finiscono in `dist/`, pubblicabili su qualsiasi
hosting statico (Vercel, Netlify, GitHub Pages, ecc.).

## Controlli camera

- **Trascina** (mouse/touch) → ruota la vista attorno al quartiere
- **Scroll / pinch** → zoom in/out
- I limiti di zoom e l'angolo massimo sono configurabili in `src/main.js`
  (`controls.minDistance`, `controls.maxDistance`, `controls.maxPolarAngle`)

## Personalizzare la città

**I dati** vivono in [`manual_city.json`](manual_city.json): un oggetto per
quartiere (es. `Q1`), con un `color` (hex) e una mappa `buildings` dove ogni
chiave è l'id dell'edificio (es. `b1_1`) e il valore è la sua popolazione.
Aggiungere/rimuovere quartieri o edifici, o cambiarne popolazione/colore,
basta modificare questo file — `main.js` lo importa staticamente, quindi
serve un riavvio/refresh di Vite per vedere le modifiche.

**Il layout** (come i quartieri e gli edifici vengono disposti nello spazio,
con corridoi stradali tra loro) è regolato dalle costanti in
[`src/city_generator/layout.js`](src/city_generator/layout.js):

- `LOT_GAP` → spazio tra edifici dentro lo stesso quartiere
- `MAIN_ROAD_WIDTH` → larghezza dei corridoi tra quartieri e tra righe
- `MAX_ROW_WIDTH` → larghezza oltre cui i quartieri vanno a capo su una nuova riga

**La geometria** dei singoli edifici (come la popolazione si traduce in
altezza/base) è in [`src/city_generator/scale.js`](src/city_generator/scale.js):
`MIN_HEIGHT`/`MAX_HEIGHT` e `MIN_FOOTPRINT`/`MAX_FOOTPRINT`.

## Interazione

- Passando il mouse su un edificio compare un tooltip con quartiere e id
  dell'edificio, e l'edificio viene evidenziato con un contorno bianco
  (raycasting, vedi `src/main.js`)

## Prossimi passi possibili

- Strade come mesh vere (non solo corridoi vuoti nel terreno)
- Ombre morbide (`renderer.shadowMap`, `castShadow`/`receiveShadow`)
- Pannello info al click su un palazzo (oltre al tooltip hover)
- Caricamento dei dati via fetch/import dinamico invece di import statico
- Passaggio a React Three Fiber se l'interfaccia cresce in complessità
