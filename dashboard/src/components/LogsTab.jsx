import { useState } from "react"
import useFetch from "../hooks/useFetch"
import { Card, CardContent } from "./ui/Card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/Table"
import { Badge } from "./ui/Badge"
import { Button } from "./ui/Button"
import { Input } from "./ui/Input"
import { Skeleton } from "./ui/Skeleton"
import { Search, Pause, Play, FileSearch, Clock, AlertTriangle, Info, XCircle } from "lucide-react"

const levelConfig = {
  Error: { variant: "destructive", icon: XCircle },
  Warning: { variant: "warning", icon: AlertTriangle },
  Info: { variant: "secondary", icon: Info },
}

function LogSkeleton() {
  return (
    <div className="space-y-1">
      {Array.from({ length: 8 }).map((_, i) => (
        <div key={i} className="flex items-center gap-3 px-4 py-2.5">
          <Skeleton className="h-3 w-20" />
          <Skeleton className="h-5 w-14 rounded-full" />
          <Skeleton className="h-3 flex-1" />
          <Skeleton className="h-5 w-10 rounded-full" />
          <Skeleton className="h-3 w-16" />
        </div>
      ))}
    </div>
  )
}

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center py-12 text-muted-foreground">
      <FileSearch className="w-12 h-12 mb-3 opacity-30" />
      <p className="text-sm font-medium">No requests recorded yet</p>
      <p className="text-xs mt-1">Requests will appear here as they come in</p>
    </div>
  )
}

function NoResults({ searchTerm }) {
  return (
    <div className="flex flex-col items-center justify-center py-12 text-muted-foreground">
      <Search className="w-12 h-12 mb-3 opacity-30" />
      <p className="text-sm font-medium">No results for "{searchTerm}"</p>
      <p className="text-xs mt-1">Try a different search term or change the filter</p>
    </div>
  )
}

