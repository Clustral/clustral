"use client";

import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { fetchAuditEvents, fetchAuditEventDetail } from "@/lib/api";
import type { AuditFilters } from "@/types/api";

export const auditKeys = {
  all: ["audit"],
  list: (filters?: AuditFilters) => [...auditKeys.all, "list", filters],
  detail: (uid: string) => [...auditKeys.all, "detail", uid],
};

export function useAudit(filters?: AuditFilters) {
  const { data: session } = useSession();
  const token = (session as any)?.accessToken as string | undefined;

  return useQuery({
    queryKey: auditKeys.list(filters),
    queryFn: () => fetchAuditEvents(token!, filters),
    enabled: !!token,
    refetchInterval: 30_000,
  });
}

export function useAuditDetail(uid: string | null) {
  const { data: session } = useSession();
  const token = (session as any)?.accessToken as string | undefined;

  return useQuery({
    queryKey: auditKeys.detail(uid!),
    queryFn: () => fetchAuditEventDetail(token!, uid!),
    enabled: !!token && !!uid,
  });
}
