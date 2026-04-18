"use client";

import { useState } from "react";
import { Eye, EyeOff, AlertTriangle } from "lucide-react";
import { CopyBlock } from "@/components/CopyBlock";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";

interface Props {
  clusterName: string;
  clusterId: string;
  bootstrapToken: string;
  onDone: () => void;
}

export function AgentSetupSteps({
  clusterName,
  clusterId,
  bootstrapToken,
  onDone,
}: Props) {
  const [tokenVisible, setTokenVisible] = useState(false);

  const kubectlCmd = [
    "# Apply RBAC",
    "kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/clustral-agent/k8s/serviceaccount.yaml",
    "kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/clustral-agent/k8s/clusterrole.yaml",
    "kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/clustral-agent/k8s/clusterrolebinding.yaml",
    "",
    "# Create the agent secret",
    `kubectl -n clustral create secret generic clustral-agent-config \\`,
    `  --from-literal=cluster-id="${clusterId}" \\`,
    `  --from-literal=control-plane-url="https://<YOUR_HOST>:5443" \\`,
    `  --from-literal=bootstrap-token="${bootstrapToken}"`,
    "",
    "# Deploy",
    "kubectl apply -f https://raw.githubusercontent.com/Clustral/clustral/main/src/clustral-agent/k8s/deployment.yaml",
  ].join("\n");

  const goRunCmd = [
    "cd src/clustral-agent",
    `export AGENT_CLUSTER_ID="${clusterId}"`,
    `export AGENT_CONTROL_PLANE_URL="https://localhost:5443"`,
    `export AGENT_BOOTSTRAP_TOKEN="${bootstrapToken}"`,
    "export AGENT_CREDENTIAL_PATH=~/.clustral/agent.token",
    "go run .",
  ].join("\n");

  return (
    <div className="space-y-5">
      <Alert variant="destructive">
        <AlertTriangle className="h-4 w-4" />
        <div className="text-sm ml-2">
          <p className="font-medium text-foreground">
            Save the bootstrap token now
          </p>
          <p className="text-muted-foreground mt-0.5">
            This token is shown only once and cannot be retrieved later. The
            agent exchanges it for a client certificate and JWT on first boot
            (mTLS).
          </p>
        </div>
      </Alert>

      <CopyBlock label="Cluster ID" value={clusterId} />

      <div>
        <p className="mb-1 text-sm font-medium">Bootstrap Token</p>
        <div className="flex items-start gap-2 rounded-md bg-muted px-3 py-2 font-mono text-sm">
          <pre className="flex-1 overflow-x-auto whitespace-pre-wrap break-all text-muted-foreground">
            {tokenVisible
              ? bootstrapToken
              : "\u25CF\u25CF\u25CF\u25CF\u25CF\u25CF\u25CF\u25CF\u25CF\u25CF\u25CF\u25CF"}
          </pre>
          <Button
            variant="ghost"
            size="icon-xs"
            onClick={() => setTokenVisible(!tokenVisible)}
            aria-label={tokenVisible ? "Hide token" : "Show token"}
            className="shrink-0"
          >
            {tokenVisible ? (
              <EyeOff className="h-4 w-4" />
            ) : (
              <Eye className="h-4 w-4" />
            )}
          </Button>
        </div>
        <div className="mt-1">
          <CopyBlock value={bootstrapToken} />
        </div>
      </div>

      <Separator />

      <div>
        <h4 className="text-sm font-medium mb-2">
          Deploy the agent to{" "}
          <span className="font-semibold">{clusterName}</span>
        </h4>

        <p className="text-xs text-muted-foreground mb-1">
          The agent connects directly to the ControlPlane on port 5443 (mTLS).
          On first boot, it exchanges the bootstrap token for a client
          certificate and JWT. Certificates auto-renew before expiry.
        </p>

        <p className="text-xs text-muted-foreground mt-4 mb-3">
          Option 1: Kubernetes (production)
        </p>
        <CopyBlock value={kubectlCmd} />

        <p className="text-xs text-muted-foreground mt-4 mb-3">
          Option 2: Go (local dev)
        </p>
        <CopyBlock value={goRunCmd} />
      </div>

      <div className="flex justify-end pt-2">
        <Button onClick={onDone}>Done</Button>
      </div>
    </div>
  );
}
