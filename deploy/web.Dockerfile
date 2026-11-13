# Frontend: Caddy serves the Angular build on :80 inside the cluster (TLS and /api are on Traefik).
# Built by deploy.yml after `ng build` (build context: repository root).
FROM caddy:2-alpine

COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY cityfy-web/dist/cityfy-web/browser /srv
