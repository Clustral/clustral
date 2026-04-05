"use client";

import { useState } from "react";
import { useSession, signOut } from "next-auth/react";
import { redirect } from "next/navigation";
import { useClusters, clusterKeys } from "@/hooks/useClusters";
import { ClusterCard } from "@/components/ClusterCard";
import { ConnectSteps } from "@/components/ConnectSteps";
import { RegisterClusterDialog } from "@/components/RegisterClusterDialog";
import { deleteCluster } from "@/lib/api";
import type { Cluster } from "@/types/api";
import { LogOut, RefreshCw, Server, Plus } from "lucide-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

export default function ClustersPage() {
  const { data: session, status } = useSession();
  const token = (session as any)?.accessToken as string | undefined;
  const queryClient = useQueryClient();
  const { data, isLoading, isError, error } = useClusters();

  const [selected, setSelected] = useState<Cluster | null>(null);
  const [registerOpen, setRegisterOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<Cluster | null>(null);

  const deleteMutation = useMutation({
    mutationFn: (cluster: Cluster) => deleteCluster(token!, cluster.id),
    onSuccess: (_data, cluster) => {
      if (selected?.id === cluster.id) setSelected(null);
      queryClient.invalidateQueries({ queryKey: clusterKeys.all });
      setDeleteTarget(null);
    },
  });

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
      {/* Header */}
      <header className="border-b">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
          <div className="flex items-center gap-2">
            <Server className="h-5 w-5" />
            <span className="text-lg font-semibold">Clustral</span>
          </div>
          <div className="flex items-center gap-4">
            {session?.user?.email && (
              <span className="text-sm text-muted-foreground">
                {session.user.email}
              </span>
            )}
            <button
              type="button"
              onClick={() => signOut({ callbackUrl: "/login" })}
              className="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent transition-colors"
            >
              <LogOut className="h-3.5 w-3.5" />
              Sign out
            </button>
          </div>
        </div>
      </header>

      {/* Body */}
      <main className="mx-auto max-w-6xl px-6 py-8">
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-xl font-semibold">Clusters</h2>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() =>
                queryClient.invalidateQueries({ queryKey: clusterKeys.all })
              }
              className="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-accent transition-colors"
            >
              <RefreshCw className="h-3.5 w-3.5" />
              Refresh
            </button>
            <button
              type="button"
              onClick={() => setRegisterOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
            >
              <Plus className="h-3.5 w-3.5" />
              Register Cluster
            </button>
          </div>
        </div>

        {isLoading && (
          <p className="text-sm text-muted-foreground">Loading clusters...</p>
        )}

        {isError && (
          <div className="rounded-md border border-destructive/50 bg-destructive/5 p-4 text-sm text-destructive">
            Failed to load clusters: {(error as Error).message}
          </div>
        )}

        {data && data.clusters.length === 0 && (
          <div className="flex flex-col items-center justify-center py-16 text-center">
            <Server className="h-12 w-12 text-muted-foreground/50 mb-4" />
            <p className="text-sm text-muted-foreground mb-4">
              No clusters registered yet.
            </p>
            <button
              type="button"
              onClick={() => setRegisterOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
            >
              <Plus className="h-4 w-4" />
              Register your first cluster
            </button>
          </div>
        )}

        {data && data.clusters.length > 0 && (
          <div className="grid gap-6 lg:grid-cols-[1fr_1fr]">
            <div className="space-y-3">
              {data.clusters.map((c) => (
                <ClusterCard
                  key={c.id}
                  cluster={c}
                  selected={selected?.id === c.id}
                  onSelect={setSelected}
                  onDelete={setDeleteTarget}
                />
              ))}
            </div>
            <div>
              {selected ? (
                <ConnectSteps cluster={selected} />
              ) : (
                <div className="flex h-full items-center justify-center rounded-lg border border-dashed p-12">
                  <p className="text-sm text-muted-foreground">
                    Select a cluster to see connection instructions.
                  </p>
                </div>
              )}
            </div>
          </div>
        )}
      </main>

      <RegisterClusterDialog
        open={registerOpen}
        onClose={() => setRegisterOpen(false)}
      />

      {/* Delete confirmation */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div
            className="absolute inset-0 bg-foreground/20"
            onClick={() => setDeleteTarget(null)}
          />
          <div className="relative w-full max-w-sm rounded-lg border bg-background p-6 shadow-lg mx-4">
            <h3 className="text-lg font-semibold mb-2">Delete cluster</h3>
            <p className="text-sm text-muted-foreground mb-1">
              Are you sure you want to delete{" "}
              <span className="font-medium text-foreground">
                {deleteTarget.name}
              </span>
              ?
            </p>
            <p className="text-xs text-muted-foreground mb-5">
              This will remove the cluster registration and revoke all associated
              credentials. Connected agents will be disconnected.
            </p>
            {deleteMutation.isError && (
              <div className="rounded-md border border-destructive/50 bg-destructive/5 p-3 text-sm text-destructive mb-4">
                {(deleteMutation.error as Error).message}
              </div>
            )}
            <div className="flex justify-end gap-3">
              <button
                type="button"
                onClick={() => {
                  setDeleteTarget(null);
                  deleteMutation.reset();
                }}
                className="rounded-md border px-4 py-2 text-sm hover:bg-accent transition-colors"
              >
                Cancel
              </button>
              <button
                type="button"
                disabled={deleteMutation.isPending}
                onClick={() => deleteMutation.mutate(deleteTarget)}
                className="rounded-md bg-destructive px-4 py-2 text-sm font-medium text-white hover:bg-destructive/90 transition-colors disabled:opacity-50"
              >
                {deleteMutation.isPending ? "Deleting..." : "Delete"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
