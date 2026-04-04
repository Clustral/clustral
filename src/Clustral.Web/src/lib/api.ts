import type { ClusterListResponse, RegisterClusterRequest, RegisterClusterResponse } from "@/types/api";

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
