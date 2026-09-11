import { ArrowDown, ExternalLink, RefreshCw, Server } from "lucide-react"
import { useFetchWithRefetch } from "../hooks/useFetch"
import { Button } from "./ui/Button"
import { Card, CardContent } from "./ui/Card"

function forwardedPath(route) {
  const prefix = route.removePrefix
  const path = route.path || "/"
  const matches = prefix && (path.toLowerCase() === prefix.toLowerCase() || path.toLowerCase().startsWith(`${prefix.toLowerCase()}/`))
  return matches ? path.slice(prefix.length) || "/" : path
}

function Mapping({ route, destination }) {
  const publicUrl = `${window.location.origin}${route.path}`
  const upstreamUrl = `${destination.replace(/\/+$/, "")}${forwardedPath(route)}`
  return <div className="mapping-flow"><div><span>Public request</span><code>{publicUrl}</code></div><ArrowDown aria-hidden="true" /><div><span>Forwarded to</span><code>{upstreamUrl}</code></div></div>
}

export default function RoutesTab({ backend = false }) {
  const { data, error, loading, refetch } = useFetchWithRefetch("/api/management/route-mappings", 10000)
  const routes = data ?? []
  const targets = [...new Set(routes.flatMap(route => route.destinations))]
  const sections = backend ? targets.map(address => ({ address, routes: routes.filter(route => route.destinations.includes(address)) })) : null
  const heading = backend ? "Backend targets" : "Active routes"
  const subtitle = backend ? "Each target lists the public routes currently forwarding to it." : "Routes loaded by the proxy. Disabled groups and endpoints are not included."
  return <div className="space-y-4">
    <div className="dashboard-toolbar"><div><p className="eyebrow">Current proxy configuration</p><p className="text-sm text-muted-foreground">{subtitle}</p></div><Button variant="outline" onClick={refetch}><RefreshCw aria-hidden="true" /> Refresh</Button></div>

    {error && <p role="alert" className="route-preview text-destructive">Could not refresh route mappings. Displayed configuration may be outdated.</p>}
    {loading && <p role="status">Loading {heading.toLowerCase()}…</p>}
    {!loading && !routes.length && <div className="empty-state"><ExternalLink aria-hidden="true" /><strong>No active routes</strong><span>Add an endpoint and enable its group and endpoint to make a public route available.</span></div>}
    {!backend && routes.map(route => <Card key={route.routeId} className="mapping-card"><CardContent className="p-5"><div className="mapping-card__header"><div><p className="eyebrow">{route.group || "Ungrouped service"}</p><h2>{route.name}</h2></div><span className="route-chip">{route.destinations.length} target{route.destinations.length === 1 ? "" : "s"}</span></div>{route.destinations.map(destination => <Mapping key={destination} route={route} destination={destination} />)}{!route.destinations.length && <p className="text-sm text-destructive">No destination is loaded for this route.</p>}</CardContent></Card>)}
    {backend && sections?.map(({ address, routes: targetRoutes }) => <Card key={address} className="target-card"><CardContent className="p-5"><div className="target-card__header"><Server aria-hidden="true" /><div><p className="eyebrow">Backend target</p><code>{address}</code></div><span className="route-chip">{targetRoutes.length} route{targetRoutes.length === 1 ? "" : "s"}</span></div><div className="target-routes">{targetRoutes.map(route => <div key={route.routeId}><strong>{route.group ? `${route.group} · ` : ""}{route.name}</strong><code>{window.location.origin}{route.path}</code></div>)}</div></CardContent></Card>)}
  </div>
}
