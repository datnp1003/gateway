import useFetch from "../hooks/useFetch"
import { Card, CardContent, CardHeader, CardTitle } from "./ui/Card"
import { Skeleton } from "./ui/Skeleton"
import { Badge } from "./ui/Badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/Table"
import { Activity, ArrowUpRight, AlertTriangle, Clock, Wifi, WifiOff, Globe, Server } from "lucide-react"

function StatCard({ label, value, subtitle, icon: Icon, color = "emerald", loading }) {
  const gradients = {
    emerald: "from-emerald-500/20 to-emerald-500/5 border-emerald-500/20",
    blue: "from-blue-500/20 to-blue-500/5 border-blue-500/20",
    amber: "from-amber-500/20 to-amber-500/5 border-amber-500/20",
    red: "from-red-500/20 to-red-500/5 border-red-500/20",
    purple: "from-purple-500/20 to-purple-500/5 border-purple-500/20",
  }
  const iconColors = {
    emerald: "text-emerald-400",
    blue: "text-blue-400",
    amber: "text-amber-400",
    red: "text-red-400",
    purple: "text-purple-400",
  }

  if (loading) {
    return (
      <Card className="bg-card/50">
        <CardContent className="p-5 space-y-3">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-8 w-16" />
          <Skeleton className="h-3 w-32" />
        </CardContent>
      </Card>
    )
  }

  return (
    <Card className={`bg-gradient-to-br ${gradients[color]} border relative overflow-hidden group transition-all duration-300 hover:scale-[1.02]`}>
      <div className="absolute top-0 right-0 w-24 h-24 opacity-5 group-hover:opacity-10 transition-opacity">
        <Icon className={`w-full h-full ${iconColors[color]}`} />
      </div>
      <CardContent className="p-5 relative">
        <div className="flex items-center justify-between mb-2">
          <span className="text-xs font-medium text-muted-foreground uppercase tracking-wider">{label}</span>
          <Icon className={`w-4 h-4 ${iconColors[color]}`} />
        </div>
        <div className="text-3xl font-bold tracking-tight">{value}</div>
        {subtitle && <div className="text-xs text-muted-foreground mt-1">{subtitle}</div>}
      </CardContent>
    </Card>
  )
}

function StatusCodeBar({ statusCodes }) {
  if (!statusCodes || Object.keys(statusCodes).length === 0) return null
  const total = Object.values(statusCodes).reduce((a, b) => a + b, 0)
  const codeStyle = {
    "2xx": "bg-emerald-500",
    "3xx": "bg-blue-500",
    "4xx": "bg-amber-500",
    "5xx": "bg-red-500",
  }

  function classify(code) {
    const first = String(code)[0]
    return first === "2" ? "2xx" : first === "3" ? "3xx" : first === "4" ? "4xx" : "5xx"
  }

  const grouped = {}
  Object.entries(statusCodes).forEach(([code, count]) => {
    const grp = classify(code)
    grouped[grp] = (grouped[grp] || 0) + count
  })

  return (
    <div className="space-y-3">
      <div className="flex h-3 rounded-full overflow-hidden bg-muted">
        {Object.entries(grouped).map(([grp, count]) => (
          <div
            key={grp}
            className={`${codeStyle[grp] || "bg-gray-500"} transition-all duration-500`}
            style={{ width: `${(count / total) * 100}%` }}
            title={`${grp}: ${count}`}
          />
        ))}
      </div>
      <div className="flex flex-wrap gap-3">
        {Object.entries(statusCodes).map(([code, count]) => (
          <Badge key={code} variant="outline" className="text-xs gap-1.5">
            <span className={`inline-block w-2 h-2 rounded-full ${codeStyle[classify(code)]}`} />
            {code}: {count}
          </Badge>
        ))}
      </div>
    </div>
  )
}

