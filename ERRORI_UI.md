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
| `UNAUTHENTICATED` | L'utente non ha effettuato l'accesso, oppure la sessione è scaduta (dopo 14 giorni di inattività) | "Accedi con Spotify per vedere la tua città." | Pulsante **Accedi con Spotify**; dopo l'accesso si torna alla pagina di partenza |
| `FORBIDDEN` | Utente autenticato ma senza i permessi necessari (per esempio un utente normale sulle pagine admin) | "Non hai i permessi per questa pagina." | Link alla home |
| `NOT_FOUND` | Risorsa o pagina inesistente | "Non abbiamo trovato quello che cercavi." | Link alla home |
| `REQUEST_REJECTED` | Richiesta non accettata (altri casi) | "Operazione non consentita." | Nessuna azione specifica |

---

## 3. Caricamento dell'export Spotify

Riguarda la pagina in cui l'utente carica lo zip scaricato da Spotify.

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `UPLOAD_FILE_MISSING` | Invio senza file | "Seleziona il file zip scaricato da Spotify." | Area di caricamento evidenziata |
| `UPLOAD_USER_MISSING` | Manca l'utente proprietario dell'export | "Collega prima il tuo account Spotify." | Pulsante **Collega Spotify** |
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

## 4. Account Spotify

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `SPOTIFY_AUTH_DENIED` | L'utente ha rifiutato l'accesso nella pagina di Spotify | "Senza il permesso di Spotify non possiamo leggere i tuoi ascolti recenti." | **Riprova a collegare**; in alternativa **Carica l'export** |
| `SPOTIFY_LOGIN_EXPIRED` | Il login è scaduto (oltre 10 minuti) o è stato aperto due volte | "Il collegamento è scaduto. Riprova." | **Collega Spotify** |
| `SPOTIFY_TOKEN_EXCHANGE_FAILED` | Spotify non ha completato il collegamento | "Spotify non ha completato il collegamento. Riprova tra poco." | **Riprova** |
| `SPOTIFY_ACCOUNT_NOT_LINKED` | Si chiede un aggiornamento per un account non collegato | "Il tuo account Spotify non è collegato." | **Collega Spotify** |
| `SPOTIFY_REAUTH_REQUIRED` | L'accesso è stato revocato o non è più valido | "Il collegamento con Spotify non è più attivo. Ricollega l'account per continuare ad aggiornare la città." | **Ricollega Spotify**. Conviene un banner persistente, non solo un avviso temporaneo |
| `SPOTIFY_RATE_LIMITED` | Spotify limita temporaneamente le richieste | "Spotify è momentaneamente occupato. Riproveremo in automatico." | Nessuna azione: si risolve da solo |
| `SPOTIFY_UNAVAILABLE` | Spotify non risponde | "Spotify non risponde in questo momento. Riproveremo più tardi." | Nessuna azione obbligatoria |
| `SPOTIFY_RETRIEVE_FAILED` | Altro errore nella lettura degli ascolti | "Non siamo riusciti ad aggiornare i tuoi ascolti." | **Riprova** |
| `RETRIEVE_JOB_NOT_FOUND` | Aggiornamento inesistente | "Non troviamo questo aggiornamento." | Nessuna azione |

**Errori durante l'accesso.** Se l'accesso con Spotify non va a buon fine, l'utente viene riportato all'interfaccia con il codice nell'indirizzo, per esempio `/login?error=SPOTIFY_AUTH_DENIED`. La pagina di accesso legge `error` e mostra il messaggio corrispondente.

Gli aggiornamenti da Spotify partono anche **in automatico** ogni 30 minuti. Se uno di questi fallisce, non serve interrompere l'utente: basta un indicatore discreto (per esempio "Ultimo aggiornamento non riuscito"). Fa eccezione `SPOTIFY_REAUTH_REQUIRED`, che richiede un'azione dell'utente.

---

## 5. La città

