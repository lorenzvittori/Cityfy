---
name: dotnet-conventions
description: Convenzioni di codice .NET valide in ogni progetto — gestione dei casi fuori standard (chiedere e poi aggiornare le skill), lingua, file, namespace, interfacce, classi sealed/primary constructor, naming, DI, Options, errori con codici stabili (anche per campo), logica pura, modelli con metodi e delegate, LINQ senza logica e oggetti creati con factory, parametri da stringa, porting numerico da JS, persistenza Mongo, test. Usala prima di creare, rinominare o spostare classi, interfacce, DTO, Options o registrazioni DI, e prima di scrivere query LINQ.
---

# Convenzioni .NET (tutti i progetti)

Regole generali. Per l'architettura carica anche `onion-architecture` (singola applicazione a layer) oppure `aspnet-microservices` (più servizi ASP.NET/Aspire).

## Comportamenti fuori standard

Le skill sono lo standard. Se un caso non è coperto da nessuna skill, o richiede di derogare a una regola (nuovo pattern, nuova cartella o suffisso, eccezione a un layer, libreria nuova, convenzione diversa):

1. **Fermati e chiedi** all'utente prima di implementarlo: spiega il caso, la regola coinvolta e proponi un'opzione consigliata.
2. **Se l'utente approva**, aggiungi la regola alla skill generica pertinente (`dotnet-conventions`, `aspnet-microservices`, `onion-architecture`, `feature-workflow`). Scrivila in forma portabile, senza nomi di progetto, servizi o domini.
3. Se l'utente approva un'eccezione valida solo una volta, documentala nel codice con un commento e non nella skill.

Non introdurre in silenzio pattern nuovi e non lasciare in sospeso deroghe già approvate.

## Lingua

- **Tutto in inglese**: identificatori, commenti, log, messaggi di eccezione, testi restituiti dalle API, testi di progresso.
- La traduzione per l'utente la fa il frontend/UI a partire da un **codice di errore stabile**, mai dal testo tecnico.

## File e namespace

- Namespace **file-scoped** (`namespace X.Y;`), uguale a `<Progetto>.<Cartella>`.
- Una classe pubblica per file; **ogni interfaccia in un file a sé** `I<Nome>.cs`, nella stessa cartella dell'implementazione (o in `Abstractions/` nei progetti a layer).
- DTO/record piccoli e strettamente correlati possono stare in un unico file (`<Area>Models.cs`); i nomi dei file seguono il tipo (`SpotifyDtos.cs`, non `SpotifyDTOS.cs`).

## Classi

- Nuove classi `sealed` salvo ereditarietà prevista (basi astratte esplicite: `<X>Base`, `MessageConsumer<T>`, `DownstreamClient`).
- Dipendenze tramite **primary constructor**; campi `private readonly` solo se servono trasformazioni (es. `options.Value`, `CreateProtector(...)`).
- Ogni Service, SubService, Repository e Client ha un'interfaccia ed è registrato tramite essa.
- Costanti con nome per numeri e stringhe significative (`private const int MaxPages = 20;`), mai magic number nel codice.

## Naming

| Cosa | Interfaccia / classe | Cartella |
|---|---|---|
| Caso d'uso / flusso | `I<Feature>Service` / `<Feature>Service` | `Services` |
| Logica (con o senza I/O) | `I<Feature><Action>SubService` / `<Feature><Action>SubService` | `SubServices` |
| Accesso a dati | `I<Entity>Repository` / `<Tecnologia><Entity>Repository` (es. `MongoSpotifyRepository`) | `Repositories` |
| Sistema esterno (HTTP, file, broker) | `I<Name>Client` / `<Name>Client`, base comune `<Name>ClientBase` | `Clients` |
| Conversione tra modelli | `I<Name>Mapper` / `<Name>Mapper` (metodi `To<Target>` / `MapTo<Target>`) | `Mappers` |
| Configurazione | `<Area>Options` con `public const string SectionName` | `Options` |
| Lavoro in background | `<Name>HostedService`, `<Name>Scheduler` | `HostedServices` |
| Consumer di messaggi | `<Message>Consumer` | `Consumers` |
| DTO esterni | `<Name>Dto`; richieste/risposte `<UseCase>Request` / `<UseCase>Result` | `Models` |

- Suffisso `Async` solo e sempre sui metodi asincroni; ogni metodo asincrono pubblico accetta un `CancellationToken ct` (anche nelle action dei controller).

## Dependency Injection

