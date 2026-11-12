# Piano — Generazione della città nel backend, parametri e città salvate

## Obiettivo

1. Tutta la logica che decide **com'è fatta la città** passa al backend. Oggi è in `cityfy-web/src/app/features/city/layout/`, circa 1.300 righe: forma e posizione dei quartieri, isolati, lotti degli edifici, strade, strisce pedonali, lampioni, semafori, cartelli e colori. Il frontend si limita a **disegnare** ciò che riceve.
2. L'utente genera una città scegliendo:
   - **data inizio** e **data fine**;
   - **tipo di mappa**: `organic`, `grid` oppure `custom`;
   - **generi e sottogeneri** da includere.
3. Ogni città generata **resta salvata**. L'utente la ritrova in una lista paginata, la apre nel dettaglio e può cancellarle una per una.

## Decisioni prese

| # | Decisione |
|---|---|
| D1 | L'import dei dati utente resta in background (pipeline). La **generazione** la avvia l'utente con i suoi parametri: è un job in background che calcola la città e la salva. |
| D2 | Il template **raggiera** si toglie. |
| D3 | Il filtro funziona su generi (quartieri) e sottogeneri (isolati). Gli artisti esclusi **spariscono** dalla città: niente "Other genres" per loro. |
| D4 | Geometria come **modello con metodi** (`Polygon`), con le parti variabili come **delegate**. Il tutto è guidato da una "ricetta" (vedi §3). |
| D5 | Le città generate sono salvate per utente: lista paginata, dettaglio, cancellazione singola. |
| D6 | **Nessun Redis**: solo Mongo. Una richiesta con parametri identici a una città già pronta, sullo stesso catalogo, la riusa. |
| D7 | Massimo **10 città salvate** per utente. All'undicesima si risponde `CITY_LIMIT_REACHED` (409): l'utente deve cancellarne una. |
| D8 | **Nome**: lo sceglie l'utente; se lo lascia vuoto viene generato, per esempio "Rock e Jazz · 2024 · Griglia". |
| D9 | **Custom = stringa di parametri** scritta al volo (§3.1). Non ci sono preset salvati: la stringa si usa per generare e resta solo nel documento della città, per mostrarla e per "duplica e modifica". |
| D10 | Due servizi: **CityFy.Catalog** (Elaboration rinominato) per i dati, **CityFy.CityGenerator** (nuovo) per generazione, layout e città salvate (§0). |

## Situazione attuale

| Cosa | Dove | Note |
|---|---|---|
| Modello città (quartiere = macro-genere, isolato = genere, edificio = artista) | Elaboration › `CityBuildSubService` | Già nel backend |
| Periodi | Orchestrator › `Pipeline:Periods` = `all, 1y, 6m, 30d` | Quattro città precalcolate per utente in `city_models` (id `user:period`) |
| Layout (organica / griglia / raggiera, sotto-isolati, lotti, strade, arredo urbano) | Frontend › `layout/*.ts` | Puro e deterministico: seed = hash degli id dei quartieri |
| Rendering | Frontend › `city-scene.ts` | three.js, tema giorno/notte, variazione di colore per edificio |

## Architettura

### 0. Servizi: Catalog decide *cosa* c'è nella città, CityGenerator *com'è fatta*

| | **CityFy.Catalog** (ex Elaboration, rinominato) | **CityFy.CityGenerator** (nuovo) |
|---|---|---|
| Dati suoi | `listening_events`, `user_artist_placements` | `generated_cities` |
| Compiti | aggrega gli ascolti in `[from, to]`, colloca ogni artista in generi e sottogeneri, filtra, costruisce il `CityModel` (quartieri, isolati, edifici, altezze, colori) | job di generazione, parametri (stringa e schema), geometria e layout, lista paginata, dettaglio, cancellazione, limite di 10 città, riuso delle città già generate |
| Endpoint interni | `POST api/catalog/city-model` (date + generi → `CityModel` + `CatalogVersion`), `GET api/catalog/genres/{userId}?from&to`, cancellazione dati utente | `api/city-generator/cities/*`, `api/city-generator/recipe/schema`, cancellazione dati utente |
| Messaggi | consuma `BuildCityRequested`, che diventa "aggiorna il catalogo"; pubblica `UserCatalogUpdated` | consuma `UserCatalogUpdated` (città iniziale al primo accesso) e `GenerateCityRequested` (la propria coda di job) |

