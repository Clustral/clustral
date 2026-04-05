# Build context: src/Clustral.Web
#   docker build -f src/Clustral.Web/Dockerfile -t clustral-web src/Clustral.Web

# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

RUN npm install -g bun

COPY package.json bun.lock ./
RUN bun install --frozen-lockfile

COPY . .
RUN bun run build

# ── Stage 2: production ──────────────────────────────────────────────────────
FROM node:20-alpine AS final
WORKDIR /app

RUN apk add --no-cache openssl

# Copy standalone Next.js output.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static

# Custom HTTPS server + entrypoint.
COPY server.ts ./server.js
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 3000

ENTRYPOINT ["/docker-entrypoint.sh"]
