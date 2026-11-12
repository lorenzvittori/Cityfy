---
name: dotnet-conventions
description: Convenzioni di codice .NET valide in ogni progetto — gestione dei casi fuori standard (chiedere e poi aggiornare le skill), lingua, file, namespace, interfacce, classi sealed/primary constructor, naming, DI, Options, errori con codici stabili, logica pura, test. Usala prima di creare, rinominare o spostare classi, interfacce, DTO, Options o registrazioni DI.
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

## Errori

- Errori attesi → eccezione applicativa con **codice stabile**: `<App>Exception(code, message, statusCode)`, con i codici in una classe statica `ErrorCodes` (costanti `UPPER_SNAKE_CASE`).
- Un codice non cambia mai significato: una nuova situazione richiede un nuovo codice.
- `message` è tecnico (inglese, per log e sviluppatori); il client mostra il testo in base al codice.
- Factory statiche per gli errori ricorrenti di un'area (`SpotifyAccountException.NotLinked(userId)`).
- Dove un'eccezione tecnica va tradotta in codice (job, consumer), usa un metodo `internal static string ToErrorCode(Exception ex)` con `switch` sui tipi, e testalo.
- Dati corrotti in input a un parser → `InvalidDataException`.

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
- **Determinismo**: stesso input + stesso seed = stesso output. Niente `Random` senza seed e niente dipendenza dall'ordine di dizionari o hash randomizzati.

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

## Anti-pattern

- Interfaccia dichiarata nello stesso file della classe.
- `new HttpClient()` o client HTTP singleton costruiti a mano.
- Testi per l'utente costruiti lato server o parsing del messaggio tecnico lato client.
- `DateTime.Now` (usa `DateTime.UtcNow` e passa `now` alla logica pura).
- Logica di dominio in controller, consumer o `Program.cs`.
- Nuove classi `static` di utilità che contengono logica (usa modelli con metodi o un SubService).