- Il nome dell'evento è `UserCatalogUpdated` perché `CatalogUpdated` esiste già: lo pubblica MusicBrainz quando aggiorna il catalogo dei generi.
- Il job chiede il `CityModel` a Catalog via HTTP con retry: è un client tipizzato `ICatalogClient` dentro CityGenerator. Catalog non conosce CityGenerator.
- Il gateway CityFy.Api inoltra: gli endpoint `cities/*` vanno a CityGenerator, `city/genres` va a Catalog.
- La cancellazione dati utente del gateway chiama entrambi i servizi.

**Rinomina di Elaboration → Catalog.** Cambiano:
- progetti `CityFy.Catalog` e `CityFy.Catalog.Tests`;
- namespace;
- risorsa AppHost `Catalog`;
- rotte interne da `api/elaboration` a `api/catalog`;
- client del gateway `ICatalogClient`;
- elenco dei servizi nel pannello admin e negli health check;
- documentazione.

Si fa come primo passo, a parità di comportamento, così il resto del piano parla già dei nomi nuovi.

### 1. Catalogo artisti dell'utente (CityFy.Catalog, pipeline in background)

La pipeline di oggi (import → run → `BuildCityRequested`) non costruisce più quattro città: aggiorna il **catalogo di collocazione** dell'utente. Per ogni artista ascoltato salva:
- MBID, nome, URI Spotify e generi;
- macro-genere (quartiere) e genere (isolato) scelti, come fa oggi `ChooseMacro`/`ChooseBlock`;
- il colore del quartiere.

Il catalogo va in una nuova collection `user_artist_placements`, con una `CatalogVersion` per utente che cambia a ogni aggiornamento.

- La risoluzione su MusicBrainz, la parte lenta, avviene solo qui.
- `BuildCityRequested` perde `Periods` e diventa "aggiorna il catalogo". `CityBuilt` diventa `UserCatalogUpdated`. L'Orchestrator continua a mostrare le run nel pannello admin.
- Il frontend usa le run per dire "stiamo ancora importando i tuoi dati": il pulsante Genera resta disattivato finché non esiste un catalogo.

### 2. Generazione (CityFy.CityGenerator, job avviato dall'utente)

```
POST   api/v1/cities                 { name?, from, to, layout, recipe?, genres[], subgenres[] }
                                      layout = "organic" | "grid" | "custom"; recipe obbligatoria solo con "custom" (§3.1)
                                      → 202 { cityId, status: Pending }
GET    api/v1/cities?page=1&size=20  → lista paginata (nome, intervallo, tipo, n. generi, n. artisti, stato, createdAt)
GET    api/v1/cities/{cityId}        → dettaglio: parametri, riepilogo, CityLayoutDto (solo se Done)
DELETE api/v1/cities/{cityId}        → 204
POST   api/v1/cities/{cityId}/cancel → annulla un job Pending/InProgress
GET    api/v1/city/genres?from&to    → generi e sottogeneri disponibili nel periodo (con gli ascolti), per il selettore
```

Il gateway inoltra a CityGenerator (`api/city-generator/...`), tranne `city/genres`, che va a Catalog.

**Flusso** (tutto dentro CityGenerator, tranne il passo che chiama Catalog)

1. `POST`: `CityGenerationService` valida i parametri con `CityRequestSubService`. Errori: `CITY_INVALID_RANGE`, `CITY_INVALID_LAYOUT`, `CITY_UNKNOWN_GENRE`, `CITY_INVALID_RECIPE`, `CITY_CATALOG_MISSING`, `CITY_LIMIT_REACHED` (oltre 10 città). Poi:
   - cerca una città già **Done** con lo stesso hash dei parametri e la stessa `CatalogVersion`; se c'è, la restituisce subito;
   - altrimenti crea il documento in `generated_cities` con stato `Pending` e pubblica `GenerateCityRequested`.
