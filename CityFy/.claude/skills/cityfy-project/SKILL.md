---
name: cityfy-project
description: Come è fatto CityFy (solo questo progetto, non portabile) — servizi e responsabilità, collection Mongo e proprietari, messaggi RabbitMQ e code, endpoint del gateway, flusso ascolti → catalogo artisti → città generate → mappa, test e comandi, vincoli esterni (Spotify, Last.fm), piani in corso. Usala prima di toccare un servizio, un messaggio, una collection o un endpoint di CityFy.
---

# CityFy — come funziona il progetto

Skill specifica di questo repository: non copiarla in altri progetti. Le regole generali stanno nelle skill della radice (`dotnet-conventions`, `aspnet-microservices`, `feature-workflow`), da caricare insieme a questa.

## Cosa fa

Gli ascolti musicali di un utente diventano una città 3D:
- **quartiere** = macro-genere (radice della gerarchia dei generi MusicBrainz);
- **isolato** = genere;
- **edificio** = artista (altezza e larghezza crescono con gli ascolti).

L'utente genera città salvate scegliendo periodo, tipo di mappa e generi; il frontend le disegna.

## Struttura del repository

| Percorso | Contenuto |
|---|---|
| `CityFy/` | Solution .NET 10 (`CityFy.slnx`) con Aspire. **Lavora solo qui e in `cityfy-web/`**: gli script Python in `spotify_data/` e `city_generator/` sono storici, da ignorare |
| `cityfy-web/` | Frontend Angular (vedi la skill `cityfy-web` dentro quella cartella) |
| `deploy/` | `docker-compose.yml` di produzione (VM Oracle, Caddy) |
| `.github/workflows/deploy.yml` | Build delle immagini: elenco `nome:Progetto` da aggiornare per ogni servizio nuovo |
| `ERRORI_UI.md` | Catalogo dei codici d'errore per la UI (vedi Test) |
| `PIANO_*.md` | Piani di lavoro con decisioni e fasi |
| `README.md`, `DEPLOY.md` | Documentazione da aggiornare insieme al codice |

## Servizi (nomi delle risorse AppHost)

