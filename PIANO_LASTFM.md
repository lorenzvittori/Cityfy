# Piano — Ascolti da Last.fm (in quasi tempo reale) e sessioni anonime

## Obiettivo

1. Gli ascolti dell'utente arrivano a CityFy **mentre ascolta**, da qualunque dispositivo Spotify, senza passare dall'API di Spotify. Quell'API in modalità sviluppo accetta solo 5 utenti.
2. Il brano **in corso** arriva subito al browser: è la base per la città che cresce con gli ascolti.
3. **Niente login obbligatorio.** Chi entra usa CityFy con una **sessione anonima** (`sessionId`) che dura 1 giorno: alla scadenza i suoi dati vengono cancellati. Il login esiste solo se l'utente sceglie **"Accedi con Last.fm"**, e in quel caso l'account è persistente.

## Come funziona

```
Spotify (tutti i dispositivi) ──server-to-server──► Last.fm
                                                       │  user.getRecentTracks (polling)
                                                       ▼
                                          CityFy.Retrieve.LastFm
                                    ┌──────────────┴───────────────┐
                    nuovi ascolti ──► listening_events      brano in corso ──► NowPlayingChanged
                                    │                                          │
                         ListeningImported ──► Orchestrator ──► Catalog      gateway ──SSE──► browser
```

- **Utente anonimo:** apre CityFy, carica l'export ZIP di Spotify e vede la sua città. Non crea account. Dopo 24 ore la sessione scade e tutto quello che ha caricato viene cancellato.
- **Utente Last.fm:** collega Spotify a Last.fm **una volta**, dalle impostazioni di Last.fm (sul telefono non serve installare nulla), poi accede a CityFy con Last.fm. CityFy importa lo storico e legge gli ascolti nuovi. I dati restano finché non li cancella lui.
- CityFy legge Last.fm con una sola chiave API dell'applicazione. I limiti sono quelli di Last.fm, non quelli di Spotify.
- Gli ascolti nuovi entrano nella pipeline che esiste già: `ListeningImported` → Orchestrator → catalogo artisti → città.

## Decisioni

Stato: **decisioni confermate e fase 0 completata il 2026-11-13. Prossima: fase 1.**

| # | Domanda | Decisione |
|---|---|---|
| L1 | **Login.** | **Nessun login obbligatorio.** L'unico login è **"Accedi con Last.fm"** (flusso web `auth.getSession`: l'utente autorizza CityFy, noi riceviamo nome utente verificato e chiave di sessione Last.fm, senza password da gestire). Il **login Spotify si toglie**. Resta il login admin. *(rivista il 2026-11-13)* |
| L2 | **Identità.** | Due tipi di proprietario dei dati: **sessione anonima** (`sessionId`, effimera) e **utente Last.fm** (`userId` CityFy, persistente). Verso i servizi interni resta un unico identificativo opaco (vedi § 4): i servizi non sanno se è anonimo o no. *(rivista il 2026-11-13)* |
| L3 | **Doppioni tra fonti.** Lo stesso ascolto può arrivare da Last.fm e dall'export. | Ogni ascolto ha una **chiave di confronto** (proprietario + artista + brano normalizzati + minuto di inizio, con tolleranza di ±2 minuti) e si scarta il secondo arrivato. |
| L4 | **Storico Last.fm.** | Al collegamento si importa tutto lo storico Last.fm (pagine da 200 ascolti). Spesso copre anni, quindi l'export di Spotify diventa facoltativo. |
| L5 | **Retrieve dall'API Spotify.** | **Si dismette** insieme al login Spotify (servizio `CityFy.Retrieve.Spotify`, collection `spotify_accounts` e `spotify_retrieve_tasks`). *(rivista il 2026-11-13)* |
| L6 | **Durata della sessione anonima.** | **1 giorno, scorrevole.** Se l'utente torna prima della scadenza, la sessione si rinnova per altre 24 h dall'ultima visita. Se non torna in tempo, scade e i dati si cancellano. Cookie e sessione scadono insieme. *(confermata il 2026-11-13)* |
| L7 | **Dati alla scadenza.** | Alla scadenza della sessione anonima i suoi dati vengono **cancellati su tutti i servizi**, con la stessa cancellazione di oggi (`UserDataEraser`). Gli utenti Last.fm non scadono. |
| L8 | **Da anonimo a Last.fm.** Un anonimo che poi accede con Last.fm. | I dati della sessione **passano all'utente Last.fm** (vedi § 4, "Passaggio da anonimo"), poi la sessione anonima si chiude. *(confermata il 2026-11-13)* |
| L9 | **Utenti di oggi** (id Spotify, di test). | **Nessuna migrazione:** i database si cancellano e si ricreano al rilascio delle fasi 1–3. *(confermata il 2026-11-13)* |

