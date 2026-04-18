"use client"

import * as React from "react"
import { usePathname } from "next/navigation"
import Link from "next/link"

import { NavMain } from "@/components/nav-main"
import { NavUser } from "@/components/nav-user"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"
import Image from "next/image"
import {
  ServerIcon,
  UsersIcon,
  ShieldCheckIcon,
  KeyIcon,
  ScrollTextIcon,
} from "lucide-react"
import { useSession } from "next-auth/react"

const navItems = [
  {
    title: "Clusters",
    url: "/clusters",
    icon: <ServerIcon />,
  },
  {
    title: "Users",
    url: "/users",
    icon: <UsersIcon />,
  },
  {
    title: "Roles",
    url: "/roles",
    icon: <ShieldCheckIcon />,
  },
  {
    title: "Access Requests",
    url: "/access-requests",
    icon: <KeyIcon />,
  },
  {
    title: "Audit Log",
    url: "/audit",
    icon: <ScrollTextIcon />,
  },
]

export function AppSidebar({ ...props }: React.ComponentProps<typeof Sidebar>) {
  const { data: session } = useSession()
  const pathname = usePathname()

  const itemsWithActive = navItems.map((item) => ({
    ...item,
    isActive: pathname === item.url || pathname?.startsWith(item.url + "/"),
  }))

  const user = session?.user
    ? {
        name: session.user.name ?? "User",
        email: session.user.email ?? "",
        avatar: "",
      }
    : { name: "Not signed in", email: "", avatar: "" }

  return (
    <Sidebar collapsible="offcanvas" {...props}>
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton
              className="data-[slot=sidebar-menu-button]:p-1.5!"
              render={<Link href="/clusters" />}
            >
              <Image src="/logo.svg" alt="Clustral" width={20} height={20} className="size-5!" />
              <span className="text-base font-semibold">Clustral</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        <NavMain items={itemsWithActive} />
      </SidebarContent>
      <SidebarFooter>
        <NavUser user={user} />
      </SidebarFooter>
    </Sidebar>
  )
}
