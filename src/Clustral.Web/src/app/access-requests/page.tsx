"use client";

import { useState, type FormEvent } from "react";
import { useSession } from "next-auth/react";
import { redirect } from "next/navigation";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchAccessRequests,
  createAccessRequest,
  approveAccessRequest,
  denyAccessRequest,
  fetchRoles,
  fetchClusters,
  fetchUsers,
} from "@/lib/api";
import type { AccessRequest, AccessRequestStatus } from "@/types/api";
import { NavHeader } from "@/components/NavHeader";
import { Shield, Clock, Plus, Check, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";

const statusBadge = (status: AccessRequestStatus) => {
  switch (status) {
    case "Pending":
      return <Badge className="bg-yellow-500/20 text-yellow-600 border-yellow-500/30">Pending</Badge>;
    case "Approved":
      return <Badge className="bg-green-500/20 text-green-600 border-green-500/30">Approved</Badge>;
    case "Denied":
      return <Badge variant="destructive">Denied</Badge>;
    case "Expired":
      return <Badge variant="secondary">Expired</Badge>;
  }
};

function timeRemaining(expiresAt: string): string {
  const ms = new Date(expiresAt).getTime() - Date.now();
  if (ms <= 0) return "expired";
  const hours = Math.floor(ms / 3600000);
  const mins = Math.floor((ms % 3600000) / 60000);
  return hours > 0 ? `${hours}h ${mins}m` : `${mins}m`;
}

export default function AccessRequestsPage() {
  const { data: session, status } = useSession();
  const token = (session as any)?.accessToken as string | undefined;
  const queryClient = useQueryClient();

  const [tab, setTab] = useState<"mine" | "pending">("mine");
  const [showCreate, setShowCreate] = useState(false);
  const [denyTarget, setDenyTarget] = useState<AccessRequest | null>(null);
  const [denyReason, setDenyReason] = useState("");

  const { data: myRequests, isLoading: loadingMine } = useQuery({
    queryKey: ["access-requests", "mine"],
    queryFn: () => fetchAccessRequests(token!, { mine: true }),
    enabled: !!token && tab === "mine",
    refetchInterval: 10000,
  });

  const { data: pendingRequests, isLoading: loadingPending } = useQuery({
    queryKey: ["access-requests", "pending"],
    queryFn: () => fetchAccessRequests(token!, { status: "Pending" }),
    enabled: !!token && tab === "pending",
    refetchInterval: 10000,
  });

  const approveMutation = useMutation({
    mutationFn: (id: string) => approveAccessRequest(token!, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["access-requests"] });
    },
  });

  const denyMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      denyAccessRequest(token!, id, reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["access-requests"] });
      setDenyTarget(null);
      setDenyReason("");
    },
  });

  if (status === "loading")
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p className="text-sm text-muted-foreground">Loading...</p>
      </div>
    );
  if (status === "unauthenticated") redirect("/login");

  const requests = tab === "mine" ? myRequests?.requests : pendingRequests?.requests;
  const loading = tab === "mine" ? loadingMine : loadingPending;

  return (
    <div className="min-h-screen bg-background">
      <NavHeader />
      <main className="mx-auto max-w-6xl px-6 py-8">
        <div className="mb-6 flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-semibold">Access Requests</h1>
            <p className="text-sm text-muted-foreground mt-1">
              Request temporary access to clusters or review pending requests.
            </p>
          </div>
          <Button onClick={() => setShowCreate(true)}>
            <Plus className="mr-1 h-4 w-4" />
            New Request
          </Button>
        </div>

        {/* Tab bar */}
        <div className="mb-6 flex gap-2">
          <Button
            variant={tab === "mine" ? "default" : "outline"}
            size="sm"
            onClick={() => setTab("mine")}
          >
            My Requests
          </Button>
          <Button
            variant={tab === "pending" ? "default" : "outline"}
            size="sm"
            onClick={() => setTab("pending")}
          >
            Pending Reviews
          </Button>
        </div>

        {loading && (
          <p className="text-sm text-muted-foreground py-8 text-center">
            Loading...
          </p>
        )}

        {!loading && (!requests || requests.length === 0) && (
          <p className="text-sm text-muted-foreground py-8 text-center">
            {tab === "mine"
              ? "You haven't made any access requests yet."
              : "No pending requests to review."}
          </p>
        )}

        <div className="space-y-3">
          {requests?.map((r) => (
            <Card key={r.id} className="p-4">
              <div className="flex items-start justify-between">
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <Shield className="h-4 w-4 text-muted-foreground" />
                    <span className="font-medium">{r.roleName}</span>
                    <span className="text-muted-foreground">on</span>
                    <span className="font-medium">{r.clusterName}</span>
                    {statusBadge(r.status as AccessRequestStatus)}
                  </div>
                  {r.reason && (
                    <p className="text-sm text-muted-foreground pl-6">
                      {r.reason}
                    </p>
                  )}
                  <div className="flex items-center gap-4 text-xs text-muted-foreground pl-6">
                    {tab === "pending" && (
                      <span>by {r.requesterDisplayName || r.requesterEmail}</span>
                    )}
                    <span>
                      <Clock className="inline h-3 w-3 mr-0.5" />
                      {new Date(r.createdAt).toLocaleString()}
                    </span>
                    {r.status === "Approved" && r.grantExpiresAt && (
                      <span className="text-green-600">
                        Grant: {timeRemaining(r.grantExpiresAt)} remaining
                      </span>
                    )}
                    {r.status === "Denied" && r.denialReason && (
                      <span className="text-red-600">
                        Reason: {r.denialReason}
                      </span>
                    )}
                    {r.reviewerEmail && (
                      <span>Reviewed by {r.reviewerEmail}</span>
                    )}
                  </div>
                  {r.suggestedReviewers.length > 0 && r.status === "Pending" && (
                    <div className="flex items-center gap-1 pl-6 mt-1">
                      <span className="text-xs text-muted-foreground">Reviewers:</span>
                      {r.suggestedReviewers.map((rev) => (
                        <Badge key={rev.id} variant="outline" className="text-xs">
                          {rev.displayName || rev.email}
                        </Badge>
                      ))}
                    </div>
                  )}
                </div>
                {tab === "pending" && r.status === "Pending" && (
                  <div className="flex gap-2">
                    <Button
                      size="sm"
                      onClick={() => approveMutation.mutate(r.id)}
                      disabled={approveMutation.isPending}
                    >
                      <Check className="mr-1 h-3.5 w-3.5" />
                      Approve
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => setDenyTarget(r)}
                    >
                      <X className="mr-1 h-3.5 w-3.5" />
                      Deny
                    </Button>
                  </div>
                )}
              </div>
            </Card>
          ))}
        </div>
      </main>

      {/* Create Access Request Dialog */}
      {showCreate && token && (
        <CreateRequestDialog
          token={token}
          onClose={() => setShowCreate(false)}
          onCreated={() => {
            queryClient.invalidateQueries({ queryKey: ["access-requests"] });
            setShowCreate(false);
          }}
        />
      )}

      {/* Deny Dialog */}
      <Dialog open={!!denyTarget} onOpenChange={() => setDenyTarget(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Deny Access Request</DialogTitle>
            <DialogDescription>
              Provide a reason for denying this request.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 pt-2">
            <div className="space-y-2">
              <Label>Reason</Label>
              <Input
                value={denyReason}
                onChange={(e) => setDenyReason(e.target.value)}
                placeholder="e.g., Not authorized for production access"
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setDenyTarget(null)}>
                Cancel
              </Button>
              <Button
                variant="destructive"
                disabled={!denyReason.trim() || denyMutation.isPending}
                onClick={() =>
                  denyTarget &&
                  denyMutation.mutate({ id: denyTarget.id, reason: denyReason })
                }
              >
                Deny
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// ── Create Request Dialog ─────────────────────────────────────────────────────

function CreateRequestDialog({
  token,
  onClose,
  onCreated,
}: {
  token: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [roleId, setRoleId] = useState("");
  const [clusterId, setClusterId] = useState("");
  const [reason, setReason] = useState("");
  const [duration, setDuration] = useState("PT8H");
  const [selectedReviewers, setSelectedReviewers] = useState<string[]>([]);
  const [error, setError] = useState("");

  const { data: roles } = useQuery({
    queryKey: ["roles"],
    queryFn: () => fetchRoles(token),
  });

  const { data: clusters } = useQuery({
    queryKey: ["clusters"],
    queryFn: () => fetchClusters(token),
  });

  const { data: users } = useQuery({
    queryKey: ["users"],
    queryFn: () => fetchUsers(token),
  });

  const mutation = useMutation({
    mutationFn: () =>
      createAccessRequest(token, {
        roleId,
        clusterId,
        reason: reason || undefined,
        requestedDuration: duration,
        suggestedReviewerEmails:
          selectedReviewers.length > 0 ? selectedReviewers : undefined,
      }),
    onSuccess: onCreated,
    onError: (err) => setError(err.message),
  });

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    setError("");
    mutation.mutate();
  };

  const toggleReviewer = (email: string) => {
    setSelectedReviewers((prev) =>
      prev.includes(email)
        ? prev.filter((e) => e !== email)
        : [...prev, email],
    );
  };

  const durations = [
    { label: "1 hour", value: "PT1H" },
    { label: "4 hours", value: "PT4H" },
    { label: "8 hours", value: "PT8H" },
    { label: "24 hours", value: "PT24H" },
  ];

  return (
    <Dialog open onOpenChange={onClose}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Request Access</DialogTitle>
          <DialogDescription>
            Request temporary access to a cluster with a specific role.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4 pt-2">
          <div className="space-y-2">
            <Label>Role</Label>
            <select
              className="w-full rounded-md border px-3 py-2 text-sm"
              value={roleId}
              onChange={(e) => setRoleId(e.target.value)}
              required
            >
              <option value="">Select a role...</option>
              {roles?.roles.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name} — {r.description}
                </option>
              ))}
            </select>
          </div>

          <div className="space-y-2">
            <Label>Cluster</Label>
            <select
              className="w-full rounded-md border px-3 py-2 text-sm"
              value={clusterId}
              onChange={(e) => setClusterId(e.target.value)}
              required
            >
              <option value="">Select a cluster...</option>
              {clusters?.clusters.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>

          <div className="space-y-2">
            <Label>Duration</Label>
            <div className="flex gap-2">
              {durations.map((d) => (
                <Button
                  key={d.value}
                  type="button"
                  size="sm"
                  variant={duration === d.value ? "default" : "outline"}
                  onClick={() => setDuration(d.value)}
                >
                  {d.label}
                </Button>
              ))}
            </div>
          </div>

          <div className="space-y-2">
            <Label>Reason</Label>
            <Input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="e.g., Deploying hotfix for INCIDENT-123"
            />
          </div>

          <div className="space-y-2">
            <Label>Suggest Reviewers (optional)</Label>
            <div className="flex flex-wrap gap-1.5 max-h-32 overflow-y-auto">
              {users?.users.map((u) => (
                <Badge
                  key={u.id}
                  variant={
                    selectedReviewers.includes(u.email)
                      ? "default"
                      : "outline"
                  }
                  className="cursor-pointer"
                  onClick={() => toggleReviewer(u.email)}
                >
                  {u.displayName || u.email}
                </Badge>
              ))}
            </div>
          </div>

          {error && (
            <p className="text-sm text-red-600">{error}</p>
          )}

          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={!roleId || !clusterId || mutation.isPending}
            >
              {mutation.isPending ? "Requesting..." : "Request Access"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
