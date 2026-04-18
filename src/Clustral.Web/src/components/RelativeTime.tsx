"use client";

import { useMemo } from "react";
import {
  Tooltip,
  TooltipTrigger,
  TooltipContent,
} from "@/components/ui/tooltip";

interface RelativeTimeProps {
  date: string;
}

function formatRelative(date: string): string {
  const now = Date.now();
  const then = new Date(date).getTime();
  const diffMs = now - then;

  if (diffMs < 0) return "just now";

  const seconds = Math.floor(diffMs / 1000);
  if (seconds < 60) return "just now";

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;

  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export function RelativeTime({ date }: RelativeTimeProps) {
  const relative = useMemo(() => formatRelative(date), [date]);
  const full = useMemo(() => new Date(date).toISOString(), [date]);

  return (
    <Tooltip>
      <TooltipTrigger className="cursor-default text-muted-foreground">
        {relative}
      </TooltipTrigger>
      <TooltipContent>{full}</TooltipContent>
    </Tooltip>
  );
}
