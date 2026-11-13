# Deploy di Cityfy (cluster K3s su Contabo)

Cityfy gira nel namespace `cityfy` del cluster K3s `k8s-lab-01` (VPS Contabo, x86-64, nodo singolo). **GitHub Actions** compila, esegue i test, crea le immagini e aggiorna il cluster a ogni push su `main`. Il catalogo MusicBrainz sta sul **PostgreSQL condiviso** del cluster (namespace `data`), che serve anche ad altre applicazioni.

```
Browser ──https──► Traefik (Ingress, cert-manager) ──/api──► api ──► catalog, city-generator, orchestrator,
                     cityfy.app.pacoaccess.com    └── /  ──► web       retrieve-lastfm, retrieve-dump-spotify,
                                                              (Caddy)   retrieve-musicbrainz
                                                                       mongo, broker (namespace cityfy)
                                                                       postgres.data (condiviso) ◄── retrieve-musicbrainz, api

GitHub Actions ──ssh, solo tunnel──► 127.0.0.1:6443 (API Kubernetes, mai pubblica)
               └─ kubectl come ServiceAccount cityfy-deployer: diritti solo nel namespace cityfy
```

Da Internet si raggiunge solo Traefik, sulle porte 80 e 443, e solo `/api/*` arriva a un servizio .NET (il gateway). Il resto resta dentro il cluster.

| File | A cosa serve |
|---|---|
| `.github/workflows/ci.yml` | Build e test (.NET e Angular) a ogni push e pull request |
| `.github/workflows/deploy.yml` | Su `main`: CI, poi immagini amd64 su GitHub Container Registry, poi tunnel SSH e `kubectl apply` |
| `deploy/k8s/base/` | I manifest di CityFy (kustomize): rispecchiano l'AppHost, vanno aggiornati quando cambia |
| `deploy/cluster/` | Risorse del cluster che applica **l'amministratore a mano**: StorageClass, PostgreSQL condiviso, accesso della CI, Traefik |
| `deploy/Caddyfile`, `deploy/web.Dockerfile` | Frontend: Caddy serve la build Angular in HTTP dentro il cluster |
| `deploy/musicbrainz.Dockerfile` | RetrieveMusicBrainz con `bzip2`, che serve per estrarre il dump |

---

## Una volta sola

Ogni passo finisce con un **checkpoint**: non andare avanti finché il checkpoint non torna come descritto. I comandi `kubectl` si lanciano da Windows, con il tunnel SSH attivo e il kubeconfig amministratore:

```powershell
$env:KUBECONFIG = "$env:USERPROFILE\.kube\k3s-headlamp.yaml"
kubectl get nodes   # k8s-lab-01 Ready
```

I comandi marcati **(server)** si lanciano in una sessione SSH come `root`. Tieni sempre aperta una seconda sessione SSH mentre tocchi qualcosa che riguarda SSH.

### 0. Controlli preliminari (server, sola lettura)

```bash
grep -o -m1 -w avx /proc/cpuinfo                       # MongoDB 7 richiede AVX: deve stampare "avx"
sshd -T | grep -Ei '^(allowtcpforwarding|disableforwarding)'   # allowtcpforwarding yes, disableforwarding no
df -h /var/lib/rancher                                 # spazio libero per i volumi (servono ~70 GB)
```

**Checkpoint:** `avx` presente, forwarding TCP permesso, spazio sufficiente. Se `allowtcpforwarding` è `no`, fermati: va cambiato `sshd_config`, ed è una modifica da concordare.

### 1. DNS su Cloudflare

Aggiungi un record: **Type** `A`, **Name** `cityfy.app`, **Content** `185.2.101.25`, **Proxy** *DNS only* (nuvola grigia), **TTL** *Auto*.

```powershell
Resolve-DnsName cityfy.app.pacoaccess.com -Type A
```

**Checkpoint:** risponde `185.2.101.25`.

### 2. StorageClass con dati conservati

`local-path` ha `reclaimPolicy: Delete`: cancellare un PVC cancella i dati. Per i database e le chiavi di cifratura c'è `local-path-retain`, che conserva la cartella sul nodo anche se il PVC viene cancellato.

