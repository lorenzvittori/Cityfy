# Cityfy: piano di lavoro della solution .NET

Il piano è diviso in cinque fasi. Ogni fase indica obiettivo, attività e criterio di completamento.
Riguarda solo la solution .NET (`CityFy/CityFy.slnx`) e il nuovo frontend Angular.

| Fase | Contenuto | Dipende da |
|---|---|---|
| **F1** | Refactor: correggere ciò che non funziona e completare i pezzi mancanti | — |
| **F2** | Orchestratore della pipeline | F1, F4 (broker) |
| **F3** | Layer API (City API) | F1 |
| **F4** | Mongo, SQL Server (e broker) nell'AppHost | — |
| **F5** | Frontend Angular | F3 |

> **Nota sull'ordine.** F4 è piccola e non dipende da nulla, mentre F2 e F3 ne hanno bisogno: il broker e le connection string iniettate dall'AppHost. Conviene farla subito dopo F1, o in parallelo.

---

## F1: Refactor

**Obiettivo:** con le chiamate manuali agli endpoint, partire dal caricamento di un export Spotify e arrivare a un documento `city_model` in Mongo.

> **Stato (11/11/2026):** F1 è scritta e la solution compila. Ci sono 50 test: 47 superati, 3 di integrazione Mongo saltati perché Docker non era attivo. Il giro completo in esecuzione non è ancora stato provato.
>
> **Differenze rispetto al piano:**
> - **F1.4:** l'import di MusicBrainz è stato riscritto. Il vecchio parser leggeva le colonne sbagliate (i nomi dei generi erano GUID, le relazioni tra generi zero) e non scaricava il dump *derived*, che contiene i generi degli artisti. La migration `RebuildForColumnBasedImport` ricrea le tabelle: dopo l'aggiornamento bisogna rilanciare `POST api/retrieve`.
> - **F1.4:** al posto di `api/musicbrainz/graphs` ci sono `GET api/genres/graph` e `GET api/genres/{id}/roots`, oltre a `POST api/artists/resolve`.
> - **F1.5:** il vecchio flusso TagGraph di Elaboration è stato rimosso, perché chiamava un endpoint inesistente. Il city builder salva solo metriche in `city_models`: la disposizione nello spazio resta al frontend, come nel prototipo `city_generator`.
> - **F1.5:** le collection `artist_aggregates` e `genre_aggregates` non sono state create: gli aggregati stanno dentro `city_models`.

### F1.1 Base comune (`ServiceDefault`)
- Trasformare `ServiceDefault` in un vero progetto *ServiceDefaults* di Aspire, con un'estensione `AddServiceDefaults()` / `MapDefaultEndpoints()` che includa:
  - endpoint `/health` e `/alive`. Oggi mancano in `Retrieve.MusicBrainz` ed `Elaboration`, quindi l'AppHost li vede non sani;
  - OpenTelemetry (log, metriche, tracce);
  - resilienza standard degli `HttpClient`;
  - configurazione condivisa di CORS e Swagger, oggi copiata uguale in ogni `Program.cs`.
- Spostare in `ServiceDefault/Contracts` i modelli condivisi tra servizi: evento di ascolto, task di elaborazione e, in F2, gli eventi.

### F1.2 Modello unico degli ascolti
- Definire `ListeningEvent` nella collection Mongo `listening_events`, con i campi `userId`, `playedAt`, `msPlayed`, `trackUri`, `trackName`, `artistName`, `artistUri`, `albumName`, `source` (dump/api), `sourceRef` (uploadId/jobId).
- Usare come chiave `userId + playedAt + trackUri`, così il dato è idempotente e i duplicati tra dump e API vengono eliminati.
- **Retrieve.Dump.Spotify:**
  - associare l'upload a un utente. Oggi esiste solo `_uploadId` e gli ascolti di utenti diversi si mescolano;
  - scrivere in `listening_events`.
