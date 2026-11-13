# Cluster resources (applied BY HAND by the administrator, never by CI)

Shared infrastructure of the K3s cluster `k8s-lab-01` and the access of CityFy's CI.
The GitHub workflow cannot apply these: its ServiceAccount only has rights inside the `cityfy` namespace.

| File | What it is | Scope |
|---|---|---|
| `storageclass-retain.yaml` | StorageClass `local-path-retain` (data survives the deletion of the PVC) | cluster |
| `postgres.yaml` | Shared PostgreSQL 17, namespace `data`, service `postgres.data.svc.cluster.local:5432` | all the applications |
| `cityfy-access.yaml` | Namespace `cityfy` + ServiceAccount `cityfy-deployer` used by GitHub Actions | CityFy |
| `traefik-config.yaml` | Traefik: real client IP and longer read timeout (uploads) | cluster, restarts Traefik |

Apply from Windows (tunnel active) with the admin kubeconfig:

```powershell
$env:KUBECONFIG = "$env:USERPROFILE\.kube\k3s-headlamp.yaml"
kubectl apply --dry-run=server -f deploy/cluster/<file>.yaml   # check first
kubectl apply -f deploy/cluster/<file>.yaml
```

The step-by-step procedure (order, secrets, checks) is in `DEPLOY.md`.
