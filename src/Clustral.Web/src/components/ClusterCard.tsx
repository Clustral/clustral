import { cn } from "@/lib/utils";
import type { Cluster, ClusterStatus } from "@/types/api";
import { Server, Clock, Tag, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";

const statusVariant: Record<ClusterStatus, "default" | "secondary" | "destructive" | "outline"> = {
  Connected: "default",
  Pending: "secondary",
  Disconnected: "destructive",
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
    <Card
      role="button"
      tabIndex={0}
      onClick={() => onSelect(cluster)}
      onKeyDown={(e) => e.key === "Enter" && onSelect(cluster)}
      className={cn(
        "w-full text-left p-4 cursor-pointer transition-colors",
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
          <Badge variant={statusVariant[cluster.status]}>
            {cluster.status}
          </Badge>
          <Button
            variant="ghost"
            size="icon-xs"
            title="Delete cluster"
            onClick={(e) => {
              e.stopPropagation();
              onDelete(cluster);
            }}
            className="text-muted-foreground hover:text-destructive hover:bg-destructive/10"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
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
            <Badge key={k} variant="secondary" className="font-mono text-xs">
              {k}={v}
            </Badge>
          ))}
        </div>
      )}
    </Card>
  );
}
