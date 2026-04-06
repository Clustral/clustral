"use client";

import { signIn } from "next-auth/react";
import { LogIn, Server } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

export default function LoginPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <Card className="w-full max-w-sm">
        <CardHeader className="text-center">
          <div className="flex justify-center mb-2">
            <Server className="h-10 w-10 text-foreground" />
          </div>
          <CardTitle className="text-2xl">Clustral</CardTitle>
          <CardDescription>
            Sign in to manage your Kubernetes clusters.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button
            className="w-full"
            onClick={() => signIn("oidc", { callbackUrl: "/clusters" })}
          >
            <LogIn className="h-4 w-4" />
            Sign in with SSO
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
