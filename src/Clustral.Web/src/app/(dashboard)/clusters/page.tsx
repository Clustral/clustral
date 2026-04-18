"use client";

import { useState } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useClusters, clusterKeys } from "@/hooks/useClusters";
import { ClusterCard } from "@/components/ClusterCard";
import { ConnectSteps } from "@/components/ConnectSteps";
import { RegisterClusterDialog } from "@/components/RegisterClusterDialog";
import { deleteCluster } from "@/lib/api";
import type { Cluster } from "@/types/api";
import { RefreshCw, Server, Plus } from "lucide-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

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

      {/* Body */}
      <main className="mx-auto max-w-6xl px-6 py-8">
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-xl font-semibold">Clusters</h2>
          <div className="flex items-center gap-2">
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
            <Button
              size="sm"
              onClick={() => setRegisterOpen(true)}
            >
              <Plus className="h-3.5 w-3.5" />
              Register Cluster
            </Button>
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
            <Button onClick={() => setRegisterOpen(true)}>
              <Plus className="h-4 w-4" />
              Register your first cluster
            </Button>
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
      <Dialog
        open={!!deleteTarget}
        onOpenChange={(open) => {
          if (!open) {
            setDeleteTarget(null);
            deleteMutation.reset();
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete cluster</DialogTitle>
            <DialogDescription>
              Are you sure you want to delete{" "}
              <span className="font-medium text-foreground">
                {deleteTarget?.name}
              </span>
              ? This will remove the cluster registration and revoke all associated
              credentials. Connected agents will be disconnected.
            </DialogDescription>
          </DialogHeader>
          {deleteMutation.isError && (
            <div className="rounded-md border border-destructive/50 bg-destructive/5 p-3 text-sm text-destructive">
              {(deleteMutation.error as Error).message}
            </div>
          )}
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => {
                setDeleteTarget(null);
                deleteMutation.reset();
              }}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              disabled={deleteMutation.isPending}
              onClick={() => deleteTarget && deleteMutation.mutate(deleteTarget)}
            >
              {deleteMutation.isPending ? "Deleting..." : "Delete"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
