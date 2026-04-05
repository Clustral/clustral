FROM node:20-alpine AS base

# ── Stage 1: deps ─────────────────────────────────────────────────────────────
FROM base AS deps
WORKDIR /app

RUN npm install -g bun
COPY package.json bun.lock ./
RUN bun install --frozen-lockfile

# ── Stage 2: build ────────────────────────────────────────────────────────────
FROM base AS builder
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY . .

RUN npm run build

# ── Stage 3: production ──────────────────────────────────────────────────────
FROM base AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1

RUN apk add --no-cache openssl
RUN addgroup --system --gid 1001 nodejs
RUN adduser --system --uid 1001 nextjs

# Copy standalone Next.js output.
COPY --from=builder /app/.next/standalone ./
COPY --from=builder /app/.next/static ./.next/static

# HTTPS server + entrypoint.
COPY https-server.js ./https-server.js
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

# Cert directory writable by nextjs user.
RUN mkdir -p /etc/clustral-web && chown nextjs:nodejs /etc/clustral-web

USER nextjs

EXPOSE 3000
ENV PORT=3000

ENTRYPOINT ["/docker-entrypoint.sh"]
