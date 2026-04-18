"use client";

import { Server, Plus } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

interface ClusterEmptyStateProps {
  onRegister: () => void;
}

export function ClusterEmptyState({ onRegister }: ClusterEmptyStateProps) {
  return (
    <Card className="flex flex-col items-center justify-center border-dashed py-16 text-center">
      <Server className="h-12 w-12 text-muted-foreground/30 mb-4" />
      <h3 className="text-lg font-semibold mb-2">No clusters registered</h3>
      <p className="text-sm text-muted-foreground mb-6 max-w-sm">
        Register a cluster to start proxying kubectl traffic through Clustral.
        Agents connect via gRPC and require no inbound firewall rules.
      </p>
      <Button onClick={onRegister}>
        <Plus className="h-4 w-4" />
        Register your first cluster
      </Button>
      <a
        href="https://github.com/Clustral/clustral#readme"
        target="_blank"
        rel="noopener noreferrer"
        className="mt-3 text-xs text-muted-foreground hover:underline"
      >
        Read the docs
      </a>
    </Card>
  );
}
