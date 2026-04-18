"use client";

import { Search, Plus } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";

interface ClusterToolbarProps {
  searchQuery: string;
  onSearchChange: (value: string) => void;
  statusFilter: string;
  onStatusFilterChange: (value: string) => void;
  sortBy: string;
  onSortByChange: (value: string) => void;
  onRegister: () => void;
}

export function ClusterToolbar({
  searchQuery,
  onSearchChange,
  statusFilter,
  onStatusFilterChange,
  sortBy,
  onSortByChange,
  onRegister,
}: ClusterToolbarProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative">
        <Search className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          placeholder="Search clusters..."
          value={searchQuery}
          onChange={(e) => onSearchChange(e.target.value)}
          className="pl-8 w-[220px]"
        />
      </div>

      <Select value={statusFilter} onValueChange={(val) => { if (val) onStatusFilterChange(val); }}>
        <SelectTrigger className="w-[150px]">
          <SelectValue placeholder="All statuses" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All</SelectItem>
          <SelectItem value="Connected">Connected</SelectItem>
          <SelectItem value="Pending">Pending</SelectItem>
          <SelectItem value="Disconnected">Disconnected</SelectItem>
        </SelectContent>
      </Select>

      <ToggleGroup
        value={[sortBy]}
        onValueChange={(value) => {
          if (value.length > 0) onSortByChange(value[value.length - 1]);
        }}
        variant="outline"
        size="sm"
      >
        <ToggleGroupItem value="name">Name</ToggleGroupItem>
        <ToggleGroupItem value="recent">Recent</ToggleGroupItem>
      </ToggleGroup>

      <Button size="sm" onClick={onRegister} className="ml-auto">
        <Plus className="h-3.5 w-3.5" />
        Register Cluster
      </Button>
    </div>
  );
}