```powershell
kubectl apply --dry-run=server -f deploy/cluster/storageclass-retain.yaml
kubectl apply -f deploy/cluster/storageclass-retain.yaml
kubectl get storageclass
```

**Checkpoint:** compaiono `local-path (default)` e `local-path-retain`, quest'ultima con `RECLAIMPOLICY Retain`. La classe di default non cambia.

### 3. PostgreSQL condiviso (namespace `data`)

PostgreSQL 17 (`postgres:17-alpine`): leggero (~100 MB di RAM), senza limiti di dimensione, con l'immagine fissata alla versione maggiore. Ogni applicazione avrà un suo ruolo e un suo database, mai il superutente `postgres`. Si connettono solo i namespace con l'etichetta `pacoaccess.com/postgres-client=true`.

1. Genera la password del superutente `postgres` e salvala nel tuo password manager:
   ```powershell
   function New-Password { $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); ([Convert]::ToBase64String($b) -replace '[^a-zA-Z0-9]','').Substring(0,24) + 'Aa1' }
   $pg = New-Password; $pg   # copiala nel password manager
   ```
2. Crea namespace e segreto, poi il server:
   ```powershell
   kubectl create namespace data
   kubectl -n data create secret generic postgres-superuser --from-literal=password="$pg"
   kubectl apply --dry-run=server -f deploy/cluster/postgres.yaml
   kubectl apply -f deploy/cluster/postgres.yaml
   kubectl -n data rollout status statefulset/postgres --timeout=5m
   kubectl -n data logs postgres-0 --tail=20
   ```
   **Checkpoint:** `postgres-0` è `Running` e `1/1`; nel log c'è `database system is ready to accept connections`.
3. Crea ruolo e database di CityFy. Genera la password (`$cf = New-Password; $cf`, salvala), poi apri `psql` dentro il pod. Dall'interno del container non chiede password:
   ```powershell
   kubectl -n data exec -it postgres-0 -- psql -U postgres
   ```
   e scrivi, sostituendo `<password cityfy>`:
   ```sql
   CREATE ROLE cityfy LOGIN PASSWORD '<password cityfy>';
   CREATE DATABASE musicbrainz OWNER cityfy;
   \l musicbrainz
   \q
   ```
   **Checkpoint:** `\l` mostra `musicbrainz` con owner `cityfy`. Al primo avvio RetrieveMusicBrainz crea da solo collation e tabelle (migration). Il proprietario del database ha i diritti per farlo.
4. La connection string, che andrà nel segreto `MUSICBRAINZ_SQL_CONNECTION`, è:
   ```
   Host=postgres.data.svc.cluster.local;Port=5432;Database=musicbrainz;Username=cityfy;Password=<password cityfy>
   ```
   Senza TLS: il traffico non esce dal nodo, e la NetworkPolicy lascia entrare solo i namespace autorizzati.

### 4. Namespace `cityfy` e identità della CI

`cityfy-access.yaml` crea il namespace, con l'etichetta che dà accesso a PostgreSQL, e il ServiceAccount `cityfy-deployer`. Questo può gestire deployment, servizi, segreti, volumi e ingress **solo** in `cityfy`: nessun diritto sul cluster, sugli altri namespace o sui permessi.

```powershell
kubectl apply --dry-run=server -f deploy/cluster/cityfy-access.yaml   # 4 errori "namespaces cityfy not found": normali
kubectl apply -f deploy/cluster/cityfy-access.yaml
```

Il dry-run non crea davvero il namespace, quindi le risorse che ci stanno dentro non lo trovano. L'`apply` vero crea prima il namespace e poi il resto.

Crea il kubeconfig del ServiceAccount, che contiene un token: trattalo come una password.