## Esito della fase 0 (2026-11-13)

Fonti lette: [termini API](https://www.last.fm/api/tos), [autenticazione](https://www.last.fm/api/authspec), [web auth](https://www.last.fm/api/webauth), [user.getRecentTracks](https://www.last.fm/api/show/user.getRecentTracks).

### Termini d'uso Last.fm e cosa comportano

| Regola (clausola) | Conseguenza per CityFy |
|---|---|
| Solo uso **non commerciale** (3.1); per quello commerciale serve un accordo (3.2). "Non commerciale" non è definito | CityFy resta gratuito, senza pubblicità né abbonamenti. Nel dubbio si chiede a Last.fm |
| Le pagine **pubbliche** che usano Last.fm vanno **approvate per iscritto** da Last.fm (2.7) | ⚠️ **Prima di aprire al pubblico** il login Last.fm serve una mail a partners@last.fm con la descrizione dell'app. Lo sviluppo e le prove private non sono bloccati. La mail la manda l'utente |
| Pulsante **"powered by AudioScrobbler"** con link a Last.fm (2.7); link al profilo Last.fm dell'utente e alla pagina Last.fm di artisti e brani mostrati | Pulsante nel footer e nella pagina account; nome utente con link al profilo; nella barra "Stai ascoltando" il brano con link a Last.fm |
| Logo e marchi Last.fm solo con approvazione scritta (7.1); niente pubblicità o comunicati sull'uso di Last.fm senza consenso (8.2) | Il pulsante "Accedi con Last.fm" è solo testo, senza logo, finché non arriva l'approvazione |
| Immagini, artwork e audio **esclusi** dalla licenza (5.1.6–5.1.8) | Non si usano le immagini di Last.fm |
| Vietato usare i dati per identificare o contattare gli utenti (5.1.6) | Si salva solo il nome utente Last.fm, nessuna email |
| Caching secondo gli header HTTP; "Reasonable Usage Cap" di 100 MB (4.3.4), formulazione poco chiara | Da chiarire nella stessa mail: lo storico di molti utenti può superare 100 MB |
| Alla fine della licenza vanno cancellati **tutti** i dati Last.fm (9.3) | Ogni ascolto porta la sorgente `lastfm`; comando admin "cancella tutti i dati Last.fm" |
| Dati conservati secondo le leggi locali e una privacy almeno pari a quella di Last.fm (2.3, 2.8) | Pagina privacy con cosa salviamo, per quanto (anonimi: 1 giorno), e cancellazione |
| Limiti di richieste **non dichiarati**, a discrezione di Last.fm, applicati **per IP** (4.4, errore 29) | Limitatore globale (default 4 richieste/s) e, all'errore 29, **pausa di tutta la lettura** con attesa crescente (30 s, 1 min, 2 min… fino a 10 min). Con un solo IP la cadenza veloce (15 s) regge circa 60 utenti attivi insieme: oltre, la cadenza si allunga da sola |

### Dettagli tecnici verificati

- **Autorizzazione web:** redirect a `last.fm/api/auth/?api_key=…&cb=…` → ritorno con `?token=…`. Il token vale **60 minuti** ed è monouso. `auth.getSession` restituisce nome utente e chiave di sessione.
- **Chiave di sessione:** **non scade**, ma l'utente può revocarla dalle impostazioni di Last.fm (errore 9). Si salva cifrata; con l'errore 9 l'account passa a `revoked` e la pagina account chiede di ricollegarlo.
- **Firma `api_sig`:** parametri in ordine alfabetico, `nome+valore` concatenati senza separatori, poi il segreto; MD5 in esadecimale. `format` e `callback` sono esclusi. È logica pura, con test sull'esempio della documentazione.
- **`user.getRecentTracks`:** `limit` massimo 200, `page`, `from`/`to` in secondi UNIX UTC, `extended=1`. Il brano in corso ha `nowplaying="true"` e non ha data. Non richiede autenticazione; con i profili privati il comportamento non è documentato: si passa comunque `sk` e si verifica a mano in fase 2.
- **Errori Last.fm → codici CityFy:**

  | Last.fm | CityFy |
  |---|---|
  | 29 (limite) | `LASTFM_RATE_LIMITED` + pausa globale |
  | 4, 9 (autenticazione, sessione non valida) | `LASTFM_AUTH_FAILED`, account `revoked` |
  | 6 (parametri, es. utente inesistente) | `LASTFM_AUTH_FAILED` al collegamento, log negli altri casi |
  | 8, 11, 16 (operazione fallita, servizio offline, errore temporaneo) | `LASTFM_UNAVAILABLE`, si ritenta al giro dopo |
  | 10, 26 (chiave API non valida o sospesa) | `LASTFM_UNAVAILABLE` verso l'utente, log critico e avviso nel pannello admin: è un problema di configurazione |

### Altre decisioni della fase 0

- **`anonymous_sessions` e `users` stanno nel gateway** (Api), non in un servizio `CityFy.Identity`. Il gateway ha già la sessione, la cancellazione su tutti i servizi e ora il passaggio di proprietario: un servizio in più aggiungerebbe solo chiamate. Il gateway riceve un suo database Mongo (`cityfy-api`).
- **Passaggio degli ascolti:** l'`_id` di `ListeningEvent` contiene lo `userId` (`BuildId`), quindi il passaggio reinserisce con l'id nuovo e poi cancella il vecchio. Un doppione già presente per l'utente Last.fm si unisce: si aggiungono le sorgenti, non si duplica.
- **Sorgenti:** `ListeningSources` ha già `dump` e `api`; si aggiunge `lastfm`. `api` resta per i dati storici finché i database non vengono ricreati (L9), poi si toglie con la fase 6.
- **Regole nelle skill:** passaggio di proprietario, proprietari temporanei e dati di fonti con licenza in `aspnet-microservices`; identità, sessioni e termini Last.fm in `cityfy-project`. La regola sugli SSE si scrive nella fase 5, quando c'è il primo stream.

## Architettura

### 1. Nuovo servizio `CityFy.Retrieve.LastFm`

Possiede tre collection:
- `lastfm_accounts`: userId, nome utente Last.fm, chiave di sessione Last.fm cifrata (Data Protection), cursore `lastScrobbleAt`, stato;
- `lastfm_import_tasks`: import dello storico, come job con stato;
- il brano in corso dell'utente, solo in memoria.

**Lettura periodica (`LastFmPollingHostedService`)**
- `user.getRecentTracks&from={lastScrobbleAt}` con `extended=1`, che dà anche l'MBID dell'artista quando c'è.
- **Cadenza adattiva:**
  - ogni 15 s per chi sta ascoltando (`nowplaying` presente) o ha una pagina CityFy aperta;
  - ogni 2 min per chi ha ascoltato nell'ultima ora;
  - ogni 15 min per gli altri.
- **Limite di richieste:** limitatore globale configurabile (`LastFm:MaxRequestsPerSecond`, default 4). Il valore preciso consentito da Last.fm va verificato nei termini.
- **Output:** ascolti nuovi → `listening_events` (sorgente `lastfm`) → `ListeningImported`. Cambio del brano in corso → `NowPlayingChanged`.

**Import dello storico (job annullabile, come gli upload)**
- Pagine da 200 dal più recente al più vecchio, con avanzamento ("pagina 12 di 340").
- Riprende da dove era rimasto dopo un riavvio.
- A fine import pubblica `ListeningImported`.

**Endpoint**
- `GET internal/lastfm/authorize-url`
- `POST internal/lastfm/link` (token → sessione Last.fm, nome utente)
- `GET internal/lastfm/accounts/{userId}`
- `POST .../import`
- `GET .../import/{id}`
- `DELETE internal/lastfm/users/{userId}/data`

**Errori**
- `LASTFM_NOT_LINKED`
- `LASTFM_AUTH_FAILED`
- `LASTFM_UNAVAILABLE`
- `LASTFM_RATE_LIMITED`
- `LASTFM_USER_PRIVATE`: profilo con ascolti nascosti
- `LASTFM_SPOTIFY_NOT_SCROBBLING`: nessuno scrobble da giorni mentre l'utente risulta attivo. È solo un suggerimento, non un errore bloccante: "ricollega Spotify su Last.fm".

### 2. `ListeningEvent` più generale

- **Chiave dell'ascolto:** oggi `TrackUri` è un URI Spotify. Diventa un identificativo della fonte (`spotify:track:…` per l'export oppure `lastfm:{artista}|{brano}`), più la chiave di confronto per i doppioni (L3).
- **Nuovi campi:** `ArtistMbid` e `TrackMbid`, quando la fonte li fornisce.
- **Catalog:** se l'ascolto ha già l'MBID dell'artista, la risoluzione su MusicBrainz usa quello invece del nome. È più preciso e più veloce.

