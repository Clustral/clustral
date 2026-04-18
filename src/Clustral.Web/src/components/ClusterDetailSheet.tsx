"use client";

import type { Cluster, ClusterStatus } from "@/types/api";
import { cn } from "@/lib/utils";
import { RelativeTime } from "@/components/RelativeTime";
import { ConnectSteps } from "@/components/ConnectSteps";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from "@/components/ui/sheet";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";

const statusVariant: Record<
  ClusterStatus,
  "default" | "secondary" | "destructive" | "outline"
> = {
  Connected: "default",
  Pending: "secondary",
  Disconnected: "destructive",
};

interface ClusterDetailSheetProps {
  cluster: Cluster | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function ClusterDetailSheet({
  cluster,
  open,
  onOpenChange,
}: ClusterDetailSheetProps) {
  if (!cluster) return null;

  const labelEntries = Object.entries(cluster.labels);

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-lg overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{cluster.name}</SheetTitle>
          <SheetDescription className="flex items-center gap-2">
            <Badge variant={statusVariant[cluster.status]}>
              <span className="relative flex h-2 w-2 mr-1">
                <span
                  className={cn(
                    "absolute inline-flex h-full w-full rounded-full",
                    cluster.status === "Connected" &&
                      "animate-ping bg-current opacity-75",
                  )}
                />
                <span
                  className={cn(
                    "relative inline-flex h-2 w-2 rounded-full",
                    cluster.status === "Connected" && "bg-current",
                    cluster.status === "Pending" && "bg-current",
                    cluster.status === "Disconnected" && "bg-current",
                  )}
                />
              </span>
              {cluster.status}
            </Badge>
            {cluster.lastSeenAt && (
              <RelativeTime date={cluster.lastSeenAt} />
            )}
          </SheetDescription>
        </SheetHeader>

        <div className="px-4 space-y-3">
          {cluster.description && (
            <p className="text-sm text-muted-foreground">
              {cluster.description}
            </p>
          )}

          <div className="grid grid-cols-2 gap-2 text-sm">
            {cluster.kubernetesVersion && (
              <div>
                <p className="text-xs text-muted-foreground">
                  Kubernetes Version
                </p>
                <p className="font-medium">{cluster.kubernetesVersion}</p>
              </div>
            )}
            <div>
              <p className="text-xs text-muted-foreground">Cluster ID</p>
              <p className="font-mono text-xs truncate">{cluster.id}</p>
            </div>
          </div>

          {labelEntries.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {labelEntries.map(([k, v]) => (
                <Badge
                  key={k}
                  variant="secondary"
                  className="font-mono text-xs"
                >
                  {k}={v}
                </Badge>
              ))}
            </div>
          )}
        </div>

        <Separator className="my-2" />

        <div className="px-4 pb-4">
          <Tabs defaultValue="connect">
            <TabsList>
              <TabsTrigger value="connect">Connect</TabsTrigger>
              <TabsTrigger value="agent">Agent Setup</TabsTrigger>
            </TabsList>
            <TabsContent value="connect" className="mt-4">
              <ConnectSteps cluster={cluster} />
            </TabsContent>
            <TabsContent value="agent" className="mt-4">
              <div className="space-y-3 text-sm">
                <p className="text-muted-foreground">
                  To deploy an agent for this cluster, use the registration
                  flow to get a bootstrap token. The agent connects directly
                  to the ControlPlane on port 5443 (mTLS).
                </p>
                <div>
                  <p className="text-xs text-muted-foreground mb-1">
                    Cluster ID
                  </p>
                  <code className="rounded bg-muted px-2 py-1 font-mono text-xs">
                    {cluster.id}
                  </code>
                </div>
              </div>
            </TabsContent>
          </Tabs>
        </div>
      </SheetContent>
    </Sheet>
  );
}