2. Il consumer di CityGenerator fa una sola chiamata, `CityGenerationService.GenerateAsync`, che in sequenza:
   - porta lo stato a `InProgress` (`GeneratedCityStoreSubService`);
   - chiede il `CityModel` a Catalog (`CityModelFetchSubService` → `ICatalogClient`). Dentro Catalog, il `CityModelService` aggrega gli ascolti in `[from, to]` (`ListeningAggregationSubService`, a cui va aggiunto `until`), li unisce al catalogo e applica il filtro (`ArtistPlacementSubService`), poi costruisce quartieri, isolati ed edifici (`CityBuildSubService`, con il merge dei quartieri piccoli dopo il filtro);
   - genera la mappa (`CityLayoutSubService`, vedi §3);
   - salva il risultato con stato `Done` (`GeneratedCityStoreSubService`).

   Gli errori portano lo stato a `Failed` con un `errorCode`. Il job è annullabile, come da skill.
3. Stati del job: `Pending, InProgress, Done, Failed, Cancelled`, cioè `ProcessingTask`, già usato negli altri servizi.

**Altre regole**
- **Primo accesso:** a fine della prima pipeline si genera da sola una città "Tutto il periodo · Organica · tutti i generi", così l'utente non trova la pagina vuota.
- **Cancellazione dati utente:** Catalog cancella `user_artist_placements` e CityGenerator cancella `generated_cities`. Il `UserDataDeletionSubService` del gateway chiama entrambi.
- **Città salvate quando il catalogo cambia:** restano come sono, perché sono una fotografia del momento. Il dettaglio mostra "generata con dati del …".

### 3. Layout in CityFy.CityGenerator: modello + delegate + ricetta

**Modello** (`Models/Geometry/`). Record immutabili con il loro comportamento, senza helper statici:
- `Point`;
- `Polygon`: `Area`, `Centroid`, `Inset`, `Intersect`, `ClipHalfPlane`, `ConvexHull`, `SplitByAreas(weights, cut)`;
- `PowerDiagram`: la treemap Voronoi dell'organica.

**Delegate** (`Models/Geometry/Strategies.cs`). Le parti che cambiano da una città all'altra:

```csharp
public delegate IReadOnlyList<Polygon> CityShape(double area, int seed);              // forma della città
public delegate IReadOnlyList<Polygon> DistrictSplit(Polygon city, IReadOnlyList<double> areas, int seed); // quartieri
public delegate IReadOnlyList<Line>    BlockCut(Polygon cell, int pieces);            // come si taglia un isolato
public delegate LotGrid                LotArrangement(Polygon plate, int count, double pitch); // lotti
public delegate IEnumerable<int>       DistrictOrder(IReadOnlyList<CityDistrict> d);  // chi va al centro
```

**Ricetta** (`LayoutRecipe`, la proposta per il "custom"). Al posto di un template scritto a mano per ogni tipo, tutto parte da un unico oggetto di parametri:

```csharp
public sealed record LayoutRecipe(
    CityShapeKind Shape,        // Circle, Square, Elongated
    double Regularity,          // 0 = quartieri irregolari (Voronoi), 1 = griglia
    double BlockSize,           // moltiplicatore 0.5–2
    double StreetWidth,         // 2–6
    bool Plazas,                // piazza e rotonda al centro dei quartieri
    DistrictOrderKind Order,    // MostListenedCenter, ByGenreAffinity, Alphabetical
    HeightScaleKind Heights,    // Log, Linear
    double LampSpacing,         // densità dei lampioni
    int Seed);                  // "rigenera" = stesso layout con un altro seed
```

