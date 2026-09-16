import { Activity, Clock3, RefreshCw, TriangleAlert } from "lucide-react"
import { useFetchWithRefetch } from "../hooks/useFetch"
import { Button } from "./ui/Button"
import { Card, CardContent, CardHeader, CardTitle } from "./ui/Card"
import MetricsChart from "./MetricsChart"
import Sparkline from "./Sparkline"

const number = value => value == null ? "—" : value.toLocaleString()
const seconds = value => value == null ? "—" : `${(value / 1000).toFixed(3)} s`
const percent = (failed, attempts) => attempts ? `${(failed * 100 / attempts).toFixed(2)}%` : "0.00%"
const difference = (current, previous, suffix = "") => previous == null ? "No prior window" : `${current >= previous ? "▲" : "▼"} ${Math.abs(current - previous).toLocaleString()}${suffix} vs prior`
const endpointStatus = endpoint => endpoint.latestOutcome === "network_failure" || endpoint.latestOutcome === "gateway_failure" || endpoint.latestResponseStatus >= 500 ? "err" : endpoint.latestResponseStatus >= 400 || endpoint.latestOutcome === "gateway_rejected" ? "warn" : "ok"

function MetricCard({ Icon, label, value, comparison }) {
  return <Card className="metric-card"><CardContent className="p-5"><div className="metric-card__label"><Icon aria-hidden="true" />{label}</div><p className="metric-card__value">{value}</p><p className="text-xs text-muted-foreground">{comparison}</p></CardContent></Card>
}

export default function OverviewTab({ onOpenLogs }) {
  const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const overview = useFetchWithRefetch(`/api/management/operations/overview?timezone=${encodeURIComponent(timezone)}`, 10000)
  const data = overview.data
  const current = data?.requests?.currentMinute
  const previous = data?.requests?.previousMinute
  const today = data?.requests?.today
  const yesterday = data?.requests?.yesterday
  const refresh = () => overview.refetch()
  // The server resolves each local midnight independently, including across DST.
  const trafficWindow = data?.windows?.traffic
  const trafficDomain = trafficWindow
    ? { start: Date.parse(trafficWindow.from), end: Date.parse(trafficWindow.to) }
    : (() => { const s = new Date(); s.setHours(0, 0, 0, 0); const e = new Date(s); e.setDate(e.getDate() + 1); return { start: +s, end: +e } })()
  // Fixed 1h UTC domain for the per-group sparklines, from the API's advertised groupsHourly window; falls back to now-1h→now.
  const groupsWindow = data?.windows?.groupsHourly
  const groupsDomain = groupsWindow
    ? { start: Date.parse(groupsWindow.from), end: Date.parse(groupsWindow.to) }
    : { start: Date.now() - 3600000, end: Date.now() }

  return <div className="space-y-6">
    <div className="dashboard-toolbar"><div><p className="eyebrow">Observed retained proxy attempts</p><p className="text-xs text-muted-foreground">{timezone} · safe inbound paths; query strings excluded</p></div><Button variant="outline" onClick={refresh}><RefreshCw aria-hidden="true" /> Refresh</Button></div>
    {overview.error && <p role="alert" className="route-preview text-destructive">Could not refresh persisted operations.</p>}
    {overview.loading && <p role="status">Loading operations overview…</p>}

    <div className="metric-grid">
      <MetricCard Icon={Activity} label="Requests / min" value={number(current?.attempts)} comparison={current ? difference(current.attempts, previous?.attempts) : "Recent UTC minute"} />
      <MetricCard Icon={Activity} label="Total Today" value={number(today?.attempts)} comparison={today ? "Yesterday: " + number(yesterday?.attempts) : "Since local midnight"} />
      <MetricCard Icon={TriangleAlert} label="Error Rate" value={current ? percent(current.failedRequests, current.attempts) : "—"} comparison={current ? difference(current.failedRequests * 100 / Math.max(current.attempts, 1), previous ? previous.failedRequests * 100 / Math.max(previous.attempts, 1) : null, " pp") : "Recent UTC minute"} />
      <MetricCard Icon={Clock3} label="Avg Latency" value={seconds(current?.averageLatencyMs)} comparison="Completed attempts with duration" />
    </div>

    <div className="overview-pair">
      <MetricsChart title={`Traffic — Today (${timezone})`} samples={data?.traffic} series={[["attempts", "Requests", "#2563eb"]]} unit="req" domain={trafficDomain} showLegend={false} />
      <Card><CardHeader><CardTitle>Endpoint Groups</CardTitle></CardHeader><CardContent className="group-list">
        {data && !data.groups.length && <p className="text-sm text-muted-foreground">No observed group traffic in this window.</p>}
        {data?.groups?.map(group => <div key={group.groupId} className="group-row"><div><button className="dashboard-link" onClick={() => onOpenLogs({ groupId: group.groupId })}>{group.groupName}</button><code>{number(group.observedEndpoints)} observed endpoint{group.observedEndpoints === 1 ? "" : "s"}</code></div><Sparkline buckets={group.hourly} domain={groupsDomain} label={`${group.groupName} — last hour`} /><span>{number(group.requestsPerMinute)} req/min<br />{group.measuredSuccessPercent == null ? "—" : `${group.measuredSuccessPercent.toFixed(2)}% upstream <400`}</span></div>)}
      </CardContent></Card>
    </div>

    <Card><CardHeader><CardTitle>Recent Endpoints — Last 5 min</CardTitle></CardHeader><CardContent className="endpoint-table-wrap">
      {data && !data.recentEndpoints.length && <p className="text-sm text-muted-foreground">No observed endpoint traffic in this window.</p>}
      {!!data?.recentEndpoints?.length && <table className="endpoint-table"><thead><tr><th scope="col">Latest result</th><th scope="col">Method</th><th scope="col">Path</th><th scope="col">Avg latency</th><th scope="col">Reqs</th></tr></thead><tbody>{data.recentEndpoints.map(endpoint => <tr key={`${endpoint.endpointId}:${endpoint.method}:${endpoint.path}`}><td><span className={`endpoint-status ${endpointStatus(endpoint)}`} aria-hidden="true" /> <span>{endpoint.latestResponseStatus == null ? endpoint.latestOutcome : `HTTP ${endpoint.latestResponseStatus}`}</span></td><td><span className="method-badge">{endpoint.method}</span></td><td><button className="dashboard-link endpoint-path" onClick={() => onOpenLogs({ endpointId: endpoint.endpointId, search: endpoint.path })}>{endpoint.path}</button></td><td className="endpoint-latency">{seconds(endpoint.averageLatencyMs)}</td><td className="endpoint-requests">{number(endpoint.attempts)}</td></tr>)}</tbody></table>}
    </CardContent></Card>

  </div>
}