- **Retrieve.Spotify:** scrivere in `listening_events` invece che in `spotify_play_history`.
- Scrivere uno script di migrazione per i dati già importati.

### F1.3 Retrieve.Spotify
- Provare a mano il giro completo: login, callback, start e stato del job.
- **OAuth:**
  - verificare lo `state`;
  - esporre gli endpoint `debug/*` solo in Development;
  - ridurre gli scope a `user-read-recently-played` e `user-read-private`.
- **Account utente:**
  - collection `spotify_accounts` con il refresh token cifrato tramite Data Protection;
  - refresh automatico dell'access token.
- **Lettura incrementale** con il cursore `after`, partendo dall'ultimo `playedAt` salvato.
- **Configurazione:** `SpotifyOptions` tipizzate con `ValidateOnStart`.
- **Pulizia:**
  - correggere il refuso nei namespace (`Rtrieve` → `Retrieve`);
  - eliminare `CityFy/Backup/`;
  - unire `Mapping/` e `Mappers/`;
  - togliere AutoMapper 12.0.1, che ha una vulnerabilità nota (GHSA-rvv3-g6hj-g44x), e sostituirlo con una mappatura manuale o con Mapperly.

### F1.4 Retrieve.MusicBrainz: API di lettura
- Verificare se `ArtistParser` importa i link URL degli artisti (relazioni artista → URL con `open.spotify.com/artist/...`). Se non lo fa, aggiungerli. Sono la chiave affidabile per collegare Spotify a MusicBrainz.
- Aggiungere `POST /api/artists/resolve`:
  - riceve una lista di Spotify artist ID o di nomi;
  - restituisce MBID, generi e tipo di abbinamento (`spotify-url` / `exact-name` / `none`).
- Aggiungere `GET /api/musicbrainz/graphs`, il grafo dei generi con le relazioni "sottogenere di", "influenzato da" e "fusione di". È l'endpoint che `Elaboration` chiama già ma che oggi non esiste. Il contratto va allineato con `TagGraph`.
- Aggiungere `GET /api/genres/{id}/macro`, che risale il grafo fino al macro-genere.
- Aggiungere gli indici SQL necessari al lookup per nome e per URL.

### F1.5 Elaboration
- **Correzioni:**
  - eliminare il doppio costruttore di `ElaborationService`;
  - il servizio è registrato come singleton ma usa un `HttpClient` tipizzato, che non va trattenuto per tutta la vita dell'app: renderlo scoped.
- **Aggregazione:** da `listening_events` più l'abbinamento artisti, calcolare per ogni utente e periodo i totali per artista, per genere e per macro-genere (`artist_aggregates` / `genre_aggregates`).
- **City builder:** a partire dagli aggregati, generare quartieri, blocchi ed edifici con posizione, altezza, colore e densità, e salvarli in `city_model`.
- **Configurazione dell'algoritmo:** palette per macro-genere, scale e periodo.

### F1.6 Test
- Un progetto di test per ciascun servizio, con xUnit.
- Test con un `HttpMessageHandler` finto per i client esterni (Spotify, dump di MusicBrainz).
- Test di integrazione con Testcontainers per Mongo e SQL Server.

**F1 è completata quando:** si carica il dump, si chiama a mano resolve, poi aggregazione, poi city builder, e in `city_model` c'è una città coerente con gli ascolti.

---

## F2: Orchestratore

**Obiettivo:** dal caricamento (o dal retrieve programmato) alla città aggiornata, senza nessuna chiamata manuale.

