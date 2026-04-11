"use client";

import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { fetchAuditEvents } from "@/lib/api";
import type { AuditFilters } from "@/types/api";

export const auditKeys = {
  all: ["audit"],
  list: (filters?: AuditFilters) => [...auditKeys.all, "list", filters],
};

export function useAudit(filters?: AuditFilters) {
  const { data: session } = useSession();
  const token = (session as any)?.accessToken as string | undefined;

  return useQuery({
    queryKey: auditKeys.list(filters),
    queryFn: () => fetchAuditEvents(token!, filters),
    enabled: !!token,
    refetchInterval: 30_000, // poll every 30s
  });
}
