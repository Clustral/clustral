import type {
  ClusterListResponse,
  RegisterClusterRequest,
  RegisterClusterResponse,
  RoleListResponse,
  UserListResponse,
  RoleAssignmentListResponse,
  Role,
  RoleAssignment,
  AccessRequest,
  AccessRequestListResponse,
} from "@/types/api";

const BASE = "/api/v1";

async function apiFetch<T>(
  path: string,
  token: string,
  init?: RequestInit,
): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
      ...init?.headers,
    },
  });

  if (res.status === 401) {
    // Token expired or invalid — auto-logout.
    if (typeof window !== "undefined") {
      window.location.href = "/api/auth/signout?callbackUrl=/login";
    }
    throw new Error("Session expired. Redirecting to login...");
  }

  if (!res.ok) {
    const detail = await res.text().catch(() => "");
    throw new Error(`${res.status}: ${detail || res.statusText}`);
  }

  return res.json() as Promise<T>;
}

// ── Clusters ─────────────────────────────────────────────────────────────────

export function fetchClusters(
  token: string,
  params?: { status?: string; pageSize?: number; pageToken?: string },
): Promise<ClusterListResponse> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set("statusFilter", params.status);
  if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
  if (params?.pageToken) qs.set("pageToken", params.pageToken);

  const query = qs.toString();
  return apiFetch<ClusterListResponse>(
    `/clusters${query ? `?${query}` : ""}`,
    token,
  );
}

export async function deleteCluster(
  token: string,
  clusterId: string,
): Promise<void> {
  const res = await fetch(`${BASE}/clusters/${clusterId}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok && res.status !== 404) {
    const detail = await res.text().catch(() => "");
    throw new Error(`${res.status}: ${detail || res.statusText}`);
  }
}

export function registerCluster(
  token: string,
  request: RegisterClusterRequest,
): Promise<RegisterClusterResponse> {
  return apiFetch<RegisterClusterResponse>("/clusters", token, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

// ── Roles ────────────────────────────────────────────────────────────────────

export function fetchRoles(token: string): Promise<RoleListResponse> {
  return apiFetch<RoleListResponse>("/roles", token);
}

export function createRole(
  token: string,
  request: { name: string; description: string; kubernetesGroups: string[] },
): Promise<Role> {
  return apiFetch<Role>("/roles", token, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateRole(
  token: string,
  id: string,
  request: { name?: string; description?: string; kubernetesGroups?: string[] },
): Promise<Role> {
  return apiFetch<Role>(`/roles/${id}`, token, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

export async function deleteRole(token: string, id: string): Promise<void> {
  await apiFetch<void>(`/roles/${id}`, token, { method: "DELETE" });
}

// ── Users ────────────────────────────────────────────────────────────────────

export function fetchUsers(token: string): Promise<UserListResponse> {
  return apiFetch<UserListResponse>("/users", token);
}

export function fetchUserAssignments(
  token: string,
  userId: string,
): Promise<RoleAssignmentListResponse> {
  return apiFetch<RoleAssignmentListResponse>(`/users/${userId}/assignments`, token);
}

export function assignRole(
  token: string,
  userId: string,
  request: { roleId: string; clusterId: string },
): Promise<RoleAssignment> {
  return apiFetch<RoleAssignment>(`/users/${userId}/assignments`, token, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export async function removeAssignment(
  token: string,
  userId: string,
  assignmentId: string,
): Promise<void> {
  await apiFetch<void>(`/users/${userId}/assignments/${assignmentId}`, token, {
    method: "DELETE",
  });
}

// ── Access Requests (JIT) ───────────────────────────────────────────────────

export function fetchAccessRequests(
  token: string,
  params?: { status?: string; mine?: boolean; active?: boolean },
): Promise<AccessRequestListResponse> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set("status", params.status);
  if (params?.mine) qs.set("mine", "true");
  if (params?.active) qs.set("active", "true");
  const query = qs.toString();
  return apiFetch<AccessRequestListResponse>(
    `/access-requests${query ? `?${query}` : ""}`,
    token,
  );
}

export function createAccessRequest(
  token: string,
  request: {
    roleId: string;
    clusterId: string;
    reason?: string;
    requestedDuration?: string;
    suggestedReviewerEmails?: string[];
  },
): Promise<AccessRequest> {
  return apiFetch<AccessRequest>("/access-requests", token, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function approveAccessRequest(
  token: string,
  requestId: string,
  durationOverride?: string,
): Promise<AccessRequest> {
  return apiFetch<AccessRequest>(`/access-requests/${requestId}/approve`, token, {
    method: "POST",
    body: JSON.stringify({ durationOverride }),
  });
}

export function denyAccessRequest(
  token: string,
  requestId: string,
  reason: string,
): Promise<AccessRequest> {
  return apiFetch<AccessRequest>(`/access-requests/${requestId}/deny`, token, {
    method: "POST",
    body: JSON.stringify({ reason }),
  });
}

export function revokeAccessRequest(
  token: string,
  requestId: string,
  reason?: string,
): Promise<AccessRequest> {
  return apiFetch<AccessRequest>(`/access-requests/${requestId}/revoke`, token, {
    method: "POST",
    body: JSON.stringify({ reason }),
  });
}
