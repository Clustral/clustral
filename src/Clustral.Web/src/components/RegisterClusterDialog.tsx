"use client";

import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { registerCluster } from "@/lib/api";
import { clusterKeys } from "@/hooks/useClusters";
import { AgentSetupSteps } from "@/components/AgentSetupSteps";
import { X, Plus } from "lucide-react";
import type { RegisterClusterResponse } from "@/types/api";

interface Props {
  open: boolean;
  onClose: () => void;
}

export function RegisterClusterDialog({ open, onClose }: Props) {
  const { data: session } = useSession();
  const token = (session as any)?.accessToken as string | undefined;
  const queryClient = useQueryClient();

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [result, setResult] = useState<RegisterClusterResponse | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      registerCluster(token!, {
        name,
        description,
        agentPublicKeyPem: "",
        labels: {},
      }),
    onSuccess: (data) => {
      setResult(data);
      queryClient.invalidateQueries({ queryKey: clusterKeys.all });
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    mutation.mutate();
  }

  function handleClose() {
    setName("");
    setDescription("");
    setResult(null);
    mutation.reset();
    onClose();
  }

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div
        className="absolute inset-0 bg-foreground/20"
        onClick={handleClose}
      />
      <div className="relative w-full max-w-lg rounded-lg border bg-background p-6 shadow-lg mx-4 max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">
            {result ? "Cluster Registered" : "Register New Cluster"}
          </h2>
          <button
            type="button"
            onClick={handleClose}
            className="text-muted-foreground hover:text-foreground transition-colors"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {!result ? (
          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label htmlFor="cluster-name" className="block text-sm font-medium mb-1">
                Cluster name
              </label>
              <input
                id="cluster-name"
                type="text"
                required
                placeholder="e.g. production, staging, dev-local"
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring"
              />
            </div>
            <div>
              <label htmlFor="cluster-desc" className="block text-sm font-medium mb-1">
                Description <span className="text-muted-foreground font-normal">(optional)</span>
              </label>
              <input
                id="cluster-desc"
                type="text"
                placeholder="e.g. Production cluster in eu-west-1"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring"
              />
            </div>
            {mutation.isError && (
              <div className="rounded-md border border-destructive/50 bg-destructive/5 p-3 text-sm text-destructive">
                {(mutation.error as Error).message}
              </div>
            )}
            <div className="flex justify-end gap-3 pt-2">
              <button type="button" onClick={handleClose} className="rounded-md border px-4 py-2 text-sm hover:bg-accent transition-colors">
                Cancel
              </button>
              <button
                type="submit"
                disabled={mutation.isPending || !name.trim()}
                className="inline-flex items-center gap-2 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors disabled:opacity-50"
              >
                <Plus className="h-4 w-4" />
                {mutation.isPending ? "Registering..." : "Register"}
              </button>
            </div>
          </form>
        ) : (
          <AgentSetupSteps
            clusterName={name}
            clusterId={result.clusterId}
            bootstrapToken={result.bootstrapToken}
            onDone={handleClose}
          />
        )}
      </div>
    </div>
  );
}
