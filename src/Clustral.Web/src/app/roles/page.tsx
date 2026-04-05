"use client";

import { useState, type FormEvent } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { fetchRoles, createRole, deleteRole } from "@/lib/api";
import type { Role } from "@/types/api";
import { NavHeader } from "@/components/NavHeader";
import { Plus, Trash2, Shield, X } from "lucide-react";

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
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [groups, setGroups] = useState("");
  const [deleteTarget, setDeleteTarget] = useState<Role | null>(null);

  const createMutation = useMutation({
    mutationFn: () =>
      createRole(token!, {
        name,
        description,
        kubernetesGroups: groups.split(",").map((g) => g.trim()).filter(Boolean),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["roles"] });
      setShowCreate(false);
      setName("");
      setDescription("");
      setGroups("");
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (role: Role) => deleteRole(token!, role.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["roles"] });
      setDeleteTarget(null);
    },
  });

  if (status === "loading") return <div className="flex min-h-screen items-center justify-center"><p className="text-sm text-muted-foreground">Loading...</p></div>;
  if (status === "unauthenticated") redirect("/login");

  function handleCreate(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    createMutation.mutate();
  }

  return (
    <div className="min-h-screen bg-background">
      <NavHeader />
      <div className="mx-auto max-w-4xl px-6 py-8">
      <div className="flex items-center justify-between mb-6">
        <h2 className="text-xl font-semibold">Roles</h2>
        <button
          type="button"
          onClick={() => setShowCreate(true)}
          className="inline-flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
        >
          <Plus className="h-3.5 w-3.5" />
          Create Role
        </button>
      </div>

      {isLoading && <p className="text-sm text-muted-foreground">Loading roles...</p>}

      {data && data.roles.length === 0 && (
        <div className="text-center py-16">
          <Shield className="h-12 w-12 text-muted-foreground/50 mx-auto mb-4" />
          <p className="text-sm text-muted-foreground mb-4">No roles defined yet.</p>
          <button type="button" onClick={() => setShowCreate(true)} className="inline-flex items-center gap-1.5 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors">
            <Plus className="h-4 w-4" /> Create your first role
          </button>
        </div>
      )}

      {data && data.roles.length > 0 && (
        <div className="space-y-3">
          {data.roles.map((role) => (
            <div key={role.id} className="rounded-lg border p-4">
              <div className="flex items-start justify-between">
                <div>
                  <div className="flex items-center gap-2">
                    <Shield className="h-4 w-4 text-muted-foreground" />
                    <span className="font-medium">{role.name}</span>
                  </div>
                  {role.description && (
                    <p className="mt-1 text-sm text-muted-foreground pl-6">{role.description}</p>
                  )}
                  <div className="mt-2 flex flex-wrap gap-1 pl-6">
                    {role.kubernetesGroups.map((g) => (
                      <span key={g} className="rounded bg-secondary px-1.5 py-0.5 text-xs text-secondary-foreground font-mono">
                        {g}
                      </span>
                    ))}
                    {role.kubernetesGroups.length === 0 && (
                      <span className="text-xs text-muted-foreground">No k8s groups</span>
                    )}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => setDeleteTarget(role)}
                  className="rounded p-1 text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      </div>
      {/* Create dialog */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="absolute inset-0 bg-foreground/20" onClick={() => setShowCreate(false)} />
          <div className="relative w-full max-w-md rounded-lg border bg-background p-6 shadow-lg mx-4">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold">Create Role</h3>
              <button type="button" onClick={() => setShowCreate(false)} className="text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
            </div>
            <form onSubmit={handleCreate} className="space-y-4">
              <div>
                <label className="block text-sm font-medium mb-1">Name</label>
                <input type="text" required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. k8s-admin" className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium mb-1">Description</label>
                <input type="text" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Full cluster admin access" className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium mb-1">Kubernetes Groups <span className="text-muted-foreground font-normal">(comma-separated)</span></label>
                <input type="text" value={groups} onChange={(e) => setGroups(e.target.value)} placeholder="system:masters" className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm font-mono" />
              </div>
              {createMutation.isError && <p className="text-sm text-destructive">{(createMutation.error as Error).message}</p>}
              <div className="flex justify-end gap-3">
                <button type="button" onClick={() => setShowCreate(false)} className="rounded-md border px-4 py-2 text-sm">Cancel</button>
                <button type="submit" disabled={createMutation.isPending} className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground disabled:opacity-50">
                  {createMutation.isPending ? "Creating..." : "Create"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Delete dialog */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="absolute inset-0 bg-foreground/20" onClick={() => setDeleteTarget(null)} />
          <div className="relative w-full max-w-sm rounded-lg border bg-background p-6 shadow-lg mx-4">
            <h3 className="text-lg font-semibold mb-2">Delete role</h3>
            <p className="text-sm text-muted-foreground mb-5">Delete <strong>{deleteTarget.name}</strong>? All user assignments for this role will be removed.</p>
            <div className="flex justify-end gap-3">
              <button type="button" onClick={() => setDeleteTarget(null)} className="rounded-md border px-4 py-2 text-sm">Cancel</button>
              <button type="button" onClick={() => deleteMutation.mutate(deleteTarget)} disabled={deleteMutation.isPending} className="rounded-md bg-destructive px-4 py-2 text-sm font-medium text-white disabled:opacity-50">
                {deleteMutation.isPending ? "Deleting..." : "Delete"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