### Errori

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare / azione |
|---|---|---|---|
| `CITY_NOT_READY` | La città per quel periodo non è ancora stata costruita | Vedi sotto: dipende dallo stato della costruzione | — |
| `CITY_INVALID_PERIOD` | Periodo non valido (non dovrebbe succedere dall'interfaccia) | "Periodo non valido." | Torna al periodo "Tutto" |
| `CITY_BUILD_FAILED` | La costruzione della città è fallita | "Non siamo riusciti a costruire la tua città. Riprova più tardi." | **Riprova**; `traceId` |
| `MUSICBRAINZ_UNAVAILABLE` | Il catalogo dei generi musicali non è raggiungibile | "Il catalogo dei generi musicali non è disponibile al momento. La città verrà costruita appena torna online." | Nessuna azione: i dati dell'utente sono al sicuro |
| `PIPELINE_TIMEOUT` | La costruzione non ha risposto in tempo (30 min) | "La costruzione sta richiedendo troppo tempo. Riprova." | **Riprova** |
| `PIPELINE_RUN_NOT_FOUND` | Costruzione inesistente | "Non troviamo questa elaborazione." | Nessuna azione |
| `PROCESS_CANCELLED` | Un amministratore ha annullato il caricamento, l'aggiornamento da Spotify o la costruzione della città (stato `CANCELLED` / `Cancelled`) | "L'operazione è stata annullata. Puoi riprovare." | **Riprova** (nel caricamento: ricarica il file) |

### `CITY_NOT_READY`: in costruzione o senza dati?
Quando la città non c'è ancora, l'interfaccia guarda lo stato dell'ultima costruzione dell'utente:

| Ultima costruzione | Cosa mostrare |
|---|---|
| `Pending` | "Stiamo raccogliendo i tuoi ascolti, la città partirà tra pochi secondi…" |
| `Building` | "Stiamo costruendo la tua città…" (animazione di cantiere) |
| `Failed` | Il messaggio del suo `errorCode` (tabella sopra) |
| Nessuna | Stato vuoto di benvenuto: "Ancora nessuna città. Collega Spotify o carica il tuo export per iniziare." con i due pulsanti |

### Avvisi sulla città (`warnings`)
La città si mostra, con un messaggio sopra o accanto.

| Codice | Quando succede | Messaggio suggerito | Cosa mostrare |
|---|---|---|---|
| `CITY_NO_PLAYS` | Nessun ascolto nel periodo scelto | "Nessun ascolto in questo periodo." | Città vuota o terreno, con il suggerimento di scegliere un periodo più lungo |
| `CITY_GENRES_UNAVAILABLE` | Ci sono ascolti, ma nessun artista è stato associato a un genere | "Non riusciamo ancora a riconoscere i generi dei tuoi artisti." | Stato vuoto dedicato; nessuna azione per l'utente |
| `CITY_LOW_COVERAGE` | Meno della metà degli ascolti è stata collocata nella città | "La città mostra solo una parte dei tuoi ascolti: alcuni artisti non sono nel nostro catalogo." | Avviso discreto; nel dettaglio la percentuale (`summary.coveragePercent`) |

### Cancellazione dei dati

Dal menu dell'utente, **Cancella i miei dati** elimina ascolti, città, caricamenti (anche i file), costruzioni e collegamento con Spotify, poi esce dall'app. L'amministratore può fare lo stesso per un utente dal pannello.

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
3. **Banner "Ricollega Spotify"**, persistente, per `SPOTIFY_REAUTH_REQUIRED`.
4. **Avanzamento del caricamento** (in coda → lettura → costruzione città).
5. **Città in costruzione**, per `Pending` e `Building`.
6. **Stato vuoto di benvenuto**, quando non c'è ancora nessuna città.
7. **Città vuota per il periodo** (`CITY_NO_PLAYS`) e **generi non riconosciuti** (`CITY_GENRES_UNAVAILABLE`).
8. **Avviso discreto di copertura parziale** (`CITY_LOW_COVERAGE`).
9. **Indicatore "ultimo aggiornamento non riuscito"** per gli aggiornamenti automatici da Spotify.
