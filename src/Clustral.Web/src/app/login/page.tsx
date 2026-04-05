"use client";

import { signIn } from "next-auth/react";
import { LogIn, Server } from "lucide-react";

export default function LoginPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="w-full max-w-sm space-y-6 text-center">
        <div className="flex flex-col items-center gap-3">
          <Server className="h-10 w-10 text-foreground" />
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            Clustral
          </h1>
          <p className="text-sm text-muted-foreground">
            Sign in to manage your Kubernetes clusters.
          </p>
        </div>

        <button
          type="button"
          onClick={() => signIn("oidc", { callbackUrl: "/clusters" })}
          className="inline-flex w-full items-center justify-center gap-2 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
        >
          <LogIn className="h-4 w-4" />
          Sign in with SSO
        </button>
      </div>
    </div>
  );
}
