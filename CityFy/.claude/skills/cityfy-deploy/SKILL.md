---
name: cityfy-deploy
description: Deploy di CityFy sul cluster K3s (solo questo progetto) — workflow GitHub Actions, immagini GHCR, manifest kustomize in deploy/k8s, risorse del cluster in deploy/cluster, segreti e variabili di GitHub, PostgreSQL condiviso, cosa aggiornare quando si aggiunge un servizio o un'impostazione. Usala insieme a k3s-cluster prima di toccare deploy/, .github/workflows/, DEPLOY.md, o quando un servizio, una variabile d'ambiente, un volume o un segreto cambia.
---

# CityFy — deploy in produzione

Le regole del cluster (sicurezza, decisioni prese, schema di pubblicazione) stanno nella skill di radice **`k3s-cluster`**: caricala insieme a questa. La procedura passo-passo per l'utente è in `DEPLOY.md`.

## In breve

- URL pubblico: **https://cityfy.app.pacoaccess.com** (Cloudflare: record A `cityfy.app`, DNS only).
- Namespace `cityfy`. Da fuori si raggiungono solo `/api/*` → `api:8080` e `/` → `web:80`, entrambi tramite Ingress Traefik con il certificato `cityfy-tls` (letsencrypt-prod).
- Deploy: push su `main` (o *Run workflow*) → `ci.yml` → immagini `ghcr.io/<owner>/cityfy-<nome>:<sha>` (linux/amd64) → tunnel SSH come `devops` (chiave limitata al tunnel) → `kubectl` come `cityfy-deployer` → segreti → `kubectl apply -k deploy/k8s/release` → `rollout status`.
- Il workflow non parte finché manca la variabile `DEPLOY_HOST`.

## File

| Percorso | Chi lo applica | Contenuto |
|---|---|---|
| `.github/workflows/deploy.yml` | GitHub | Immagini, tunnel, segreti, overlay con registry e tag, rollout |
| `deploy/k8s/base/` | GitHub (kustomize) | `config.yaml` (ConfigMap `cityfy-config` con `PUBLIC_URL`, PVC), `mongo.yaml`, `broker.yaml`, `services.yaml` (7 servizi .NET), `web.yaml`, `ingress.yaml` (Ingress + Middleware) |
| `deploy/k8s/release/` | generato dal workflow, in `.gitignore` | `images:` con `newName`/`newTag` per ogni `cityfy-*` |
| `deploy/cluster/` | **l'amministratore, a mano** | `storageclass-retain.yaml`, `postgres.yaml` (condiviso), `cityfy-access.yaml` (namespace + RBAC della CI), `traefik-config.yaml` |
| `deploy/Caddyfile`, `deploy/web.Dockerfile` | — | Caddy su `:80` senza TLS, solo file statici Angular con fallback su `index.html` |
| `deploy/musicbrainz.Dockerfile` | — | aspnet + `bzip2` (estrazione del dump) |

## Risorse nel namespace

| Risorsa | Note |
|---|---|
| `mongo` (StatefulSet, `mongo:7`) | utente root `cityfy`; password letta solo al primo avvio; PVC `data-mongo-0` 10 Gi retain |
| `broker` (StatefulSet, `rabbitmq:4-alpine`) | pod sempre `broker-0` (nome del nodo RabbitMQ stabile); PVC 2 Gi retain |
| `api` | `replicas: 1` obbligatorio (SSE), Recreate, PVC `api-keys` (DataProtection, retain), `Proxy__KnownNetworks__0=10.42.0.0/16` |
| `catalog`, `city-generator` | RollingUpdate |
| `orchestrator` | Recreate, una sola istanza (debounce in memoria) |
| `retrieve-dump-spotify` | Recreate, PVC `uploads` (local-path) |
| `retrieve-lastfm` | Recreate, una sola istanza, PVC `lastfm-keys` (retain, mai perderlo) |
| `retrieve-musicbrainz` | Recreate, PVC `mb-dumps` 20 Gi (local-path), migration all'avvio (startupProbe lunga) |
| `web` | Caddy |

Connessioni come nell'AppHost: Mongo + RabbitMQ per tutti tranne RetrieveMusicBrainz; PostgreSQL `musicbrainz` per RetrieveMusicBrainz e Api. Le connection string con password usano `$(MONGO_PASSWORD)`/`$(RABBITMQ_PASSWORD)`, definite **prima** nella lista `env`. Probe: `/alive` per startup e liveness, `/health` per readiness.

## Segreti e variabili di GitHub (ambiente `production`)

- **Variabili:** `DEPLOY_HOST` (`185.2.101.25`), `DEPLOY_USER` (`devops`), `DEPLOY_KNOWN_HOSTS`, `GHCR_PULL_USER`.
- **Segreti:** `DEPLOY_SSH_KEY`, `CITYFY_KUBECONFIG`, `GHCR_PULL_TOKEN` (PAT classic `cityfy-cluster-pull`, solo `read:packages`, senza scadenza per scelta dell'utente), `MONGO_PASSWORD`, `RABBITMQ_PASSWORD` (solo lettere e numeri), `MUSICBRAINZ_SQL_CONNECTION` (ruolo `cityfy`, proprietario del database `musicbrainz` su `postgres.data.svc.cluster.local`; formato Npgsql `Host=...;Database=...;Username=...;Password=...`), `LASTFM_API_KEY`, `LASTFM_SHARED_SECRET`.
- Il workflow li trasforma nei Secret `cityfy-secrets` (chiavi `mongo-password`, `rabbitmq-password`, `musicbrainz-connection`, `lastfm-api-key`, `lastfm-shared-secret`) e `ghcr-pull`.

## Quando cambia qualcosa

- **Servizio nuovo:**
  - progetto nel ciclo `nome:Progetto` di `deploy.yml`;
  - nome nell'elenco `images` dell'overlay e in quello di `rollout status`;
  - Service + Deployment in `services.yaml`, con le stesse connessioni dell'AppHost;
  - `services__<Risorsa>__http__0` nei servizi che lo chiamano.
- **Impostazione nuova:** variabile `env` nel Deployment; se è un segreto, aggiungerlo in tre posti: segreto GitHub, controllo "Check the configuration", `kubectl create secret generic cityfy-secrets`, con una chiave nuova.
- **Volume nuovo:** PVC in `config.yaml`. Usare `local-path-retain` se il dato non si può ricreare, e mettere il Deployment in `Recreate`.
- **Dominio:** sta in `config.yaml` (`PUBLIC_URL`) e in `ingress.yaml` (host e tls), più la callback dell'applicazione Last.fm.
- **Risorse del namespace di tipo nuovo** (per esempio CronJob, HPA): il Role `deployer` in `deploy/cluster/cityfy-access.yaml` va esteso, e lo applica l'amministratore **prima** del deploy.
- Aggiornare `DEPLOY.md` e questa skill insieme ai file.

## Verifica locale (senza cluster)

`kubectl kustomize deploy/k8s/base` deve renderizzare senza errori (kubectl di Docker Desktop: `C:\Program Files\Docker\Docker\resources\bin\kubectl.exe`). Non lanciare `kubectl apply` contro il cluster: lo fanno la CI o l'utente.
