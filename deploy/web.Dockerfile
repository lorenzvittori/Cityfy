# Frontend + public entry point: Caddy serves the Angular build, proxies /api to the Api and gets
# the HTTPS certificate (Let's Encrypt) for $DOMAIN by itself.
# Built by deploy.yml after `ng build` (build context: repository root).
FROM caddy:2-alpine

COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY cityfy-web/dist/cityfy-web/browser /srv
