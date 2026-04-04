import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuthStore } from "@/stores/useAuthStore";

export function CallbackPage() {
  const handleCallback = useAuthStore((s) => s.handleCallback);
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    handleCallback()
      .then(() => navigate("/", { replace: true }))
      .catch((err: Error) => setError(err.message));
  }, [handleCallback, navigate]);

  if (error) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background px-4">
        <div className="w-full max-w-sm space-y-4 text-center">
          <h2 className="text-lg font-semibold text-destructive">Authentication failed</h2>
          <p className="text-sm text-muted-foreground">{error}</p>
          <a
            href="/login"
            className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
          >
            Try again
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background">
      <p className="text-sm text-muted-foreground">Completing sign-in...</p>
    </div>
  );
}
