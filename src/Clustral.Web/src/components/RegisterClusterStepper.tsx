"use client";

import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { registerCluster } from "@/lib/api";
import { clusterKeys, useCluster } from "@/hooks/useClusters";
import { CopyBlock } from "@/components/CopyBlock";
import { cn } from "@/lib/utils";
import { CheckCircle2, Eye, EyeOff } from "lucide-react";
import type { RegisterClusterResponse } from "@/types/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Alert } from "@/components/ui/alert";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

interface RegisterClusterStepperProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onRegistered: (id: string) => void;
}

function StepIndicator({ current, total }: { current: number; total: number }) {
  return (
    <div className="flex items-center justify-center gap-0 mb-6">
      {Array.from({ length: total }, (_, i) => {
        const step = i + 1;
        const isActive = step === current;
        const isCompleted = step < current;
        return (
          <div key={step} className="flex items-center">
            {i > 0 && (
              <div
                className={cn(
                  "h-px w-10",
                  isCompleted || isActive ? "bg-primary" : "bg-muted",
                )}
              />
            )}
            <div
              className={cn(
                "flex h-8 w-8 items-center justify-center rounded-full text-sm font-medium",
                isActive && "bg-primary text-primary-foreground",
                isCompleted && "bg-primary/20 text-primary",
                !isActive && !isCompleted && "bg-muted text-muted-foreground",
              )}
            >
              {step}
            </div>
          </div>
        );
      })}
    </div>
  );
}

export function RegisterClusterStepper({
  open,
  onOpenChange,
  onRegistered,
}: RegisterClusterStepperProps) {
  const { data: session } = useSession();
  const token = (session as never as { accessToken: string })?.accessToken;
  const queryClient = useQueryClient();

  const [step, setStep] = useState(1);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [result, setResult] = useState<RegisterClusterResponse | null>(null);
  const [tokenVisible, setTokenVisible] = useState(false);

  const mutation = useMutation({
    mutationFn: () =>
      registerCluster(token!, {
        name,
        description,
        labels: {},
      }),
    onSuccess: (data) => {
      setResult(data);
      queryClient.invalidateQueries({ queryKey: clusterKeys.all });
      setStep(2);
    },
  });

  const registeredId = result?.clusterId ?? "";
  const { data: clusterData } = useCluster(step === 3 ? registeredId : "");
  const isConnected = clusterData?.status === "Connected";

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    mutation.mutate();
  }

  function handleClose() {
    setStep(1);
    setName("");
    setDescription("");
    setResult(null);
    setTokenVisible(false);
    mutation.reset();
    onOpenChange(false);
  }

  function handleDone() {
    if (result) onRegistered(result.clusterId);
    handleClose();
  }

  const helmCmd = [
    `helm repo add clustral https://charts.clustral.io`,
    `helm install clustral-agent clustral/clustral-agent \\`,
    `  --set agent.clusterId="${registeredId}" \\`,
    `  --set agent.controlPlaneUrl="https://<YOUR_HOST>:5443" \\`,
    `  --set agent.bootstrapToken="${result?.bootstrapToken ?? ""}"`,
  ].join("\n");

  const localCmd = [
    `cd src/clustral-agent`,
    `export AGENT_CLUSTER_ID="${registeredId}"`,
    `export AGENT_CONTROL_PLANE_URL="https://localhost:5443"`,
    `export AGENT_BOOTSTRAP_TOKEN="${result?.bootstrapToken ?? ""}"`,
    `go run .`,
  ].join("\n");

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent className="sm:max-w-xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {step === 1 && "Register New Cluster"}
            {step === 2 && "Deploy Agent"}
            {step === 3 && "Verify Connection"}
          </DialogTitle>
        </DialogHeader>

        <StepIndicator current={step} total={3} />

        {step === 1 && (
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="stepper-name">Cluster name</Label>
              <Input
                id="stepper-name"
                required
                placeholder="e.g. production, staging, dev-local"
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="stepper-desc">
                Description{" "}
                <span className="text-muted-foreground font-normal">
                  (optional)
                </span>
              </Label>
              <Input
                id="stepper-desc"
                placeholder="e.g. Production cluster in eu-west-1"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
              />
            </div>
            {mutation.isError && (
              <Alert variant="destructive">
                {(mutation.error as Error).message}
              </Alert>
            )}
            <div className="flex justify-end gap-3 pt-2">
              <Button
                type="button"
                variant="outline"
                onClick={handleClose}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={mutation.isPending || !name.trim()}
              >
                {mutation.isPending ? "Registering..." : "Register"}
              </Button>
            </div>
          </form>
        )}

        {step === 2 && result && (
          <div className="space-y-5">
            <Alert>
              Cluster registered successfully. Deploy the agent to connect.
            </Alert>

            <CopyBlock label="Cluster ID" value={result.clusterId} />

            <div>
              <p className="mb-1 text-sm font-medium">Bootstrap Token</p>
              <div className="flex items-start gap-2 rounded-md bg-muted px-3 py-2 font-mono text-sm">
                <pre className="flex-1 overflow-x-auto whitespace-pre-wrap break-all text-muted-foreground">
                  {tokenVisible
                    ? result.bootstrapToken
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
                <CopyBlock value={result.bootstrapToken} />
              </div>
            </div>

            <Tabs defaultValue="kubernetes">
              <TabsList>
                <TabsTrigger value="kubernetes">Kubernetes</TabsTrigger>
                <TabsTrigger value="local">Local Dev</TabsTrigger>
              </TabsList>
              <TabsContent value="kubernetes" className="mt-3">
                <CopyBlock value={helmCmd} />
              </TabsContent>
              <TabsContent value="local" className="mt-3">
                <CopyBlock value={localCmd} />
              </TabsContent>
            </Tabs>

            <div className="flex justify-end gap-3 pt-2">
              <Button variant="outline" onClick={() => setStep(3)}>
                Next
              </Button>
            </div>
          </div>
        )}

        {step === 3 && (
          <div className="space-y-5">
            {isConnected ? (
              <Alert>
                <CheckCircle2 className="h-4 w-4 text-connected" />
                <div className="ml-2">
                  <p className="font-medium">Agent connected!</p>
                  {clusterData?.kubernetesVersion && (
                    <p className="text-sm text-muted-foreground mt-0.5">
                      Kubernetes {clusterData.kubernetesVersion}
                    </p>
                  )}
                </div>
              </Alert>
            ) : (
              <div className="flex flex-col items-center py-8 text-center">
                <div className="flex items-center gap-1 text-sm text-muted-foreground mb-4">
                  <span className="animate-pulse">.</span>
                  <span className="animate-pulse [animation-delay:200ms]">
                    .
                  </span>
                  <span className="animate-pulse [animation-delay:400ms]">
                    .
                  </span>
                  <span className="ml-2">
                    Waiting for agent to connect...
                  </span>
                </div>
                <Skeleton className="h-4 w-[200px]" />
              </div>
            )}

            <div className="flex flex-col items-center gap-2 pt-2">
              <Button onClick={handleDone}>
                {isConnected ? "Done" : "Done"}
              </Button>
              {!isConnected && (
                <button
                  type="button"
                  onClick={handleDone}
                  className="text-xs text-muted-foreground hover:underline"
                >
                  Skip for now
                </button>
              )}
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