### 3. Brano in corso nel browser (solo utenti Last.fm)

- **Messaggio:** `NowPlayingChanged(UserId, Artist, Track, ArtistMbid?, StartedAt)`, oppure `Stopped`.
- **Gateway:**
  - consuma il messaggio, il primo consumer RabbitMQ del gateway;
  - espone `GET api/v1/live` in **Server-Sent Events** per l'utente Last.fm, con una connessione per scheda; per una sessione anonima risponde `LASTFM_NOT_LINKED`;
  - segnala a Retrieve.LastFm che l'utente ha una pagina aperta (cadenza veloce) tramite un evento `UserActive` ogni minuto finché la connessione resta aperta.
- **Frontend:**
  - barra in alto con "♪ Stai ascoltando: Artista – Brano";
  - nel dettaglio di una città, se l'artista è presente, l'edificio si illumina e la legenda lo evidenzia.

  È il primo passo verso "la città che cresce in diretta". Aggiungere edifici al volo sarà un piano a parte.

### 4. Sessioni anonime e identità (L1, L2, L6, L7)

**Identificativo verso i servizi**
- I servizi interni continuano a ricevere un `userId` opaco e non cambiano. Il gateway lo valorizza così:
  - sessione anonima → `anon_{sessionId}` (GUID casuale, 128 bit);
  - utente Last.fm → `userId` CityFy dell'utente.
