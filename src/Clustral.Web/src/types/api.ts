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

// ── Roles ────────────────────────────────────────────────────────────────────

export interface Role {
  id: string;
  name: string;
  description: string;
  kubernetesGroups: string[];
  createdAt: string;
}

export interface RoleListResponse {
  roles: Role[];
}

// ── Users (managed) ──────────────────────────────────────────────────────────

export interface ManagedUser {
  id: string;
  email: string;
  displayName: string | null;
  createdAt: string;
  lastSeenAt: string | null;
}

export interface UserListResponse {
  users: ManagedUser[];
}

// ── Role Assignments ─────────────────────────────────────────────────────────

export interface RoleAssignment {
  id: string;
  userId: string;
  roleId: string;
  roleName: string;
  clusterId: string;
  clusterName: string;
  assignedAt: string;
  assignedBy: string;
}

export interface RoleAssignmentListResponse {
  assignments: RoleAssignment[];
}

// ── Access Requests (JIT) ───────────────────────────────────────────────────

export type AccessRequestStatus = "Pending" | "Approved" | "Denied" | "Expired" | "Revoked";

export interface ReviewerInfo {
  id: string;
  email: string;
  displayName: string | null;
}

export interface AccessRequest {
  id: string;
  requesterId: string;
  requesterEmail: string;
  requesterDisplayName: string | null;
  roleId: string;
  roleName: string;
  clusterId: string;
  clusterName: string;
  status: AccessRequestStatus;
  reason: string;
  requestedDuration: string;
  createdAt: string;
  requestExpiresAt: string;
  suggestedReviewers: ReviewerInfo[];
  reviewerId: string | null;
  reviewerEmail: string | null;
  reviewedAt: string | null;
  denialReason: string | null;
  grantExpiresAt: string | null;
  revokedAt: string | null;
  revokedByEmail: string | null;
  revokedReason: string | null;
}

export interface AccessRequestListResponse {
  requests: AccessRequest[];
}

// ── Audit Events ────────────────────────────────────────────────────────────
// Mirrors AuditEventResponse / AuditListResponse from the AuditService REST API.

export type AuditSeverity = "Info" | "Warning" | "Error";

export interface AuditEvent {
  uid: string;
  event: string;
  code: string;
  category: string;
  severity: AuditSeverity;
  success: boolean;
  user: string | null;
  userId: string | null;
  resourceType: string | null;
  resourceId: string | null;
  resourceName: string | null;
  clusterName: string | null;
  clusterId: string | null;
  time: string;
  receivedAt: string | null;
  message: string | null;
  error: string | null;
  metadata: Record<string, unknown> | null;
}

export interface AuditListResponse {
  events: AuditEvent[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface AuditFilters {
  category?: string;
  code?: string;
  severity?: string;
  user?: string;
  clusterId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}
