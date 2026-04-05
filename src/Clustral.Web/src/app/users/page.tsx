"use client";

import { useState } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { fetchUsers, fetchUserAssignments, fetchRoles, fetchClusters, assignRole, removeAssignment } from "@/lib/api";
import type { ManagedUser, RoleAssignment } from "@/types/api";
import { NavHeader } from "@/components/NavHeader";
import { Users, UserCheck, Plus, Trash2, X } from "lucide-react";

export default function UsersPage() {
  const { data: session, status } = useSession();
  const token = (session as any)?.accessToken as string | undefined;
  const queryClient = useQueryClient();

  const { data: usersData, isLoading } = useQuery({
    queryKey: ["users"],
    queryFn: () => fetchUsers(token!),
    enabled: !!token,
  });

  const [selectedUser, setSelectedUser] = useState<ManagedUser | null>(null);
  const [showAssign, setShowAssign] = useState(false);

  if (status === "loading") return <div className="flex min-h-screen items-center justify-center"><p className="text-sm text-muted-foreground">Loading...</p></div>;
  if (status === "unauthenticated") redirect("/login");

  return (
    <div className="min-h-screen bg-background">
      <NavHeader />
      <div className="mx-auto max-w-6xl px-6 py-8">
      <h2 className="text-xl font-semibold mb-6">Users</h2>

      {isLoading && <p className="text-sm text-muted-foreground">Loading users...</p>}

      {usersData && usersData.users.length === 0 && (
        <div className="text-center py-16">
          <Users className="h-12 w-12 text-muted-foreground/50 mx-auto mb-4" />
          <p className="text-sm text-muted-foreground">No users have logged in yet.</p>
        </div>
      )}

      {usersData && usersData.users.length > 0 && (
        <div className="grid gap-6 lg:grid-cols-[1fr_1fr]">
          {/* User list */}
          <div className="space-y-3">
            {usersData.users.map((user) => (
              <button
                key={user.id}
                type="button"
                onClick={() => setSelectedUser(user)}
                className={`w-full text-left rounded-lg border p-4 transition-colors hover:border-primary/40 hover:bg-accent/50 ${selectedUser?.id === user.id ? "border-primary bg-accent" : ""}`}
              >
                <div className="flex items-center gap-2">
                  <UserCheck className="h-4 w-4 text-muted-foreground" />
                  <span className="font-medium">{user.displayName || user.email}</span>
                </div>
                {user.displayName && (
                  <p className="mt-1 text-sm text-muted-foreground pl-6">{user.email}</p>
                )}
                <p className="mt-1 text-xs text-muted-foreground pl-6">
                  {user.lastSeenAt ? `Last seen ${new Date(user.lastSeenAt).toLocaleString()}` : "Never logged in"}
                </p>
              </button>
            ))}
          </div>

          {/* Assignments panel */}
          <div>
            {selectedUser ? (
              <UserAssignmentsPanel
                user={selectedUser}
                token={token!}
                onAssign={() => setShowAssign(true)}
              />
            ) : (
              <div className="flex h-full items-center justify-center rounded-lg border border-dashed p-12">
                <p className="text-sm text-muted-foreground">Select a user to manage role assignments.</p>
              </div>
            )}
          </div>
        </div>
      )}

      </div>
      {/* Assign role dialog */}
      {showAssign && selectedUser && (
        <AssignRoleDialog
          user={selectedUser}
          token={token!}
          onClose={() => setShowAssign(false)}
        />
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

function UserAssignmentsPanel({ user, token, onAssign }: { user: ManagedUser; token: string; onAssign: () => void }) {
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ["assignments", user.id],
    queryFn: () => fetchUserAssignments(token, user.id),
  });

  const removeMutation = useMutation({
    mutationFn: (assignment: RoleAssignment) => removeAssignment(token, user.id, assignment.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["assignments", user.id] }),
  });

  return (
    <div className="rounded-lg border p-6">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h3 className="text-lg font-medium">{user.displayName || user.email}</h3>
          {user.displayName && <p className="text-sm text-muted-foreground">{user.email}</p>}
        </div>
        <button
          type="button"
          onClick={onAssign}
          className="inline-flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
        >
          <Plus className="h-3.5 w-3.5" />
          Assign Role
        </button>
      </div>

      {isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}

      {data && data.assignments.length === 0 && (
        <p className="text-sm text-muted-foreground">No role assignments. This user cannot access any cluster.</p>
      )}

      {data && data.assignments.length > 0 && (
        <div className="space-y-2">
          {data.assignments.map((a) => (
            <div key={a.id} className="flex items-center justify-between rounded-md bg-secondary px-3 py-2">
              <div>
                <span className="text-sm font-medium">{a.clusterName}</span>
                <span className="text-sm text-muted-foreground mx-2">&rarr;</span>
                <span className="text-sm font-mono bg-background rounded px-1.5 py-0.5">{a.roleName}</span>
              </div>
              <button
                type="button"
                onClick={() => removeMutation.mutate(a)}
                className="rounded p-1 text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

function AssignRoleDialog({ user, token, onClose }: { user: ManagedUser; token: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [roleId, setRoleId] = useState("");
  const [clusterId, setClusterId] = useState("");

  const { data: rolesData } = useQuery({
    queryKey: ["roles"],
    queryFn: () => fetchRoles(token),
  });

  const { data: clustersData } = useQuery({
    queryKey: ["clusters", "all"],
    queryFn: () => fetchClusters(token),
  });

  const mutation = useMutation({
    mutationFn: () => assignRole(token, user.id, { roleId, clusterId }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assignments", user.id] });
      onClose();
    },
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="absolute inset-0 bg-foreground/20" onClick={onClose} />
      <div className="relative w-full max-w-md rounded-lg border bg-background p-6 shadow-lg mx-4">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold">Assign role to {user.displayName ? `${user.displayName} (${user.email})` : user.email}</h3>
          <button type="button" onClick={onClose} className="text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
        </div>

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium mb-1">Cluster</label>
            <select value={clusterId} onChange={(e) => setClusterId(e.target.value)} className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm">
              <option value="">Select a cluster...</option>
              {clustersData?.clusters.map((c) => (
                <option key={c.id} value={c.id}>{c.name} ({c.status})</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Role</label>
            <select value={roleId} onChange={(e) => setRoleId(e.target.value)} className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm">
              <option value="">Select a role...</option>
              {rolesData?.roles.map((r) => (
                <option key={r.id} value={r.id}>{r.name} — {r.kubernetesGroups.join(", ") || "no groups"}</option>
              ))}
            </select>
          </div>

          {mutation.isError && <p className="text-sm text-destructive">{(mutation.error as Error).message}</p>}

          <div className="flex justify-end gap-3">
            <button type="button" onClick={onClose} className="rounded-md border px-4 py-2 text-sm">Cancel</button>
            <button
              type="button"
              disabled={!roleId || !clusterId || mutation.isPending}
              onClick={() => mutation.mutate()}
              className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground disabled:opacity-50"
            >
              {mutation.isPending ? "Assigning..." : "Assign"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
