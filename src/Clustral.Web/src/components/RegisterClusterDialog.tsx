"use client";

import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { registerCluster } from "@/lib/api";
import { clusterKeys } from "@/hooks/useClusters";
import { AgentSetupSteps } from "@/components/AgentSetupSteps";
import { Plus } from "lucide-react";
import type { RegisterClusterResponse } from "@/types/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Alert } from "@/components/ui/alert";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

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

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent className="max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {result ? "Cluster Registered" : "Register New Cluster"}
          </DialogTitle>
        </DialogHeader>

        {!result ? (
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="cluster-name">Cluster name</Label>
              <Input
                id="cluster-name"
                required
                placeholder="e.g. production, staging, dev-local"
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="cluster-desc">
                Description <span className="text-muted-foreground font-normal">(optional)</span>
              </Label>
              <Input
                id="cluster-desc"
                placeholder="e.g. Production cluster in eu-west-1"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
              />
            </div>
            {mutation.isError && (
              <Alert variant="destructive">
                {(mutation.error as Error).message}
              </Alert>
            )}
            <div className="flex justify-end gap-3 pt-2">
              <Button type="button" variant="outline" onClick={handleClose}>
                Cancel
              </Button>
              <Button type="submit" disabled={mutation.isPending || !name.trim()}>
                <Plus className="h-4 w-4" />
                {mutation.isPending ? "Registering..." : "Register"}
              </Button>
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
      </DialogContent>
    </Dialog>
  );
}
