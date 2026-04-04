import { useQuery } from "@tanstack/react-query";
import { fetchClusters } from "@/lib/api";
import { useAuthStore } from "@/stores/useAuthStore";

export const clusterKeys = {
  all: ["clusters"] as const,
  list: (status?: string) => [...clusterKeys.all, "list", status] as const,
};

export function useClusters(statusFilter?: string) {
  const token = useAuthStore((s) => s.token);

  return useQuery({
    queryKey: clusterKeys.list(statusFilter),
    queryFn: () => fetchClusters(token!, { status: statusFilter }),
    enabled: !!token,
    refetchInterval: 15_000,
  });
}
