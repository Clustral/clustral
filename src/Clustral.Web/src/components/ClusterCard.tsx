"use client";

import { cn } from "@/lib/utils";
import type { Cluster, ClusterStatus } from "@/types/api";
import { Server, Clock, Tag, MoreHorizontal, Eye, Trash2 } from "lucide-react";
import { RelativeTime } from "@/components/RelativeTime";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Tooltip,
  TooltipTrigger,
  TooltipContent,
} from "@/components/ui/tooltip";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";

const statusVariant: Record<ClusterStatus, "default" | "secondary" | "destructive" | "outline"> = {
  Connected: "default",
  Pending: "secondary",
  Disconnected: "destructive",
};

const dotColor: Record<ClusterStatus, string> = {
  Connected: "bg-emerald-500",
  Pending: "bg-yellow-500",
  Disconnected: "bg-destructive",
};

interface ClusterCardProps {
  cluster: Cluster;
  onSelect: (cluster: Cluster) => void;
  onDelete: (cluster: Cluster) => void;
}

export function ClusterCard({ cluster, onSelect, onDelete }: ClusterCardProps) {
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
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2 min-w-0">
          <Server className="h-4 w-4 shrink-0 text-muted-foreground" />
          <Tooltip>
            <TooltipTrigger className="truncate font-medium">
              {cluster.name}
            </TooltipTrigger>
            <TooltipContent>{cluster.name}</TooltipContent>
          </Tooltip>
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <Badge variant={statusVariant[cluster.status]}>
            <span className="relative flex h-2 w-2 mr-1">
              {cluster.status === "Connected" && (
                <span
                  className={cn(
                    "absolute inline-flex h-full w-full rounded-full opacity-75 animate-ping",
                    dotColor[cluster.status],
                  )}
                />
              )}
              <span
                className={cn(
                  "relative inline-flex h-2 w-2 rounded-full",
                  dotColor[cluster.status],
                )}
              />
            </span>
            {cluster.status}
          </Badge>

          <DropdownMenu>
            <DropdownMenuTrigger
              render={
                <Button
                  variant="ghost"
                  size="icon-xs"
                  className="text-muted-foreground"
                  onClick={(e) => e.stopPropagation()}
                />
              }
            >
              <MoreHorizontal className="h-3.5 w-3.5" />
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem
                onClick={(e) => {
                  e.stopPropagation();
                  onSelect(cluster);
                }}
              >
                <Eye className="h-4 w-4" />
                View Details
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                variant="destructive"
                onClick={(e) => {
                  e.stopPropagation();
                  onDelete(cluster);
                }}
              >
                <Trash2 className="h-4 w-4" />
                Delete
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
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
            <RelativeTime date={cluster.lastSeenAt} />
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
