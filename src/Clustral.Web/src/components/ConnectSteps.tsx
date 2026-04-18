import type { Cluster } from "@/types/api";
import { Copy, Check, Terminal, Key, ArrowRight } from "lucide-react";
import { useState, useCallback } from "react";
import { Button } from "@/components/ui/button";

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
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

interface StepProps {
  number: number;
  icon: React.ReactNode;
  title: string;
  description: string;
  command: string;
}

function Step({ number, icon, title, description, command }: StepProps) {
  const [copied, setCopied] = useState(false);

  const copy = useCallback(() => {
    navigator.clipboard.writeText(command).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [command]);

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

        <div className="mt-2 flex items-center gap-2 rounded-md bg-secondary px-3 py-2 font-mono text-sm">
          <code className="flex-1 truncate text-secondary-foreground">
            $ {command}
          </code>
          <Button
            variant="ghost"
            size="icon"
            onClick={copy}
            aria-label="Copy command"
            className="shrink-0 h-6 w-6"
          >
            {copied ? (
              <Check className="h-4 w-4 text-connected" />
            ) : (
              <Copy className="h-4 w-4" />
            )}
          </Button>
        </div>
      </div>
    </li>
  );
}
