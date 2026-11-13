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
| **Api** (`CityFy.Api`) | Gateway, l'unico esposto al browser: sessione (cookie `cityfy.auth`), sessioni anonime con scadenza e pulizia dei dati (`ExpiredSessionCleanupScheduler`), account con login Last.fm (`AccountService`) e passaggio dei dati da una sessione anonima, API `api/v1`, pannello admin (utente fittizio, solo Development), cancellazione dati su tutti i servizi, health check | `anonymous_sessions`, `users` |
| **Catalog** (ex Elaboration) | Aggrega gli ascolti, tiene il **catalogo artisti** dell'utente (artista → quartiere, genere, colore), costruisce su richiesta il `CityModel` di un intervallo con filtro generi/sottogeneri | `user_catalogs`, `user_artist_placements`; legge `listening_events` (modello condiviso) |
| **CityGenerator** | Città salvate: richiesta e job di generazione, layout della mappa (geometria in C#), parametri della mappa custom, limite di 10 città, riuso per hash | `generated_cities` |
| **Orchestrator** | Debounce degli import, run della pipeline (aggiornamento del catalogo) | `pipeline_runs` |
| **RetrieveLastFm** | Collegamento account Last.fm (token → chiave di sessione cifrata), import dello storico (job ripristinabile, cursore `HistoryImport` sull'account), lettura degli ascolti nuovi con cadenza adattiva (`LastFmPollingHostedService`), brano in corso in memoria, limitatore unico delle richieste (`LastFmRequestGate`) | `lastfm_accounts`, `lastfm_import_tasks` |
| **RetrieveDumpSpotify** | Upload dell'export zip di Spotify, import dello storico | task di upload, file su disco |
| **RetrieveMusicBrainz** | Import del dump MusicBrainz in SQL Server, API di lettura: resolve degli artisti, grafo dei generi | DB SQL `musicbrainz` |

Collection condivisa: `listening_events`, scritta dalle sorgenti (Dump, cioè l'export di Spotify, e Last.fm) e letta da Catalog.
- Gli ascolti Last.fm hanno `TrackUri` = `lastfm:{artista}|{brano}` in minuscolo, e `PlayedAt` = `StartedAt` = ora dello scrobble. Per l'export `PlayedAt` = `ts` (la fine) e `StartedAt` = `ts - ms_played`.
- **Deduplica tra fonti** (`ListeningEventWriter`, usato da `MongoListeningEventStore` e `MongoListeningEventTransfer`):
  - stesso id → upsert;
  - altrimenti si unisce a un ascolto di **un'altra** fonte con la stessa `MatchKey` (`ListeningEvent.BuildMatchKey`) e un inizio entro `MatchTolerance` (2 min);
  - il primo arrivato resta, il secondo aggiunge sorgente e campi.
- **Risoluzione degli artisti in Catalog:** id Spotify (`ArtistUri`, oggi non lo scrive nessuna sorgente), poi `ArtistMbid` (Last.fm), poi nome. `api/artists/resolve` di RetrieveMusicBrainz accetta `mbids` e risponde `byMbid`. Collection dismessa: `city_models` (città per periodo); Catalog la ripulisce ancora nella cancellazione dati.

## Flusso dei dati

```
Dump.Spotify / LastFm ──► listening_events ──► ListeningImported
Orchestrator  ListeningImported ──(debounce 30 s)──► RefreshCatalogRequested
Catalog       ricalcolo catalogo (resolve MusicBrainz + gerarchia) ──► UserCatalogUpdated | CatalogRefreshFailed
Orchestrator  UserCatalogUpdated | CatalogRefreshFailed ──► run Done | Failed
CityGenerator UserCatalogUpdated ──► prima città di un utente senza città
MusicBrainz   import finito ──► CatalogUpdated ──► Orchestrator aggiorna i cataloghi di tutti
Utente        POST api/v1/cities ──► CityGenerator (Pending) ──► GenerateCityRequested (a se stesso)
              ──► POST api/catalog/city-model/{userId} ──► layout ──► generated city Done
```

- **Brano in corso** (`Contracts/LiveMessages.cs`):
  - Retrieve.LastFm pubblica `NowPlayingChanged` quando il polling vede un brano diverso, o nessuno;
  - il gateway lo consuma (`api.now-playing-changed`) e lo manda in SSE su `api/v1/live`;
  - una pagina aperta pubblica `UserActive` ogni minuto (`retrieve-lastfm.user-active`), e l'utente viene letto ogni 15 s per 150 s.
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

- `session` (POST, "Inizia senza account"), `auth/lastfm/login`, `auth/lastfm/callback`, `auth/logout`, `me` (`kind` anonymous | lastfm, `expiresAt`, `lastFm`), `me/data` (cancellazione), `lastfm/imports/{id}`.
- `live`: SSE solo per gli account, con gli eventi `now-playing` (`NowPlayingDto`) e `ping`; al massimo 5 pagine per utente (`LiveHubSubService`).
- `cities` (lista paginata, POST 202), `cities/{id}` (GET, DELETE 204), `cities/{id}/cancel`, `cities/recipe/schema`.
- `city/genres?from&to`, `city/rebuild` (aggiorna il catalogo).
- `uploads`, `pipeline/runs`.
- Admin: `api/admin/*`.

Il gateway inoltra con `DownstreamClient` (in `ServiceDefault/Clients`), che conserva codice, status ed estensioni dei ProblemDetails.

## Errori

- **Codici:** in `ServiceDefault/Errors/ErrorCodes.cs`.
- **Vincolo dei test:** `ServiceDefault.Tests` **fallisce** se un codice manca in `ERRORI_UI.md` o in `cityfy-web/src/app/core/errors/error-messages.ts`. Ogni codice nuovo va aggiunto in tutti e tre i posti.
- **Estensioni:** `CityFyException.Extensions` finisce nel ProblemDetails (es. `problems`).

## Comandi e verifica

- **Backend:** `dotnet build CityFy/CityFy.slnx` (0 warning). I test si lanciano **dalla cartella `CityFy/`** (lì c'è il `global.json` che attiva Microsoft.Testing.Platform) con `dotnet test --solution CityFy.slnx`; dalla radice `dotnet test` fallisce con "Testing with VSTest target is no longer supported". L'output è in italiano: cercare `Avvisi`, `Errori`, `totale`, `non riuscito`.
- **Test del gateway:** non aprono Mongo né RabbitMQ (`ApiFactory.Published` registra i messaggi pubblicati, il consumer viene tolto). `ApiFactory` sostituisce i servizi interni con `FakeServices` e `anonymous_sessions` con `InMemoryAnonymousSessionRepository`; `StartAnonymousAsync()` apre una sessione anonima vera.
- **Frontend:** da `cityfy-web/`, `npx ng build` e `npx ng test --watch=false`.
- **App:** si avvia con l'AppHost (`CityFy.AppHost`), ma **non va avviata**: le prove manuali le fa l'utente.
- **Ambiente:** `git` non è nel PATH; la shell è Windows PowerShell 5.1.
- **Configurazione locale di Last.fm:**
  - applicazione "CityFy" su last.fm/api/accounts, creata il 2026-11-13 (la prima applicazione, senza secret salvato, va disattivata);
  - API key e shared secret negli user-secrets dell'AppHost (`Parameters:lastfm-api-key`, `Parameters:lastfm-shared-secret`), mai nel repository;
  - callback `http://127.0.0.1:4200/api/v1/auth/lastfm/callback`; CityFy la manda comunque a ogni autorizzazione (`cb`);
  - in produzione: segreti GitHub `LASTFM_API_KEY` e `LASTFM_SHARED_SECRET`.

## Vincoli esterni (verificati il 2026-11-12)

- **Spotify Web API in modalità sviluppo:** al massimo 5 utenti autorizzati e sviluppatore Premium, nessun webhook: per questo il login e il retrieve Spotify sono stati tolti (fase 6 di `PIANO_LASTFM.md`). Da Spotify arriva solo l'export zip.
- **Last.fm:** l'utente collega Spotify a Last.fm dalle impostazioni di Last.fm, e Spotify invia gli ascolti lato server da ogni dispositivo. `user.getRecentTracks` dà anche il brano in corso (`nowplaying`). Nessun push verso terzi.
- **Termini Last.fm (verificati il 2026-11-13, dettagli in `PIANO_LASTFM.md`):**
  - solo uso **non commerciale**; per un uso commerciale serve un accordo;
  - le pagine pubbliche che usano Last.fm vanno **approvate per iscritto** da Last.fm prima di aprirle al pubblico;
  - attribuzione con il pulsante "powered by AudioScrobbler" che porta a Last.fm, e link al profilo Last.fm dell'utente; niente logo Last.fm né pubblicità senza consenso;
  - immagini e artwork di Last.fm esclusi: non si usano;
  - limite di richieste non dichiarato, per IP (errore 29); alla fine della licenza vanno cancellati tutti i dati Last.fm.

## Identità e sessioni (da `PIANO_LASTFM.md`)

- Il `userId` che i servizi interni ricevono è **opaco**: `anon_{sessionId}` per una sessione anonima, `u_{24 hex}` per un account. I servizi non distinguono i due casi.
- Sessione anonima: 1 giorno, si rinnova a ogni visita; alla scadenza il gateway cancella i dati su tutti i servizi.
- Account: l'account Last.fm è l'identità (`internal/lastfm/link` restituisce il proprietario esistente, se c'è). Cookie da 30 giorni, rinnovato al massimo una volta al giorno; rifiutato se l'utente non è più in `users`.
- Passaggio da anonimo ad account (`AccountService`, `UserDataTransferSubService`):
  - `POST internal/users/{from}/transfer` su RetrieveDumpSpotify (upload finiti; `complete: false` finché un import gira) e Catalog (ascolti, con `IListeningEventTransfer`);
  - poi DELETE dei derivati della sessione su Orchestrator e CityGenerator e trigger della pipeline per l'account;
  - la sessione resta `Transferring` (mai cancellata dal cleanup) finché il passaggio non è completo; lo scheduler ritenta.
- `anonymous_sessions` (e poi `users`) stanno nel **gateway** (Api), nel database condiviso `cityfy`.
- **Limiti di richieste** (`RateLimiting/RateLimitSetup.cs`, sezione `RateLimits`):
  - per rete del client: `POST session`, `POST uploads`, login Last.fm, tutto `api/v1`;
  - per utente: upload al giorno;
  - per tutto il server: upload in corso.
  
  In produzione l'IP arriva da Caddy (`Proxy:KnownNetworks`). I test usano `ApiFactory.RateLimits` (alti di default) e `X-Forwarded-For` per scegliere la rete.
- Il cookie di una sessione anonima porta solo l'id (claim `cityfy:session`): `CityFyCookieEvents` controlla il documento a ogni richiesta, risponde `SESSION_EXPIRED` se non è più valida e riemette il cookie quando la rinnova. Lo sliding automatico del cookie è spento.
- Gli ascolti in `listening_events` portano le sorgenti (`Sources`, valori in `ListeningSources`: `dump`, `lastfm`).
- L'`_id` di un ascolto contiene lo `userId` (`ListeningEvent.BuildId`): il passaggio di proprietario non può fare un semplice update, deve reinserire con l'id nuovo e cancellare il vecchio.
- **ListenBrainz:** API aperta compatibile Last.fm, ascolti con MBID, endpoint `playing-now`. Il suo import da Spotify può restare indietro di ore.

## Piani

- **`PIANO_GENERAZIONE_CITTA.md`:** completato (fasi 0–6). Rimasto aperto: arrotondamento e compressione della risposta del dettaglio città (circa 280 KB per una città grande).
- **`PIANO_LASTFM.md`:** rivisto e confermato il 2026-11-13, **completato** (fasi 0–6). Ha portato:
  - nessun login obbligatorio: sessione anonima (`sessionId`) da 1 giorno che si rinnova a ogni visita, con i dati cancellati su tutti i servizi alla scadenza;
  - "Accedi con Last.fm" come unico login, con un account persistente; i dati della sessione anonima passano all'account;
  - nessuna migrazione degli utenti di oggi: i database si ricreano;
  - un servizio `Retrieve.LastFm` con lettura a cadenza adattiva e import dello storico;
  - deduplica tra Last.fm ed export;
  - brano in corso nel browser via SSE;
  - rimozione del login Spotify e del retrieve Spotify (il cookie di un login Spotify vecchio viene rifiutato).
  Fatti anche il comando admin "cancella tutti i dati Last.fm" (`POST api/admin/lastfm/purge` con la frase di conferma; cancella tutti gli account con i loro dati) e l'avviso `LASTFM_SPOTIFY_NOT_SCROBBLING` (`me.lastFm.warning`). Rimasta aperta: l'approvazione scritta di Last.fm prima dell'apertura al pubblico.
