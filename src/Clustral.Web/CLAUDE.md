# Clustral.Web — Claude Code Guide

Next.js 14 application serving as the Clustral dashboard. Server-side OIDC
authentication via NextAuth.js. UI built with shadcn/ui + Tailwind CSS 4.

---

## Tech stack

| Layer | Library |
|---|---|
| Framework | Next.js 14 (App Router) |
| UI | React 18, TypeScript |
| Components | shadcn/ui (preset `b5J5UIRQQ`, Radix UI primitives + Tailwind) |
| Layout | dashboard-01 sidebar block (SidebarProvider + AppSidebar + SiteHeader) |
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
│   │   ├── layout.tsx                 ← Root layout + Providers + Inter font + TooltipProvider
│   │   ├── globals.css                ← Tailwind v4 @theme (design tokens, preset b5J5UIRQQ)
│   │   ├── page.tsx                   ← Redirect → /clusters
│   │   ├── login/page.tsx             ← SSO login (Card + Button)
│   │   ├── (dashboard)/
│   │   │   ├── layout.tsx             ← Sidebar layout (SidebarProvider + AppSidebar + SiteHeader)
│   │   │   ├── clusters/page.tsx      ← Cluster list + connect panel
│   │   │   ├── users/page.tsx         ← User list + role assignments
│   │   │   ├── roles/page.tsx         ← Role management (CRUD)
│   │   │   ├── access-requests/page.tsx ← Access request management
│   │   │   └── audit/page.tsx         ← Audit log with filters + table
│   │   ├── api/auth/[...nextauth]/    ← NextAuth route handler
│   │   ├── api/v1/[...path]/          ← ControlPlane REST proxy (Web UI only)
│   │   └── .well-known/clustral-*/    ← CLI service discovery endpoint
│   │
│   ├── components/
│   │   ├── ui/                        ← shadcn/ui components (DO NOT import from packages)
│   │   │   ├── button.tsx, input.tsx, badge.tsx, card.tsx, dialog.tsx
│   │   │   ├── alert.tsx, select.tsx, separator.tsx, label.tsx
│   │   │   ├── table.tsx, tabs.tsx, tooltip.tsx, popover.tsx
│   │   │   ├── dropdown-menu.tsx, sidebar.tsx, sheet.tsx, drawer.tsx
│   │   │   ├── avatar.tsx, breadcrumb.tsx, skeleton.tsx, sonner.tsx
│   │   │   ├── chart.tsx, checkbox.tsx, toggle.tsx, toggle-group.tsx
│   │   │   └── (+ more — run `ls src/components/ui/` for full list)
│   │   │
│   │   ├── app-sidebar.tsx            ← Sidebar navigation (Clusters / Users / Roles / Access Requests / Audit)
│   │   ├── nav-main.tsx               ← Sidebar nav item list
│   │   ├── nav-user.tsx               ← Sidebar user footer (session-aware)
│   │   ├── site-header.tsx            ← Top header with sidebar trigger + page title
│   │   ├── NavHeader.tsx              ← (legacy, kept for reference — sidebar replaces it)
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

### Preset

The project uses shadcn preset `b5J5UIRQQ`. To regenerate or reinitialize:

```bash
cd src/Clustral.Web
npx shadcn@latest init --preset b5J5UIRQQ --force
```

### Adding a new component

```bash
cd src/Clustral.Web
bunx shadcn@latest add <component-name>
# e.g. bunx shadcn@latest add accordion collapsible
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

- **Never** use raw `<button>`, `<select>`, `<input>`, `<label>`, `<table>`, or hand-rolled modal `<div>`s. Always import from `@/components/ui/*`.
- **Always** use shadcn Button instead of raw `<button>` elements
- **Always** use shadcn Select (with SelectTrigger/SelectContent/SelectItem) instead of raw `<select>`
- **Always** use shadcn Input/Label instead of raw `<input>/<label>`
- **Always** use shadcn Table (with TableHeader/TableBody/TableRow/TableHead/TableCell) instead of raw `<table>`
- **Always** use shadcn Dialog for modals — it handles focus trap, escape key, overlay
- Use Badge for status indicators and tags
- Use Card for content containers
- Use Alert for error/warning messages
- Keep customizations in the component files, not via className overrides

### Layout

All authenticated pages live in the `(dashboard)` route group. The layout
provides the sidebar (`AppSidebar`), header (`SiteHeader`), and content area.

To add a new page to the sidebar:
1. Create `src/app/(dashboard)/<page>/page.tsx`
2. Add a nav item to the `navItems` array in `src/components/app-sidebar.tsx`
3. Add a title entry to `pageTitles` in `src/components/site-header.tsx`

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
| `/api/v1/*` | `CONTROLPLANE_URL/api/v1/*` | REST API proxy (Web UI only) |
| `/api/auth/*` | NextAuth handler | OIDC auth |
| `/.well-known/clustral-configuration` | Served directly from env vars | CLI service discovery |

The `/.well-known` endpoint returns `controlPlaneUrl`, `oidcAuthority`, `oidcClientId`,
`oidcScopes`, and `version` (fetched from ControlPlane's `/api/v1/version`) so the CLI can
discover where to connect. This is the sole OIDC discovery endpoint for the CLI. After
discovery, the CLI talks directly to nginx (which routes to the API Gateway) — it does
not use the `/api/v1/*` proxy route.

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
| `CONTROLPLANE_URL` | Yes | ControlPlane REST API (internal, for Web UI server-side proxying) |
| `CONTROLPLANE_PUBLIC_URL` | No | Public ControlPlane URL returned to CLI via `.well-known` discovery. Falls back to `CONTROLPLANE_URL`. |
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
| 7 | Cluster health metrics dashboard | new `app/clusters/[id]/metrics/page.tsx` |
