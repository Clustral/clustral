# Clustral.Web — Claude Code Guide

Next.js 14 application serving as the Clustral dashboard. Server-side OIDC
authentication via NextAuth.js. UI built with shadcn/ui + Tailwind CSS 4.

---

## Tech stack

| Layer | Library |
|---|---|
| Framework | Next.js 14 (App Router) |
| UI | React 18, TypeScript |
| Components | shadcn/ui (Radix UI primitives + Tailwind) |
| Styling | Tailwind CSS 4, `class-variance-authority`, `clsx`, `tailwind-merge` |
| Icons | Lucide React |
| Auth | NextAuth.js v5 (server-side OIDC) |
| Server state | TanStack Query 5 |
| Package manager | bun |

---

## File map

```
Clustral.Web/
├── next.config.mjs                    ← Standalone output config
├── components.json                    ← shadcn/ui configuration
├── postcss.config.mjs                 ← Tailwind v4 PostCSS plugin
│
├── src/
│   ├── app/
│   │   ├── layout.tsx                 ← Root layout + Providers + Inter font
│   │   ├── globals.css                ← Tailwind v4 @theme (design tokens)
│   │   ├── page.tsx                   ← Redirect → /clusters
│   │   ├── login/page.tsx             ← SSO login (Card + Button)
│   │   ├── clusters/page.tsx          ← Cluster list + connect panel
│   │   ├── users/page.tsx             ← User list + role assignments
│   │   ├── roles/page.tsx             ← Role management (CRUD)
│   │   ├── access-requests/page.tsx  ← Access request management (My Requests, Pending Reviews, Active Grants tabs)
│   │   ├── api/auth/[...nextauth]/    ← NextAuth route handler
│   │   ├── api/v1/[...path]/          ← ControlPlane REST proxy
│   │   ├── api/proxy/[...path]/       ← kubectl tunnel proxy
│   │   └── .well-known/clustral-*/    ← CLI discovery endpoint
│   │
│   ├── components/
│   │   ├── ui/                        ← shadcn/ui components (DO NOT import from packages)
│   │   │   ├── button.tsx
│   │   │   ├── input.tsx
│   │   │   ├── badge.tsx
│   │   │   ├── card.tsx
│   │   │   ├── dialog.tsx
│   │   │   ├── alert.tsx
│   │   │   ├── select.tsx
│   │   │   ├── separator.tsx
│   │   │   └── label.tsx
│   │   │
│   │   ├── NavHeader.tsx              ← Top navigation (Clusters / Users / Roles / Access Requests + sign out)
│   │   ├── ClusterCard.tsx            ← Cluster row (Card + Badge + Button)
│   │   ├── ConnectSteps.tsx           ← CLI connection instructions
│   │   ├── RegisterClusterDialog.tsx  ← Dialog for cluster registration
│   │   └── AgentSetupSteps.tsx        ← Post-registration agent deploy instructions
│   │
│   ├── hooks/
│   │   └── useClusters.ts             ← TanStack Query, polls /api/v1/clusters every 15s
│   │
│   ├── lib/
│   │   ├── api.ts                     ← Typed fetch wrapper for REST API (includes revokeAccessRequest)
│   │   ├── auth.ts                    ← NextAuth config (generic OIDC provider)
│   │   └── utils.ts                   ← cn() — clsx + tailwind-merge
│   │
│   ├── providers.tsx                  ← SessionProvider + QueryClientProvider
│   └── types/api.ts                   ← TypeScript interfaces for REST DTOs
│
├── Dockerfile                         ← Multi-stage: bun build → node:20-alpine standalone
└── package.json
```

---

## shadcn/ui

Components are in `src/components/ui/`. They are **owned by us** — not a
package dependency. Modify them directly when needed.

### Adding a new component

```bash
cd src/Clustral.Web
bunx shadcn@latest add <component-name>
# e.g. bunx shadcn@latest add table tooltip popover
```

