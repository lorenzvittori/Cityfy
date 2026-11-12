---
name: dotnet-conventions
description: Convenzioni di codice .NET valide in ogni progetto — lingua, file, namespace, interfacce, classi sealed/primary constructor, naming, DI, Options, errori con codici stabili, logica pura, test. Usala prima di creare, rinominare o spostare classi, interfacce, DTO, Options o registrazioni DI.
---

# Convenzioni .NET (tutti i progetti)

Regole generali. Per l'architettura carica anche `onion-architecture` (singola applicazione a layer) oppure `aspnet-microservices` (più servizi ASP.NET/Aspire).

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
- Ogni Service, Repository e Client ha un'interfaccia ed è registrato tramite essa. Fanno eccezione le classi di **logica pura**.
- Costanti con nome per numeri e stringhe significative (`private const int MaxPages = 20;`), mai magic number nel codice.

## Naming

| Cosa | Interfaccia / classe | Cartella |
|---|---|---|
| Caso d'uso / orchestrazione | `I<Feature>Service` / `<Feature>Service` | `Services` |
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

## Logica pura

- Algoritmi e regole senza I/O (calcoli, trasformazioni, policy, parsing) vanno in classi dedicate, deterministiche, che ricevono tutto come parametri (incluso `now`), senza interfaccia: `static` se non hanno stato, altrimenti `sealed` con le sole Options nel costruttore.
- Il Service raccoglie i dati via repository/client, chiama la logica pura e salva il risultato.

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
