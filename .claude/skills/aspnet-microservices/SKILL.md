---
name: aspnet-microservices
description: Regole per soluzioni a più servizi ASP.NET Core (Aspire, Mongo/SQL, RabbitMQ) — struttura di un servizio, libreria condivisa ServiceDefaults, errori ProblemDetails con codice, endpoint pubblici/interni/debug, client verso altri servizi, messaggistica, job in background annullabili. Usala prima di aggiungere un servizio, un endpoint, un messaggio, un consumer o un job.
---

# Microservizi ASP.NET Core

Carica anche `dotnet-conventions` (naming, classi, errori, test).

## Struttura della solution

- `<App>.AppHost`: orchestrazione Aspire (risorse, riferimenti, parametri e segreti).
- Una **libreria condivisa** (`ServiceDefault`/`<App>.ServiceDefaults`) con:
  - bootstrap comune `AddServiceDefaults()` / `UseServiceDefaults()`: logging, OpenTelemetry, health `/health` e `/alive`, service discovery, ProblemDetails, enum JSON come stringhe;
  - `Errors/` (`<App>Exception`, `ErrorCodes`, handler);
  - `Contracts/` (messaggi);
  - `Messaging/` (publisher, base dei consumer, deduplicazione);
  - modelli condivisi tra servizi (es. eventi scritti da uno e letti da un altro, stato dei task).
- Un progetto per servizio, `<App>.<Area>[.<Sorgente>]`, più il suo `<...>.Tests`.
- Un unico servizio **gateway** (`<App>.Api`) è esposto al browser: autenticazione, API pubbliche versionate, inoltro agli altri servizi.
- I servizi prendono il nome dalla **responsabilità**, non dall'operazione tecnica (`Catalog`, `Billing`, `ReportGenerator`; non `Processing` o `Elaboration`).

## Confini tra servizi

- Ogni collection o tabella nuova ha **un solo servizio proprietario**, l'unico che la scrive. Gli altri servizi ottengono quei dati tramite il suo client HTTP o tramite eventi, mai leggendo la collection. L'unica eccezione sono i modelli dichiarati condivisi nella libreria comune.
- Un servizio nuovo si crea quando una responsabilità ha **dati propri** e un **contratto di input piccolo e stabile**, oppure quando ha un carico molto diverso (calcolo CPU contro query) che va scalato a parte. Esempio: il servizio dati produce un modello, il servizio di calcolo lo trasforma e salva il risultato.
- Le dipendenze vanno in una sola direzione: chi calcola chiama il servizio dati, mai il contrario. Per avvisare a monte si usano gli eventi.
- La cancellazione dei dati utente coinvolge ogni servizio proprietario (vedi "Dati utente").

## Struttura di un servizio

Cartelle: `Controllers/`, `Services/`, `Repositories/`, `Clients/`, `Mappers/`, `Models/`, `Options/`, `HostedServices/`, `Consumers/`.

`Program.cs` in quest'ordine, con un commento di una riga per blocco che spiega il perché:
1. `builder.AddServiceDefaults()`;
2. controller, Swagger, CORS;
3. Options (`ValidateOnStart`);
4. infrastruttura (database, cache, data protection);
5. client HTTP;
6. repository e servizi;
7. hosted service;
8. messaggistica e consumer;
9. `app.UseServiceDefaults()`, autenticazione/autorizzazione, `MapControllers()`.

## Livelli dentro un servizio

`Controller / Consumer / HostedService → Service → SubService → Repository | Client`

- **Service = flusso.** Un metodo di un Service è una lista di chiamate a SubService (più eventuali `if`/`foreach`/`try` sul flusso, log e mapping del risultato). Non usa mai repository, client, `IMongoDatabase`, `DbContext`, cache o publisher direttamente, nemmeno per una lettura semplice.
- **SubService = logica effettiva.** Un blocco operativo coeso: algoritmi, validazioni, mapping, e l'I/O che gli serve tramite repository/client. Non chiama altri SubService (se deve farlo, è un Service). Nome `I<Feature><Action>SubService`, cartella `SubServices/`.
- Anche la logica pura (calcoli, policy, mapping di errori) è un SubService con interfaccia, registrato in DI.
- Controller, consumer e hosted service parlano solo con i Service; un consumer fa una chiamata al Service per messaggio.
- Eccezione: nel **gateway** i client verso i servizi interni (`I<Servizio>Client`) fanno da layer di servizio per le chiamate passanti; i controller li ricevono tramite interfaccia. Serve un Service solo quando c'è un flusso su più chiamate (es. `IUserDataEraser`).
- I controller mappano verso DTO tipizzati (`record`), non oggetti anonimi.

