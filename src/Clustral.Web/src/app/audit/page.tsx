"use client";

import { useState } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useAudit } from "@/hooks/useAudit";
import type { AuditEvent, AuditSeverity, AuditFilters } from "@/types/api";
import { NavHeader } from "@/components/NavHeader";
import { ScrollText, ChevronLeft, ChevronRight, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";

// ── Helpers ──────────────────────────────────────────────────────────────────

const severityBadge = (severity: AuditSeverity) => {
  switch (severity) {
    case "Info":
      return <Badge className="bg-blue-500/20 text-blue-600 border-blue-500/30">Info</Badge>;
    case "Warning":
      return <Badge className="bg-yellow-500/20 text-yellow-600 border-yellow-500/30">Warning</Badge>;
    case "Error":
      return <Badge variant="destructive">Error</Badge>;
  }
};

function timeAgo(dateStr: string): string {
  const ms = Date.now() - new Date(dateStr).getTime();
  if (ms < 0) return "just now";
  const seconds = Math.floor(ms / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

const categories = [
  { value: "", label: "All categories" },
  { value: "access_requests", label: "Access Requests" },
  { value: "credentials", label: "Credentials" },
  { value: "clusters", label: "Clusters" },
  { value: "roles", label: "Roles" },
  { value: "auth", label: "Auth" },
  { value: "proxy", label: "Proxy" },
];

const severities = [
  { value: "", label: "All severities" },
  { value: "Info", label: "Info" },
  { value: "Warning", label: "Warning" },
  { value: "Error", label: "Error" },
];

// ── Page ─────────────────────────────────────────────────────────────────────

export default function AuditPage() {
  const { data: session, status } = useSession();
  if (status === "unauthenticated") redirect("/login");

  const [filters, setFilters] = useState<AuditFilters>({
    page: 1,
    pageSize: 25,
  });

  const { data, isLoading, isError, error, refetch } = useAudit(filters);

  const updateFilter = (key: keyof AuditFilters, value: string) => {
    setFilters((prev) => ({
      ...prev,
      [key]: value || undefined,
      page: key !== "page" ? 1 : prev.page, // reset to page 1 on filter change
    }));
  };

  return (
    <div className="min-h-screen bg-background">
      <NavHeader />
      <main className="mx-auto max-w-6xl px-6 py-8">
        {/* ── Header ──────────────────────────────────────────── */}
        <div className="mb-6 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <ScrollText className="h-6 w-6" />
            <h1 className="text-2xl font-bold">Audit Log</h1>
          </div>
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>

        <div className="flex gap-6">
          {/* ── Filters sidebar ────────────────────────────────── */}
          <Card className="w-64 shrink-0 p-4">
            <h2 className="mb-4 text-sm font-semibold text-muted-foreground">Filters</h2>

            <div className="space-y-4">
              <div>
                <Label htmlFor="category" className="text-xs">Category</Label>
                <select
                  id="category"
                  className="mt-1 w-full rounded-md border bg-background px-3 py-2 text-sm"
                  value={filters.category ?? ""}
                  onChange={(e) => updateFilter("category", e.target.value)}
                >
                  {categories.map((c) => (
                    <option key={c.value} value={c.value}>{c.label}</option>
                  ))}
                </select>
              </div>

              <div>
                <Label htmlFor="severity" className="text-xs">Severity</Label>
                <select
                  id="severity"
                  className="mt-1 w-full rounded-md border bg-background px-3 py-2 text-sm"
                  value={filters.severity ?? ""}
                  onChange={(e) => updateFilter("severity", e.target.value)}
                >
                  {severities.map((s) => (
                    <option key={s.value} value={s.value}>{s.label}</option>
                  ))}
                </select>
              </div>

              <div>
                <Label htmlFor="user" className="text-xs">User</Label>
                <Input
                  id="user"
                  placeholder="admin@corp.com"
                  className="mt-1"
                  value={filters.user ?? ""}
                  onChange={(e) => updateFilter("user", e.target.value)}
                />
              </div>

              <div>
                <Label htmlFor="code" className="text-xs">Event Code</Label>
                <Input
                  id="code"
                  placeholder="CAR002I"
                  className="mt-1"
                  value={filters.code ?? ""}
                  onChange={(e) => updateFilter("code", e.target.value)}
                />
              </div>

              <div>
                <Label htmlFor="from" className="text-xs">From</Label>
                <Input
                  id="from"
                  type="date"
                  className="mt-1"
                  value={filters.from ?? ""}
                  onChange={(e) => updateFilter("from", e.target.value)}
                />
              </div>

              <div>
                <Label htmlFor="to" className="text-xs">To</Label>
                <Input
                  id="to"
                  type="date"
                  className="mt-1"
                  value={filters.to ?? ""}
                  onChange={(e) => updateFilter("to", e.target.value)}
                />
              </div>

              <Button
                variant="ghost"
                size="sm"
                className="w-full"
                onClick={() => setFilters({ page: 1, pageSize: 25 })}
              >
                Clear filters
              </Button>
            </div>
          </Card>

          {/* ── Event table ────────────────────────────────────── */}
          <div className="flex-1">
            {isLoading && (
              <div className="py-12 text-center text-muted-foreground">
                Loading audit events...
              </div>
            )}

            {isError && (
              <div className="py-12 text-center text-destructive">
                Error: {(error as Error).message}
              </div>
            )}

            {data && data.events.length === 0 && (
              <div className="py-12 text-center text-muted-foreground">
                No audit events found.
              </div>
            )}

            {data && data.events.length > 0 && (
              <>
                <div className="overflow-hidden rounded-lg border">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b bg-muted/50">
                        <th className="px-4 py-3 text-left font-medium">Code</th>
                        <th className="px-4 py-3 text-left font-medium">Event</th>
                        <th className="px-4 py-3 text-left font-medium">Severity</th>
                        <th className="px-4 py-3 text-left font-medium">User</th>
                        <th className="px-4 py-3 text-left font-medium">Cluster</th>
                        <th className="px-4 py-3 text-left font-medium">Time</th>
                        <th className="px-4 py-3 text-left font-medium">Message</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.events.map((event) => (
                        <AuditRow key={event.uid} event={event} />
                      ))}
                    </tbody>
                  </table>
                </div>

                {/* ── Pagination ──────────────────────────────── */}
                <div className="mt-4 flex items-center justify-between text-sm text-muted-foreground">
                  <span>
                    {data.totalCount} event{data.totalCount !== 1 ? "s" : ""} total
                  </span>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="outline"
                      size="icon-sm"
                      disabled={data.page <= 1}
                      onClick={() =>
                        setFilters((f) => ({ ...f, page: (f.page ?? 1) - 1 }))
                      }
                    >
                      <ChevronLeft className="h-4 w-4" />
                    </Button>
                    <span>
                      Page {data.page} of {data.totalPages}
                    </span>
                    <Button
                      variant="outline"
                      size="icon-sm"
                      disabled={data.page >= data.totalPages}
                      onClick={() =>
                        setFilters((f) => ({ ...f, page: (f.page ?? 1) + 1 }))
                      }
                    >
                      <ChevronRight className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      </main>
    </div>
  );
}

// ── Row component ────────────────────────────────────────────────────────────

function AuditRow({ event }: { event: AuditEvent }) {
  return (
    <tr className="border-b last:border-0 hover:bg-muted/30 transition-colors">
      <td className="px-4 py-3 font-mono text-xs">{event.code}</td>
      <td className="px-4 py-3 text-muted-foreground">{event.event}</td>
      <td className="px-4 py-3">{severityBadge(event.severity)}</td>
      <td className="px-4 py-3">{event.user ?? <span className="text-muted-foreground">—</span>}</td>
      <td className="px-4 py-3">{event.clusterName ?? <span className="text-muted-foreground">—</span>}</td>
      <td className="px-4 py-3 text-muted-foreground whitespace-nowrap">{timeAgo(event.time)}</td>
      <td className="px-4 py-3 max-w-xs truncate text-muted-foreground" title={event.message ?? ""}>
        {event.message ?? "—"}
      </td>
    </tr>
  );
}
