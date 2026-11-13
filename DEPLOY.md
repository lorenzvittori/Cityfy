# Deploy di Cityfy (costo zero)

Cityfy gira su **una macchina virtuale gratuita di Oracle Cloud** (ARM, 4 core, 24 GB di RAM) con Docker. Il catalogo MusicBrainz sta su **Azure SQL Database** con l'offerta gratuita. **GitHub Actions** compila, esegue i test, crea le immagini e aggiorna la macchina a ogni push su `main`.

```
Browser ──https──► Caddy (web) ──/api──► Api ──► Catalog, CityGenerator, Orchestrator, RetrieveLastFm,
                     │                          RetrieveDumpSpotify, RetrieveMusicBrainz
                     └── app Angular            MongoDB, RabbitMQ (rete interna)      ──► Azure SQL
```

Solo Caddy è raggiungibile da Internet, sulle porte 80 e 443. I servizi interni, MongoDB e RabbitMQ restano sulla rete Docker interna.

| File | A cosa serve |
|---|---|
| `.github/workflows/ci.yml` | Build e test (.NET e Angular) a ogni push e pull request |
| `.github/workflows/deploy.yml` | Su `main`: CI, poi immagini ARM64 su GitHub Container Registry, poi aggiornamento via SSH |
| `deploy/docker-compose.yml` | I servizi in produzione: rispecchia l'AppHost, va aggiornato quando cambia |
| `deploy/Caddyfile`, `deploy/web.Dockerfile` | Frontend, proxy `/api` e certificato HTTPS automatico |
| `deploy/musicbrainz.Dockerfile` | RetrieveMusicBrainz con `bzip2`, che serve per estrarre il dump |
| `deploy/.env.example` | Le variabili del compose. In produzione il file `.env` lo scrive il workflow a partire dai segreti di GitHub |

---

## Una volta sola

Servono circa 30-45 minuti. Sia Oracle sia Azure chiedono una carta di credito per la verifica, ma non addebitano nulla.

### 1. Macchina su Oracle Cloud

