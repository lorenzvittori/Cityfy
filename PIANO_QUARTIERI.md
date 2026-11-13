# Piano: numero di quartieri selezionabile

## Obiettivo

Chi crea una città sceglie quanti quartieri può avere al massimo. Oggi è un valore fisso.

## Situazione attuale

- **Dove sta il limite.** In `CityFy.Catalog`, `CityBuildSubService.MergeSmallDistricts` usa `CityOptions.MaxDistricts` (configurazione `City:MaxDistricts`, default 12, range 2–100).
- **Come funziona.** Se i macro-generi sono più del limite, restano i primi `MaxDistricts - 1` per ascolti. Gli altri vengono uniti nel quartiere `other_genres` ("Altri generi"). I blocchi di quel quartiere mantengono il loro genere e il loro colore.
- **Rapporto con i filtri.** Il limite si applica dopo il filtro dei generi.
- **Il percorso di una richiesta:**
  1. il gateway riceve `POST api/v1/cities` (`CreateCityDto`);
  2. CityGenerator la valida (`CityRequestSubService.Validate`), calcola l'hash per la deduplica (`Hash`) e salva `GeneratedCity`;
  3. il job chiede il modello a Catalog con `CityModelQuery(From, To, Genres, Subgenres)`.
- **La UI.** `new-city.page` ha date, tipo di mappa, albero dei generi e pannello custom. "Duplica e modifica" precompila il form con i query param.

## Decisioni

