"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { deleteCluster } from "@/lib/api";
import { clusterKeys } from "@/hooks/useClusters";
import { toast } from "sonner";
import type { Cluster } from "@/types/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Alert } from "@/components/ui/alert";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

interface DeleteClusterDialogProps {
  cluster: Cluster | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onDeleted: () => void;
}

export function DeleteClusterDialog({
  cluster,
  open,
  onOpenChange,
  onDeleted,
}: DeleteClusterDialogProps) {
  const { data: session } = useSession();
  const token = (session as never as { accessToken: string })?.accessToken;
  const queryClient = useQueryClient();
  const [confirmation, setConfirmation] = useState("");

  const mutation = useMutation({
    mutationFn: () => deleteCluster(token, cluster!.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: clusterKeys.all });
      toast.success("Cluster deleted");
      setConfirmation("");
      mutation.reset();
      onDeleted();
    },
  });

  function handleOpenChange(nextOpen: boolean) {
    if (!nextOpen) {
      setConfirmation("");
      mutation.reset();
    }
    onOpenChange(nextOpen);
  }

  const nameMatches = confirmation === cluster?.name;

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Delete cluster</DialogTitle>
          <DialogDescription>This action cannot be undone.</DialogDescription>
        </DialogHeader>

        {cluster?.status === "Connected" && (
          <Alert variant="destructive">
            This cluster has an active agent connection. Deleting it will
            immediately disconnect the agent and revoke all associated
            credentials.
          </Alert>
        )}

        <p className="text-sm text-muted-foreground">
          To confirm, type{" "}
          <span className="font-semibold text-foreground">{cluster?.name}</span>{" "}
          below:
        </p>

        <Input
          value={confirmation}
          onChange={(e) => setConfirmation(e.target.value)}
          placeholder={cluster?.name}
        />

        {mutation.isError && (
          <Alert variant="destructive">
            {(mutation.error as Error).message}
          </Alert>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => handleOpenChange(false)}>
            Cancel
          </Button>
          <Button
            variant="destructive"
            disabled={!nameMatches || mutation.isPending}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? "Deleting..." : "Delete"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
