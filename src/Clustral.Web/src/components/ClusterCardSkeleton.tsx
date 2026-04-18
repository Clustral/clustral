import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

export function ClusterCardSkeleton() {
  return (
    <Card className="w-full p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-2">
          <Skeleton className="h-5 w-[180px]" />
          <Skeleton className="h-4 w-[100px]" />
        </div>
      </div>
      <div className="mt-3 pl-6">
        <Skeleton className="h-3 w-[140px]" />
      </div>
    </Card>
  );
}