function ActiveConnectionsGauge({ active, total = 100 }) {
  const pct = Math.min(100, Math.round((active / total) * 100))
  const color = pct > 80 ? "stroke-red-400" : pct > 50 ? "stroke-amber-400" : "stroke-emerald-400"
  const radius = 40
  const circumference = 2 * Math.PI * radius
  const offset = circumference - (pct / 100) * circumference

  return (
    <div className="flex flex-col items-center">
      <svg width="100" height="100" viewBox="0 0 100 100" className="transform -rotate-90">
        <circle cx="50" cy="50" r={radius} fill="none" stroke="hsl(var(--muted))" strokeWidth="8" />
        <circle
          cx="50" cy="50" r={radius} fill="none" strokeWidth="8"
          className={`transition-all duration-700 ease-out ${color}`}
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
        />
        <text x="50" y="50" textAnchor="middle" dy="0.35em" className="fill-foreground text-lg font-bold" transform="rotate(90 50 50)">
          {active}
        </text>
      </svg>
      <span className="text-xs text-muted-foreground mt-1">Active connections</span>
    </div>
  )
}

export default function OverviewTab() {
  const { data: db, loading } = useFetch("/api/management/dashboard", 3000)

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map(i => <StatCard key={i} loading />)}
        </div>
        <Card>
          <CardHeader><Skeleton className="h-4 w-48" /></CardHeader>
          <CardContent><Skeleton className="h-24 w-full" /></CardContent>
        </Card>
      </div>
    )
  }

  if (!db) {
    return (
      <Card className="border-destructive/20 bg-destructive/5">
        <CardContent className="p-6 text-center">
          <AlertTriangle className="w-8 h-8 text-destructive mx-auto mb-2" />
          <p className="text-destructive font-medium">Failed to load dashboard data</p>
          <p className="text-xs text-muted-foreground mt-1">Check if the Gateway service is running</p>
        </CardContent>
      </Card>
    )
  }

  const errCount = db.recentLogs?.filter(l => l.level === "Error").length || 0
  const totalRequests = db.totalRequests || 0
  const routesCount = db.routes?.length || 0
  const clustersCount = db.clusters?.length || 0

  // Calculate requests per second (rough estimate based on recent logs)
  const recentCount = db.recentLogs?.length || 0
  const rps = recentCount > 0 ? (recentCount / 60).toFixed(1) : "0"

  // Error rate
  const errorRate = totalRequests > 0 ? ((errCount / totalRequests) * 100).toFixed(1) : "0"

  // Average latency
  const durations = db.recentLogs?.filter(l => l.durationMs != null).map(l => l.durationMs) || []
  const avgLatency = durations.length > 0 ? (durations.reduce((a, b) => a + b, 0) / durations.length).toFixed(0) : "—"

  // Top routes
  const routeHits = {}
  db.recentLogs?.forEach(l => {
    if (l.path) {
      const key = l.method ? `${l.method} ${l.path}` : l.path
      routeHits[key] = (routeHits[key] || 0) + 1
    }
  })
  const topRoutes = Object.entries(routeHits)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 5)

  // Status codes for chart
  const statusCodes = db.recentLogs?.reduce((acc, l) => {
    if (l.statusCode) { acc[l.statusCode] = (acc[l.statusCode] || 0) + 1 }
    return acc
  }, {})

  const activeConnections = db.activeConnections ?? (db.recentLogs?.length ? Math.min(db.recentLogs.length, 100) : 0)

  return (
    <div className="space-y-6">
      {/* Stat Cards */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          label="Total Requests"
          value={totalRequests.toLocaleString()}
          subtitle="all time"
          icon={Activity}
          color="emerald"
        />
        <StatCard
          label="Requests/sec"
          value={rps}
          subtitle="estimated"
          icon={ArrowUpRight}
          color="blue"
        />
        <StatCard
          label="Error Rate"
          value={`${errorRate}%`}
          subtitle={`${errCount} errors`}
          icon={AlertTriangle}
          color={errCount > 0 ? "red" : "emerald"}
        />
        <StatCard
          label="Avg Latency"
          value={avgLatency !== "—" ? `${avgLatency}ms` : "—"}
          subtitle="response time"
          icon={Clock}
          color={avgLatency > 500 ? "amber" : "blue"}
        />
      </div>

      {/* Status Code Distribution + Active Connections */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Card className="md:col-span-2">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium">Status Code Distribution</CardTitle>
          </CardHeader>
          <CardContent>
            <StatusCodeBar statusCodes={statusCodes} />
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium">Connections</CardTitle>
          </CardHeader>
          <CardContent className="flex justify-center">
            <ActiveConnectionsGauge active={activeConnections} total={Math.max(100, activeConnections * 1.5)} />
          </CardContent>
        </Card>
      </div>

      {/* Top Routes + Summary */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* Top 5 Routes */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium flex items-center gap-2">
              <Globe className="w-4 h-4 text-emerald-400" />
              Top Routes
            </CardTitle>
          </CardHeader>
          <CardContent>
            {topRoutes.length === 0 ? (
              <p className="text-xs text-muted-foreground py-4 text-center">No requests recorded yet</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="text-xs">Route</TableHead>
                    <TableHead className="text-xs text-right">Hits</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {topRoutes.map(([route, count]) => (
                    <TableRow key={route}>
                      <TableCell className="font-mono text-xs py-2">{route}</TableCell>
                      <TableCell className="text-right text-xs py-2">
                        <Badge variant="secondary" className="font-mono">{count}</Badge>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        {/* Quick Summary */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium flex items-center gap-2">
              <Server className="w-4 h-4 text-blue-400" />
              Configuration
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="flex items-center justify-between p-3 rounded-lg bg-muted/50">
              <span className="text-sm text-muted-foreground">Routes</span>
              <Badge variant="secondary" className="font-mono">{routesCount}</Badge>
            </div>
            <div className="flex items-center justify-between p-3 rounded-lg bg-muted/50">
              <span className="text-sm text-muted-foreground">Clusters</span>
              <Badge variant="secondary" className="font-mono">{clustersCount}</Badge>
            </div>
            <div className="flex items-center justify-between p-3 rounded-lg bg-muted/50">
              <span className="text-sm text-muted-foreground">Gateway Status</span>
              <Badge variant="success" className="gap-1.5">
                <Wifi className="w-3 h-3" /> Online
              </Badge>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Recent Activity */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-medium">Recent Activity</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="space-y-1 max-h-64 overflow-auto">
            {db.recentLogs?.slice(-20).reverse().map((log, i) => (
              <div key={i} className="flex items-center gap-3 text-xs font-mono py-1.5 px-2 rounded hover:bg-muted/50 transition-colors">
                <Badge
                  variant={
                    log.level === "Error" ? "destructive" :
                    log.level === "Warning" ? "warning" : "secondary"
                  }
                  className="w-16 justify-center shrink-0 text-[10px]"
                >
                  {log.level}
                </Badge>
                <span className="text-foreground/80 truncate flex-1">{log.message}</span>
                {log.statusCode && (
                  <Badge
                    variant={log.statusCode >= 500 ? "destructive" : log.statusCode >= 400 ? "warning" : "success"}
                    className="w-10 justify-center shrink-0 font-mono text-[11px]"
                  >
                    {log.statusCode}
                  </Badge>
                )}
                {log.durationMs != null && (
                  <span className="w-16 text-right text-muted-foreground shrink-0">{log.durationMs.toFixed(0)}ms</span>
                )}
              </div>
            ))}
            {(!db.recentLogs || db.recentLogs.length === 0) && (
              <div className="text-muted-foreground text-xs text-center py-4">
                <WifiOff className="w-6 h-6 mx-auto mb-1 opacity-40" />
                No requests recorded yet
              </div>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