- `organic` e `grid` diventano **ricette predefinite** (`LayoutRecipes.Organic`, `LayoutRecipes.Grid`). `custom` è una ricetta modificata dall'utente.
- `LayoutStrategySubService` traduce la ricetta nei delegate. Per esempio `Regularity` < 0,5 sceglie lo split Voronoi, altrimenti quello a griglia; `Order` sceglie il `DistrictOrder`.
- `CityLayoutSubService` riceve i delegate ed esegue la pipeline comune, che è il `city-layout.ts` di oggi: quartieri → isolati → sotto-isolati → marciapiedi → lotti → strade → arredo urbano.

**Perché è più semplice di un template per tipo.**
- C'è un solo generatore.
- Il "custom" non è codice nuovo: è la stessa ricetta con altri valori.
- Aggiungere una variante (per esempio una città circolare a griglia) significa aggiungere un valore all'enum e un delegate.

**In una fase successiva** la ricetta può ricevere seed manuali per i quartieri: è la "zonizzazione", dove l'utente trascina i quartieri sulla mappa. Lo split Voronoi li usa già come punti di partenza.

### 3.1 Il "custom": una stringa di parametri

Con `layout = "custom"` l'utente scrive al volo una stringa nel formato `chiave=valore; chiave=valore; …`. La stringa non viene salvata come preset da nessuna parte: si usa per generare e resta solo nel documento della città, per mostrarla e per "duplica e modifica".

**Regole della stringa**
- Le coppie `chiave=valore` sono separate da `;`. Spazi e maiuscole non contano, al massimo 300 caratteri.
- `base=organic|grid` dice da quali valori partire (default `organic`). Le chiavi assenti prendono il valore della base.
- Le chiavi sconosciute, duplicate o con valori fuori range non vengono ignorate in silenzio: rispondono 400 `CITY_INVALID_RECIPE` con l'elenco dei problemi per chiave (`problems: [{ key, code }]`), così il frontend può tradurli.
- Il parser produce una **forma canonica**: tutte le chiavi, in ordine fisso, con i valori normalizzati. È quella che si salva e che entra nell'hash, quindi `regularity=0.70` e `regularity=0.7` danno la stessa città.

**Chiavi**

| Chiave | Valori | `organic` | `grid` | Effetto |
|---|---|---|---|---|
| `shape` | `circle`, `square`, `elongated` | `circle` | `square` | Contorno della città |
| `regularity` | 0 – 1 | 0 | 1 | 0 = quartieri irregolari (Voronoi), 1 = quartieri rettangolari a griglia |
| `blocks` | 0.5 – 2 | 1 | 1 | Dimensione degli isolati (più alto = isolati più grandi, meno strade interne) |
| `streets` | 2 – 6 | 3 | 3 | Larghezza delle strade tra gli isolati |
| `avenues` | 4 – 10 | 6 | 6 | Larghezza delle strade principali tra i quartieri |
| `plazas` | `on`, `off` | `on` | `off` | Piazza con rotonda al centro di ogni quartiere |
| `order` | `center`, `affinity`, `alpha` | `center` | `center` | Il quartiere più ascoltato al centro / generi affini vicini / alfabetico |
| `heights` | `log`, `linear` | `log` | `log` | Come l'altezza cresce con gli ascolti |
| `lamps` | 0 – 2 | 1 | 1 | Densità dei lampioni (0 = nessuno) |
| `seed` | intero | hash dei quartieri | hash dei quartieri | Stessi parametri + altro seed = stessa città con una disposizione diversa |

**Esempi**

```text
base=grid; blocks=0.6; plazas=on
```
Una griglia con isolati piccoli e una piazza in ogni quartiere.

```text
shape=elongated; regularity=0.2; order=affinity; lamps=1.5
```
Una città lunga e irregolare, con i generi affini vicini e strade molto illuminate.

```text
base=grid; shape=circle; heights=linear; seed=7
```
Una griglia dentro un cerchio, con altezze lineari (gli artisti più ascoltati svettano di più) e una disposizione alternativa.

