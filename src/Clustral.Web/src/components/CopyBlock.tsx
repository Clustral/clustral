"use client";

import { useState, useCallback } from "react";
import { Copy, Check } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";

interface CopyBlockProps {
  value: string;
  label?: string;
}

export function CopyBlock({ value, label }: CopyBlockProps) {
  const [copied, setCopied] = useState(false);

  const copy = useCallback(() => {
    navigator.clipboard.writeText(value).then(() => {
      setCopied(true);
      toast.success("Copied to clipboard");
      setTimeout(() => setCopied(false), 2000);
    });
  }, [value]);

  return (
    <div>
      {label && (
        <p className="mb-1 text-sm font-medium">{label}</p>
      )}
      <div className="flex items-start gap-2 rounded-md bg-muted px-3 py-2 font-mono text-sm">
        <pre className="flex-1 overflow-x-auto whitespace-pre-wrap break-all text-muted-foreground">
          {value}
        </pre>
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
    </div>
  );
}
