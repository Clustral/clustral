"use client";

import type { Cluster } from "@/types/api";
import { Key, Terminal, ArrowRight } from "lucide-react";
import { CopyBlock } from "@/components/CopyBlock";

interface ConnectStepsProps {
  cluster: Cluster;
}

export function ConnectSteps({ cluster }: ConnectStepsProps) {
  return (
    <div className="rounded-lg border p-6">
      <h3 className="text-lg font-medium mb-1">
        Connect to {cluster.name}
      </h3>
      <p className="text-sm text-muted-foreground mb-6">
        Run these commands in your terminal to configure kubectl access.
      </p>

      <ol className="space-y-4">
        <Step
          number={1}
          icon={<Key className="h-4 w-4" />}
          title="Authenticate with Clustral"
          description="Opens your browser for Keycloak SSO login."
          command="clustral login"
        />
        <Step
          number={2}
          icon={<Terminal className="h-4 w-4" />}
          title="Get kubeconfig credentials"
          description="Issues a short-lived token and writes it to ~/.kube/config."
          command={`clustral kube login ${cluster.id}`}
        />
        <Step
          number={3}
          icon={<ArrowRight className="h-4 w-4" />}
          title="Use kubectl as usual"
          description={`Your context is set to clustral-${cluster.id}.`}
          command="kubectl get namespaces"
        />
      </ol>

      <a
        href="https://github.com/Clustral/clustral#readme"
        target="_blank"
        rel="noopener noreferrer"
        className="mt-6 block text-xs text-muted-foreground hover:underline"
      >
        View CLI docs
      </a>
    </div>
  );
}

// ---------------------------------------------------------------------------

interface StepProps {
  number: number;
  icon: React.ReactNode;
  title: string;
  description: string;
  command: string;
}

function Step({ number, icon, title, description, command }: StepProps) {
  return (
    <li className="flex gap-3">
      <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground text-xs font-medium">
        {number}
      </span>

      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 font-medium text-sm">
          {icon}
          {title}
        </div>
        <p className="text-xs text-muted-foreground mt-0.5">{description}</p>

        <div className="mt-2">
          <CopyBlock value={`$ ${command}`} />
        </div>
      </div>
    </li>
  );
}