```powershell
$token = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((kubectl -n cityfy get secret cityfy-deployer-token -o jsonpath='{.data.token}')))
$ca = kubectl -n cityfy get secret cityfy-deployer-token -o jsonpath='{.data.ca\.crt}'
@"
apiVersion: v1
kind: Config
clusters:
- name: k8s-lab-01
  cluster:
    server: https://127.0.0.1:6443
    certificate-authority-data: $ca
users:
- name: cityfy-deployer
  user:
    token: $token
contexts:
- name: cityfy
  context:
    cluster: k8s-lab-01
    user: cityfy-deployer
    namespace: cityfy
current-context: cityfy
"@ | Set-Content -Encoding ascii "$env:USERPROFILE\.kube\cityfy-deployer.yaml"

kubectl --kubeconfig "$env:USERPROFILE\.kube\cityfy-deployer.yaml" auth can-i create deployments   # yes
kubectl --kubeconfig "$env:USERPROFILE\.kube\cityfy-deployer.yaml" get nodes                       # Forbidden
kubectl --kubeconfig "$env:USERPROFILE\.kube\cityfy-deployer.yaml" -n data get pods                # Forbidden
```

**Checkpoint:** `yes`, poi due `Forbidden`. Il contenuto del file va nel segreto `CITYFY_KUBECONFIG` (passo 7). Dopo, cancella il file da Windows.

Per revocare l'accesso della CI: `kubectl -n cityfy delete secret cityfy-deployer-token`, poi riapplica `cityfy-access.yaml` e rigenera il kubeconfig.

### 5. Chiave SSH della CI (solo tunnel)

Il runner di GitHub raggiunge l'API Kubernetes come fai tu da Windows: con un tunnel SSH verso `127.0.0.1:6443`. La chiave è dedicata, appartiene all'utente `devops` e sul server viene **limitata al solo tunnel**: niente shell, niente altri inoltri. Firewall e configurazione di `sshd` non cambiano.

1. **Windows:** crea la chiave, senza passphrase:
   ```powershell
   ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\cityfy_github_deploy" -N '""' -C github-cityfy-deploy
   Get-Content "$env:USERPROFILE\.ssh\cityfy_github_deploy.pub"
   ```
2. **(server)** Aggiungi la chiave pubblica a `devops` con le restrizioni davanti. La riga è una sola:
   ```bash
   cp -a /home/devops/.ssh/authorized_keys /home/devops/.ssh/authorized_keys.bak
   echo 'restrict,port-forwarding,permitopen="127.0.0.1:6443",command="/bin/false" ssh-ed25519 AAAA...incolla... github-cityfy-deploy' >> /home/devops/.ssh/authorized_keys
   tail -n 1 /home/devops/.ssh/authorized_keys
   ```
   Le chiavi già presenti non cambiano.
3. **Windows**, prova della chiave (con il tunnel abituale su 6443 attivo, la prova usa 16443):
   ```powershell
   $key = "$env:USERPROFILE\.ssh\cityfy_github_deploy"
   ssh -i $key -o IdentitiesOnly=yes devops@185.2.101.25          # deve chiudersi subito, senza shell
   ssh -i $key -o IdentitiesOnly=yes -N -L 16443:127.0.0.1:6443 devops@185.2.101.25
   ```
   Lascia aperta l'ultima finestra e in un'altra prova:
   ```powershell
   kubectl --kubeconfig "$env:USERPROFILE\.kube\cityfy-deployer.yaml" --server https://127.0.0.1:16443 auth can-i list pods   # yes
   ```
   Infine verifica che un altro inoltro venga rifiutato: `ssh -i $key -o IdentitiesOnly=yes -N -L 16444:127.0.0.1:22 devops@185.2.101.25`, poi `Test-NetConnection 127.0.0.1 -Port 16444`. L'output SSH deve dire *administratively prohibited*.

   **Checkpoint:** nessuna shell, tunnel 6443 funzionante, altri inoltri rifiutati, e le tue sessioni root e devops funzionano come prima.
4. **Host key** da fissare in GitHub, così il runner non si fida di un server diverso. Confronta l'impronta vista da Windows con quella del server:
   ```powershell
   ssh-keyscan -t ed25519 185.2.101.25 2>$null | Tee-Object -Variable kh | ssh-keygen -lf -
   ```
   ```bash
   ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub   # (server)
   ```
   **Checkpoint:** le due impronte `SHA256:...` sono identiche. La riga `$kh` (`185.2.101.25 ssh-ed25519 AAAA...`) va nella variabile `DEPLOY_KNOWN_HOSTS`.