| Servizio | Responsabilità | Dati propri |
|---|---|---|
| **Api** (`CityFy.Api`) | Gateway, l'unico esposto al browser: sessione (cookie `cityfy.auth`), API `api/v1`, pannello admin (utente fittizio, solo Development), cancellazione dati su tutti i servizi, health check | — |
| **Catalog** (ex Elaboration) | Aggrega gli ascolti, tiene il **catalogo artisti** dell'utente (artista → quartiere, genere, colore), costruisce su richiesta il `CityModel` di un intervallo con filtro generi/sottogeneri | `user_catalogs`, `user_artist_placements`; legge `listening_events` (modello condiviso) |
| **CityGenerator** | Città salvate: richiesta e job di generazione, layout della mappa (geometria in C#), parametri della mappa custom, limite di 10 città, riuso per hash | `generated_cities` |
| **Orchestrator** | Debounce degli import, run della pipeline (aggiornamento del catalogo) | `pipeline_runs` |
| **RetrieveSpotify** | Login OAuth Spotify, retrieve `recently-played` (manuale e ogni 30 min) | `spotify_accounts`, `spotify_retrieve_tasks` |
| **RetrieveDumpSpotify** | Upload dell'export zip di Spotify, import dello storico | task di upload, file su disco |
| **RetrieveMusicBrainz** | Import del dump MusicBrainz in SQL Server, API di lettura: resolve degli artisti, grafo dei generi | DB SQL `musicbrainz` |

Collection condivisa: `listening_events`, scritta dalle sorgenti (Spotify, Dump) e letta da Catalog. Collection dismessa: `city_models` (città per periodo); Catalog la ripulisce ancora nella cancellazione dati.

## Flusso dei dati

```
Dump.Spotify / Spotify ──► listening_events ──► ListeningImported
Orchestrator  ListeningImported ──(debounce 30 s)──► RefreshCatalogRequested
Catalog       ricalcolo catalogo (resolve MusicBrainz + gerarchia) ──► UserCatalogUpdated | CatalogRefreshFailed
Orchestrator  UserCatalogUpdated | CatalogRefreshFailed ──► run Done | Failed
CityGenerator UserCatalogUpdated ──► prima città di un utente senza città
MusicBrainz   import finito ──► CatalogUpdated ──► Orchestrator aggiorna i cataloghi di tutti
Utente        POST api/v1/cities ──► CityGenerator (Pending) ──► GenerateCityRequested (a se stesso)
              ──► POST api/catalog/city-model/{userId} ──► layout ──► generated city Done
```

- **Code:** `<servizio>.<azione>`, per esempio `catalog.refresh-catalog`, `orchestrator.user-catalog-updated`, `orchestrator.catalog-refresh-failed`, `city-generator.generate-city`, `city-generator.user-catalog-updated`.
- **Code orfane:** sul broker possono restare le code vecchie (`elaboration.build-city`, `catalog.build-city`, `orchestrator.city-built`, `orchestrator.city-build-failed`).
- **Stati della run:** `Pending → Building → Done | Failed | Cancelled`. `Building` significa "catalogo in aggiornamento"; non è stato rinominato per non cambiare l'API.

## Modelli chiave (`ServiceDefault/City/`)

- **`CityModel`:** dati della città (quartieri > isolati > edifici, riepilogo, avvisi `CITY_*`, `From`/`To` con `To` esclusivo). Non si salva da solo: vive dentro la città generata.
- **`CityModelQuery` / `CityModelResult`:** richiesta a Catalog e risposta con la `CatalogVersion`.
- **`GenerateCityRequest`, `GeneratedCityInfo`/`Detail`/`Page`:** contratto di CityGenerator. `Status` è una stringa, `Name` è null se scelto dalla UI.
- **`CityLayout`:** la mappa (edifici con x/z/rotazione, quartieri, isolati con sotto-isolati e marciapiedi, piazze, strade, strisce, lampioni, semafori, cartelli, segnaletica). È un **contratto condiviso esposto così com'è dal v1**: eccezione approvata alla regola dei DTO separati.
- **`RecipeSchema`, `RecipeProblem`:** parametri della mappa custom.

## Mappa (CityGenerator)

- **Geometria (`Models/Geometry/`):**
  - `Polygon`, modello immutabile con i metodi di ritaglio, restringimento, intersezione, involucro convesso, diagramma di potenza, divisione per aree e lotti;
  - delegate `CutFamily` e `CityTemplate`;
  - `Seed` (hash FNV deterministico) e `Vector.Hypot` (come il browser).
- **Layout (`Models/Layout/`):** `OrganicTemplate` (treemap Voronoi pesato, piazze a spicchi), `GridTemplate`, `StreetPlan` (strade dai bordi condivisi, arredo urbano, segnaletica), `LayoutRecipe`.
- **Parametri (`LayoutRecipe`):** Organic e Grid sono preset; "custom" è una stringa `chiave=valore; …` letta da `LayoutRecipeParseSubService`, che produce la forma canonica e gli errori `problems` con `CITY_INVALID_RECIPE`. Chiavi: `base`, `shape`, `regularity`, `blocks`, `streets`, `avenues`, `plazas`, `heights`, `lamps`, `seed`.
- **Golden test:** `CityFy.CityGenerator.Tests/Golden/*.json` contiene l'output **congelato** del vecchio layout TypeScript, ormai cancellato. I preset devono continuare a riprodurlo; per cambiare l'algoritmo bisogna aggiornare le fixture di proposito e dirlo all'utente.

## Gateway: API pubblica `api/v1`

- `auth/login`, `auth/callback`, `auth/logout`, `me`, `me/data` (cancellazione).
- `cities` (lista paginata, POST 202), `cities/{id}` (GET, DELETE 204), `cities/{id}/cancel`, `cities/recipe/schema`.
- `city/genres?from&to`, `city/rebuild` (aggiorna il catalogo).
- `uploads`, `spotify/sync`, `pipeline/runs`.
- Admin: `api/admin/*`.

Il gateway inoltra con `DownstreamClient` (in `ServiceDefault/Clients`), che conserva codice, status ed estensioni dei ProblemDetails.

## Errori

- **Codici:** in `ServiceDefault/Errors/ErrorCodes.cs`.
- **Vincolo dei test:** `ServiceDefault.Tests` **fallisce** se un codice manca in `ERRORI_UI.md` o in `cityfy-web/src/app/core/errors/error-messages.ts`. Ogni codice nuovo va aggiunto in tutti e tre i posti.
- **Estensioni:** `CityFyException.Extensions` finisce nel ProblemDetails (es. `problems`).

## Comandi e verifica

- **Backend:** `dotnet build CityFy/CityFy.slnx` (0 warning) e `dotnet test CityFy/CityFy.slnx`. L'output è in italiano: cercare `Avvisi`, `Errori`, `totale`, `non riuscito`.
- **Frontend:** da `cityfy-web/`, `npx ng build` e `npx ng test --watch=false`.
- **App:** si avvia con l'AppHost (`CityFy.AppHost`), ma **non va avviata**: le prove manuali le fa l'utente.
- **Ambiente:** `git` non è nel PATH; la shell è Windows PowerShell 5.1.

## Vincoli esterni (verificati il 2026-11-12)

- **Spotify Web API in modalità sviluppo:** al massimo 5 utenti autorizzati e sviluppatore Premium; gli altri ricevono 403 dopo il login. Nessun webhook. La quota estesa è riservata ad app affermate. Il login Spotify attuale quindi non scala.
- **Last.fm:** l'utente collega Spotify a Last.fm dalle impostazioni di Last.fm, e Spotify invia gli ascolti lato server da ogni dispositivo. `user.getRecentTracks` dà anche il brano in corso (`nowplaying`). Nessun push verso terzi.
- **ListenBrainz:** API aperta compatibile Last.fm, ascolti con MBID, endpoint `playing-now`. Il suo import da Spotify può restare indietro di ore.

## Piani

- **`PIANO_GENERAZIONE_CITTA.md`:** completato (fasi 0–6). Rimasto aperto: arrotondamento e compressione della risposta del dettaglio città (circa 280 KB per una città grande).
- **`PIANO_LASTFM.md`:** approvato, **non iniziato**. Prevede:
  - un servizio `Retrieve.LastFm` con lettura a cadenza adattiva e import dello storico;
  - deduplica tra fonti;
  - identità CityFy con "Accedi con Last.fm";
  - brano in corso nel browser via SSE;
  - ritiro del retrieve Spotify.
