---
name: onion-architecture
description: Regole di layer (Onion) e ruoli Service/SubService/Client/ViewModel per applicazioni .NET strutturate in progetti Domain/Application/Infrastructure/UI. Usala prima di creare, spostare o rinominare classi, interfacce, DTO o registrazioni DI in un progetto a layer.
---

# Onion Architecture

Carica anche `dotnet-conventions` (naming generale, classi, errori, test). Per soluzioni a microservizi usa `aspnet-microservices`.

## Layer e dipendenze

Le dipendenze puntano sempre verso l'interno: `UI → Application → Domain` e `Infrastructure → Application`.

| Progetto | Contiene | Non contiene |
|---|---|---|
| `<App>.Domain` | record ed enum di dominio (`Models/`), costanti | logica, I/O, riferimenti ad altri progetti |
| `<App>.Application` | contratti (`Abstractions/Services`, `Abstractions/SubServices`, `Abstractions/Clients`), DTO (`Models/`), `Services/`, `SubServices/`, helper `internal` (`Internal/`) | accesso a file, rete o database; UI |
| `<App>.Infrastructure` | adapter I/O che implementano `Abstractions/Clients` | regole di business |
| `<App>.UI` / `<App>.Api` | ViewModel e View, oppure controller; composition root | parsing, logica di dominio, code-behind con logica |

- Le librerie esterne che fanno logica (parser, serializer) sono ammesse in Application, ma i loro tipi restano `internal`/incapsulati: la UI riceve solo DTO di Application.
- Le librerie non thread-safe si usano dietro un unico lock condiviso (`<Lib>Gate.Lock`).

## Regola d'oro: Service e SubService

- **Service**: orchestra un caso d'uso. Valida l'input, chiama i SubService nell'ordine giusto, gestisce gli errori. Non contiene logica algoritmica.
- **SubService**: un singolo blocco di logica, deterministico e testabile. Non orchestra altri SubService.
- Anche quando un Service chiama un solo SubService, il Service esiste comunque: la UI parla solo con i Service.

## Naming specifico dei layer

| Cosa | Interfaccia / classe | Cartella |
|---|---|---|
| Caso d'uso | `I<Feature><Action>Service` / `<Feature><Action>Service` | `Abstractions/Services`, `Services` |
| Logica | `I<Feature><Action>SubService` / `<Feature><Action>SubService` | `Abstractions/SubServices`, `SubServices` |
| I/O esterno | `I<Name>Client` / `<Name>Client` | `Abstractions/Clients` (Application), `Clients` (Infrastructure) |
| ViewModel | `<Feature>ViewModel`, una sezione = `<Nome>SectionViewModel` | `UI/ViewModels`, `UI/ViewModels/Sections` |
| View | una UserControl per sezione: `<Nome>View.xaml` | `UI/Views/Sections`; controlli condivisi in `UI/Views/Controls` |

- Metodo principale: `Execute` (sincrono) o `ExecuteAsync` (asincrono).
- Namespace: `<App>.<Layer>.<Cartella>`.

## Checklist per una nuova classe

1. Interfaccia in `Abstractions/...` (file a sé) e implementazione `sealed` con primary constructor.
2. Registrazione nella composition root.
3. Almeno un test nel progetto di test, con fake scritti a mano.

## Anti-pattern

- Service con logica algoritmica: spostala in un SubService.
- SubService che chiama altri SubService in sequenza: in realtà è un Service.
- Client con regole di business.
- ViewModel o controller che leggono stream o conoscono formati di file.
- Valori codificati dentro id stringa (`"money.manual.10000"`): usa proprietà o DTO tipizzati.
- Decidere se aggiornare la UI guardando un flag cumulativo ("ci sono modifiche non salvate"): aggiorna dopo ogni operazione riuscita.

## Preferenze UI dell'utente

- Una View dedicata per ogni sezione (frame o schermate distinte).
- La configurazione manuale dei valori è sempre disponibile, non solo i preset da lista.
