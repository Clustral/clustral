import { useAuthStore } from "@/stores/useAuthStore";
import { LogIn } from "lucide-react";

export function LoginPage() {
  const login = useAuthStore((s) => s.login);

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="w-full max-w-sm space-y-6 text-center">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            Clustral
          </h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Sign in with your Keycloak account to manage Kubernetes clusters.
          </p>
        </div>

        <button
          type="button"
          onClick={() => login()}
          className="inline-flex w-full items-center justify-center gap-2 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
        >
          <LogIn className="h-4 w-4" />
          Sign in with Keycloak
        </button>
      </div>
    </div>
  );
}
