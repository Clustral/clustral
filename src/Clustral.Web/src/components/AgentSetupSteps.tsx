import { useState, useCallback } from "react";
import { Copy, Check, AlertTriangle } from "lucide-react";
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
  const helmInstallCmd = [
    "helm install clustral-agent ./infra/helm/clustral-agent \\",
    `  --set agent.clusterId="${clusterId}" \\`,
    `  --set agent.controlPlaneUrl="https://<YOUR_HOST>:5443" \\`,
    `  --set agent.bootstrapToken="${bootstrapToken}"`,
  ].join("\n");

  const goRunCmd = [
    "cd src/clustral-agent",
    `export AGENT_CLUSTER_ID="${clusterId}"`,
    "export AGENT_CONTROL_PLANE_URL=http://localhost:5001",
    `export AGENT_BOOTSTRAP_TOKEN="${bootstrapToken}"`,
    "go run .",
  ].join("\n");

  return (
    <div className="space-y-5">
      <Alert className="border-pending/50 bg-pending/5">
        <AlertTriangle className="h-4 w-4 text-pending" />
        <div className="text-sm ml-2">
          <p className="font-medium text-foreground">
            Save the bootstrap token now
          </p>
          <p className="text-muted-foreground mt-0.5">
            This token is shown only once and cannot be retrieved later. It is
            used to authenticate the agent on first connection.
          </p>
        </div>
      </Alert>

      <div>
        <h4 className="text-sm font-medium mb-1">Cluster ID</h4>
        <CopyBlock value={clusterId} />
      </div>

      <div>
        <h4 className="text-sm font-medium mb-1">Bootstrap Token</h4>
        <CopyBlock value={bootstrapToken} />
      </div>

      <Separator />

      <div>
        <h4 className="text-sm font-medium mb-2">
          Deploy the agent to <span className="font-semibold">{clusterName}</span>
        </h4>

        <p className="text-xs text-muted-foreground mb-3">
          Option 1: Helm (production)
        </p>
        <CopyBlock value={helmInstallCmd} multiline />

        <p className="text-xs text-muted-foreground mt-4 mb-3">
          Option 2: Go (local dev)
        </p>
        <CopyBlock value={goRunCmd} multiline />
      </div>

      <div className="flex justify-end pt-2">
        <Button onClick={onDone}>Done</Button>
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

function CopyBlock({
  value,
  multiline,
}: {
  value: string;
  multiline?: boolean;
}) {
  const [copied, setCopied] = useState(false);

  const copy = useCallback(() => {
    navigator.clipboard.writeText(value).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [value]);

  return (
    <div className="flex items-start gap-2 rounded-md bg-secondary px-3 py-2 font-mono text-sm">
      <code
        className={`flex-1 text-secondary-foreground ${multiline ? "whitespace-pre-wrap break-all" : "truncate"}`}
      >
        {value}
      </code>
      <Button
        variant="ghost"
        size="icon-xs"
        onClick={copy}
        aria-label="Copy"
        className="shrink-0"
      >
        {copied ? (
          <Check className="h-4 w-4 text-connected" />
        ) : (
          <Copy className="h-4 w-4" />
        )}
      </Button>
    </div>
  );
}
