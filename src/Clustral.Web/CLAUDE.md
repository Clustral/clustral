# Clustral.Web — Claude Code Guide

Vite + React 18 + TypeScript single-page application. Provides a web dashboard
for viewing registered clusters and getting connection instructions.

---

## Tech stack

| Layer | Library |
|---|---|
| Build | Vite 8, `@tailwindcss/vite` |
| UI framework | React 18, TypeScript |
| Styling | Tailwind CSS 4, `class-variance-authority`, `clsx`, `tailwind-merge` |
| Icons | Lucide React |
| Server state | TanStack Query 5 |
| Client state | Zustand 5 |
| Routing | React Router 7 |
| Package manager | bun |

---

## File map

```
Clustral.Web/src/
├── main.tsx                       ← ReactDOM.createRoot entry point
├── App.tsx                        ← QueryClientProvider + BrowserRouter + routes
├── index.css                      ← Tailwind v4 @theme (design tokens)
│
├── pages/
│   ├── LoginPage.tsx              ← Paste-a-JWT login form
│   └── ClustersPage.tsx           ← Cluster list + connect panel (two-column)
│
├── components/
│   ├── ClusterCard.tsx            ← Single cluster row: name, status badge, labels, k8s version
│   └── ConnectSteps.tsx           ← 3-step CLI instructions with copy-to-clipboard
│
├── hooks/
│   └── useClusters.ts             ← TanStack Query hook, polls GET /api/v1/clusters every 15s
│
├── stores/
│   └── useAuthStore.ts            ← Zustand: token + decoded UserInfo, setAuth / clearAuth
│
├── lib/
│   ├── api.ts                     ← Typed fetch wrapper for ControlPlane REST API
│   └── utils.ts                   ← cn() — clsx + tailwind-merge
│
└── types/
    └── api.ts                     ← TypeScript interfaces mirroring ControlPlane DTOs
```

---

## State management rules

Follow the conventions from the root `CLAUDE.md`:

| What | Where |
|---|---|
| Server data (clusters, credentials) | TanStack Query — the hook owns caching, refetching, and error state |
| Auth token + decoded user info | Zustand (`useAuthStore`) — set once on login, cleared on logout |
| Local/ephemeral UI state (selected cluster, form inputs) | React `useState` |

**Do not** put server-fetched data into Zustand. TanStack Query already owns it.

---

## API proxy

In dev, Vite proxies `/api` requests to `http://localhost:5000` (ControlPlane):

```ts
// vite.config.ts
server: {
  proxy: {
    "/api": { target: "http://localhost:5000", changeOrigin: true },
  },
},
```

In production, configure the reverse proxy (nginx, Caddy) to forward `/api`
to the ControlPlane.

---

## Query keys

Query keys are colocated with their hooks (per root CLAUDE.md convention):

```ts
// hooks/useClusters.ts
export const clusterKeys = {
  all:  ["clusters"],
  list: (status?: string) => [...clusterKeys.all, "list", status],
};
```

Invalidate with `queryClient.invalidateQueries({ queryKey: clusterKeys.all })`.

---

## Running locally

```bash
cd src/Clustral.Web
bun install
bun dev
# → http://localhost:5173
```

Requires the ControlPlane running on `:5000` (see root CLAUDE.md for
`docker-compose up -d` + `dotnet run`).

---

## Build

```bash
bun run build
# Output: dist/
```

---

## Testing

```bash
bun test          # Vitest (not yet configured)
bun e2e           # Playwright (not yet configured)
```

---

## Things to implement next

| # | What | Where |
|---|---|---|
| 1 | Keycloak OIDC redirect login (replace paste-a-token) | `LoginPage.tsx` + new `lib/oidc.ts` |
| 2 | Cluster detail page with credential history | new `pages/ClusterDetailPage.tsx` |
| 3 | shadcn/ui component primitives (Button, Card, Badge, Input) | `components/ui/` |
| 4 | Dark mode toggle | `index.css` `@media (prefers-color-scheme: dark)` theme + toggle component |
| 5 | Vitest + React Testing Library setup | `vitest.config.ts` + test files |
| 6 | Playwright e2e setup | `playwright.config.ts` + `e2e/` directory |
