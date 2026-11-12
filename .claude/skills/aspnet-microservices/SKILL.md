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
- I job asincroni rispondono `202 Accepted` con l'id e la location dello stato (`AcceptedAtAction`).
- Paginazione e filtri via query string (`?limit=`, `?page=&pageSize=`), con `Math.Clamp` sui limiti.

## Errori

- Ogni risposta d'errore è un ProblemDetails (RFC 9457) con le estensioni `code` (valore di `ErrorCodes`) e `traceId`.
- Nei controller: `return this.<App>Problem(code, detail, status)`; nei servizi: `throw new <App>Exception(code, message, status)`. L'handler globale converte le eccezioni; il messaggio di un errore 500 non previsto si vede solo in Development.
- Il catalogo dei codici è condiviso con il frontend (documento dedicato, es. `ERRORI_UI.md`).

## Client

- **Verso altri servizi**:
  - typed HttpClient con `BaseAddress = https+http://<risorsa-aspire>` e `AddStandardResilienceHandler()`;
  - base `DownstreamClient` che trasforma un ProblemDetails a valle in `<App>Exception` con lo stesso codice e status, e un servizio irraggiungibile in `SERVICE_UNAVAILABLE` (503);
  - ID nell'URL con `Uri.EscapeDataString`.
- **Verso API esterne**: retry gestito nel client (429 rispettando `Retry-After`, 5xx con backoff, attesa massima limitata). Al fallimento definitivo lancia `HttpRequestException` con lo `StatusCode`: un risultato parziale non deve mai sembrare completo.
- Upload e download lunghi: niente resilience handler standard (timeout totale di 30 s), timeout esplicito.

## Messaggistica (RabbitMQ)

- I contratti sono `sealed record` in `Contracts/`, al passato per gli eventi (`ListeningImported`) e all'imperativo + `Requested` per i comandi (`BuildCityRequested`). Il routing key è il nome del tipo: **rinominarlo è una breaking change**. In testa al file, un commento con il diagramma del flusso.
- Consumer: `sealed class <Message>Consumer : MessageConsumer<T>`.
  - `QueueName = "<servizio>.<azione-kebab>"`, una coda per consumer.
  - La base si occupa di deduplicazione, scope DI, ack manuale, retry con code di ritardo e DLQ.
  - `OnFinalFailureAsync` pubblica l'evento di fallimento (`<X>Failed` con `ErrorCode`).
  - Le eccezioni con status 4xx non si ritentano.
- Ogni messaggio porta `RunId`/`JobId` come correlation id.
- Gli handler sono idempotenti: un messaggio può arrivare più volte.

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

## Sicurezza

- Data Protection con un key ring persistente (`DataProtection:KeysPath`) per cookie e token salvati.
- Scope OAuth minimi; token di refresh cifrati; access token solo in cache in memoria, con un margine prima della scadenza.
