# Cityfy: errori e stati da mostrare nell'interfaccia

Questo documento elenca le situazioni di errore, di attesa e di avviso che l'interfaccia deve saper mostrare. Per ognuna indica quando succede, un messaggio suggerito per l'utente e cosa offrire come azione.

I messaggi sono proposte di testo e si possono cambiare liberamente. **I codici invece sono stabili**: l'interfaccia decide cosa mostrare guardando solo il codice, mai il testo tecnico che arriva dal server.

---

## 1. Come arrivano gli errori

Il backend comunica i problemi in tre modi.

### a) Una richiesta fallisce subito
Il server risponde con un errore, che contiene sempre un `code`:

```json
{
  "status": 400,
  "title": "UPLOAD_NOT_ZIP",
  "code": "UPLOAD_NOT_ZIP",
  "detail": "Only .zip files are accepted",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

- `code` decide il messaggio e la grafica da mostrare.
- `detail` è il testo tecnico, in inglese: **non va mostrato** all'utente.
- `traceId` serve al supporto per ritrovare l'errore nei log. Conviene renderlo copiabile in un "Dettagli" chiuso per default.

### b) Un'operazione in corso finisce male
Caricamento dell'export, lettura da Spotify e costruzione della città richiedono da qualche secondo a qualche minuto. L'interfaccia ne segue lo **stato**. Quando lo stato diventa fallito, è presente un `errorCode`:

| Operazione | Stati | Campo con il codice |
|---|---|---|
| Caricamento export | `Pending` → `InProgress` → `Done` / `Failed` / `Cancelled` | `errorCode` |
| Lettura da Spotify | `Pending` → `InProgress` → `Done` / `Failed` / `Cancelled` | `errorCode` |
| Costruzione città | `Pending` → `Building` → `Done` / `Failed` | `errorCode` |

### c) La città c'è, ma va spiegato qualcosa
La città contiene una lista `warnings`, di solito vuota. Ogni avviso è un codice `CITY_*` (vedi §5): la città si mostra comunque, insieme a un messaggio.

### Regole generali
- **Codice sconosciuto:** si mostra il messaggio generico di `INTERNAL_ERROR`. Non deve mai comparire una schermata vuota.
- **Errore di rete** (il server non risponde affatto): si usa il messaggio di `SERVICE_UNAVAILABLE`.
- **Tono:** non colpevolizzare l'utente, dire sempre cosa può fare.

---

## 2. Errori generali

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `INTERNAL_ERROR` | Errore imprevisto del server | "Qualcosa è andato storto. Riprova tra poco." | Pulsante **Riprova**; "Dettagli" con il `traceId` |
| `SERVICE_UNAVAILABLE` | Un servizio è momentaneamente irraggiungibile | "Il servizio non è raggiungibile in questo momento. Riprova tra qualche minuto." | **Riprova**, eventualmente automatico con attesa crescente |
| `VALIDATION_FAILED` | Dati della richiesta mancanti o non validi | "Alcuni dati non sono validi. Controlla e riprova." | Evidenziare i campi, se il form li ha |
| `UNAUTHENTICATED` | Nessuna sessione: l'utente non ha ancora iniziato, oppure il cookie è scaduto e il browser l'ha già tolto | "Inizia per creare la tua città." | Pagina di benvenuto con **Inizia senza account** |
| `SESSION_EXPIRED` | La sessione senza account è scaduta (nessuna visita per 1 giorno) oppure i suoi dati sono stati cancellati, ma il browser manda ancora il cookie | "La tua sessione è scaduta e i dati collegati sono stati cancellati. Puoi ricominciare quando vuoi." | Pagina di benvenuto con **Inizia senza account** |
| `FORBIDDEN` | Utente autenticato ma senza i permessi necessari (per esempio un utente normale sulle pagine admin) | "Non hai i permessi per questa pagina." | Link alla home |
| `NOT_FOUND` | Risorsa o pagina inesistente | "Non abbiamo trovato quello che cercavi." | Link alla home |
| `REQUEST_REJECTED` | Richiesta non accettata (altri casi) | "Operazione non consentita." | Nessuna azione specifica |
| `RATE_LIMITED` | Troppe richieste dalla stessa rete (sessioni nuove, upload, login Last.fm, o richieste in generale), troppi upload dello stesso utente in un giorno, o troppi upload in corso sul server. Status 429, con `Retry-After` ed estensione `retryAfterSeconds` quando il tempo è noto. Un login respinto torna a `/welcome?error=RATE_LIMITED` | "Troppe richieste in poco tempo: riprova tra qualche minuto." | **Riprova** più tardi; se c'è `retryAfterSeconds` si può mostrare l'attesa |

---

## 3. Caricamento dell'export Spotify

Riguarda la pagina in cui l'utente carica lo zip scaricato da Spotify.

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `UPLOAD_FILE_MISSING` | Invio senza file | "Seleziona il file zip scaricato da Spotify." | Area di caricamento evidenziata |
| `UPLOAD_USER_MISSING` | Manca l'utente proprietario dell'export (sessione non valida) | "Per caricare l'export inizia una sessione o accedi con Last.fm." | Pagina di benvenuto |
| `UPLOAD_NOT_ZIP` | Il file non è un .zip | "Il file deve essere uno .zip, così come lo invia Spotify. Non estrarlo prima di caricarlo." | Riprova il caricamento |
| `UPLOAD_TOO_LARGE` | Il file supera il limite (oggi 200 MB) | "Il file è troppo grande (massimo 200 MB)." | Riprova; link alla guida |
| `UPLOAD_INVALID_ZIP` | Zip danneggiato o incompleto | "Il file sembra danneggiato. Scaricalo di nuovo da Spotify e riprova." | Link alla guida per l'export |
| `UPLOAD_NO_STREAMING_FILES` | Lo zip non contiene la cronologia di ascolto: probabilmente è stato richiesto l'export sbagliato | "Questo zip non contiene la cronologia degli ascolti. Su Spotify richiedi la **Cronologia streaming estesa**, non i dati dell'account." | **Guida passo-passo** su come richiedere l'export giusto. È il caso più probabile, merita una grafica dedicata |
| `UPLOAD_UNREADABLE_FILES` | I file ci sono ma nessuno è leggibile | "Non riusciamo a leggere i file della cronologia. Prova a scaricare di nuovo l'export." | Link alla guida |
| `UPLOAD_PROCESSING_FAILED` | Errore durante l'importazione | "Non siamo riusciti a importare i tuoi ascolti. Riprova più tardi." | **Riprova**; `traceId` |
| `UPLOAD_NOT_FOUND` | Caricamento inesistente (link vecchio o errato) | "Non troviamo questo caricamento." | Torna al caricamento |

**Stati di attesa** (non sono errori):
- `Pending`: "File ricevuto, in coda…"
- `InProgress`: "Stiamo leggendo i tuoi ascolti…" Per export di più anni l'importazione può durare alcuni minuti.
- `DONE`: "Fatto! Stiamo costruendo la tua città…" (poi si passa allo stato della costruzione, §5).

---

## 4. Account Last.fm

Riguardano "Accedi con Last.fm". Gli errori di accesso tornano al frontend come `/welcome?error=CODICE`, per esempio `/welcome?error=LASTFM_AUTH_FAILED` quando l'utente arriva alla callback senza un token valido. Quelli della lettura degli ascolti si vedono nella pagina "I tuoi dati": l'account sospeso mostra il codice con **Accedi di nuovo con Last.fm**.

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `LASTFM_NOT_LINKED` | Si chiede qualcosa di Last.fm per un utente senza account collegato | "Il tuo account Last.fm non è collegato." | **Accedi con Last.fm** |
| `LASTFM_AUTH_FAILED` | L'autorizzazione è stata rifiutata o è scaduta (oltre 60 minuti, o link aperto due volte), oppure l'accesso è stato revocato dalle impostazioni di Last.fm | "Il collegamento con Last.fm non è riuscito o non è più attivo. Ricollega l'account." | **Ricollega Last.fm**; banner persistente se la lettura si è fermata |
| `LASTFM_UNAVAILABLE` | Last.fm non risponde o ha un problema temporaneo (anche un problema con la chiave di Cityfy, che vede l'amministratore) | "Last.fm non risponde in questo momento. Riproveremo più tardi." | **Riprova** dove ha senso; la lettura riprova da sola |
| `LASTFM_RATE_LIMITED` | Last.fm limita le richieste di Cityfy (errore 29): la lettura si ferma e riparte da sola | "Last.fm è momentaneamente occupato: gli ascolti arriveranno con un po' di ritardo." | Nessuna azione: si risolve da solo |
| `LASTFM_USER_PRIVATE` | Gli ascolti sono nascosti nelle impostazioni di privacy di Last.fm | "I tuoi ascolti su Last.fm sono privati: rendili visibili nelle impostazioni di Last.fm, poi ricollega l'account." | Link alle impostazioni di privacy di Last.fm; **Ricollega Last.fm** |
| `LASTFM_SPOTIFY_NOT_SCROBBLING` | **Non è un errore**, è un suggerimento nel campo `lastFm.warning` di `me`. L'account è attivo, ma da più di 3 giorni (`LastFm:NotScrobblingDays`) non arriva nessun ascolto nuovo: forse Spotify non invia più gli ascolti a Last.fm | "Da qualche giorno non riceviamo ascolti nuovi da Last.fm. Se hai ascoltato musica su Spotify, controlla che Spotify sia ancora collegato a Last.fm." | Link alle impostazioni di Last.fm (Applicazioni); il messaggio sparisce da solo quando arriva un ascolto |
| `LASTFM_IMPORT_NOT_FOUND` | Import dello storico inesistente (link vecchio o errato) | "Non troviamo questa importazione." | Nessuna azione |
| `LASTFM_IMPORT_FAILED` | L'import dello storico si è fermato per un errore imprevisto | "Non siamo riusciti a importare tutto lo storico di Last.fm. Riprova più tardi." | **Riprova**; `traceId`. Gli ascolti già importati restano |

**Eventi in diretta (`GET api/v1/live`).** Una sessione anonima riceve `LASTFM_NOT_LINKED` (404): il frontend apre lo stream solo per gli account. Più di 5 pagine aperte dallo stesso utente danno `RATE_LIMITED` (429). In entrambi i casi la barra "Stai ascoltando" semplicemente non compare: nessun messaggio.

**Stati dell'import dello storico** (non sono errori): `Pending` "In coda…", `InProgress` "Importazione dello storico: pagina 12 di 340" (`progressPercent` e `progressDetail`), `Done` "Storico importato: N ascolti". Un import interrotto da un riavvio riprende da solo dalla pagina a cui era arrivato.

---

## 5. La città

### Errori

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `CITY_INVALID_RANGE` | Intervallo di date non valido: inizio dopo la fine, o nel futuro | "Controlla le date: l'inizio deve venire prima della fine e non può essere nel futuro." | Evidenzia i campi data del form |
| `CITY_CATALOG_MISSING` | I dati dell'utente sono ancora in importazione: non si può ancora creare una città | "Stiamo ancora importando i tuoi ascolti. Potrai creare la tua città tra poco." | Disattiva **Genera**; mostra lo stato dell'import |
| `CITY_NOT_FOUND` | La città salvata non esiste (cancellata, o di un altro utente) | "Questa città non esiste più." | Torna alla lista delle città |
| `CITY_LIMIT_REACHED` | L'utente ha già il numero massimo di città salvate (10) | "Hai già 10 città salvate: cancellane una per crearne una nuova." | Porta alla lista delle città |
| `CITY_INVALID_LAYOUT` | Tipo di mappa sconosciuto (non dovrebbe succedere dall'interfaccia) | "Tipo di mappa non disponibile." | Torna a "Organica" |
| `CITY_INVALID_RECIPE` | Parametri della mappa personalizzata non validi. La risposta ha `problems: [{ key, code }]` con `code` tra `MISSING`, `TOO_LONG`, `SYNTAX`, `UNKNOWN_KEY`, `DUPLICATE_KEY`, `INVALID_VALUE`, `OUT_OF_RANGE` (`key` vuota = tutta la stringa) | "Controlla i parametri della mappa." | Evidenzia la chiave nel testo e il campo corrispondente del form, con il motivo |
| `CITY_INVALID_NAME` | Nome della città troppo lungo (oltre 80 caratteri) | "Il nome della città è troppo lungo." | Evidenzia il campo nome |
| `CITY_INVALID_GENRES` | Troppi generi e sottogeneri nella richiesta | "Hai selezionato troppi generi." | Suggerisci di scegliere i generi principali |
| `CITY_BUILD_FAILED` | La generazione della città (o l'aggiornamento del catalogo artisti) è fallita | "Non siamo riusciti a costruire la tua città. Riprova più tardi." | **Crea di nuovo** con gli stessi parametri; `traceId` |
| `MUSICBRAINZ_UNAVAILABLE` | Il catalogo dei generi musicali non è raggiungibile | "Il catalogo dei generi musicali non è disponibile al momento. La città verrà costruita appena torna online." | Nessuna azione: i dati dell'utente sono al sicuro |
| `PIPELINE_TIMEOUT` | L'aggiornamento del catalogo artisti non ha risposto in tempo (30 min) | "L'aggiornamento sta richiedendo troppo tempo. Riprova." | **Riprova** |
| `PIPELINE_RUN_NOT_FOUND` | Aggiornamento inesistente | "Non troviamo questa elaborazione." | Nessuna azione |
| `PROCESS_CANCELLED` | Un amministratore ha annullato il caricamento, l'aggiornamento da Spotify l'aggiornamento del catalogo o la generazione di una città (stato `CANCELLED` / `Cancelled`) | "L'operazione è stata annullata. Puoi riprovare." | **Riprova** (nel caricamento: ricarica il file) |

### Stato di una città salvata
Una città appena chiesta viene generata in background; il dettaglio (`GET /api/v1/cities/{id}`) si aggiorna da solo finché non è pronta:

| `status` | Cosa mostrare |
|---|---|
| `Pending` | "La tua città è in coda…" |
| `InProgress` | "Stiamo costruendo la tua città…" (animazione di cantiere) |
| `Done` | La città |
| `Failed` | Il messaggio del suo `errorCode` (tabella sopra), con **Crea di nuovo** |
| `Cancelled` | "Generazione annullata", con **Crea di nuovo** |

Nessuna città salvata: stato vuoto "Ancora nessuna città" con **Crea una città** e **Carica l'export**. Dopo il primo import la prima città si crea da sola.
### Avvisi sulla città (`warnings`)
La città si mostra, con un messaggio sopra o accanto.

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare |
|---|---|---|---|
| `CITY_NO_PLAYS` | Nessun ascolto nel periodo e nei generi scelti | "Nessun ascolto in questo periodo per i generi scelti." | Città vuota, con il suggerimento di allargare il periodo o i generi |
| `CITY_GENRES_UNAVAILABLE` | Ci sono ascolti, ma nessun artista è stato associato a un genere | "Non riusciamo ancora a riconoscere i generi dei tuoi artisti." | Stato vuoto dedicato; nessuna azione per l'utente |
| `CITY_LOW_COVERAGE` | Meno della metà degli ascolti è stata collocata nella città | "La città mostra solo una parte dei tuoi ascolti: alcuni artisti non sono nel nostro catalogo." | Avviso discreto; nel dettaglio la percentuale (`summary.coveragePercent`) |

### Cancellazione dei dati

Dal menu dell'utente, **Cancella i miei dati** elimina ascolti, catalogo artisti, città salvate, caricamenti (anche i file) e, per un account, il collegamento con Last.fm, poi esce dall'app. L'amministratore può fare lo stesso per un utente dal pannello.

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `USER_DATA_DELETION_INCOMPLETE` | Un servizio non ha risposto durante la cancellazione: una parte dei dati può essere rimasta | "Non siamo riusciti a cancellare tutti i tuoi dati. Riprova tra poco: la cancellazione riprende da dove si è fermata." | **Riprova** (la cancellazione si può ripetere senza problemi) |

---

## 6. Codici solo tecnici (amministrazione)

Riguardano strumenti interni e **non vanno mostrati agli utenti finali**. Servono solo se si realizza una pagina di amministrazione.

| Codice | Quando succede |
|---|---|
| `MB_RETRIEVE_ALREADY_RUNNING` | Si chiede l'aggiornamento del catalogo MusicBrainz mentre ne è già in corso uno |
| `MB_TOO_MANY_ITEMS` | Richiesta di abbinamento con più di 1000 artisti |
| `MB_GENRE_NOT_FOUND` | Genere inesistente |
| `ADMIN_INVALID_CREDENTIALS` | Credenziali errate al login del pannello admin (`/admin/login`) |
| `PROCESS_NOT_CANCELLABLE` | Annullamento richiesto per un processo già concluso o non in corso |

---

## 7. Schermate da prevedere

In sintesi, per la grafica:

1. **Messaggio di errore generico** con "Riprova" e "Dettagli" (`traceId`), riutilizzabile ovunque.
2. **Guida all'export Spotify corretto.** Serve per `UPLOAD_NO_STREAMING_FILES` ed è probabilmente l'errore più comune.
3. **Avviso "Accedi di nuovo con Last.fm"** nella pagina dei dati, per un account sospeso (`LASTFM_AUTH_FAILED`, `LASTFM_USER_PRIVATE`).
4. **Avanzamento del caricamento** (in coda → lettura → aggiornamento del catalogo).
5. **Città in costruzione**, per `Pending` e `InProgress`.
6. **Stato vuoto di benvenuto**, quando non c'è ancora nessuna città salvata.
7. **Città vuota per periodo e generi** (`CITY_NO_PLAYS`) e **generi non riconosciuti** (`CITY_GENRES_UNAVAILABLE`).
8. **Avviso discreto di copertura parziale** (`CITY_LOW_COVERAGE`).
9. **"Ultimo ascolto ricevuto"** e avanzamento dell'import dello storico Last.fm nella pagina dei dati.
