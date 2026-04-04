import { cn } from "@/lib/utils";
import type { Cluster, ClusterStatus } from "@/types/api";
import { Server, Clock, Tag, Trash2 } from "lucide-react";

const statusStyles: Record<ClusterStatus, string> = {
  Connected: "bg-connected/10 text-connected",
  Pending: "bg-pending/10 text-pending",
  Disconnected: "bg-destructive/10 text-destructive",
};

interface ClusterCardProps {
  cluster: Cluster;
  selected: boolean;
  onSelect: (cluster: Cluster) => void;
  onDelete: (cluster: Cluster) => void;
}

export function ClusterCard({ cluster, selected, onSelect, onDelete }: ClusterCardProps) {
  const labelEntries = Object.entries(cluster.labels);

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => onSelect(cluster)}
      onKeyDown={(e) => e.key === "Enter" && onSelect(cluster)}
      className={cn(
        "w-full text-left rounded-lg border p-4 transition-colors",
        "hover:border-primary/40 hover:bg-accent/50",
        selected && "border-primary bg-accent",
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2 min-w-0">
          <Server className="h-4 w-4 shrink-0 text-muted-foreground" />
          <span className="font-medium truncate">{cluster.name}</span>
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <span
            className={cn(
              "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
              statusStyles[cluster.status],
            )}
          >
            {cluster.status}
          </span>
          <button
            type="button"
            title="Delete cluster"
            onClick={(e) => {
              e.stopPropagation();
              onDelete(cluster);
            }}
            className="rounded p-1 text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {cluster.description && (
        <p className="mt-1 text-sm text-muted-foreground line-clamp-2 pl-6">
          {cluster.description}
        </p>
      )}

      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 pl-6 text-xs text-muted-foreground">
        {cluster.kubernetesVersion && (
          <span className="flex items-center gap-1">
            <Tag className="h-3 w-3" />
            {cluster.kubernetesVersion}
          </span>
        )}
        {cluster.lastSeenAt && (
          <span className="flex items-center gap-1">
            <Clock className="h-3 w-3" />
            {new Date(cluster.lastSeenAt).toLocaleString()}
          </span>
        )}
      </div>

      {labelEntries.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1 pl-6">
          {labelEntries.map(([k, v]) => (
            <span
              key={k}
              className="rounded bg-secondary px-1.5 py-0.5 text-xs text-secondary-foreground"
            >
              {k}={v}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
