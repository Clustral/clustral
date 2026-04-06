"use client";

import { useSession, signOut } from "next-auth/react";
import { usePathname, useRouter } from "next/navigation";
import { Server, LogOut } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

const navItems = [
  { href: "/clusters", label: "Clusters" },
  { href: "/users", label: "Users" },
  { href: "/roles", label: "Roles" },
  { href: "/access-requests", label: "Access Requests" },
];

export function NavHeader() {
  const { data: session } = useSession();
  const pathname = usePathname();
  const router = useRouter();

  return (
    <header className="border-b">
      <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
        <div className="flex items-center gap-6">
          <div className="flex items-center gap-2">
            <Server className="h-5 w-5" />
            <span className="text-lg font-semibold">Clustral</span>
          </div>
          <nav className="flex items-center gap-1">
            {navItems.map((item) => (
              <Button
                key={item.href}
                variant="ghost"
                size="sm"
                onClick={() => router.push(item.href)}
                className={cn(
                  pathname === item.href && "bg-accent font-medium text-accent-foreground",
                )}
              >
                {item.label}
              </Button>
            ))}
          </nav>
        </div>

        <div className="flex items-center gap-4">
          {session?.user?.email && (
            <span className="text-sm text-muted-foreground">
              {session.user.email}
            </span>
          )}
          <Button variant="outline" size="sm" onClick={() => signOut({ callbackUrl: "/login" })}>
            <LogOut className="h-3.5 w-3.5" />
            Sign out
          </Button>
        </div>
      </div>
    </header>
  );
}
