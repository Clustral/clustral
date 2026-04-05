"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchClusters } from "@/lib/api";
import { useSession } from "next-auth/react";

export const clusterKeys = {
  all: ["clusters"] as const,
  list: (status?: string) => [...clusterKeys.all, "list", status] as const,
};

export function useClusters(statusFilter?: string) {
  const { data: session } = useSession();
  const token = (session as any)?.accessToken as string | undefined;

  return useQuery({
    queryKey: clusterKeys.list(statusFilter),
    queryFn: () => fetchClusters(token!, { status: statusFilter }),
    enabled: !!token,
    refetchInterval: 15_000,
  });
}
