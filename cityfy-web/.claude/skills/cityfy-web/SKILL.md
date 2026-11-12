---
name: cityfy-web
description: Frontend Angular di CityFy (solo questo progetto, non portabile) — struttura, pagine delle città salvate, scena 3D three.js che disegna la mappa del backend, form della città con parametri custom, errori per codice, comandi di build e test. Usala prima di modificare pagine, componenti o chiamate API di cityfy-web.
---

# cityfy-web — frontend di CityFy

Skill specifica di questo progetto. Per il backend e il flusso dei dati vedi `CityFy/.claude/skills/cityfy-project`.

## Principi

- **Disegna e basta.** La geometria della città (posizioni, strade, arredo urbano, segnaletica) arriva calcolata dal backend in `map` (`CityMap`). Il frontend non calcola layout: la vecchia cartella `layout/` è stata rimossa.
- **Testi in italiano solo qui.** Il backend restituisce codici e dati. Titoli predefiniti (`cityTitle`), etichette di stato, periodi e parametri si compongono in `features/cities/city-labels.ts`; i messaggi d'errore stanno in `core/errors/error-messages.ts`.
- **Modelli scritti a mano** in `core/api/` (`api.models.ts`, `city-map.models.ts`, `recipe.models.ts`): specchiano i DTO v1 del gateway e vanno aggiornati insieme a loro.

## Struttura

| Percorso | Contenuto |
|---|---|
| `core/api/api.service.ts` | Tutte le chiamate a `/api/v1`; errori normalizzati in `ApiError` (`code`, `status`, `traceId`, `problems`) |
| `core/api/city-labels.ts` | Traduzione dei nomi generati dal backend, per esempio il quartiere `other_genres` diventa "Altri generi" |
| `core/errors/` | `ApiError`, `error-messages.ts` (un messaggio e un'azione per codice) |
| `features/cities/cities.page.*` | `/cities`: tabella paginata, polling mentre qualcosa è in costruzione, limite di 10, "Aggiorna i dati" |
| `features/cities/new-city.page.*` | `/cities/new`: nome, date con scorciatoie, tipo di mappa, albero di generi e sottogeneri, pannello custom (stili in `recipe-panel.scss`). Si può precompilare con query param (`from`, `to`, `layout`, `recipe`, `genres`, `subgenres`, `name`) |
| `features/cities/city-detail.page.*` | `/cities/:cityId`: stato con polling, scena 3D, legenda esplorabile (quartiere → generi → artisti, con volo della camera), scheda artista, "Duplica e modifica" |
| `features/cities/city-scene/` | Scena three.js: disegna `CityMap`, tema giorno e notte, minimappa, navigazione. Metodi pubblici `focusDistrict`, `focusBlock`, `focusArtist` |
| `features/cities/city-form.ts`, `recipe.ts` | Logica pura del form: date (`to` esclusivo verso l'API), selezione dei generi, lettura e scrittura della stringa dei parametri (stesse regole del parser del backend, schema da `cities/recipe/schema`) |
| `features/admin/` | Pannello admin (run della pipeline, import MusicBrainz, database) |

## Regole utili

- **Date:** nel form sono inclusive; verso l'API `to` è il giorno dopo a mezzanotte UTC (`apiRange` / `formTo`).
- **Selezione dei generi:** un quartiere intero va in `genres`, i sottogeneri singoli in `subgenres`; niente selezionato significa tutti.
- **Stringa custom:** il form la riscrive con `base` più le sole chiavi diverse dalla base. La validazione locale è solo un aiuto: fanno fede i `problems` del backend.
- **Componenti:** standalone con signals; route con input binding (`withComponentInputBinding`).
- **Budget di stile:** 4 kB per file. Se lo superi, dividi gli stili con `styleUrls`.

## Comandi

- `npx ng build`: deve finire senza warning (neanche NG8102 sui `??` inutili).
- `npx ng test --watch=false`: vitest tramite il builder Angular. **Non** lanciare `npx vitest` direttamente, perché non carica il setup di Angular.
