// ── Cluster ──────────────────────────────────────────────────────────────────
// Mirrors ClusterResponse / ClusterListResponse from the ControlPlane REST API.

export type ClusterStatus = "Pending" | "Connected" | "Disconnected";

export interface Cluster {
  id: string;
  name: string;
  description: string;
  status: ClusterStatus;
  kubernetesVersion: string | null;
  registeredAt: string;
  lastSeenAt: string | null;
  labels: Record<string, string>;
}

export interface ClusterListResponse {
  clusters: Cluster[];
  nextPageToken: string | null;
}

// ── Auth ─────────────────────────────────────────────────────────────────────
// Mirrors IssueKubeconfigCredentialResponse from the ControlPlane REST API.

export interface IssueCredentialResponse {
  credentialId: string;
  token: string;
  issuedAt: string;
  expiresAt: string;
  subject: string;
  displayName: string | null;
}

// ── Cluster registration ─────────────────────────────────────────────────────

export interface RegisterClusterRequest {
  name: string;
  description: string;
  agentPublicKeyPem: string;
  labels: Record<string, string>;
}

export interface RegisterClusterResponse {
  clusterId: string;
  bootstrapToken: string;
}

// ── User info (decoded from JWT) ─────────────────────────────────────────────

export interface UserInfo {
  sub: string;
  email: string;
  name: string;
}
