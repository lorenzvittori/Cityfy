---
name: k3s-cluster
description: Server e cluster Kubernetes privato dell'utente (VPS Contabo k8s-lab-01, K3s single-node) — risorse, componenti (Traefik, cert-manager, local-path, PostgreSQL condiviso), modello di sicurezza (API privata via tunnel SSH, porte pubbliche), decisioni già prese e vietate, schema per pubblicare un'applicazione (namespace, Ingress+TLS, storage, accesso della CI da GitHub Actions), procedura sicura per le modifiche. Usala prima di scrivere manifest Kubernetes, workflow di deploy, o di proporre qualunque modifica a server, rete, firewall, SSH o cluster.
---

# Cluster K3s `k8s-lab-01` (VPS Contabo)

Infrastruttura privata dell'utente per le applicazioni aziendali in container. Vale per **tutti** i progetti che ci vengono pubblicati; i dettagli di una singola applicazione stanno nelle skill di quel progetto.

**Mai** scrivere in questa skill, nel repository o in una risposta il contenuto di kubeconfig, token, chiavi private o password.

## Come lavorare con l'utente

- Guidarlo **passo dopo passo**: comandi precisi, cosa fa ognuno, e un **checkpoint** verificabile dopo ogni passo (cosa deve stampare). Non saltare passaggi.
- Prima di una modifica rischiosa (rete, firewall, SSH, Traefik, storage, cancellazioni) spiegare cosa cambia, cosa può rompersi e come tornare indietro, e **aspettare l'ok**.
- Preferire `kubectl apply --dry-run=server -f ...` prima di ogni `apply`.
- Le modifiche al server le esegue l'utente: Claude prepara file e comandi.

## Server

| | |
|---|---|
| Provider / piano | Contabo Cloud VPS 4 — 6 CPU, 11 GB RAM, 193 GB disco, x86-64 (immagini `linux/amd64`) |
| OS | Ubuntu 24.04.5 LTS, kernel 6.8, timezone Europe/Rome |
| Hostname | `k8s-lab-01` |
| IP pubblici | IPv4 `185.2.101.25`, IPv6 `2a02:c207:2375:3506::1` |
| Utenti | `root` (accesso diretto mantenuto **intenzionalmente**), `devops`; entrambi con chiave Ed25519 |
| Budget | sotto i 10 €/mese |

**Firewall (UFW):** incoming deny, outgoing allow, routed deny. Aperte al pubblico: **22** (SSH), **80**, **443**. Consentite le reti interne `10.42.0.0/16` (pod) e `10.43.0.0/16` (service). **6443** (API Kubernetes) e **10250** (kubelet) **non** sono pubbliche.

## Cluster

- **K3s** `v1.37.2+k3s1`, installato con `--secrets-encryption`; nodo singolo, control-plane e worker insieme.
- Flannel (CNI), CoreDNS, Metrics Server, Local Path Provisioner, **Traefik** (ingress controller, unico ingresso pubblico su 80/443, CRD `traefik.io/v1alpha1`). Le NetworkPolicy sono applicate (controller integrato di K3s).
- **cert-manager** v1.21.3 (Helm, namespace `cert-manager`). ClusterIssuer `letsencrypt-staging` e `letsencrypt-prod`, solver HTTP-01 con Traefik.
- **Headlamp** (Helm, namespace `headlamp`, ServiceAccount `headlamp` cluster-admin): già installato e funzionante, **non dedicarci altro tempo**.
- **kubectl da Windows:** kubeconfig `%USERPROFILE%\.kube\k3s-headlamp.yaml` (server `https://127.0.0.1:6443`) attraverso un tunnel SSH automatico: script `%USERPROFILE%\Start-K3sTunnel.ps1` (`ssh -N -L 6443:127.0.0.1:6443 root@185.2.101.25` in un ciclo), attività pianificata "K3s SSH Tunnel" all'accesso dell'utente. Da fare: test di riavvio completo di Windows.

### Storage

| StorageClass | Reclaim | Quando |
|---|---|---|
| `local-path` (default) | **Delete**: cancellare il PVC cancella i dati | cache, file temporanei, dati riscaricabili |
| `local-path-retain` | Retain: la cartella resta sul nodo | database, chiavi di cifratura, tutto ciò che conta |