```text
regularity=3; colour=red; plazas=on; plazas=off
```
Viene rifiutata con 400 `CITY_INVALID_RECIPE`:
```json
{ "code": "CITY_INVALID_RECIPE",
  "problems": [ { "key": "regularity", "code": "OUT_OF_RANGE" },
                { "key": "colour",     "code": "UNKNOWN_KEY" },
                { "key": "plazas",     "code": "DUPLICATE_KEY" } ] }
```

**Lato codice**

```csharp
// SubServices/LayoutRecipeParseSubService.cs
public sealed class LayoutRecipeParseSubService : ILayoutRecipeParseSubService
{
    // "organic" / "grid" -> ricetta predefinita; "custom" -> parse della stringa sopra la sua base
    public LayoutRecipe Resolve(string layout, string? recipe) => ...;
}

// Models/Layout/LayoutRecipe.cs
public sealed record LayoutRecipe(CityShapeKind Shape, double Regularity, double Blocks, double Streets,
    double Avenues, bool Plazas, DistrictOrderKind Order, HeightScaleKind Heights, double Lamps, int? Seed)
{
    public static readonly LayoutRecipe Organic = new(CityShapeKind.Circle, 0, 1, 3, 6, true,  DistrictOrderKind.Center, HeightScaleKind.Log, 1, null);
    public static readonly LayoutRecipe Grid    = new(CityShapeKind.Square, 1, 1, 3, 6, false, DistrictOrderKind.Center, HeightScaleKind.Log, 1, null);

    /// <summary>"shape=circle; regularity=0; …": salvata nella città e usata nell'hash dei parametri.</summary>
    public string ToCanonicalString() => ...;
}

// Services/CityGenerationService.cs (solo flusso)
var request = requests.Validate(dto);                        // date, generi, limite di 10 città
var recipe  = recipes.Resolve(dto.Layout, dto.Recipe);       // 400 CITY_INVALID_RECIPE
var hash    = requests.Hash(request, recipe.ToCanonicalString(), catalogVersion);
```

**Frontend**
Scegliendo *Custom* compaiono due modalità, **Form** e **Testo**, sincronizzate tra loro: si passa dall'una all'altra senza perdere nulla.

- **Form** (default), un controllo per ogni chiave:
  - `shape`, `order`, `heights`: select con descrizione;
  - `regularity`, `blocks`, `lamps`: slider con il valore accanto;
  - `streets`, `avenues`: input numerico con min e max;
  - `plazas`: toggle;
  - `seed`: input numerico con il bottone 🎲.

  Accanto a ogni campo c'è una spiegazione breve (la colonna "Effetto" della tabella). I campi non toccati mostrano il valore della base, con il marcatore "default".
- **Testo**: un campo con la stringa, per chi la conosce o la incolla. Riporta l'elenco delle chiavi e l'evidenziazione degli errori per chiave.
- **Sincronizzazione**:
  - modificando il form, la stringa si aggiorna e contiene solo le chiavi diverse dalla base, quindi resta corta e leggibile;
  - scrivendo o incollando una stringa, il form si riempie;
  - una chiave sbagliata viene evidenziata nel testo e il campo corrispondente del form va in errore.
- **Validazione nel frontend** su range, chiavi e duplicati, per un feedback immediato. Il backend resta l'autorità: valida comunque e risponde `CITY_INVALID_RECIPE`. Le regole (chiavi, range, default delle basi) arrivano da `GET api/v1/cities/recipe/schema`, così il form e la validazione non duplicano costanti del backend.
- I bottoni *parti da Organica* e *parti da Griglia* reimpostano la base. *Copia stringa* serve per riusarla in un'altra città.
- All'API va sempre la stringa (`recipe`): il form è solo un aiuto alla compilazione.
- Nel dettaglio di una città custom si vede la stringa canonica; *duplica e modifica* la riporta nel form.

**Verifica del porting (golden test).**
1. Uno script vitest esporta in JSON il layout TS di 3-4 città di esempio (piccola, media, grande, un solo quartiere) per organica e griglia.
2. I test C# della pipeline con le ricette predefinite devono riprodurli, con tolleranza 1e-6.
3. Solo dopo si cancella il TS.