- Il prefisso serve solo a gateway, log e pannello admin per riconoscere i due casi.

**Sessione anonima (nel gateway)**
- Collection `anonymous_sessions`: `sessionId`, `createdAt`, `expiresAt`, `status` (`active` / `expired` / `deleted` / `transferring` / `transferred`).
- Si crea **al primo uso che scrive dati** (es. l'upload dell'export), non alla semplice visita: così crawler e visite di passaggio non generano sessioni.
- Cookie `cityfy.auth` come oggi (HttpOnly, SameSite=Lax), con durata di 1 giorno e sliding.
- **Rinnovo (L6):** ogni richiesta di una sessione ancora attiva sposta `expiresAt` a 24 h da adesso e riemette il cookie. Per non scrivere su Mongo a ogni richiesta, il rinnovo si fa solo se dall'ultimo è passata almeno 1 ora (configurabile, `Sessions:RenewAfter`). Una sessione già scaduta non si rinnova più, anche se la cancellazione non è ancora avvenuta.
- Il gateway rifiuta un cookie la cui sessione è scaduta anche se il browser lo manda ancora (`SESSION_EXPIRED`): il cookie è la chiave, ma la verità è la collection.

**Cancellazione alla scadenza (`ExpiredSessionCleanupHostedService`, nel gateway)**
- Ogni 10 minuti prende le sessioni con `expiresAt` passato e non ancora `deleted`.
- Per ognuna: segna `expired`, chiama `UserDataEraser` (cancellazione su tutti i servizi), poi segna `deleted`.
- Se un servizio non risponde (`USER_DATA_DELETION_INCOMPLETE`) la sessione resta `expired` e si ritenta al giro dopo. La cancellazione è già ripetibile.
- Non si usa un indice TTL di Mongo: cancellerebbe solo il documento della sessione, non i dati negli altri servizi.
- Le sessioni `deleted` restano qualche giorno come traccia (solo id e date), poi le toglie un indice TTL.
- Job annullabile, come gli altri job in background. Pannello admin: sessioni attive, in scadenza, cancellazioni fallite.

**Utenti Last.fm**
- Collection `users` (nel gateway oppure in un piccolo servizio `CityFy.Identity`, da decidere in fase 0): `userId`, nome utente Last.fm, `displayName`, `createdAt`.
- **"Accedi con Last.fm":** l'Api apre l'autorizzazione Last.fm. Al rientro cerca o crea l'utente (chiave: nome utente Last.fm), apre la sessione persistente (cookie con sliding come oggi), collega l'account in Retrieve.LastFm e fa partire l'import dello storico. Se c'era una sessione anonima attiva, prima fa il passaggio qui sotto.

