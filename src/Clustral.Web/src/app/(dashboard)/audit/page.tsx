"use client";

import { useState } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useAudit, useAuditDetail } from "@/hooks/useAudit";
import type { AuditEvent, AuditSeverity, AuditFilters } from "@/types/api";
import { ScrollText, ChevronLeft, ChevronRight, RefreshCw, Eye, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";

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

function formatTimestamp(dateStr: string): string {
  return new Date(dateStr).toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    timeZoneName: "short",
  });
}

const categories = [
  { value: "all", label: "All categories" },
  { value: "access_requests", label: "Access Requests" },
  { value: "credentials", label: "Credentials" },
  { value: "clusters", label: "Clusters" },
  { value: "roles", label: "Roles" },
  { value: "auth", label: "Auth" },
  { value: "proxy", label: "Proxy" },
];

const severities = [
  { value: "all", label: "All severities" },
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

  const [selectedUid, setSelectedUid] = useState<string | null>(null);

  const { data, isLoading, isError, error, refetch } = useAudit(filters);

  const updateFilter = (key: keyof AuditFilters, value: string) => {
    setFilters((prev) => ({
      ...prev,
      [key]: value || undefined,
      page: key !== "page" ? 1 : prev.page,
    }));
  };

  const hasActiveFilters = !!(
    filters.category || filters.severity || filters.user ||
    filters.code || filters.from || filters.to
  );

  return (
    <div className="min-h-screen bg-background">
      <main className="mx-auto max-w-7xl px-6 py-8">
        {/* ── Header ──────────────────────────────────────────── */}
        <div className="mb-4 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <ScrollText className="h-6 w-6" />
            <h1 className="text-2xl font-bold">Audit Log</h1>
          </div>
          <Button variant="outline" size="sm" onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>

        {/* ── Filter bar ──────────────────────────────────────── */}
        <div className="mb-4 flex flex-wrap items-end gap-3 rounded-lg border bg-muted/30 p-3">
          <div className="min-w-[140px]">
            <Label htmlFor="category" className="mb-1 text-xs text-muted-foreground">Category</Label>
            <Select
              value={filters.category ?? "all"}
              onValueChange={(val) => updateFilter("category", val === "all" ? "" : val as string)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="All categories" />
              </SelectTrigger>
              <SelectContent>
                {categories.map((c) => (
                  <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="min-w-[120px]">
            <Label htmlFor="severity" className="mb-1 text-xs text-muted-foreground">Severity</Label>
            <Select
              value={filters.severity ?? "all"}
              onValueChange={(val) => updateFilter("severity", val === "all" ? "" : val as string)}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="All severities" />
              </SelectTrigger>
              <SelectContent>
                {severities.map((s) => (
                  <SelectItem key={s.value} value={s.value}>{s.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="min-w-[160px]">
            <Label htmlFor="user" className="mb-1 text-xs text-muted-foreground">User</Label>
            <Input
              id="user"
              placeholder="admin@corp.com"
              className="h-8 text-sm"
              value={filters.user ?? ""}
              onChange={(e) => updateFilter("user", e.target.value)}
            />
          </div>

          <div className="min-w-[100px]">
            <Label htmlFor="code" className="mb-1 text-xs text-muted-foreground">Event Code</Label>
            <Input
              id="code"
              placeholder="CAR002I"
              className="h-8 text-sm"
              value={filters.code ?? ""}
              onChange={(e) => updateFilter("code", e.target.value)}
            />
          </div>

          <div className="min-w-[130px]">
            <Label htmlFor="from" className="mb-1 text-xs text-muted-foreground">From</Label>
            <Input
              id="from"
              type="date"
              className="h-8 text-sm"
              value={filters.from ?? ""}
              onChange={(e) => updateFilter("from", e.target.value)}
            />
          </div>

          <div className="min-w-[130px]">
            <Label htmlFor="to" className="mb-1 text-xs text-muted-foreground">To</Label>
            <Input
              id="to"
              type="date"
              className="h-8 text-sm"
              value={filters.to ?? ""}
              onChange={(e) => updateFilter("to", e.target.value)}
            />
          </div>

          {hasActiveFilters && (
            <Button
              variant="ghost"
              size="sm"
              className="h-8"
              onClick={() => setFilters({ page: 1, pageSize: 25 })}
            >
              <X className="mr-1 h-3 w-3" />
              Clear
            </Button>
          )}
        </div>

        {/* ── Event table ──────────────────────────────────────── */}
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
              <Table>
                <TableHeader>
                  <TableRow className="bg-muted/50">
                    <TableHead>Code</TableHead>
                    <TableHead>Event</TableHead>
                    <TableHead>Severity</TableHead>
                    <TableHead>User</TableHead>
                    <TableHead>Cluster</TableHead>
                    <TableHead>Time</TableHead>
                    <TableHead>Message</TableHead>
                    <TableHead className="w-10" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.events.map((event) => (
                    <AuditRow
                      key={event.uid}
                      event={event}
                      onViewDetails={() => setSelectedUid(event.uid)}
                    />
                  ))}
                </TableBody>
              </Table>
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
      </main>

      {/* ── Detail dialog ──────────────────────────────────────── */}
      <AuditDetailDialog
        uid={selectedUid}
        onClose={() => setSelectedUid(null)}
      />
    </div>
  );
}

// ── Row component ────────────────────────────────────────────────────────────

function AuditRow({
  event,
  onViewDetails,
}: {
  event: AuditEvent;
  onViewDetails: () => void;
}) {
  return (
    <TableRow>
      <TableCell className="font-mono text-xs">{event.code}</TableCell>
      <TableCell className="text-muted-foreground">{event.event}</TableCell>
      <TableCell>{severityBadge(event.severity)}</TableCell>
      <TableCell>{event.user ?? <span className="text-muted-foreground">—</span>}</TableCell>
      <TableCell>{event.clusterName ?? <span className="text-muted-foreground">—</span>}</TableCell>
      <TableCell className="text-muted-foreground whitespace-nowrap">{timeAgo(event.time)}</TableCell>
      <TableCell className="max-w-xs truncate text-muted-foreground" title={event.message ?? ""}>
        {event.message ?? "—"}
      </TableCell>
      <TableCell>
        <Button variant="ghost" size="icon-xs" onClick={onViewDetails} title="View details">
          <Eye className="h-4 w-4" />
        </Button>
      </TableCell>
    </TableRow>
  );
}

// ── Detail dialog ────────────────────────────────────────────────────────────

function AuditDetailDialog({
  uid,
  onClose,
}: {
  uid: string | null;
  onClose: () => void;
}) {
  const { data: event, isLoading } = useAuditDetail(uid);

  return (
    <Dialog open={uid !== null} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent className="sm:max-w-2xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            Event Details
            {event && (
              <span className="font-mono text-sm text-muted-foreground">
                {event.code}
              </span>
            )}
          </DialogTitle>
          <DialogDescription>
            {event ? event.event : "Loading..."}
          </DialogDescription>
        </DialogHeader>

        {isLoading && (
          <div className="py-8 text-center text-muted-foreground">
            Loading event details...
          </div>
        )}

        {event && (
          <div className="space-y-6">
            {/* ── Summary ──────────────────────────────────── */}
            <div className="grid grid-cols-2 gap-4">
              <DetailField label="Event" value={event.event} />
              <DetailField label="Code" value={event.code} mono />
              <DetailField label="Category" value={event.category} />
              <DetailField label="Severity" value={event.severity} badge />
              <DetailField label="Success" value={event.success ? "Yes" : "No"} />
              <DetailField label="Time" value={formatTimestamp(event.time)} />
              {event.receivedAt && (
                <DetailField label="Received At" value={formatTimestamp(event.receivedAt)} />
              )}
            </div>

            {/* ── Actor ────────────────────────────────────── */}
            {(event.user || event.userId) && (
              <DetailSection title="Actor">
                {event.user && <DetailField label="User" value={event.user} />}
                {event.userId && <DetailField label="User ID" value={event.userId} mono />}
              </DetailSection>
            )}

            {/* ── Resource ─────────────────────────────────── */}
            {(event.resourceType || event.resourceId || event.resourceName) && (
              <DetailSection title="Resource">
                {event.resourceType && <DetailField label="Type" value={event.resourceType} />}
                {event.resourceName && <DetailField label="Name" value={event.resourceName} />}
                {event.resourceId && <DetailField label="ID" value={event.resourceId} mono />}
              </DetailSection>
            )}

            {/* ── Cluster ──────────────────────────────────── */}
            {(event.clusterName || event.clusterId) && (
              <DetailSection title="Cluster">
                {event.clusterName && <DetailField label="Name" value={event.clusterName} />}
                {event.clusterId && <DetailField label="ID" value={event.clusterId} mono />}
              </DetailSection>
            )}

            {/* ── Message ──────────────────────────────────── */}
            {event.message && (
              <DetailSection title="Message">
                <p className="text-sm">{event.message}</p>
              </DetailSection>
            )}

            {/* ── Error ────────────────────────────────────── */}
            {event.error && (
              <DetailSection title="Error">
                <p className="text-sm text-destructive">{event.error}</p>
              </DetailSection>
            )}

            {/* ── Metadata ─────────────────────────────────── */}
            {event.metadata && Object.keys(event.metadata).length > 0 && (
              <DetailSection title="Metadata">
                <pre className="overflow-x-auto rounded-md bg-muted p-3 text-xs">
                  {JSON.stringify(event.metadata, null, 2)}
                </pre>
              </DetailSection>
            )}

            {/* ── UID ──────────────────────────────────────── */}
            <div className="border-t pt-3">
              <p className="text-xs text-muted-foreground">
                Event ID: <span className="font-mono">{event.uid}</span>
              </p>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

// ── Detail sub-components ───────────────────────────────────────────────────

function DetailSection({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
        {title}
      </h3>
      <div className="grid grid-cols-2 gap-3">{children}</div>
    </div>
  );
}

function DetailField({
  label,
  value,
  mono,
  badge,
}: {
  label: string;
  value: string;
  mono?: boolean;
  badge?: boolean;
}) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      {badge ? (
        severityBadge(value as AuditSeverity)
      ) : (
        <p className={`text-sm ${mono ? "font-mono" : ""}`}>{value}</p>
      )}
    </div>
  );
}