**`CityLayoutDto`** (unità di scena, arrotondato a 2 decimali, risposta compressa) contiene:
- quartieri: contorni, etichetta, colore;
- isolati: cella, sotto-isolati, marciapiede;
- edifici: x, z, rotazione, altezza, larghezza, colore, artista;
- piazze;
- strade: segmenti, strisce, lampioni, semafori, cartelli;
- bounds.

Viene salvato nel documento della città, perché la mappa si calcola una volta sola.

### 4. Frontend

- **`/cities`, lista:** tabella paginata con nome, intervallo, tipo, generi, artisti, stato e data; azioni *apri* e *cancella*, quest'ultima con conferma. Le righe in corso mostrano lo stato e si aggiornano con polling.
- **`/cities/new`, generazione:**
  - due date, con scorciatoie *30 giorni / 6 mesi / 1 anno / Tutto*;
  - tipo di mappa;
  - per *custom*, il form dei parametri oppure la stringa, sincronizzati (§3.1);
  - nome facoltativo: se resta vuoto lo genera il backend;
  - albero dei generi con i sottogeneri (checkbox, "seleziona tutti", ricerca), popolato da `city/genres`;
  - bottone **Genera**, che porta al dettaglio in stato "in costruzione".
- **`/cities/:id`, dettaglio:** la scena three.js disegna il `CityLayoutDto` e ha accanto la legenda e il riepilogo dei parametri, con le azioni *rigenera con altro seed* e *duplica e modifica*.
- `layout/*.ts` e i suoi test spariscono. La scena tiene solo le costanti di disegno e la palette giorno/notte.
- Nuovi codici d'errore in `error-messages.ts` e `ERRORI_UI.md`.

## Fasi

| Fase | Contenuto | Come si verifica |
|---|---|---|
| 0 | Aggiornamento delle skill per le scelte approvate: modello con metodi + delegate per la logica algoritmica, ricette come parametri | — |
| 1a | Rinomina Elaboration → **CityFy.Catalog** (progetti, namespace, risorsa AppHost, rotte `api/catalog`, client del gateway, admin, documentazione), senza cambiare comportamento | Build, tutti i test verdi, nessun riferimento residuo a "Elaboration" |
| 1b | Catalogo artisti in Catalog (`user_artist_placements`, `CatalogVersion`, `UserCatalogUpdated`), endpoint `city-model` e `genres` con date e filtro generi/sottogeneri, cancellazione dati estesa | Test unit sui SubService; il flusso della pipeline resta verde |
| 2 | Nuovo servizio **CityFy.CityGenerator** (progetto, test, AppHost, health, admin), `generated_cities`, job di generazione via `ICatalogClient`, endpoint (CRUD, paginazione, annullamento, limite 10, riuso per hash), inoltro nel gateway. Il layout resta temporaneamente nel frontend, calcolato sul `CityModel` salvato | Test sul flusso del job, sulla paginazione, sul riuso per hash e sui nuovi errori; test Api sull'inoltro |
| 3 | In CityGenerator: porting della geometria (`Polygon`, `PowerDiagram`), della pipeline di layout e delle ricette Organic/Grid, con golden test; `CityLayoutDto` salvato nella città | Golden test uguali al TS |
| 4 | Frontend: lista, generazione e dettaglio; rimozione di `layout/*.ts` e del template radial | Test vitest, build, prova manuale |
| 5 | Custom: parser della stringa con forma canonica ed errori per chiave, endpoint `recipe/schema`, form e testo sincronizzati nel frontend | Test del parser (round-trip stringa → ricetta → stringa canonica), limiti, determinismo (stessa ricetta + seed = stesso layout), test vitest della sincronizzazione form ↔ testo |
| 6 | Pulizia: `Pipeline:Periods`, `city_models`, `PeriodSubService`, endpoint `city?period`, README e `ERRORI_UI.md` | Ricerca di riferimenti residui |

Alla fine di ogni fase la solution compila senza warning e i test passano.

## Domande aperte

Nessuna: il piano è pronto per l'approvazione.