1. Crea un account su [cloud.oracle.com](https://cloud.oracle.com). La *home region* non si può cambiare dopo: sceglila vicina, per esempio Milano o Francoforte.
2. Vai in **Compute → Instances → Create instance**:
   - **Image:** Canonical Ubuntu 24.04 (aarch64);
   - **Shape:** Ampere `VM.Standard.A1.Flex`, 4 OCPU, 24 GB;
   - **Boot volume:** 100 GB o più. Il dump MusicBrainz scaricato occupa diversi GB;
   - **SSH keys:** carica la tua chiave pubblica personale.

   Se Oracle risponde *out of capacity*, riprova più tardi o cambia *availability domain*.
3. Apri le porte web: **Networking → Virtual Cloud Network → Security List** della subnet → *Ingress rules*. Aggiungi TCP 80, TCP 443 e UDP 443 da `0.0.0.0/0`.
4. Entra nella macchina (`ssh ubuntu@<IP pubblico>`). Apri le stesse porte anche nel firewall interno delle immagini Oracle e installa Docker:
   ```bash
   sudo iptables -I INPUT 6 -p tcp -m multiport --dports 80,443 -j ACCEPT
   sudo iptables -I INPUT 6 -p udp --dport 443 -j ACCEPT
   sudo netfilter-persistent save
   curl -fsSL https://get.docker.com | sudo sh
   sudo usermod -aG docker ubuntu   # poi esci e rientra
   ```
5. Consigliato: passa l'account a **Pay As You Go**, così Oracle non recupera le macchine gratuite poco usate. Resta gratis entro i limiti *Always Free*. Le risorse che li superano, invece, vengono addebitate: crea solo risorse marcate *Always Free*.

### 2. Dominio gratuito

Su [duckdns.org](https://www.duckdns.org) crea un sottodominio, per esempio `cityfy-tuonome.duckdns.org`, e puntalo all'IP pubblico della macchina. Caddy ottiene e rinnova da solo il certificato HTTPS.

### 3. Database del catalogo su Azure SQL (gratuito)

1. Su [portal.azure.com](https://portal.azure.com) crea un **SQL Database** e attiva il banner **"Apply offer" (Free)**:
   - nome del database: `musicbrainz`; crea un nuovo server con **autenticazione SQL** (utente e password);
   - quando si raggiunge il limite gratuito, scegli **"Auto-pause the database until next month"**: così non paghi mai.
2. In **Networking** del server: accesso pubblico, con una regola del firewall per l'**IP pubblico della macchina Oracle**.
3. In **Connection strings → ADO.NET** copia la stringa, metti la password e aggiungi `Connection Timeout=60;`. Il database in pausa impiega fino a un minuto per riprendersi. Al primo avvio, RetrieveMusicBrainz crea le tabelle da solo (migration).

### 4. Last.fm

Crea un'applicazione su [last.fm/api/account/create](https://www.last.fm/api/account/create), con callback `https://<tuo dominio>/api/v1/auth/lastfm/callback`. API key e shared secret vanno nei segreti di GitHub (punto 6).

Prima di aprire il login Last.fm al pubblico serve l'approvazione scritta di Last.fm: vedi `PIANO_LASTFM.md`, fase 0.

### 5. Chiave SSH per il deploy

Crea una chiave dedicata a GitHub, senza passphrase:
```bash
ssh-keygen -t ed25519 -f cityfy_deploy -N "" -C "github-deploy"
ssh-copy-id -i cityfy_deploy.pub ubuntu@<IP pubblico>
```

### 6. Configurazione su GitHub

Serve un utente amministratore del repository. Vai in **Settings → Secrets and variables → Actions**.

| Tipo | Nome | Valore |
|---|---|---|
| Variable | `DEPLOY_HOST` | IP pubblico della macchina. Finché manca, il deploy non parte |
| Variable | `DEPLOY_USER` | `ubuntu` |
| Variable | `DOMAIN` | es. `cityfy-tuonome.duckdns.org` |
| Secret | `DEPLOY_SSH_KEY` | Contenuto del file **privato** `cityfy_deploy` |
| Secret | `MONGO_PASSWORD` | Password a scelta, solo lettere e numeri |
| Secret | `RABBITMQ_PASSWORD` | Password a scelta, solo lettere e numeri |
| Secret | `MUSICBRAINZ_SQL_CONNECTION` | La connection string del punto 3 |
| Secret | `LASTFM_API_KEY` | La API key dell'applicazione Last.fm (creata su https://www.last.fm/api/account/create, con callback `https://<DOMAIN>/api/v1/auth/lastfm/callback`) |
| Secret | `LASTFM_SHARED_SECRET` | Lo shared secret della stessa applicazione Last.fm |

Le password non devono contenere apici singoli (`'`).

**Immagini:** con un repository **privato** il piano gratuito di GitHub Packages offre solo 500 MB. In quel caso, dopo il primo deploy, rendi pubblici i pacchetti `cityfy-*` (pagina del pacchetto → *Package settings → Change visibility*). Le immagini non contengono segreti. Con un repository pubblico non ci sono limiti, e nemmeno sui minuti di Actions.

### 7. Primo deploy

Fai un merge su `main`, oppure apri **Actions → Deploy → Run workflow**. Il primo giro dura circa 15-20 minuti: l'immagine MusicBrainz viene costruita in emulazione ARM.

### 8. Import del catalogo MusicBrainz

Il pannello admin in produzione è spento, quindi l'import si avvia dalla macchina:
```bash
cd ~/cityfy
docker compose run --rm tools -X POST http://retrieve-musicbrainz:8080/api/retrieve
docker compose run --rm tools http://retrieve-musicbrainz:8080/api/retrieve/status   # avanzamento
```
Alla fine dell'import il catalogo artisti di tutti gli utenti si aggiorna da solo (`CatalogUpdated`): le nuove città useranno i generi aggiornati.

---

## Passaggio a Last.fm (una volta, al primo deploy dopo `PIANO_LASTFM.md`)

Il servizio `retrieve-spotify` non esiste più e i database si ricreano (decisione L9: nessuna migrazione degli utenti di prova).
```bash
cd ~/cityfy
docker compose up -d --remove-orphans          # toglie il container retrieve-spotify
docker volume rm cityfy_spotify-keys           # refresh token Spotify, non servono più
docker compose exec mongo mongosh -u cityfy -p "$MONGO_PASSWORD" --authenticationDatabase admin --eval 'db.getSiblingDB("cityfy").dropDatabase()'
docker compose restart                         # i servizi ricreano collection e indici
```
Su GitHub si può cancellare il segreto `SPOTIFY_CLIENT_SECRET`. Il catalogo MusicBrainz (Azure SQL) non si tocca.

## Uso quotidiano

```bash
cd ~/cityfy
docker compose ps                         # stato
docker compose logs -f api catalog    # log
docker compose restart retrieve-lastfm    # riavvio di un servizio
```

**Backup.** Due cose vanno salvate:
- **MongoDB:** ascolti e città, con
  ```bash
  docker compose exec mongo mongodump -u cityfy -p "$MONGO_PASSWORD" --authenticationDatabase admin --archive > backup.archive
  ```
- **Il volume `cityfy_api-keys`:** se va perso, i cookie non si leggono più: gli account rifanno l'accesso con Last.fm. Per le sessioni anonime è grave: l'utente perde l'accesso ai suoi dati, che poi scadono e vengono cancellati.

**Il volume `cityfy_lastfm-keys`:** cifra le chiavi di sessione Last.fm salvate. Se va perso, ogni account Last.fm va ricollegato (gli account passano a `Suspended` con `LASTFM_AUTH_FAILED`).

**Last.fm.** Il servizio `retrieve-lastfm` legge gli account collegati con una cadenza che si adatta (15 s mentre l'utente ascolta, 2 min se ha ascoltato nell'ultima ora, 15 min altrimenti) e importa lo storico al primo collegamento. Last.fm limita le richieste per IP: con un errore 29 la lettura si ferma e riparte da sola (`docker compose logs retrieve-lastfm | grep -i "rate\|29"`). Prima di aprire il login Last.fm al pubblico serve l'approvazione scritta di Last.fm (vedi `PIANO_LASTFM.md`, fase 0).

**Limiti di richieste.** Il gateway limita le richieste per rete di provenienza: un IPv4, oppure un /64 per IPv6. I valori di default sono:
- 10 sessioni nuove l'ora;
- 5 upload l'ora per rete e 10 al giorno per utente, con al massimo 3 upload in corso su tutto il server;
- 20 login Last.fm l'ora;
- 300 richieste al minuto.

Si cambiano con le variabili `RateLimits__SessionStartsPerHour`, `RateLimits__UploadsPerHour`, `RateLimits__UploadsPerUserPerDay`, `RateLimits__ConcurrentUploads`, `RateLimits__LastFmLoginsPerHour` e `RateLimits__ApiRequestsPerMinute`. Oltre il limite la risposta è `429 RATE_LIMITED`.

L'IP del client arriva da Caddy (`X-Forwarded-For`), considerato attendibile solo dalle reti in `Proxy__KnownNetworks__*`, cioè le reti Docker (già impostate nel compose). Se i log mostrano per tutti lo stesso IP 172.x, la rete Docker è diversa da quelle impostate e va aggiunta. In quel caso tutti gli utenti condividono un solo contatore.

**Eventi in diretta.** Il gateway (`api`) ora usa anche RabbitMQ: consuma `NowPlayingChanged` (coda `api.now-playing-changed`) e pubblica `UserActive` (coda `retrieve-lastfm.user-active`). `GET /api/v1/live` è uno stream SSE: nel `Caddyfile` è escluso dalla compressione e il proxy fa flush immediato. Se la barra "Stai ascoltando" non si aggiorna, controlla che lo stream non venga bufferizzato (`curl -N https://<DOMAIN>/api/v1/live` con il cookie di un account deve mostrare un `ping` ogni 25 secondi). Il gateway deve restare **una sola istanza**: con più istanze ogni evento arriverebbe a una sola.

**Fine della licenza Last.fm.** Se Last.fm revoca la licenza, i termini (9.3) chiedono di cancellare tutti i suoi dati.
- Si fa dal pannello admin (scheda Last.fm): **Cancella tutti i dati Last.fm…**, che chiama `POST /api/admin/lastfm/purge` con `{ "confirm": "DELETE ALL LASTFM DATA" }`.
- In produzione il pannello è spento (`/api/admin/*` risponde 404). Per quell'operazione va acceso temporaneamente, con `Admin__Enabled=true` e credenziali vere in `Admin__Username`/`Admin__Password`, e poi spento di nuovo.
- Cancella tutti gli account con tutti i loro dati; le sessioni anonime restano.
- Poi togli `LASTFM_API_KEY`/`LASTFM_SHARED_SECRET`, così nessuno può accedere di nuovo.

**Account.** Gli account (login con Last.fm) stanno nella collection `users` del gateway. Per cancellare un account con tutti i suoi dati c'è la cancellazione dati del pannello admin (o "Cancella i miei dati" dell'utente). Nell'applicazione Last.fm la callback deve essere `https://<DOMAIN>/api/v1/auth/lastfm/callback`.

**Sessioni senza account.** Le gestisce il gateway (`api`), nella collection `anonymous_sessions` di Mongo, e le cancella da solo un giorno dopo l'ultima visita. Per cambiare i tempi si usano le variabili `Sessions__Lifetime` (es. `1.00:00:00`), `Sessions__RenewAfter` e `Sessions__CleanupIntervalMinutes`. Un `docker compose logs api | grep "anonymous session"` mostra le sessioni create, le cancellazioni e quelle da ritentare.

## Limiti del costo zero

- **Azure SQL gratuito:** 100.000 vCore-secondi al mese. Il database va in pausa dopo un'ora senza richieste, e ogni risveglio consuma almeno mezz'ora di vCore. Con un uso leggero (qualche costruzione di città al giorno) si resta nel limite. Se lo si supera, il catalogo resta in pausa fino al mese successivo e le città non si costruiscono (`MUSICBRAINZ_UNAVAILABLE`). La soluzione è spostare il catalogo su PostgreSQL, sulla stessa macchina.
- **Oracle:** la capacità ARM gratuita non è sempre disponibile; le macchine inattive vengono recuperate (si evita con il passaggio a Pay As You Go del punto 1.5).
- **Last.fm:** limiti di richieste non dichiarati, per IP; uso solo non commerciale; serve l'approvazione scritta per le pagine pubbliche (vedi `PIANO_LASTFM.md`, fase 0).
- **Pannello admin:** è spento in produzione. Per accenderlo serve un login vero: l'utente fittizio di sviluppo non va esposto.