This downloads the component source into `src/components/ui/` and installs
any required Radix UI dependencies.

### Using a component

```tsx
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

<Button variant="outline" size="sm">Click me</Button>
<Badge variant="destructive">Disconnected</Badge>
<Dialog open={open} onOpenChange={setOpen}>
  <DialogContent>
    <DialogHeader>
      <DialogTitle>Title</DialogTitle>
    </DialogHeader>
    {/* content */}
  </DialogContent>
</Dialog>
```

### Available variants

- **Button**: `default`, `outline`, `secondary`, `ghost`, `destructive`, `link`
- **Button sizes**: `default`, `sm`, `xs`, `lg`, `icon`, `icon-xs`, `icon-sm`
- **Badge**: `default`, `secondary`, `destructive`, `outline`
- **Alert**: `default`, `destructive`

### Guidelines

- **Always** use shadcn Button instead of raw `<button>` elements
- **Always** use shadcn Input/Label instead of raw `<input>/<label>`
- **Always** use shadcn Dialog for modals — it handles focus trap, escape key, overlay
- Use Badge for status indicators and tags
- Use Card for content containers
- Use Alert for error/warning messages
- Keep customizations in the component files, not via className overrides

---

## State management

| What | Where |
|---|---|
| Server data (clusters, users, roles) | TanStack Query — the hook owns caching, refetching, error state |
| Auth session | NextAuth.js `useSession()` — server-managed, access token in `session.accessToken` |
| Local/ephemeral UI state | React `useState` |

**Do not** create Zustand stores. TanStack Query and NextAuth handle all shared state.

---

## API proxying

All API calls go through Next.js API routes at request time (not build-time rewrites):

| Route | Destination | Purpose |
|---|---|---|
| `/api/v1/*` | `CONTROLPLANE_URL/api/v1/*` | REST API proxy |
| `/api/proxy/*` | `CONTROLPLANE_URL/api/proxy/*` | kubectl tunnel proxy |
| `/api/auth/*` | NextAuth handler | OIDC auth |
| `/.well-known/clustral-configuration` | `CONTROLPLANE_URL/api/v1/config` | CLI discovery |

---

## Query keys

Colocated with their hooks:

```ts
// hooks/useClusters.ts
export const clusterKeys = {
  all:  ["clusters"],
  list: (status?: string) => [...clusterKeys.all, "list", status],
};
```

---

## Environment variables

All runtime (not build-time):

| Variable | Required | Description |
|---|---|---|
| `NEXTAUTH_URL` | Yes | Browser-facing URL |
| `CONTROLPLANE_URL` | Yes | ControlPlane REST API |
| `OIDC_ISSUER` | Yes | OIDC provider URL |
| `OIDC_CLIENT_ID` | No | Default: `clustral-web` |
| `OIDC_CLIENT_SECRET` | Yes | OIDC client secret |
| `AUTH_SECRET` | Yes | NextAuth encryption key |

---

## Running locally

```bash
cd src/Clustral.Web
bun install
bun dev
# → http://localhost:5173
```

---

## Build

```bash
bun run build
# Output: .next/standalone/
```

---

## Things to implement next

| # | What | Where |
|---|---|---|
| 1 | Cluster detail page with credential history | new `app/clusters/[id]/page.tsx` |
| 2 | Dark mode toggle | `globals.css` dark theme + toggle in NavHeader |
| 3 | Data table component for users/roles/access-requests lists | `bunx shadcn add table` |
| 4 | Access request notification badges in NavHeader | `components/NavHeader.tsx` |
| 5 | Vitest + React Testing Library setup | `vitest.config.ts` |
| 6 | Playwright e2e | `playwright.config.ts` + `e2e/` |
| 7 | Audit log viewer page | new `app/audit/page.tsx` |
| 8 | Cluster health metrics dashboard | new `app/clusters/[id]/metrics/page.tsx` |