- Composition root unica (`Program.cs` o equivalente); raggruppa le registrazioni comuni in extension method `Add<Area>(...)`.
- Lifetime:
  - `Singleton`: classi senza stato (mapper, repository basati su un client thread-safe, code in memoria, registry);
  - `Scoped`: servizi che dipendono da typed HttpClient, DbContext o altri scoped;
  - typed HttpClient via `AddHttpClient<IClient, Client>()` (mai `new HttpClient()`).
- Un singleton o hosted service che ha bisogno di uno scoped crea uno scope con `IServiceScopeFactory`.

## Options

```csharp
builder.Services.AddOptions<XOptions>()
    .BindConfiguration(XOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

- Validazione con DataAnnotations (`[Required]`, `[Range]`, `[Url]`): una configurazione sbagliata ferma l'avvio.
- Segreti mai in `appsettings`: user-secrets in sviluppo, variabili d'ambiente / parametri altrove.
- Un nome che collide con un tipo del framework già importato (es. `SessionOptions` di ASP.NET Core) si qualifica con l'area: `AnonymousSessionOptions`.
- `TimeSpan` nelle Options si valida con `[Range(typeof(TimeSpan), "00:05:00", "30.00:00:00")]`.

## Errori

- Errori attesi → eccezione applicativa con **codice stabile**: `<App>Exception(code, message, statusCode)`, con i codici in una classe statica `ErrorCodes` (costanti `UPPER_SNAKE_CASE`).
- Un codice non cambia mai significato: una nuova situazione richiede un nuovo codice.
- `message` è tecnico (inglese, per log e sviluppatori); il client mostra il testo in base al codice.
- Factory statiche per gli errori ricorrenti di un'area (`SpotifyAccountException.NotLinked(userId)`).
- Dove un'eccezione tecnica va tradotta in codice (job, consumer), usa un metodo `internal static string ToErrorCode(Exception ex)` con `switch` sui tipi, e testalo.
- Dati corrotti in input a un parser → `InvalidDataException`.
- Errori di più campi insieme (form, stringhe di parametri): un solo codice, più un'estensione con l'elenco `problems: [{ key, code }]` (codici stabili per campo). L'eccezione applicativa porta le estensioni (`Extensions`), che il gestore globale copia nel ProblemDetails.
- Testi per l'utente (titoli predefiniti, etichette di stato) non si costruiscono nel backend: il backend restituisce i dati (es. `name: null` + generi principali) e la UI compone il testo.

## Service e SubService

- **Service**: dà il flusso. Ogni metodo è una lista di chiamate a SubService, con al più `if`/`foreach`/`try` sul flusso, log e costruzione del risultato. Non usa mai direttamente repository, client, cache, publisher o librerie di I/O, nemmeno per una lettura banale.
- **SubService**: contiene la logica operativa effettiva di un blocco coeso (algoritmi, validazioni, mapping, policy, paginazione, retry applicativi) e l'I/O che gli serve tramite repository/client. Non chiama altri SubService: se deve farlo, quel flusso va nel Service.
- Anche la logica pura (calcoli, regole, mapping di errori) è un SubService con interfaccia, registrato in DI; riceve `now` come parametro per restare deterministico.
- Controller, consumer e hosted service chiamano solo Service.

```csharp
// Service: il flusso si legge dall'alto in basso
public async Task<CityModel> BuildAsync(string userId, string period, CancellationToken ct)
{
    var p = periods.Normalize(period);
    var plays = await listening.AggregateAsync(userId, periods.GetStart(p, now), ct);
    var artists = await artistResolve.ResolveAsync(plays, ct);
    var city = cityBuild.Build(userId, p, plays, artists, await genres.GetHierarchyAsync(ct), now);
    await cityStore.SaveAsync(city, ct);
    return city;
}
```

## Logica algoritmica e parametri

- **Calcoli senza stato esterno** (geometria, grafi, matematica): modelli immutabili (`record`) con i loro metodi, in `Models/<Area>/` (`polygon.Inset(d)`, `hierarchy.GetRoots(id)`). Non si creano nuove classi helper statiche con logica.
- **Parti intercambiabili di un algoritmo**: `delegate` con nome, in un file `Strategies.cs` accanto ai modelli. Un SubService sceglie quale delegate usare in base ai parametri. Niente gerarchie di classi o visitor quando bastano delle funzioni.
- **Parametri configurabili dall'utente**:
  - un `record` di parametri, con i preset come `static readonly` (`Recipe.Default`);
  - un SubService che li legge da una stringa `chiave=valore; chiave=valore`:
    - le chiavi assenti prendono il valore della base;
    - chiavi sconosciute, duplicate o fuori range sono errori, mai ignorati;
    - restituisce una **forma canonica** (tutte le chiavi, in ordine fisso, con valori normalizzati), usata per salvare e per l'hash di deduplica;
  - gli errori sono riportati per chiave (`problems: [{ key, code }]`);
  - lo schema (chiavi, range, default) è esposto dal backend, così la UI non ripete le costanti;
  - la UI offre sia la stringa sia un form, sincronizzati tra loro, e all'API invia sempre la stringa.
- **Algoritmi portati da un altro linguaggio o modulo**: prima di rimuovere l'originale, scrivi dei golden test che confrontano l'output della nuova implementazione con quello dell'originale su input di esempio.
- **Determinismo**: stesso input + stesso seed = stesso output. Niente `Random` senza seed e niente dipendenza dall'ordine di dizionari o hash randomizzati (`string.GetHashCode` cambia a ogni processo: per un seed usa FNV-1a o simili).

### Porting numerico da JavaScript/TypeScript

Per ottenere gli stessi numeri (golden test con tolleranza relativa 1e-6):
- **Ordinamenti**: `Array.sort` è stabile, `List.Sort` no. Usa `OrderBy`/`ThenBy`.
- **Somme**: da sinistra a destra con un ciclo, come `reduce`. L'ordine delle somme in virgola mobile cambia il risultato.
- **`Math.hypot`**: il browser scala e usa la somma di Kahan. Riproducilo invece di `Math.Sqrt(x*x+y*y)`.
- **`Math.round`**: arrotonda le metà verso +∞, quindi `Math.Floor(x + 0.5)`, non `Math.Round` (banker's).
- **`Math.imul` e `>>>`**: aritmetica `uint` in `unchecked`.
- **`Map`**: mantiene l'ordine di inserimento. Tieni una lista delle chiavi accanto al `Dictionary`.
- **`toFixed`**: formattazione invariante (`"F1"`, `CultureInfo.InvariantCulture`).
- **`x || 1`**: zero e NaN diventano 1.
- **Seno, coseno, atan2**: differiscono all'ultimo bit tra runtime. Ogni **pareggio** (distanze uguali, lati uguali, massimi uguali) va rotto con una tolleranza (`> best + 1e-9`, distanza arrotondata a 1e-6) **in entrambe le implementazioni**, poi si rigenerano i golden.

## LINQ e creazione degli oggetti

Regola dell'utente, valida per tutto il codice .NET di produzione, nuovo o toccato. Sono esclusi i progetti di test (fake e test) e il frontend.

- **Niente logica dentro le lambda di LINQ.** Una lambda contiene al massimo:
  - l'accesso a un membro (`x => x.Score`, `x => x.Plays.Count`);
  - **una sola chiamata** a un metodo o a una factory (`b => NewBuilding(b, color, max)`).
  
  Meglio ancora un **method group** (`.Select(ArtistTotals.From)`, `.OrderByDescending(ScoreOf)`).
- **Vietati nelle lambda:**
  - `new`, inizializzatori di oggetto, tipi anonimi;
  - ternari, `??` con logica, interpolazioni con calcoli, operazioni aritmetiche e confronti;
  - lambda con blocco `{ }`;
  - LINQ annidate dentro una lambda.
- **Le condizioni hanno un nome:** un predicato diventa un metodo o una proprietà (`.Where(IsPlaced)`, `artist.IsPlaced`); se serve più di una condizione, meglio un `foreach` con `if`/`continue`.
- **Gli oggetti si creano con le factory:**
  - un metodo statico `From`/`Create` sul tipo, quando il tipo ha i dati per farlo e non è un contratto condiviso;
  - altrimenti un metodo factory privato della classe che li costruisce (`NewDistrict`, `NewBuilding`) oppure un Mapper;
  - `new` e gli inizializzatori stanno solo dentro le factory, o in una costruzione diretta fuori da LINQ.
- **Tipi intermedi:** al posto di un tipo anonimo, un `private sealed record` con la sua factory (`ArtistTotals.From(group)`).
- **I selettori ricorrenti** (chiave, ordinamento, somma) diventano piccoli metodi statici con un nome (`ScoreOf`, `QualifiedOf`), raccolti in fondo alla classe, così le catene LINQ si leggono come frasi.
- **Quando una lambda non basta**, un `foreach`/`for` con `if`/`continue` è sempre ammesso: un filtro con confronti, una somma in ordine fisso, una costruzione con `new`.
- **Eccezioni:**
  - **query tradotte dal database**: filtri e update di MongoDB (`Find(x => ...)`, `Builders<T>.Filter.Where`, `Update.Set(x => x.Campo, ...)`) e le query EF (`Where`/`Select`/`OrderBy` prima di `ToListAsync`). Sono espressioni che il driver traduce in query, non codice che gira in memoria, e una proiezione EF verso un record o un tipo anonimo evita di caricare colonne inutili. Dopo la materializzazione valgono di nuovo le regole;
  - **delegate di strategia** restituiti da un metodo (es. una famiglia di tagli `s => new HalfPlane(...)`): non sono LINQ. Se il delegate è un campo statico, usa un method group (`Template = Run;`).

```csharp
// No
var buildings = placements.GroupBy(x => x.Artist.Mbid!).Select(g => new { Qualified = g.Sum(x => x.Plays.QualifiedPlays), ... });
// Sì
var buildings = placements.GroupBy(ArtistMbid).Select(ArtistTotals.From).ToList();
var districts = buildings.GroupBy(DistrictGenreIdOf).Select(d => NewDistrict(d, maxQualified)).OrderByDescending(ScoreOf).ToList();
```

## Persistenza (MongoDB)

- I documenti salvati hanno `[BsonIgnoreExtraElements]` quando un campo potrebbe sparire: togliere una proprietà non deve rompere la lettura dei documenti vecchi.
- I modelli annidati possono essere `record` immutabili (mappati dal costruttore). Evita gli `struct` nei documenti.
- Un test di andata e ritorno in memoria (`ToBson()` → `BsonSerializer.Deserialize`) per i documenti nuovi e complessi.
- Per sostituire un insieme di dati senza mostrarlo a metà: scrivi la versione nuova con un id di versione, sposta il puntatore alla versione corrente, poi cancella le vecchie.
- Le collection dismesse vanno comunque ripulite nella cancellazione dei dati utente, finché esistono.
- Indici creati una volta sola al primo uso (`Lazy<Task>`).

## Commenti

- Commenti e `<summary>` spiegano il **perché** (vincoli esterni, casi limite, scelte non ovvie), non il cosa.
- Le interfacce documentano le eccezioni/codici che un metodo può lanciare.

## Test

- xUnit; un progetto `<Progetto>.Tests` per ogni progetto con logica.
- **Niente librerie di mocking**: fake scritti a mano (es. `StubHttpHandler` per HttpClient, implementazioni in memoria delle interfacce).
- Integrazione con dipendenze reali tramite Testcontainers (database, broker); API con `WebApplicationFactory`.
- Membri `internal` testati con `<InternalsVisibleTo Include="<Progetto>.Tests" />`.
- Nome test: `<Scenario>_<RisultatoAtteso>` (es. `TooManyRequests_IsRetriedAfterRetryAfter`).
- Ogni bug corretto ha un test di regressione.
- **Test di un rifiuto** (cookie, token, permesso): costruisci anche un **controllo positivo** con lo stesso meccanismo, che deve passare. Altrimenti il test può passare per il motivo sbagliato (es. un cookie che non si decifra dà 401 comunque). Un cookie di autenticazione si fabbrica con `IOptionsMonitor<CookieAuthenticationOptions>.Get(scheme).TicketDataFormat.Protect(ticket)`.
- **`WebApplicationFactory` senza infrastruttura:**
  - repository in memoria al posto del database;
  - un publisher che registra i messaggi al posto del broker;
  - i consumer (hosted service) tolti dalla collection dei servizi;
  - una connection string fittizia e gli health check della libreria client disattivati (Aspire: `Aspire:<Integrazione>:Client:DisableHealthChecks`);
  - i limiti di richieste alti di default, abbassati solo nei test che li verificano.
- **Indirizzo del client nei test:** il test server non ne ha uno. Uno `IStartupFilter` che imposta loopback, cioè un proxy considerato attendibile, permette di scegliere la rete con `X-Forwarded-For`.
- **Stream (SSE) nei test:** leggi la risposta con `HttpCompletionOption.ResponseHeadersRead` e uno `StreamReader`, riga per riga (`event:` / `data:`).
- **Date nei test:** quelle fisse devono stare nel passato rispetto a oggi, se il codice confronta con `DateTime.UtcNow` (es. "è già il momento di leggere").

## Anti-pattern

- Interfaccia dichiarata nello stesso file della classe.
- `new HttpClient()` o client HTTP singleton costruiti a mano.
- Testi per l'utente costruiti lato server o parsing del messaggio tecnico lato client.
- `DateTime.Now` (usa `DateTime.UtcNow` e passa `now` alla logica pura).
- Logica di dominio in controller, consumer o `Program.cs`.
- Nuove classi `static` di utilità che contengono logica (usa modelli con metodi o un SubService).
- Logica, `new`, inizializzatori o tipi anonimi dentro le lambda di LINQ (vedi "LINQ e creazione degli oggetti").
