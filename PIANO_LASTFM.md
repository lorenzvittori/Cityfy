# Piano — Ascolti da Last.fm (in quasi tempo reale)

## Obiettivo

1. Gli ascolti dell'utente arrivano a CityFy **mentre ascolta**, da qualunque dispositivo Spotify, senza passare dall'API di Spotify. Quell'API in modalità sviluppo accetta solo 5 utenti.
2. Il brano **in corso** arriva subito al browser: è la base per la città che cresce con gli ascolti.
3. Il login non dipende più da Spotify, così CityFy può avere più di 5 utenti.

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

- L'utente collega Spotify a Last.fm **una volta**, dalle impostazioni di Last.fm. Sul telefono non serve installare nulla.
- CityFy legge Last.fm con una sola chiave API dell'applicazione. I limiti sono quelli di Last.fm, non quelli di Spotify.
- Gli ascolti nuovi entrano nella pipeline che esiste già: `ListeningImported` → Orchestrator → catalogo artisti → città.

## Decisioni (confermate il 2026-11-12)

Stato: **piano approvato, non ancora iniziato.**

| # | Domanda | Decisione |
|---|---|---|
| L1 | **Login.** Oggi l'identità di CityFy è l'id utente Spotify. | **"Accedi con Last.fm"** come login principale. Last.fm ha un flusso di autorizzazione web (`auth.getSession`): l'utente autorizza CityFy, noi riceviamo nome utente verificato e chiave di sessione, senza password da gestire. Il login Spotify resta solo per i 5 utenti autorizzati, oppure si toglie. |
| L2 | **Identità.** Con più modi di login serve un id nostro. | Nuova collection `users` con un `userId` CityFy e gli account collegati (`lastfm`, `spotify`). Gli utenti di oggi vengono migrati: `userId` = id Spotify attuale, così ascolti e città non cambiano. |
| L3 | **Doppioni tra fonti.** Lo stesso ascolto può arrivare da Last.fm, dall'API Spotify e dall'export. | Ogni ascolto ha una **chiave di confronto** (utente + artista + brano normalizzati + minuto di inizio, con tolleranza di ±2 minuti) e si scarta il secondo arrivato. In più, quando Last.fm è collegato, il retrieve dall'API Spotify si spegne per quell'utente. |
| L4 | **Storico Last.fm.** | Al collegamento si importa tutto lo storico Last.fm (pagine da 200 ascolti). Spesso copre anni, quindi l'export di Spotify diventa facoltativo. |
| L5 | **Retrieve dall'API Spotify.** | Resta solo per chi ha il login Spotify e non ha Last.fm; altrimenti va dismesso. |

## Architettura

### 1. Nuovo servizio `CityFy.Retrieve.LastFm`

Possiede tre collection:
- `lastfm_accounts`: userId, nome utente Last.fm, chiave di sessione cifrata (Data Protection), cursore `lastScrobbleAt`, stato;
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
- `POST internal/lastfm/link` (token → sessione, nome utente)
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

- **Chiave dell'ascolto:** oggi `TrackUri` è un URI Spotify. Diventa un identificativo della fonte (`spotify:track:…` oppure `lastfm:{artista}|{brano}`), più la chiave di confronto per i doppioni (L3).
- **Nuovi campi:** `ArtistMbid` e `TrackMbid`, quando la fonte li fornisce.
- **Catalog:** se l'ascolto ha già l'MBID dell'artista, la risoluzione su MusicBrainz usa quello invece del nome. È più preciso e più veloce.

### 3. Brano in corso nel browser

- **Messaggio:** `NowPlayingChanged(UserId, Artist, Track, ArtistMbid?, StartedAt)`, oppure `Stopped`.
- **Gateway:**
  - consuma il messaggio, il primo consumer RabbitMQ del gateway;
  - espone `GET api/v1/live` in **Server-Sent Events** per l'utente loggato, con una connessione per scheda;
  - segnala a Retrieve.LastFm che l'utente ha una pagina aperta (cadenza veloce) tramite un evento `UserActive` ogni minuto finché la connessione resta aperta.
- **Frontend:**
  - barra in alto con "♪ Stai ascoltando: Artista – Brano";
  - nel dettaglio di una città, se l'artista è presente, l'edificio si illumina e la legenda lo evidenzia.

  È il primo passo verso "la città che cresce in diretta". Aggiungere edifici al volo sarà un piano a parte.

### 4. Login e identità (L1, L2)

- **Collection `users`:** è nel gateway oppure in un piccolo servizio `CityFy.Identity` (da decidere in fase 0). Contiene `userId`, gli account collegati, `createdAt` e `displayName`.
- **"Accedi con Last.fm":** l'Api apre l'autorizzazione Last.fm. Al rientro cerca o crea l'utente CityFy, apre la sessione (stesso cookie di oggi) e collega l'account Last.fm, che fa partire l'import dello storico.
- **"Accedi con Spotify":** resta per gli utenti autorizzati e si collega allo stesso utente CityFy.
- **Pagina account:** mostra gli account collegati, con i pulsanti per collegare o scollegare Last.fm e Spotify e lo stato della lettura ("ultimo ascolto ricevuto alle 14:32").
- **Cancellazione dati:** si aggiungono Retrieve.LastFm e `users`.

## Fasi

| Fase | Contenuto | Verifica |
|---|---|---|
| 0 | Decisioni L1–L5 (✅ confermate). Verifica dei termini Last.fm: limiti di richieste, uso non commerciale, attribuzione. Regole nuove nelle skill (es. sorgenti di ascolti, SSE) | — |
| 1 | `CityFy.Retrieve.LastFm`: collegamento account (auth web Last.fm), import dello storico, lettura incrementale con cadenza adattiva, `ListeningImported`, cancellazione dati, pannello admin | Test con fake del client Last.fm (pagine, cursori, nowplaying, errori, limite di richieste) |
| 2 | `ListeningEvent` più generale (chiave di sorgente, MBID, chiave di confronto) e deduplica tra fonti; Catalog usa l'MBID quando c'è | Test di deduplica Last.fm/Spotify/export con orari sfalsati |
| 3 | Identità CityFy (`users`), "Accedi con Last.fm", migrazione degli utenti di oggi, pagina account | Test del gateway sul login e sulla migrazione; prova manuale |
| 4 | Brano in corso: `NowPlayingChanged`, gateway con SSE e `UserActive`, barra e evidenziazione nella città | Test del gateway sullo stream; test vitest |
| 5 | Retrieve dall'API Spotify: dismesso o limitato agli utenti autorizzati (L5); documentazione (README, ERRORI_UI.md, DEPLOY.md) | Ricerca di riferimenti residui |

## Rischi

- **Collegamento Spotify–Last.fm:** a volte si interrompe. Lo mitighiamo con l'avviso `LASTFM_SPOTIFY_NOT_SCROBBLING` e un link alle impostazioni di Last.fm.
- **Ascolti offline e sessione privata di Spotify:** potrebbero non arrivare a Last.fm. Va verificato e scritto nella pagina account.
- **Termini d'uso di Last.fm:** limiti di richieste e uso commerciale vanno verificati in fase 0, prima di scrivere codice.
- **Profili Last.fm privati:** la lettura con la chiave di sessione dell'utente dovrebbe funzionare comunque. Va verificato.