## Endpoint

| Tipo | Controller | Route | Swagger |
|---|---|---|---|
| Pubblico (solo gateway) | `<Area>Controller`, `[Authorize]` | `api/v1/...` | `GroupName = "v1"` |
| Tra servizi | `Internal<Area>Controller` | `internal/...` | `GroupName = "Internal"` |
| API del servizio | `<Area>Controller` | `api/<area>/...` | `GroupName = "<Servizio>"` |
| Debug / admin di sviluppo | `Debug<Area>Controller` | `debug/...` | senza `GroupName` (documento "debug") |

- Gli endpoint `debug/` non vengono chiamati dal flusso di produzione: se il gateway ne ha bisogno, l'endpoint diventa `internal/`.
- I DTO pubblici sono separati dai modelli interni e mappati in un mapper dedicato del gateway (`V1Mapper`).
  - Eccezione: un contratto di **output puro e stabile** (es. il risultato geometrico di un calcolo, senza id interni, stati o dettagli tecnici) definito nella libreria condivisa può essere esposto dal v1 così com'è, invece di duplicarne i record.
- I job asincroni rispondono `202 Accepted` con l'id e la location dello stato (`AcceptedAtAction`).
- Paginazione e filtri via query string (`?limit=`, `?page=&pageSize=`), con `Math.Clamp` sui limiti.
- **Risorse create e salvate dall'utente** (es. risultati generati):
  - lista paginata `GET ...?page=&pageSize=`, che risponde `{ items, page, pageSize, total }`;
  - dettaglio `GET .../{id}`;
  - cancellazione singola `DELETE .../{id}`, che risponde 204;
  - accesso solo alle proprie risorse: una risorsa di un altro utente risponde 404, non 403;
  - un eventuale limite per utente risponde 409 `<AREA>_LIMIT_REACHED`.
- **Deduplica**: una richiesta con parametri in forma canonica identici a un risultato già pronto, sugli stessi dati di origine (versione), restituisce quel risultato invece di rifare il calcolo.

## Errori

- Ogni risposta d'errore è un ProblemDetails (RFC 9457) con le estensioni `code` (valore di `ErrorCodes`) e `traceId`.
- Nei controller: `return this.<App>Problem(code, detail, status)`; nei servizi: `throw new <App>Exception(code, message, status)`. L'handler globale converte le eccezioni; il messaggio di un errore 500 non previsto si vede solo in Development.
- Il catalogo dei codici è condiviso con il frontend (documento dedicato, es. `ERRORI_UI.md`).

## Client

