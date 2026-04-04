import { useState, useCallback } from "react";
import { Copy, Check, AlertTriangle } from "lucide-react";

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
    `  --set agent.controlPlaneUrl="<YOUR_CONTROLPLANE_GRPC_URL>" \\`,
    `  --set agent.bootstrapToken="${bootstrapToken}"`,
  ].join("\n");

  const dotnetRunCmd = [
    "dotnet run --project src/Clustral.Agent -- \\",
    `  --Agent:ClusterId="${clusterId}" \\`,
    "  --Agent:ControlPlaneUrl=http://localhost:5001 \\",
    `  --Agent:BootstrapToken="${bootstrapToken}"`,
  ].join("\n");

  return (
    <div className="space-y-5">
      <div className="rounded-md border border-pending/50 bg-pending/5 p-3 flex gap-2">
        <AlertTriangle className="h-4 w-4 text-pending shrink-0 mt-0.5" />
        <div className="text-sm">
          <p className="font-medium text-foreground">
            Save the bootstrap token now
          </p>
          <p className="text-muted-foreground mt-0.5">
            This token is shown only once and cannot be retrieved later. It is
            used to authenticate the agent on first connection.
          </p>
        </div>
      </div>

      <div>
        <h4 className="text-sm font-medium mb-1">Cluster ID</h4>
        <CopyBlock value={clusterId} />
      </div>

      <div>
        <h4 className="text-sm font-medium mb-1">Bootstrap Token</h4>
        <CopyBlock value={bootstrapToken} />
      </div>

      <hr className="border-border" />

      <div>
        <h4 className="text-sm font-medium mb-2">
          Deploy the agent to <span className="font-semibold">{clusterName}</span>
        </h4>

        <p className="text-xs text-muted-foreground mb-3">
          Option 1: Helm (production)
        </p>
        <CopyBlock value={helmInstallCmd} multiline />

        <p className="text-xs text-muted-foreground mt-4 mb-3">
          Option 2: dotnet run (local dev)
        </p>
        <CopyBlock value={dotnetRunCmd} multiline />
      </div>

      <div className="flex justify-end pt-2">
        <button
          type="button"
          onClick={onDone}
          className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
        >
          Done
        </button>
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
      <button
        type="button"
        onClick={copy}
        className="shrink-0 mt-0.5 text-muted-foreground hover:text-foreground transition-colors"
        aria-label="Copy"
      >
        {copied ? (
          <Check className="h-4 w-4 text-connected" />
        ) : (
          <Copy className="h-4 w-4" />
        )}
      </button>
    </div>
  );
}