| # | Domanda | Proposta |
|---|---|---|
| Q1 | Dove sta il parametro | **Campo a sé della richiesta** (`maxDistricts`), valido per tutti i tipi di mappa. Non è una chiave della stringa custom: decide *quali* quartieri esistono, cosa che calcola Catalog. La stringa custom decide invece *come* si disegnano, cosa che calcola CityGenerator. |
| Q2 | Range e default | ✅ **Nessun massimo** (decisione dell'utente). Il minimo è 2 con "unisci" e 1 con "scarta". Il default resta 12. Oltre una soglia (`WarnDistricts`, 20, configurabile) compare un **warning**, non un errore: nel form prima di generare e sulla città generata (codice `CITY_MANY_DISTRICTS` nei `Warnings` del modello). Il form offre anche "tutti", cioè nessun accorpamento. |
| Q3 | "Altri generi" è compreso nel numero? | **Sì**: N è il numero di quartieri che si vedono sulla mappa, come oggi. Con N = 8 e "unisci" si vedono 7 generi più "Altri generi". |
| Q4 | Cosa succede ai generi in eccesso | ✅ **Lo sceglie l'utente** con il campo `extraGenres`: `merge` (default, come oggi: uniti in "Altri generi") oppure `drop` (restano solo i primi N). ✅ Con `drop`, i generi scartati si trattano come esclusi dal filtro, quindi spariscono anche dai totali e dalla copertura. Così la città non riceve un `CITY_LOW_COVERAGE` dovuto a una scelta voluta. |
| Q5 | Rapporto con il filtro dei generi | **Il limite vale sempre.** Il form mostra un avviso se i macro-generi selezionati sono più di N ("ne verranno uniti/scartati X"). |
| Q6 | Deduplica | **Entrambi i valori entrano nell'hash in forma canonica**: un valore assente diventa il default prima dell'hash, e "tutti" ha una forma fissa. Le città salvate prima della modifica non hanno i campi: si leggono come 12 e `merge`, e la UI le mostra così. |
| Q7 | Limiti per la UI | Range e default arrivano dal backend: un nuovo endpoint `GET api/v1/cities/options`, oppure un campo aggiunto allo schema esistente `cities/recipe/schema`. **Proposta: endpoint nuovo `cities/options`.** Lo schema riguarda solo la stringa custom; `options` può contenere anche gli altri limiti del form (lunghezza del nome, numero di generi). |
| Q8 | Errori e warning | **Nuovo errore `CITY_INVALID_DISTRICTS` (400)** per un valore sotto il minimo o un `extraGenres` sconosciuto. **Nuovo warning `CITY_MANY_DISTRICTS`.** Entrambi vanno aggiunti ai tre cataloghi (ErrorCodes.cs, ERRORI_UI.md, error-messages.ts). |
| Q9 | Come si rappresenta "tutti" | **`maxDistricts: 0` = tutti.** Basta un solo campo, senza un flag in più; null resta "default". |

## Architettura

- **Contratti (ServiceDefault):**
  - `GenerateCityRequest` e `GeneratedCityInfo` aggiungono `int? MaxDistricts` e `string? ExtraGenres`;
  - `CityModelQuery` aggiunge gli stessi due campi (null significa il default di Catalog).
- **Catalog:**
  - `ICityBuildSubService.Build` riceve un `DistrictLimit(int Max, ExtraGenres Mode)` (Max 0 = tutti) invece di leggere le Options;
  - `MergeSmallDistricts` diventa `LimitDistricts`: con `Merge` fa quello che fa oggi, con `Drop` tiene i primi N e toglie i blocchi scartati dai totali;
  - aggiunge il warning `CITY_MANY_DISTRICTS` quando i quartieri finali superano `CityOptions.WarnDistricts`;
  - un valore sotto il minimo o una modalità sconosciuta risponde 400 `CITY_INVALID_DISTRICTS`.
- **CityGenerator:**
  - `CityGeneratorOptions` aggiunge `DefaultDistricts` = 12 e `WarnDistricts` = 20;
  - `Validate` normalizza (null diventa il default, modalità assente diventa `merge`) e controlla il minimo;
  - `CityRequest` e `GeneratedCity` aggiungono i due campi;
  - i campi entrano nel canonical di `Hash`;
  - `CityModelFetchSubService` li passa a Catalog;
  - nuovo endpoint `GET api/city-generator/cities/options` (default e soglia del warning).
- **Gateway:**
  - `CreateCityDto` e `CityDto` aggiungono `maxDistricts` e `extraGenres`;
  - nuovo `GET api/v1/cities/options` che inoltra a CityGenerator;
  - aggiornamento di `V1Mapper`.
- **Frontend:**
  - nel form, un controllo "Quartieri": campo numerico più "tutti", con il default preso da `cities/options`;
  - una scelta "Generi in eccesso: unisci in Altri generi / scarta";
  - un avviso sopra la soglia ("mappa affollata, generazione più lenta");
  - un avviso quando i generi del periodo o della selezione sono più di N ("ne verranno uniti/scartati X");
  - i query param `districts` ed `extra` per "Duplica e modifica";
  - i valori nel dettaglio della città;
  - la traduzione del nuovo errore e del nuovo warning.
- **Dati.** Nessuna migrazione. `GeneratedCity` ha già `[BsonIgnoreExtraElements]`, e un campo assente vale null, cioè 12.

## Fasi

| Fase | Contenuto | Come si verifica |
|---|---|---|
| 1 ✅ | Catalog: `DistrictLimit` come parametro di `Build`, modalità `merge`/`drop`, "tutti", nuovo errore e nuovo warning | Test di `CityBuilderTests`: N = 3 con `merge` dà 2 generi più "Altri generi"; N = 3 con `drop` dà 3 generi e totali senza gli scartati; 0 = tutti; null usa il default; sotto il minimo risponde 400; warning sopra la soglia. Test di coerenza dei cataloghi d'errore. |
| 2 ✅ | CityGenerator e gateway: campi nella richiesta, normalizzazione, hash, salvataggio, passaggio a Catalog, endpoint `cities/options` | `CityRequestTests` (minimo, null diventa default, hash diverso per N o modalità diversi e uguale tra null e 12/merge); test del fetch con i valori passati; test del gateway per i DTO e per `options`; un documento senza i campi si legge come null |
| 3 ✅ | Frontend: campo quartieri con "tutti", scelta della modalità, avvisi, precompilazione da query param, dettaglio, messaggi | vitest su `city-form.ts` (serializzazione, precompilazione, calcolo degli avvisi), `ng build` senza warning |
| 4 ✅ | Documentazione: README (`City:MaxDistricts` diventa il default, nuovo endpoint), ERRORI_UI.md, skill di progetto `cityfy-project` e `cityfy-web` | Revisione |

### Note sulla fase 1

- **Regole condivise.** Stanno nel modello `ServiceDefault.City.DistrictLimit` (`From(max, extra, default)`, `IsExceededBy`, `ExtraName`), che CityGenerator riuserà nella fase 2 per validare la richiesta con le stesse regole.
- **Configurazione rinominata.** `City:MaxDistricts` diventa `City:DefaultDistricts`; nuovo `City:WarnDistricts` (20). Nessun file di configurazione usava il vecchio nome.
- **Classifica per `drop`.** I generi scartati si decidono prima del calcolo, con la stessa classifica dei quartieri (ascolti qualificati, a parità vince l'ordine di comparsa). Poi i loro ascolti escono da `considered`, come fa il filtro dei generi.
- **Avvisi.** `CITY_MANY_DISTRICTS` può arrivare insieme a uno degli altri avvisi; prima la lista degli avvisi conteneva al più un codice.
- **Catalogo degli errori.** `CITY_INVALID_DISTRICTS` e `CITY_MANY_DISTRICTS` sono già nei tre cataloghi.

### Note sulla fase 2

- **Validazione.** `CityRequestSubService.Validate` usa `DistrictLimit.From` con `CityGenerator:DefaultDistricts`. La città salva sempre valori espliciti (`MaxDistricts`, `ExtraGenres` = "merge"/"drop") e li passa a Catalog. Il default di Catalog serve solo alle chiamate dirette e alle città salvate prima della modifica, che hanno i campi a null.
- **Default in due posti.** `CityGenerator:DefaultDistricts`/`WarnDistricts` e `City:DefaultDistricts`/`WarnDistricts` di Catalog vanno tenuti uguali (entrambi 12 e 20). È annotato nelle Options e nella skill di progetto.
- **Hash.** Il canonical aggiunge `Max|merge|drop` dopo i generi, quindi gli hash delle città già salvate cambiano. Effetto: una richiesta identica a una città esistente ne calcola una nuova una volta sola, poi la riusa.
- **Prima città di un utente.** `CityRequest.Default` ora è un metodo che riceve il limite; il default lo applica `ICityRequestSubService.Default()`.
- **Endpoint `cities/options`.** È `GET api/city-generator/options` (controller `FormOptionsController`), esposto dal gateway come `GET api/v1/cities/options`. Risponde `CityFormOptionsDto`: `defaultDistricts`, `warnDistricts`, `maxNameLength`, `maxGenres`.
- **DTO.** `CreateCityRequest` e `GeneratedCityDto` hanno `maxDistricts` ed `extraGenres`, null per le città salvate prima.

### Note sulla fase 3

- **Form.** La sezione "Quartieri" sta dopo i generi:
  - un campo numerico (vuoto = default, mostrato come placeholder) e la casella "Tutti i generi principali";
  - la scelta "Unisci gli altri nel quartiere Altri generi" / "Lascia fuori gli altri".
- **Logica pura in `city-form.ts`:** `districtChoiceOf` (query param), `districtsError` (minimo), `districtsRequest` (0 = tutti, null = default) e `districtPreview`.
- **Anteprima.** Sotto il campo dice quanti generi principali ci sono (quelli scelti, o tutti quelli del periodo) e quanti verranno uniti o scartati. Oltre la soglia avvisa che la mappa può essere affollata e lenta.
- **Default.** Il form li prende da `cities/options`; se l'endpoint non risponde, usa 12 e 20.
- **Dettaglio.** Il sottotitolo aggiunge per esempio "8 quartieri, altri generi esclusi". "Duplica e modifica" passa `districts` ed `extra`. `CITY_MANY_DISTRICTS` è un avviso discreto (non bloccante), come `CITY_LOW_COVERAGE`.
- **Stili** in `districts-panel.scss`, per il budget di 4 kB.

## Rischi e domande aperte

- **Prestazioni senza massimo.** ✅ Accettato dall'utente: con tanti quartieri la generazione può essere lenta, ma è una scelta di chi crea la città. Il warning lo avvisa, e il job è già asincrono e annullabile. Nessun tetto tecnico.
- **Leggibilità.** Con pochi quartieri, "Altri generi" può diventare il quartiere più grande della città. È voluto: mostra quanto ascolto finisce fuori dai generi principali.
- **Città già salvate.** Non cambiano, perché sono fotografie. Duplicandole si ottiene N = 12.