### 6. Token per scaricare le immagini

Le immagini `cityfy-*` sono private, e il cluster le scarica anche giorni dopo il deploy, per esempio quando un pod riparte. Il token del job scade con il job, quindi ne serve uno dedicato.

Su GitHub: **Settings (del tuo profilo) → Developer settings → Personal access tokens → Tokens (classic) → Generate new token**:
- **Note:** `cityfy-cluster-pull`;
- **Expiration:** *No expiration*. Un token scaduto bloccherebbe in silenzio i pod che ripartono (`ImagePullBackOff`);
- **Scope:** **solo `read:packages`**. Chi lo ottenesse potrebbe solo scaricare le immagini, che non contengono segreti.

Se pensi che il token sia stato esposto, revocalo dalla stessa pagina, creane uno nuovo, aggiorna `GHCR_PULL_TOKEN` e rilancia il deploy.

### 7. Configurazione su GitHub

Serve un utente amministratore del repository. In **Settings → Environments** crea l'ambiente `production`, che il job di deploy usa. Opzionale: *Required reviewers* per approvare ogni deploy. Poi, in **Settings → Secrets and variables → Actions**, oppure nell'ambiente `production`:

| Tipo | Nome | Valore |
|---|---|---|
| Variable | `DEPLOY_HOST` | `185.2.101.25`. Finché manca, il deploy non parte |
| Variable | `DEPLOY_USER` | `devops` |
| Variable | `DEPLOY_KNOWN_HOSTS` | La riga `$kh` del passo 5.4 |
| Variable | `GHCR_PULL_USER` | Il tuo utente GitHub (proprietario del token del passo 6) |
| Secret | `DEPLOY_SSH_KEY` | Contenuto del file **privato** `cityfy_github_deploy` |
| Secret | `CITYFY_KUBECONFIG` | Contenuto di `cityfy-deployer.yaml` (passo 4) |
| Secret | `GHCR_PULL_TOKEN` | Il token del passo 6 |
| Secret | `MONGO_PASSWORD` | `New-Password`: solo lettere e numeri |
| Secret | `RABBITMQ_PASSWORD` | `New-Password`: solo lettere e numeri |
| Secret | `MUSICBRAINZ_SQL_CONNECTION` | La connection string del passo 3.4 |
| Secret | `LASTFM_API_KEY` | API key dell'applicazione Last.fm, con callback `https://cityfy.app.pacoaccess.com/api/v1/auth/lastfm/callback` |
| Secret | `LASTFM_SHARED_SECRET` | Shared secret della stessa applicazione |

Si possono cancellare i vecchi segreti `SPOTIFY_CLIENT_SECRET` e la variabile `DOMAIN`: il dominio ora sta nei manifest (`deploy/k8s/base/config.yaml` e `ingress.yaml`).

La password di Mongo viene letta **solo al primo avvio**, con il volume vuoto. Se cambi poi `MONGO_PASSWORD`, va cambiata anche dentro Mongo (`db.changeUserPassword`), altrimenti i servizi non si connettono più.

### 8. Traefik: IP reale del client e upload lunghi (consigliato prima dell'apertura al pubblico)

Senza questa modifica Traefik vede come mittente l'indirizzo interno del nodo. Così i limiti di richieste per rete di Cityfy contano tutti gli utenti come uno solo. Inoltre Traefik v3 chiude dopo 60 secondi una richiesta ancora in lettura, e un export Spotify da 200 MB su una connessione lenta non ci sta. **Applicarla riavvia Traefik**: tutte le applicazioni restano qualche secondo senza HTTP/HTTPS.

```powershell
kubectl -n kube-system get helmchartconfig                    # se esiste già "traefik", fermati: va unito, non sovrascritto
kubectl apply --dry-run=server -f deploy/cluster/traefik-config.yaml
kubectl apply -f deploy/cluster/traefik-config.yaml
kubectl -n kube-system get pods -w                            # attendi il nuovo pod traefik Running (Ctrl+C)
kubectl -n kube-system get svc traefik -o jsonpath='{.spec.externalTrafficPolicy}'   # Local
curl.exe -s https://app.pacoaccess.com | Select-String 'RemoteAddr|X-Forwarded-For|X-Real-Ip'
```

