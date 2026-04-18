"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchClusters, fetchCluster } from "@/lib/api";
import { useSession } from "next-auth/react";

export const clusterKeys = {
  all: ["clusters"] as const,
  list: (status?: string) => [...clusterKeys.all, "list", status] as const,
  detail: (id: string) => [...clusterKeys.all, "detail", id] as const,
};

export function useClusters(statusFilter?: string) {
  const { data: session } = useSession();
  const token = (session as never as { accessToken: string })?.accessToken;

  return useQuery({
    queryKey: clusterKeys.list(statusFilter),
    queryFn: () => fetchClusters(token!, { status: statusFilter }),
    enabled: !!token,
    refetchInterval: 15_000,
  });
}

export function useCluster(id: string) {
  const { data: session } = useSession();
  const token = (session as never as { accessToken: string })?.accessToken;

  return useQuery({
    queryKey: clusterKeys.detail(id),
    queryFn: () => fetchCluster(token!, id),
    enabled: !!token && !!id,
    refetchInterval: 3_000,
  });
}
