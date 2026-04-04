# Build context: src/Clustral.Web
#   docker build -f src/Clustral.Web/Dockerfile -t clustral-web src/Clustral.Web

# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM oven/bun:1 AS build
WORKDIR /app

COPY package.json bun.lock ./
RUN bun install --frozen-lockfile

COPY . .
RUN bun run build

# ── Stage 2: serve with nginx + self-signed TLS ──────────────────────────────
FROM nginx:1.27-alpine AS final

# openssl for self-signed cert generation at startup.
RUN apk add --no-cache openssl

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 3000

ENTRYPOINT ["/docker-entrypoint.sh"]