export default function LogsTab() {
  const [filter, setFilter] = useState("All")
  const [search, setSearch] = useState("")
  const [autoRefresh, setAutoRefresh] = useState(true)

  const { data: logs, loading } = useFetch(
    autoRefresh ? "/api/management/logs?count=200" : null,
    autoRefresh ? 3000 : 0
  )

  const rawLogs = logs || []
  const searchLower = search.toLowerCase()

  const filtered = rawLogs
    .filter(l => filter === "All" || l.level === filter)
    .filter(l => {
      if (!search) return true
      const msg = (l.message || "").toLowerCase()
      const path = (l.path || "").toLowerCase()
      const method = (l.method || "").toLowerCase()
      return msg.includes(searchLower) || path.includes(searchLower) || method.includes(searchLower)
    })

  if (loading && !logs) {
    return (
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <Skeleton className="h-9 w-full md:w-64" />
          <div className="flex gap-2">
            {[1, 2, 3, 4].map(i => <Skeleton key={i} className="h-8 w-20 rounded-full" />)}
          </div>
          <Skeleton className="h-8 w-24 rounded-full ml-auto" />
        </div>
        <Card>
          <CardContent className="p-0">
            <LogSkeleton />
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Controls */}
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative flex-1 min-w-[200px] max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground pointer-events-none" />
          <Input
            type="text"
            placeholder="Search by path or message…"
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="pl-9"
            aria-label="Search logs by path or message"
          />
        </div>

        {/* Level filter pills */}
        <div className="flex gap-1.5 flex-wrap">
          {["All", "Error", "Warning", "Info"].map(f => {
            const config = levelConfig[f]
            const Icon = config?.icon
            return (
              <button
                key={f}
                onClick={() => setFilter(f)}
                className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-xs rounded-full border transition-colors cursor-pointer ${
                  filter === f
                    ? f === "Error" ? "bg-red-500/15 border-red-500/30 text-red-400"
                    : f === "Warning" ? "bg-amber-500/15 border-amber-500/30 text-amber-400"
                    : f === "Info" ? "bg-gray-500/15 border-gray-500/30 text-gray-400"
                    : "bg-primary/15 border-primary/30 text-primary"
                    : "border-border text-muted-foreground hover:text-foreground hover:bg-muted"
                }`}
              >
                {Icon && <Icon className="w-3 h-3" />}
                {f}
              </button>
            )
          })}
        </div>

        <div className="flex items-center gap-2 ml-auto">
          <Badge variant="outline" className="text-xs font-mono">
            {filtered.length} entries
          </Badge>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setAutoRefresh(r => !r)}
            aria-label={autoRefresh ? "Pause auto-refresh" : "Resume auto-refresh"}
            title={autoRefresh ? "Pause auto-refresh" : "Resume auto-refresh"}
          >
            {autoRefresh ? (
              <><Pause className="w-3.5 h-3.5" /><span className="hidden sm:inline">Pause</span></>
            ) : (
              <><Play className="w-3.5 h-3.5" /><span className="hidden sm:inline">Resume</span></>
            )}
          </Button>
        </div>
      </div>

      {/* Log table */}
      <Card className="overflow-hidden">
        <CardContent className="p-0">
          <div className="max-h-[calc(100vh-240px)] overflow-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[100px] text-xs">
                    <Clock className="w-3 h-3 inline mr-1" />Time
                  </TableHead>
                  <TableHead className="w-[70px] text-xs">Method</TableHead>
                  <TableHead className="w-[80px] text-xs">Level</TableHead>
                  <TableHead className="text-xs">Path</TableHead>
                  <TableHead className="w-[70px] text-xs text-right">Status</TableHead>
                  <TableHead className="w-[80px] text-xs text-right">Duration</TableHead>
                  <TableHead className="w-[130px] text-xs">Client IP</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filtered.length === 0 && search && <tr><td colSpan={7}><NoResults searchTerm={search} /></td></tr>}
                {filtered.length === 0 && !search && (
                  <tr><td colSpan={7}><EmptyState /></td></tr>
                )}
                {filtered.slice().reverse().map((log, i) => {
                  const config = levelConfig[log.level] || levelConfig.Info
                  const Icon = config.icon
                  return (
                    <TableRow
                      key={i}
                      className={`${
                        log.level === "Error" ? "bg-red-500/5 hover:bg-red-500/10"
                        : log.level === "Warning" ? "bg-amber-500/5 hover:bg-amber-500/10"
                        : "hover:bg-muted/50"
                      }`}
                    >
                      <TableCell className="py-2 text-xs font-mono text-muted-foreground">
                        {new Date(log.timestamp).toLocaleTimeString()}
                      </TableCell>
                      <TableCell className="py-2">
                        <Badge variant="outline" className="text-[10px] font-mono px-1.5">
                          {log.method || "—"}
                        </Badge>
                      </TableCell>
                      <TableCell className="py-2">
                        <Badge variant={config.variant} className="text-[10px] gap-1">
                          <Icon className="w-3 h-3" />
                          {log.level}
                        </Badge>
                      </TableCell>
                      <TableCell className="py-2 text-xs font-mono text-foreground/80 max-w-[300px] truncate">
                        {log.message || log.path || "—"}
                      </TableCell>
                      <TableCell className="py-2 text-right">
                        {log.statusCode ? (
                          <Badge
                            variant={log.statusCode >= 500 ? "destructive" : log.statusCode >= 400 ? "warning" : "success"}
                            className="text-[10px] font-mono"
                          >
                            {log.statusCode}
                          </Badge>
                        ) : (
                          <span className="text-xs text-muted-foreground">—</span>
                        )}
                      </TableCell>
                      <TableCell className="py-2 text-right text-xs text-muted-foreground font-mono">
                        {log.durationMs != null ? `${log.durationMs.toFixed(0)}ms` : "—"}
                      </TableCell>
                      <TableCell className="py-2 text-xs font-mono text-muted-foreground max-w-[130px] truncate">
                        {log.clientIp || "—"}
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