**Checkpoint:** `whoami` risponde ancora, e `X-Real-Ip` / `X-Forwarded-For` mostrano il tuo IP pubblico, non un `10.x`. Con più nodi, `externalTrafficPolicy: Local` va rivisto.

### 9. Primo deploy

Fai un merge su `main`, oppure apri **Actions → Deploy → Run workflow**. Il job `deploy` mostra alla fine pod, ingress, certificato ed eventi.

```powershell
kubectl -n cityfy get pods                          # tutti Running e READY
kubectl -n cityfy get certificate                   # cityfy-tls READY True (può servire un minuto)
curl.exe -I https://cityfy.app.pacoaccess.com       # 200, con strict-transport-security
curl.exe -I http://cityfy.app.pacoaccess.com        # 308 verso https
curl.exe -si https://cityfy.app.pacoaccess.com/api/v1/me   # 401 dall'Api (senza cookie), non la pagina HTML
```

Al primo giro i servizi possono riavviarsi un paio di volte (`RESTARTS` > 0) mentre Mongo e RabbitMQ partono: è normale, se poi restano stabili.

### 10. Import del catalogo MusicBrainz

Il pannello admin in produzione è spento, quindi l'import si avvia con un pod temporaneo:
```powershell
kubectl -n cityfy run mb --rm -it --restart=Never --image=curlimages/curl -- -sS -X POST http://retrieve-musicbrainz:8080/api/retrieve
kubectl -n cityfy run mb --rm -it --restart=Never --image=curlimages/curl -- -sS http://retrieve-musicbrainz:8080/api/retrieve/status   # avanzamento
```
Alla fine dell'import il catalogo artisti di tutti gli utenti si aggiorna da solo (`CatalogUpdated`): le nuove città useranno i generi aggiornati.

---

## Uso quotidiano

```powershell
kubectl -n cityfy get pods                                  # stato
kubectl -n cityfy logs deploy/api -f                        # log (anche: catalog, retrieve-lastfm, ...)
kubectl -n cityfy rollout restart deploy/retrieve-lastfm    # riavvio di un servizio
kubectl -n cityfy rollout undo deploy/api                   # torna all'immagine precedente
```
Headlamp mostra le stesse cose nel namespace `cityfy`.

**Dati che contano.** I backup non sono ancora configurati (decisione presa). `local-path-retain` protegge dalla cancellazione di un PVC, **non** dalla perdita del disco del VPS. Contano:
- **MongoDB** (PVC `data-mongo-0`): ascolti, città, account, sessioni;
- **`api-keys`:** se va perso, i cookie non si leggono più: gli account rifanno l'accesso con Last.fm. Per le sessioni anonime è grave: l'utente perde l'accesso ai suoi dati, che poi scadono e vengono cancellati;
- **`lastfm-keys`:** cifra le chiavi di sessione Last.fm salvate. Se va perso, ogni account Last.fm va ricollegato (gli account passano a `Suspended` con `LASTFM_AUTH_FAILED`);
- **PostgreSQL** (`data/data-postgres-0`): il catalogo si può reimportare dal dump, ma ci vogliono ore. Gli altri database ospitati lì dipendono dalle rispettive applicazioni.

**Last.fm.** Il servizio `retrieve-lastfm` legge gli account collegati con una cadenza che si adatta (15 s mentre l'utente ascolta, 2 min se ha ascoltato nell'ultima ora, 15 min altrimenti) e importa lo storico al primo collegamento. Last.fm limita le richieste per IP: con un errore 29 la lettura si ferma e riparte da sola (`kubectl -n cityfy logs deploy/retrieve-lastfm | Select-String "rate|29"`). Prima di aprire il login Last.fm al pubblico serve l'approvazione scritta di Last.fm (vedi `PIANO_LASTFM.md`, fase 0).

**Limiti di richieste.** Il gateway limita le richieste per rete di provenienza: un IPv4, oppure un /64 per IPv6. I valori di default sono:
- 10 sessioni nuove l'ora;
- 5 upload l'ora per rete e 10 al giorno per utente, con al massimo 3 upload in corso su tutto il server;
- 20 login Last.fm l'ora;
- 300 richieste al minuto.