> **Stato (11/11/2026):** F2 è scritta e compila senza avvisi. Ci sono 57 test: 51 superati, 6 di integrazione (Mongo e RabbitMQ) saltati perché Docker non era attivo. Non è ancora stata provata in esecuzione.
>
> **Differenze rispetto al piano:**
> - **Passaggi del flusso:** sono due, non quattro. Elaboration fa già abbinamento degli artisti, aggregazione e costruzione della città in un solo comando, quindi il flusso è `ListeningImported → BuildCityRequested → CityBuilt | CityBuildFailed`. MusicBrainz resta un'API sincrona, chiamata da Elaboration.
> - **Retrieve programmato:** lo scheduler vive in Retrieve.Spotify (`SpotifyRetrieveScheduler`) e non nell'orchestratore, perché è lì che stanno gli account collegati.
> - **Retry:** gli intervalli sono fissi per ogni livello (5 s, 30 s, 2 min), con una coda di ritardo per livello. Una coda unica con TTL per messaggio bloccherebbe i messaggi in attesa dietro quello in testa.
> - **Tracciamento distribuito:** lo propaga RabbitMQ.Client 7 da solo (header `traceparent`), quindi non c'è codice dedicato.

### Architettura
- **Nuovo servizio `CityFy.Orchestrator`:** gestisce lo stato della pipeline per ogni esecuzione (`pipeline_runs`). Invia comandi ai servizi e reagisce ai loro eventi.
- **Broker:** RabbitMQ gestito da Aspire (vedi F4).
- **Libreria di messaggistica:** `RabbitMQ.Client` usato direttamente (decisione presa), tramite l'integrazione client di Aspire `Aspire.RabbitMQ.Client`, che gestisce connessione, health check e tracciamento. Sopra va costruito un piccolo wrapper in `ServiceDefault/Messaging`:
  - `IMessagePublisher.PublishAsync<T>(T message)`: serializzazione JSON, `MessageId` e `CorrelationId` (= id dell'esecuzione della pipeline), messaggi persistenti, publisher confirms;
  - `MessageConsumer<T>`, una classe base `BackgroundService` per i consumer, con ack manuale, `prefetch` configurabile e propagazione del contesto OpenTelemetry tramite gli header;
  - topologia dichiarata all'avvio: un exchange `topic` `cityfy.events`, una coda durevole per ogni consumer, una dead-letter exchange `cityfy.dlx` con la coda `<nome>.dlq`;
  - retry con backoff tramite code di ritardo con TTL che rimandano il messaggio alla coda originale; dopo N tentativi il messaggio finisce nella DLQ;
  - deduplica lato consumer sul `MessageId`, salvando i messaggi già elaborati in Mongo con un indice TTL.
- **Contratti** dei messaggi in `ServiceDefault/Contracts`.

### Flusso
```
UploadCompleted / SpotifyRetrieveCompleted
  → ResolveArtists  (MusicBrainz)       → ArtistsResolved
  → Aggregate       (Elaboration)        → AggregationCompleted
  → BuildCity       (Elaboration)        → CityBuilt
  → pipeline_run = DONE
```

### Attività
- Realizzare il wrapper `ServiceDefault/Messaging` descritto sopra, con test di integrazione su RabbitMQ in Testcontainers (pubblicazione, consumo, retry, DLQ).
- Ogni consumer deve essere idempotente: se riceve due volte lo stesso messaggio, il risultato non cambia.
- Stato `FAILED` con il motivo quando un messaggio finisce nella DLQ.
- Sostituire il polling ogni minuto di `UploadProcessingHostedService` (Dump.Spotify) con l'evento `UploadCompleted`.
- Scheduler del retrieve Spotify per ogni account collegato, per esempio ogni 30–60 minuti. Al termine pubblica `SpotifyRetrieveCompleted`.
- Gestire le richieste ravvicinate: se arrivano più eventi per lo stesso utente in poco tempo, eseguire una sola elaborazione della città.
- Endpoint `GET /api/pipeline/runs/{id}` e `GET /api/pipeline/runs?userId=`, per mostrare l'avanzamento nel frontend.
- Tracciamento distribuito con OpenTelemetry su tutta la catena, visibile nella dashboard di Aspire.

**F2 è completata quando:** il caricamento di uno zip produce una città aggiornata senza interventi manuali, e un errore in un passaggio si vede nello stato della pipeline e nella dashboard.

---

## F3: Layer API

**Obiettivo:** un unico punto di accesso per il frontend.

> **Stato (11/11/2026):** F3 è scritta e compila senza avvisi. Ci sono 98 test in totale: 91 superati, 7 di integrazione saltati perché Docker non era attivo. Dei 98, 19 riguardano l'API e girano su un host in memoria con i servizi interni simulati: login, sessione, città, controllo di proprietà, inoltro degli errori. L'API non è ancora stata provata in esecuzione.
>
> **Differenze rispetto al piano:**
> - **Niente YARP.** Ogni inoltro deve aggiungere l'utente della sessione e verificare che la risorsa gli appartenga, cosa che un proxy trasparente non fa. Al suo posto ci sono client HTTP tipizzati.
> - **Login.** La redirect URI di Spotify è ora l'API (`/api/v1/auth/callback`). L'API passa codice e `state` a Retrieve.Spotify (`internal/spotify/link`) e apre una sessione con cookie. Così l'API è l'unico servizio esposto.
> - **Cache rimandata.** La cache in uscita, invalidata da `CityBuilt`, non è stata fatta: ogni richiesta della città costa una lettura su Mongo in Elaboration, quindi va aggiunta solo se servirà.
> - **Correzioni collaterali:**
>   - gli enum ora vengono serializzati come testo in tutti i servizi;
>   - l'upload di Dump.Spotify accetta file fino a 200 MB (prima Kestrel si fermava a 30 MB);
>   - la risorsa `RetriveDumpSpotify` è stata rinominata in `RetrieveDumpSpotify`.

### Nuovo servizio `CityFy.Api`
- **Endpoint di lettura** (da `city_model` e dagli aggregati; nessun calcolo a ogni richiesta):
  - `GET /api/city?period=30d|6m|1y|all`
  - `GET /api/genres`, `GET /api/genres/{genre}`
  - `GET /api/districts`, `GET /api/districts/{id}`
  - `GET /api/artists/{artistId}`
- **Inoltro verso i servizi interni** con YARP, così il frontend parla con un solo host:
  - caricamento dello zip → Dump.Spotify;
  - login e callback Spotify → Retrieve.Spotify;
  - stato della pipeline → Orchestrator.
- **Autenticazione utente:** come minimo una sessione legata allo Spotify user id dopo il login OAuth. Ogni endpoint filtra i dati sull'utente corrente.
- **Contratto:**
  - DTO pubblici versionati (`/api/v1`), separati dai documenti Mongo;
  - OpenAPI pubblicato e usato per generare il client TypeScript per Angular.
  - Errori nel formato già in uso in tutti i servizi: ProblemDetails con `code` stabile e `traceId`. Il catalogo dei codici per la UI è in [ERRORI_UI.md](ERRORI_UI.md). Il gateway deve inoltrare questi errori senza modificarli.
- **Prestazioni:** cache in uscita, invalidata dall'evento `CityBuilt`; paginazione e filtri.

**F3 è completata quando:** il frontend (o Swagger) ottiene la città dell'utente autenticato da un solo host, con filtro per periodo.

---

## F4: Mongo, SQL Server e broker nell'AppHost

**Obiettivo:** `dotnet run` sull'AppHost avvia tutta l'infrastruttura, senza connection string scritte a mano.

> **Stato (11/11/2026):** F4 è scritta, compila senza avvisi e i test passano. Non è ancora stata avviata, perché il daemon Docker non era attivo.
>
> **Differenze rispetto al piano:**
> - Aspire è stato aggiornato alla 13.6.5, che corregge CVE-2026-120006 (elevazione di privilegi in Aspire.Hosting < 13.6.5).
> - Il volume per il dump di MusicBrainz non serve: i servizi girano come processi locali e il dump resta in `%LOCALAPPDATA%\Cityfy\mb_dumps`, o nel percorso indicato da `DumpStorage:RootPath`.
> - Il broker è già dichiarato ma ancora non usato da nessun servizio: verrà collegato in F2.

### Attività
- **AppHost:**
  - `AddMongoDB("mongo").WithDataVolume()` più Mongo Express in sviluppo, e `.AddDatabase("cityfy")`;
  - `AddSqlServer("sql").WithDataVolume()` e `.AddDatabase("musicbrainz")`;
  - `AddRabbitMQ("broker").WithManagementPlugin()`, che serve a F2;
  - ogni progetto collegato con `.WithReference(...)` e `.WaitFor(...)`.
- **Segreti:** passarli come parametri dell'AppHost (`AddParameter(..., secret: true)`), per esempio lo Spotify ClientSecret.
- **Servizi:** usare le integrazioni client di Aspire (`Aspire.MongoDB.Driver`, `Aspire.Microsoft.EntityFrameworkCore.SqlServer`). Eliminare le sezioni `Mongo` e `ConnectionStrings` dagli `appsettings.json`, e con esse password e host scritti a mano.
- **MusicBrainz:** il volume del dump scaricato deve restare tra un avvio e l'altro, per non riscaricarlo.
- **Documentazione:** aggiornare il README con i prerequisiti (Docker) e i comandi di avvio.

**F4 è completata quando:** su una macchina pulita con Docker, un solo comando avvia tutti i servizi e la dashboard di Aspire li mostra sani.

---

## F5: Frontend Angular

**Obiettivo:** visualizzare la città dell'utente partendo dai dati reali.

> **Stato (11/11/2026):** F5 è scritta in `cityfy-web/`.
> - **Build:** riesce; Three.js sta solo nel chunk caricato in modo lazy della pagina città.
> - **Test:** 12 test Vitest superati (layout, errori, shell dell'app).
> - **Dev server:** è stato avviato, serve l'app e il proxy verso l'API risponde.
> - **Da fare:** il giro completo con backend e login Spotify non è ancora stato provato.
>
> **Differenze rispetto al piano:**
> - **Angular 21 invece di 22:** Angular 22 richiede Node ≥ 22.22, sulla macchina c'è la 22.17.
> - **Client API scritto a mano** (`core/api`) e non generato dall'OpenAPI. Generarlo richiede l'API in esecuzione; i tipi seguono i DTO `v1`.
> - **Pagine:** il dettaglio di quartiere, genere e artista è un pannello laterale della pagina città, non una pagina separata. I filtri per genere e artista non sono ancora stati fatti; c'è solo il filtro per periodo.
> - **Login senza ngrok:** il frontend gira su `http://127.0.0.1:4200` e inoltra `/api` all'API. La redirect URI di Spotify punta al frontend, così browser, callback e cookie stanno sulla stessa origine.
> - **Messaggi di errore:** sono in `core/errors/error-messages.ts`. Un test .NET fallisce se un codice del backend non vi compare.

### Attività
- **Nuovo progetto** `cityfy-web/` (Angular, componenti standalone), con la struttura proposta nel README (`core/services`, `features/city`, `features/filters`, `shared`).
- **Integrazione:** registrarlo nell'AppHost con l'integrazione JavaScript/Node di Aspire, con riferimento a `CityFy.Api`.
- **Client API** TypeScript generato dall'OpenAPI di F3.
- **Pagine:**
  1. **Collega Spotify**: login OAuth.
  2. **Carica export**: caricamento dello zip con l'avanzamento della pipeline (F2).
  3. **Città**: scena Three.js, riprendendo la logica di `city_generator/`, con filtri per periodo, genere e artista.
  4. **Dettaglio**: quartiere, genere e artista.
- **Stati dell'interfaccia:** caricamento, città vuota, errore.

**F5 è completata quando:** un nuovo utente collega Spotify, carica l'export e, al termine della pipeline, vede la propria città nel browser.
