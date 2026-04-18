"use client";

import { useState } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { fetchUsers, fetchUserAssignments, fetchRoles, fetchClusters, assignRole, removeAssignment } from "@/lib/api";
import type { ManagedUser, RoleAssignment } from "@/types/api";
import { Users, UserCheck, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

export default function UsersPage() {
  const { data: session, status } = useSession();
  const token = (session as any)?.accessToken as string | undefined;
  const queryClient = useQueryClient();

  const { data: usersData, isLoading, isError, error } = useQuery({
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
      <div className="mx-auto max-w-6xl px-6 py-8">
      <h2 className="text-xl font-semibold mb-6">Users</h2>

      {isLoading && <p className="text-sm text-muted-foreground">Loading users...</p>}

      {isError && (
        <div className="rounded-md border border-destructive/50 bg-destructive/5 p-4 text-sm text-destructive mb-4">
          Failed to load users: {(error as Error).message}
        </div>
      )}

      {!token && status === "authenticated" && (
        <div className="rounded-md border border-pending/50 bg-pending/5 p-4 text-sm mb-4">
          No access token in session. The OIDC provider may not be returning an access token.
        </div>
      )}

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
              <Button
                key={user.id}
                variant="outline"
                onClick={() => setSelectedUser(user)}
                className={`w-full justify-start text-left h-auto p-4 ${selectedUser?.id === user.id ? "border-primary bg-accent" : ""}`}
              >
                <div>
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
                </div>
              </Button>
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
        <Button onClick={onAssign}>
          <Plus className="h-3.5 w-3.5" />
          Assign Role
        </Button>
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
              <Button
                variant="ghost"
                size="icon-xs"
                onClick={() => removeMutation.mutate(a)}
                className="text-muted-foreground hover:text-destructive hover:bg-destructive/10"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
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
    <Dialog open onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Assign role to {user.displayName ? `${user.displayName} (${user.email})` : user.email}</DialogTitle>
          <DialogDescription>
            Select a cluster and role to assign to this user.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Cluster</Label>
            <Select value={clusterId || undefined} onValueChange={(val) => setClusterId(val as string)}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Select a cluster..." />
              </SelectTrigger>
              <SelectContent>
                {clustersData?.clusters.map((c) => (
                  <SelectItem key={c.id} value={c.id}>{c.name} ({c.status})</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label>Role</Label>
            <Select value={roleId || undefined} onValueChange={(val) => setRoleId(val as string)}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Select a role..." />
              </SelectTrigger>
              <SelectContent>
                {rolesData?.roles.map((r) => (
                  <SelectItem key={r.id} value={r.id}>{r.name} — {r.kubernetesGroups.join(", ") || "no groups"}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {mutation.isError && <p className="text-sm text-destructive">{(mutation.error as Error).message}</p>}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button
            disabled={!roleId || !clusterId || mutation.isPending}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? "Assigning..." : "Assign"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
