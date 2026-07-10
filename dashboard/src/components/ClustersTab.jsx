import useFetch from "../hooks/useFetch"
import { Card, CardContent, CardHeader, CardTitle } from "./ui/Card"
import { Badge } from "./ui/Badge"
import { Skeleton } from "./ui/Skeleton"
import { Server, CircleCheck, ArrowRight } from "lucide-react"

function ClusterSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 2 }).map((_, i) => (
        <Card key={i}>
          <CardHeader><Skeleton className="h-4 w-32" /></CardHeader>
          <CardContent>
            <div className="grid grid-cols-2 gap-2">
              {[1, 2, 3, 4].map(j => (
                <Skeleton key={j} className="h-16 w-full rounded" />
              ))}
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  )
}

function EmptyState() {
  return (
    <Card>
      <CardContent className="p-8 text-center">
        <Server className="w-10 h-10 text-muted-foreground/30 mx-auto mb-3" />
        <p className="text-sm font-medium text-muted-foreground">No backend targets configured</p>
        <p className="text-xs text-muted-foreground mt-1">Each API route has its own backend target cluster that maps to one or more destination addresses.</p>
      </CardContent>
    </Card>
  )
}

export default function ClustersTab() {
  const { data: clusters, loading } = useFetch("/api/management/clusters", 5000)

  if (loading) return <ClusterSkeleton />

  return (
    <div className="space-y-4">
      {clusters?.map((c, i) => (
        <Card key={i}>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-mono font-bold text-primary flex items-center gap-2">
              <Server className="w-4 h-4" />
              {c.clusterId}
            </CardTitle>
            <p className="text-xs text-muted-foreground flex items-center gap-1 mt-0.5">
              <ArrowRight className="w-3 h-3" /> destination{(c.destinations?.length ?? 0) !== 1 ? "s" : ""}: {c.destinations?.length ?? 0}
            </p>
          </CardHeader>
          <CardContent>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
              {c.destinations?.map((d, j) => (
                <div key={j} className="flex items-center justify-between p-3 bg-muted/50 rounded-lg hover:bg-muted/80 transition-colors">
                  <div className="min-w-0">
                    <div className="text-sm font-mono font-medium truncate">{d.name}</div>
                    <div className="text-xs text-muted-foreground truncate">{d.address}</div>
                  </div>
                  <Badge variant="success" className="gap-1.5 ml-2 shrink-0">
                    <CircleCheck className="w-3 h-3" /> Healthy
                  </Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      ))}
      {(!clusters || clusters.length === 0) && <EmptyState />}
    </div>
  )
}