- **Verso altri servizi**:
  - typed HttpClient con `BaseAddress = https+http://<risorsa-aspire>` e `AddStandardResilienceHandler()`;
  - base `DownstreamClient` **nella libreria condivisa** (la usano il gateway e i servizi che chiamano altri servizi): trasforma un ProblemDetails a valle in `<App>Exception` con lo stesso codice, status ed estensioni (es. `problems`), e un servizio irraggiungibile in `SERVICE_UNAVAILABLE` (503);
  - i client leggono il JSON con le opzioni Web di default, che **non** convertono gli enum scritti come stringhe: nei contratti tra servizi gli stati sono `string` (il nome dell'enum), oppure il client registra il converter;
  - ID nell'URL con `Uri.EscapeDataString`.
- **Verso API esterne**: retry gestito nel client (429 rispettando `Retry-After`, 5xx con backoff, attesa massima limitata). Al fallimento definitivo lancia `HttpRequestException` con lo `StatusCode`: un risultato parziale non deve mai sembrare completo.
- Upload e download lunghi: niente resilience handler standard (timeout totale di 30 s), timeout esplicito.

## Messaggistica (RabbitMQ)

- I contratti sono `sealed record` in `Contracts/`, al passato per gli eventi (`ListeningImported`) e all'imperativo + `Requested` per i comandi (`BuildCityRequested`). Il routing key è il nome del tipo: **rinominarlo è una breaking change**, e il nome deve essere unico e specifico in tutta la solution (`UserProfileUpdated` e `ProductCatalogUpdated`, mai due significati per lo stesso nome). In testa al file, un commento con il diagramma del flusso.
- Consumer: `sealed class <Message>Consumer : MessageConsumer<T>`.
  - `QueueName = "<servizio>.<azione-kebab>"`, una coda per consumer.
  - La base si occupa di deduplicazione, scope DI, ack manuale, retry con code di ritardo e DLQ.
  - `OnFinalFailureAsync` pubblica l'evento di fallimento (`<X>Failed` con `ErrorCode`).
  - Le eccezioni con status 4xx non si ritentano.
- Ogni messaggio porta `RunId`/`JobId` come correlation id.
- Gli handler sono idempotenti: un messaggio può arrivare più volte.
- **Eventi di fine run e di dato cambiato.** Lo stesso evento può fare da tutti e due (es. `<Data>Updated` con `RunId` nullable: chiude la run di chi l'ha chiesta e avvisa chi usa quel dato). Un esito "niente da fare" (es. utente senza dati) chiude comunque la run, con un codice esplicito.
- **Rinominare un messaggio** crea code nuove: le vecchie restano sul broker, vuote. Segnalalo all'utente come pulizia da fare al deploy, insieme ai container rinominati (`docker compose up -d --remove-orphans`).

## Job in background

- Il controller (o lo scheduler) **accoda**; un `BackgroundService` esegue.
- Lo stato è persistito in un documento task: `Pending → InProgress → Done | Failed | Cancelled` (enum PascalCase, salvato come stringa), con `ErrorCode`, `Message` tecnico, `ProgressPercent` (null = indeterminato), `ProgressDetail` e `UpdatedAt`.
- Annullamento con un `CancellationRegistry` (token per job):
  - prima si scrive `CANCELLED`, poi si annulla il token;
  - un job già `CANCELLED` non viene mai sovrascritto da un DONE/FAILED tardivo (filtro sull'update);
  - gli update di stato usano `CancellationToken.None`, così vengono salvati anche quando il job è annullato.
- Distingui l'annullamento dell'admin (`cts` del job) dallo shutdown (`stoppingToken`): solo il secondo diventa FAILED e viene rilanciato.
- Scheduler: `PeriodicTimer` con intervallo da Options (0 = disabilitato); un giro fallito viene loggato e non ferma il servizio.
- All'avvio, un servizio di recovery riaccoda i task rimasti `Pending`.

## Dati utente

- Ogni servizio che possiede dati di un utente espone `DELETE internal/.../users/{userId}/data`, restituisce i conteggi (`UserDataDeletion`) ed è idempotente.
- Il gateway orchestra la cancellazione su tutti i servizi.
- Prima di salvare un risultato derivato, verifica che i dati di origine esistano ancora (cancellazione arrivata mentre un job era in coda).
- Un job che sta girando quando l'utente cancella i suoi dati non deve ricreare nulla: l'update finale filtra sullo stato atteso (es. `InProgress`), quindi un documento cancellato o annullato non viene riscritto.
- **Passaggio di proprietario** (es. da una sessione anonima a un account): stessa forma della cancellazione, `POST internal/.../users/{fromUserId}/transfer` con body `{ toUserId }`, idempotente, orchestrato dal gateway. Si spostano solo i **dati di origine**; i derivati del vecchio proprietario si cancellano e si ricalcolano per il nuovo con l'evento della pipeline.
- **Proprietari temporanei** (sessioni con scadenza): la verità sulla scadenza è un documento nel gateway, non il cookie. Alla scadenza un job del gateway chiama la cancellazione su tutti i servizi e ritenta finché non è completa. Un indice TTL non basta: toglierebbe solo il documento della sessione.
- **Dati da fonti esterne con licenza** (es. Last.fm): ogni dato salvato porta la sua sorgente, così si può cancellare tutto ciò che viene da una fonte se la licenza termina.

## Dati derivati e risultati salvati

- **Dato precalcolato con versione.** Ciò che costa calcolare (es. arricchimento da un'API esterna) si calcola nella pipeline, si salva con una `Version` e si espone con un endpoint leggero che restituisce solo la versione corrente.
- **Calcoli su richiesta** per i parametri liberi (date, filtri): sul dato precalcolato, senza salvare nulla.
- **Risultati salvati dall'utente:** sono fotografie. Salvano i parametri canonici, l'hash dei parametri e la versione del dato con cui sono stati calcolati; non cambiano quando il dato si aggiorna.
- **Calcolo pesante** in un servizio separato dal servizio dati (vedi "Confini tra servizi"), con un job asincrono: `Pending` alla richiesta, poi l'evento `<X>Requested` consumato dal servizio stesso, recupero all'avvio dei job rimasti a metà.

## Sicurezza

- Data Protection con un key ring persistente (`DataProtection:KeysPath`) per cookie e token salvati.
- Scope OAuth minimi; token di refresh cifrati; access token solo in cache in memoria, con un margine prima della scadenza.
