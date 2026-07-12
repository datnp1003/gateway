import useFetch from "../hooks/useFetch"
import { Card, CardContent } from "./ui/Card"
import { Badge } from "./ui/Badge"
import { Skeleton } from "./ui/Skeleton"
import { ArrowRight, Route, Server } from "lucide-react"

function RoutesSkeleton() {
  return (
    <div className="space-y-2">
      {Array.from({ length: 4 }).map((_, i) => (
        <Card key={i}>
          <CardContent className="p-4">
            <div className="flex items-center gap-3 mb-2">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-5 w-20 rounded-full" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
            <Skeleton className="h-3 w-48" />
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
        <Route className="w-10 h-10 text-muted-foreground/30 mx-auto mb-3" />
        <p className="text-sm font-medium text-muted-foreground">No routes configured</p>
        <p className="text-xs text-muted-foreground mt-1">Each endpoint maps to its own cluster → destination. Add endpoints to see routes here.</p>
      </CardContent>
    </Card>
  )
}

export default function RoutesTab() {
  const { data: routes, loading } = useFetch("/api/management/routes", 5000)

  if (loading) return <RoutesSkeleton />

  return (
    <div className="space-y-3">
      {routes?.map((r, i) => (
        <Card key={i} className="hover:border-primary/20 transition-colors">
          <CardContent className="p-4">
            <div className="flex items-center gap-3 flex-wrap">
              <Badge variant="success" className="font-mono text-sm font-bold">{r.routeId}</Badge>
              <ArrowRight className="w-3.5 h-3.5 text-muted-foreground" />
              <Badge variant="secondary" className="font-mono gap-1">
                <Server className="w-3 h-3" />{r.clusterId}
              </Badge>
            </div>
            <div className="mt-2 font-mono text-xs text-muted-foreground">{r.matchPath}</div>
            {r.transforms?.length > 0 && (
              <div className="mt-2 flex gap-1.5 flex-wrap">
                {r.transforms.map((t, j) => (
                  <Badge key={j} variant="secondary" className="text-[10px] font-mono">{t}</Badge>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      ))}
      {(!routes || routes.length === 0) && <EmptyState />}
    </div>
  )
}