Si cambiano con le variabili `RateLimits__SessionStartsPerHour`, `RateLimits__UploadsPerHour`, `RateLimits__UploadsPerUserPerDay`, `RateLimits__ConcurrentUploads`, `RateLimits__LastFmLoginsPerHour` e `RateLimits__ApiRequestsPerMinute` (in `deploy/k8s/base/services.yaml`, deployment `api`). Oltre il limite la risposta è `429 RATE_LIMITED`.

L'IP del client arriva da Traefik (`X-Forwarded-For`), considerato attendibile solo dalla rete dei pod (`Proxy__KnownNetworks__0: 10.42.0.0/16`). Se i log mostrano per tutti lo stesso IP `10.x`, Traefik non vede l'IP reale (passo 8). In quel caso tutti gli utenti condividono un solo contatore.

**Eventi in diretta.** Il gateway (`api`) consuma `NowPlayingChanged` (coda `api.now-playing-changed`) e pubblica `UserActive` (coda `retrieve-lastfm.user-active`). `GET /api/v1/live` è uno stream SSE: Traefik non lo bufferizza e non lo comprime. Non aggiungere un middleware `compress` all'Ingress. Se la barra "Stai ascoltando" non si aggiorna, controlla lo stream: `curl.exe -N https://cityfy.app.pacoaccess.com/api/v1/live`, con il cookie di un account, deve mostrare un `ping` ogni 25 secondi. Il gateway deve restare **una sola istanza** (`replicas: 1`): con più istanze ogni evento arriverebbe a una sola.

**Fine della licenza Last.fm.** Se Last.fm revoca la licenza, i termini (9.3) chiedono di cancellare tutti i suoi dati.
- Si fa dal pannello admin (scheda Last.fm): **Cancella tutti i dati Last.fm…**, che chiama `POST /api/admin/lastfm/purge` con `{ "confirm": "DELETE ALL LASTFM DATA" }`.
- In produzione il pannello è spento (`/api/admin/*` risponde 404). Per quell'operazione va acceso temporaneamente, con `Admin__Enabled=true` e credenziali vere in `Admin__Username`/`Admin__Password`, e poi spento di nuovo.
- Cancella tutti gli account con tutti i loro dati; le sessioni anonime restano.
- Poi togli `LASTFM_API_KEY`/`LASTFM_SHARED_SECRET`, così nessuno può accedere di nuovo.

**Account.** Gli account (login con Last.fm) stanno nella collection `users` del gateway. Per cancellare un account con tutti i suoi dati c'è la cancellazione dati del pannello admin (o "Cancella i miei dati" dell'utente).

**Sessioni senza account.** Le gestisce il gateway (`api`), nella collection `anonymous_sessions` di Mongo, e le cancella da solo un giorno dopo l'ultima visita. Per cambiare i tempi si usano le variabili `Sessions__Lifetime` (es. `1.00:00:00`), `Sessions__RenewAfter` e `Sessions__CleanupIntervalMinutes`. `kubectl -n cityfy logs deploy/api | Select-String "anonymous session"` mostra le sessioni create, le cancellazioni e quelle da ritentare.

## Limiti

- **PostgreSQL condiviso:** un solo server per tutte le applicazioni. Un aggiornamento di versione maggiore (17 → 18) richiede dump e restore di tutti i database, e va pianificato.
- **Nodo singolo:** un riavvio del VPS ferma tutto; i volumi `local-path` stanno sul disco del nodo e non si spostano su un altro nodo.
- **Nessun backup** (vedi *Dati che contano*).
- **Token GHCR:** è senza scadenza. Se viene revocato, i pod che ripartono non scaricano più le immagini.
- **Last.fm:** limiti di richieste non dichiarati, per IP; uso solo non commerciale; serve l'approvazione scritta per le pagine pubbliche (vedi `PIANO_LASTFM.md`, fase 0).
- **Pannello admin:** è spento in produzione. Per accenderlo serve un login vero: l'utente fittizio di sviluppo non va esposto.
