"use client";

import { useState, type FormEvent } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { fetchRoles, createRole, updateRole, deleteRole } from "@/lib/api";
import type { Role } from "@/types/api";
import { NavHeader } from "@/components/NavHeader";
import { Plus, Trash2, Shield, Pencil } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Alert } from "@/components/ui/alert";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";

export default function RolesPage() {
  const { data: session, status } = useSession();
  const token = (session as any)?.accessToken as string | undefined;
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ["roles"],
    queryFn: () => fetchRoles(token!),
    enabled: !!token,
  });

  const [showCreate, setShowCreate] = useState(false);
  const [editTarget, setEditTarget] = useState<Role | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Role | null>(null);

  const deleteMutation = useMutation({
    mutationFn: (role: Role) => deleteRole(token!, role.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["roles"] });
      setDeleteTarget(null);
    },
  });

  if (status === "loading") return <div className="flex min-h-screen items-center justify-center"><p className="text-sm text-muted-foreground">Loading...</p></div>;
  if (status === "unauthenticated") redirect("/login");

  return (
    <div className="min-h-screen bg-background">
      <NavHeader />
      <div className="mx-auto max-w-4xl px-6 py-8">
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-xl font-semibold">Roles</h2>
          <Button onClick={() => setShowCreate(true)}>
            <Plus className="h-3.5 w-3.5" />
            Create Role
          </Button>
        </div>

        {isLoading && <p className="text-sm text-muted-foreground">Loading roles...</p>}

        {data && data.roles.length === 0 && (
          <div className="text-center py-16">
            <Shield className="h-12 w-12 text-muted-foreground/50 mx-auto mb-4" />
            <p className="text-sm text-muted-foreground mb-4">No roles defined yet.</p>
            <Button onClick={() => setShowCreate(true)}>
              <Plus className="h-4 w-4" /> Create your first role
            </Button>
          </div>
        )}

        {data && data.roles.length > 0 && (
          <div className="space-y-3">
            {data.roles.map((role) => (
              <Card key={role.id} className="p-4">
                <div className="flex items-start justify-between">
                  <div className="flex-1">
                    <div className="flex items-center gap-2">
                      <Shield className="h-4 w-4 text-muted-foreground" />
                      <span className="font-medium">{role.name}</span>
                    </div>
                    {role.description && (
                      <p className="mt-1 text-sm text-muted-foreground pl-6">{role.description}</p>
                    )}
                    <div className="mt-2 flex flex-wrap gap-1 pl-6">
                      {role.kubernetesGroups.map((g) => (
                        <Badge key={g} variant="secondary" className="font-mono text-xs">
                          {g}
                        </Badge>
                      ))}
                      {role.kubernetesGroups.length === 0 && (
                        <span className="text-xs text-muted-foreground">No k8s groups</span>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="icon-xs"
                      onClick={() => setEditTarget(role)}
                      className="text-muted-foreground hover:text-foreground"
                    >
                      <Pencil className="h-3.5 w-3.5" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-xs"
                      onClick={() => setDeleteTarget(role)}
                      className="text-muted-foreground hover:text-destructive hover:bg-destructive/10"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>

      {/* Create dialog */}
      <RoleFormDialog
        open={showCreate}
        onClose={() => setShowCreate(false)}
        token={token!}
      />

      {/* Edit dialog */}
      {editTarget && (
        <RoleFormDialog
          open={true}
          onClose={() => setEditTarget(null)}
          token={token!}
          role={editTarget}
        />
      )}

      {/* Delete dialog */}
      <Dialog open={!!deleteTarget} onOpenChange={(o) => !o && setDeleteTarget(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Delete role</DialogTitle>
            <DialogDescription>
              Delete <strong>{deleteTarget?.name}</strong>? All user assignments for this role will be removed.
            </DialogDescription>
          </DialogHeader>
          <div className="flex justify-end gap-3">
            <Button variant="outline" onClick={() => setDeleteTarget(null)}>Cancel</Button>
            <Button
              variant="destructive"
              disabled={deleteMutation.isPending}
              onClick={() => deleteTarget && deleteMutation.mutate(deleteTarget)}
            >
              {deleteMutation.isPending ? "Deleting..." : "Delete"}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

function RoleFormDialog({
  open,
  onClose,
  token,
  role,
}: {
  open: boolean;
  onClose: () => void;
  token: string;
  role?: Role;
}) {
  const isEdit = !!role;
  const queryClient = useQueryClient();

  const [name, setName] = useState(role?.name ?? "");
  const [description, setDescription] = useState(role?.description ?? "");
  const [groups, setGroups] = useState(role?.kubernetesGroups.join(", ") ?? "");

  const mutation = useMutation({
    mutationFn: () => {
      const parsedGroups = groups.split(",").map((g) => g.trim()).filter(Boolean);
      if (isEdit) {
        return updateRole(token, role.id, {
          name: name || undefined,
          description,
          kubernetesGroups: parsedGroups,
        });
      }
      return createRole(token, { name, description, kubernetesGroups: parsedGroups });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["roles"] });
      handleClose();
    },
  });

  function handleClose() {
    setName(role?.name ?? "");
    setDescription(role?.description ?? "");
    setGroups(role?.kubernetesGroups.join(", ") ?? "");
    mutation.reset();
    onClose();
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    mutation.mutate();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{isEdit ? "Edit Role" : "Create Role"}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label>Name</Label>
            <Input
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. k8s-admin"
            />
          </div>
          <div className="space-y-2">
            <Label>Description</Label>
            <Input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Full cluster admin access"
            />
          </div>
          <div className="space-y-2">
            <Label>
              Kubernetes Groups{" "}
              <span className="text-muted-foreground font-normal">(comma-separated)</span>
            </Label>
            <Input
              value={groups}
              onChange={(e) => setGroups(e.target.value)}
              placeholder="system:masters"
              className="font-mono"
            />
          </div>
          {mutation.isError && (
            <Alert variant="destructive">
              {(mutation.error as Error).message}
            </Alert>
          )}
          <div className="flex justify-end gap-3">
            <Button type="button" variant="outline" onClick={handleClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={mutation.isPending || !name.trim()}>
              {mutation.isPending
                ? isEdit ? "Saving..." : "Creating..."
                : isEdit ? "Save" : "Create"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