`local-path-retain` si crea con `storageclass-retain.yaml` (nel repo CityFy: `deploy/cluster/`). I volumi stanno su `/var/lib/rancher/k3s/storage/` del nodo. **Nessun backup configurato** (decisione dell'utente, per ora): non proporlo di propria iniziativa, ma segnalare quali dati non sarebbero recuperabili.

### PostgreSQL condiviso

- È il database relazionale **unico** del cluster per tutte le applicazioni: scelto dall'utente al posto di SQL Server, perché è più leggero. Non proporre SQL Server né un database per applicazione.
- Namespace `data`, StatefulSet `postgres` (`postgres:17-alpine`, versione maggiore fissata), volume `local-path-retain` da 30 Gi, `/dev/shm` in memoria da 256 Mi.
- Indirizzo interno: `postgres.data.svc.cluster.local:5432`; mai esposto fuori dal cluster, senza TLS (il traffico resta sul nodo).
- Password del superutente `postgres` nel Secret `data/postgres-superuser`, creato a mano. Ogni applicazione ha il **suo ruolo e il suo database**, di cui il ruolo è proprietario (`CREATE ROLE <app> LOGIN PASSWORD '...'; CREATE DATABASE <db> OWNER <app>;`). Mai usare `postgres` nelle applicazioni.
- Amministrazione: `kubectl -n data exec -it postgres-0 -- psql -U postgres` (dal pod non chiede password).
- Accesso solo dai namespace con l'etichetta `pacoaccess.com/postgres-client: "true"` (NetworkPolicy `postgres-clients`).
- Connection string (Npgsql): `Host=postgres.data.svc.cluster.local;Port=5432;Database=<db>;Username=<ruolo>;Password=<...>`.
- Un passaggio di versione maggiore richiede dump e restore di tutti i database: va pianificato con l'utente.
- Manifest: `deploy/cluster/postgres.yaml` del repo CityFy (applicato a mano).

### Dominio

- `pacoaccess.com`, DNS su **Cloudflare**, record in modalità **DNS only** (nuvola grigia): il TLS lo fa cert-manager, non Cloudflare.
- `app.pacoaccess.com` → `185.2.101.25`: app dimostrativa `whoami` (namespace `app-test`, secret TLS `app-pacoaccess-tls`, certificato prod valido fino all'11/02/2027). **Non cancellare** `app-test` né le sue risorse senza concordare la pulizia. È utile per verificare gli header (IP del client visto da Traefik).
- Le applicazioni usano un sottodominio proprio sotto `app.pacoaccess.com`, ad esempio `cityfy.app.pacoaccess.com`, ognuno con il suo record A.

## Decisioni già prese (non riproporre alternative)

- **Niente WireGuard**: è stato rimosso.
- Non modificare `PermitRootLogin` né l'autenticazione a password di SSH senza chiedere.
- **API Kubernetes mai pubblica**: si raggiunge solo con un tunnel SSH.
- Headlamp e l'API non si espongono su Internet; amministrazione separata dalla pubblicazione.
- Porte 80/443 aperte per la pubblicazione controllata delle applicazioni, una per una tramite Ingress.
- Firewall e rete: nessuna modifica senza spiegazione, verifica preventiva e una procedura che mantenga aperte le sessioni SSH esistenti (una seconda sessione sempre aperta).

## Pubblicare un'applicazione (schema)

1. **Namespace dedicato** creato dall'amministratore, con l'etichetta `pacoaccess.com/postgres-client: "true"` solo se usa PostgreSQL.
2. **Identità della CI:** ServiceAccount `<app>-deployer` con un Role limitato al namespace: configmap, secret, service, PVC, deployment, statefulset, ingress, networkpolicy, middleware Traefik; lettura di pod, log ed eventi. **Mai** ClusterRole o RBAC. Il token sta in un Secret `kubernetes.io/service-account-token`; il kubeconfig (server `https://127.0.0.1:6443`) va in un segreto di GitHub. Modello: `deploy/cluster/cityfy-access.yaml`.
3. **Accesso di GitHub Actions** con un tunnel SSH, non con l'API pubblica:
   - chiave Ed25519 dedicata nell'`authorized_keys` di `devops`, con `restrict,port-forwarding,permitopen="127.0.0.1:6443",command="/bin/false"`, cioè solo il tunnel, senza shell;
   - host key fissata in una variabile (`DEPLOY_KNOWN_HOSTS`), niente `ssh-keyscan` nel workflow;
   - nel job: `ssh -M -S <sock> -f -N -L 6443:127.0.0.1:6443 devops@185.2.101.25`, poi `kubectl`, poi `ssh -S <sock> -O exit`;
   - versione di `kubectl` con la stessa minor del cluster.
4. **Immagini** `linux/amd64` su GHCR. Con pacchetti privati serve un pull secret `docker-registry` creato dal workflow da un PAT classic con **solo `read:packages`** e **senza scadenza** (scelta dell'utente: un token scaduto blocca in silenzio i pod che ripartono). Il `GITHUB_TOKEN` non basta, perché scade con il job.
5. **Ingress:** `ingressClassName: traefik`, annotazione `cert-manager.io/cluster-issuer: letsencrypt-prod` (staging per le prove), `tls` con un `secretName` dedicato. Middleware Traefik nel namespace dell'app per il redirect HTTPS e gli header di sicurezza, referenziati come `<namespace>-<nome>@kubernetescrd`. Traefik non comprime né bufferizza di default (va bene per SSE).
6. **Pod:** `runAsNonRoot`, `automountServiceAccountToken: false`, `enableServiceLinks: false`, requests/limits di memoria (11 GB in tutto; PostgreSQL ha un limite di 1 GB), probe su liveness e readiness. Più repliche solo se l'app lo regge.
7. **Segreti** creati dal workflow con `kubectl create secret ... --dry-run=client -o yaml | kubectl apply -f -`; mai nel repository.
8. **Client IP:** Traefik sta nella rete dei pod (`10.42.0.0/16`), che va indicata come proxy fidato per `X-Forwarded-For`. L'IP reale arriva solo con `externalTrafficPolicy: Local` sul service di Traefik (`deploy/cluster/traefik-config.yaml`, HelmChartConfig che riavvia Traefik). Lo stesso file alza `readTimeout` a 600 s, perché il default di Traefik v3 è 60 s e interrompe gli upload lunghi.

## Checkpoint utili

```powershell
$env:KUBECONFIG = "$env:USERPROFILE\.kube\k3s-headlamp.yaml"
Test-NetConnection 127.0.0.1 -Port 6443          # tunnel attivo: TcpTestSucceeded True
kubectl get nodes                                # k8s-lab-01 Ready
kubectl get clusterissuer                        # letsencrypt-staging / -prod Ready
kubectl -n <ns> get pods,ingress,certificate
curl.exe -I https://<host>                       # 200 senza -k
```