**Passaggio da anonimo (L8, `UserDataTransfer` nel gateway)**
- Stessa forma della cancellazione: un endpoint per servizio, `POST internal/.../users/{fromUserId}/transfer` con body `{ toUserId }`, chiamato su tutti i servizi dal gateway.
- **Dati di origine** (ascolti in `listening_events`, upload e loro task): cambiano proprietario. Se l'utente Last.fm aveva già ascolti, i doppioni si scartano con la chiave di confronto (L3).
- **Dati derivati** (catalogo artisti, città generate, pipeline run): non si spostano. Si cancellano per la sessione anonima e si ricalcolano per l'utente Last.fm, che dopo il passaggio riceve un `ListeningImported`. Così non serve fondere due cataloghi o due città.
- La sessione anonima va in stato `transferring` e poi `transferred`. Il cleanup della scadenza la salta finché è `transferring`, così non si cancella a metà.
- Ripetibile: se un servizio non risponde (`USER_DATA_TRANSFER_INCOMPLETE`), il login riesce lo stesso e il gateway ritenta in background. La sessione non scade finché il passaggio non è completo.
- Un passaggio ripetuto non sposta niente due volte: i dati già spostati non hanno più `fromUserId`.
- **Pagina account:** account Last.fm collegato, stato della lettura ("ultimo ascolto ricevuto alle 14:32"), scollegamento, cancellazione dati.
- **Pagina per l'anonimo:** dice chiaramente quando scade la sessione ("se non torni entro le 15:40 di domani i tuoi dati verranno cancellati") e offre "Accedi con Last.fm" per tenerli per sempre.
- **Cancellazione dati:** si aggiungono Retrieve.LastFm e `users`.

## Fasi

| Fase | Contenuto | Verifica |
|---|---|---|
| 0 ✅ | Decisioni L1–L9 confermate. Termini Last.fm verificati (vedi "Esito della fase 0"). Regole nuove nelle skill | — |
| 1 | **Sessioni anonime** nel gateway: `anonymous_sessions`, creazione al primo upload, cookie da 1 giorno con rinnovo a ogni visita, `SESSION_EXPIRED`, cleanup alla scadenza con `UserDataEraser`, pannello admin. Rimozione del login Spotify dal frontend | Test del gateway: creazione, rinnovo entro la scadenza (e nessun rinnovo dopo), scadenza, cookie scaduto rifiutato, cancellazione completa e ritentata dopo un errore |
| 2 | `CityFy.Retrieve.LastFm`: collegamento account (auth web Last.fm), import dello storico, lettura incrementale con cadenza adattiva, `ListeningImported`, cancellazione dati, pannello admin | Test con fake del client Last.fm (pagine, cursori, nowplaying, errori, limite di richieste) |
| 3 | "Accedi con Last.fm": `users`, sessione persistente, passaggio da anonimo (L8) con endpoint di transfer su ogni servizio, pagina account. Al rilascio delle fasi 1–3 i database si cancellano e si ricreano (L9) | Test del gateway sul login; test del passaggio: dati di origine spostati, derivati ricalcolati, doppioni scartati, ritentativo dopo un errore, nessuna cancellazione durante il passaggio; prova manuale |
| 4 | `ListeningEvent` più generale (chiave di sorgente, MBID, chiave di confronto) e deduplica Last.fm/export; Catalog usa l'MBID quando c'è | Test di deduplica con orari sfalsati |
| 5 | Brano in corso: `NowPlayingChanged`, gateway con SSE e `UserActive`, barra e evidenziazione nella città | Test del gateway sullo stream; test vitest |
| 6 | Dismissione di `CityFy.Retrieve.Spotify` (servizio, collection, client nel gateway, AppHost); documentazione (README, ERRORI_UI.md, DEPLOY.md, skill `cityfy-project`) | Ricerca di riferimenti residui |

## Rischi

- **Abuso delle sessioni anonime:** senza login chiunque può creare sessioni e caricare ZIP. Serve un limite di upload per IP e per sessione, e la creazione della sessione solo al primo upload.
- **Perdita dati inattesa per l'anonimo:** l'utente può non aspettarsi la cancellazione dopo un giorno. Lo mitighiamo con l'avviso fisso della scadenza e l'invito a usare Last.fm.
- **Cancellazione incompleta:** se un servizio è giù alla scadenza, i dati restano finché il ritentativo non riesce. Il pannello admin mostra le cancellazioni bloccate.
- **Collegamento Spotify–Last.fm:** a volte si interrompe. Lo mitighiamo con l'avviso `LASTFM_SPOTIFY_NOT_SCROBBLING` e un link alle impostazioni di Last.fm.
- **Ascolti offline e sessione privata di Spotify:** potrebbero non arrivare a Last.fm. Va verificato e scritto nella pagina account.
- **Approvazione di Last.fm (2.7):** senza l'approvazione scritta il login Last.fm non può andare in produzione. Se Last.fm non risponde o rifiuta, CityFy resta con le sole sessioni anonime e l'export, che funzionano da sole.
- **Limite di richieste per IP:** non dichiarato e deciso da Last.fm. La pausa globale sull'errore 29 evita di peggiorare, ma con molti utenti attivi la cadenza "quasi in tempo reale" si allunga.
- **Profili Last.fm privati:** la lettura con la chiave di sessione dell'utente dovrebbe funzionare comunque. Va verificato a mano in fase 2.
