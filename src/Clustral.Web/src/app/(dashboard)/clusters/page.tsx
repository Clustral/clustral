"use client";

import { useState, useMemo } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useClusters } from "@/hooks/useClusters";
import { ClusterCard } from "@/components/ClusterCard";
import { ClusterCardSkeleton } from "@/components/ClusterCardSkeleton";
import { ClusterEmptyState } from "@/components/ClusterEmptyState";
import { ClusterToolbar } from "@/components/ClusterToolbar";
import { ClusterDetailSheet } from "@/components/ClusterDetailSheet";
import { RegisterClusterStepper } from "@/components/RegisterClusterStepper";
import { DeleteClusterDialog } from "@/components/DeleteClusterDialog";
import type { Cluster } from "@/types/api";
import { RefreshCw } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { clusterKeys } from "@/hooks/useClusters";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/alert";

export default function ClustersPage() {
  const { status } = useSession();
  const queryClient = useQueryClient();

  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [sortBy, setSortBy] = useState("name");
  const [selectedClusterId, setSelectedClusterId] = useState<string | null>(
    null,
  );
  const [registerOpen, setRegisterOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<Cluster | null>(null);

  const apiStatusFilter =
    statusFilter === "all" ? undefined : statusFilter;
  const { data, isLoading, isError, error } = useClusters(apiStatusFilter);

  const filtered = useMemo(() => {
    if (!data) return [];
    let clusters = data.clusters;

    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      clusters = clusters.filter((c) =>
        c.name.toLowerCase().includes(q),
      );
    }

    clusters = [...clusters].sort((a, b) => {
      if (sortBy === "name") {
        return a.name.localeCompare(b.name);
      }
      // "recent" — sort by lastSeenAt descending, nulls last
      const aTime = a.lastSeenAt ? new Date(a.lastSeenAt).getTime() : 0;
      const bTime = b.lastSeenAt ? new Date(b.lastSeenAt).getTime() : 0;
      return bTime - aTime;
    });

    return clusters;
  }, [data, searchQuery, sortBy]);

  const selectedCluster =
    data?.clusters.find((c) => c.id === selectedClusterId) ?? null;

  const hasAnyClusters = data && data.clusters.length > 0;
  const hasFilteredResults = filtered.length > 0;

  if (status === "loading") {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background">
        <p className="text-sm text-muted-foreground">Loading...</p>
      </div>
    );
  }

  if (status === "unauthenticated") {
    redirect("/login");
  }

  return (
    <div className="min-h-screen bg-background">
      <main className="mx-auto max-w-6xl px-6 py-8">
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-xl font-semibold">Clusters</h2>
          <Button
            variant="outline"
            size="sm"
            onClick={() =>
              queryClient.invalidateQueries({ queryKey: clusterKeys.all })
            }
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </Button>
        </div>

        <div className="mb-6">
          <ClusterToolbar
            searchQuery={searchQuery}
            onSearchChange={setSearchQuery}
            statusFilter={statusFilter}
            onStatusFilterChange={setStatusFilter}
            sortBy={sortBy}
            onSortByChange={setSortBy}
            onRegister={() => setRegisterOpen(true)}
          />
        </div>

        {isLoading && (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            <ClusterCardSkeleton />
            <ClusterCardSkeleton />
            <ClusterCardSkeleton />
          </div>
        )}

        {isError && (
          <Alert variant="destructive">
            <div className="flex items-center justify-between w-full">
              <span>
                Failed to load clusters: {(error as Error).message}
              </span>
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  queryClient.invalidateQueries({
                    queryKey: clusterKeys.all,
                  })
                }
              >
                Retry
              </Button>
            </div>
          </Alert>
        )}

        {data && !hasAnyClusters && (
          <ClusterEmptyState onRegister={() => setRegisterOpen(true)} />
        )}

        {data && hasAnyClusters && !hasFilteredResults && (
          <p className="text-sm text-muted-foreground text-center py-12">
            No clusters match your search or filter criteria.
          </p>
        )}

        {data && hasAnyClusters && hasFilteredResults && (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {filtered.map((c) => (
              <ClusterCard
                key={c.id}
                cluster={c}
                onSelect={(cluster) => setSelectedClusterId(cluster.id)}
                onDelete={setDeleteTarget}
              />
            ))}
          </div>
        )}
      </main>

      <ClusterDetailSheet
        cluster={selectedCluster}
        open={!!selectedClusterId}
        onOpenChange={(open) => {
          if (!open) setSelectedClusterId(null);
        }}
      />

      <RegisterClusterStepper
        open={registerOpen}
        onOpenChange={setRegisterOpen}
        onRegistered={() => {
          queryClient.invalidateQueries({ queryKey: clusterKeys.all });
        }}
      />

      <DeleteClusterDialog
        cluster={deleteTarget}
        open={!!deleteTarget}
        onOpenChange={(open) => {
          if (!open) setDeleteTarget(null);
        }}
        onDeleted={() => {
          if (selectedClusterId === deleteTarget?.id) {
            setSelectedClusterId(null);
          }
          setDeleteTarget(null);
        }}
      />
    </div>
  );
}
