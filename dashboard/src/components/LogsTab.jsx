import { useEffect, useMemo, useState } from "react"
import { useFetchWithRefetch } from "../hooks/useFetch"
import { Card, CardContent } from "./ui/Card"
import { Button } from "./ui/Button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/Table"

const periods = { "1h": 60 * 60 * 1000, "24h": 24 * 60 * 60 * 1000, "7d": 7 * 24 * 60 * 60 * 1000 }
const emptyFilters = { search: "", status: "", outcome: "", failureOnly: false, period: "24h", limit: "25", groupId: "", endpointId: "" }
const windowFor = period => { const end = new Date(); return { from: new Date(end - periods[period]).toISOString(), to: end.toISOString() } }

function EventPage({ query, onNext, onPrevious, canGoBack }) {
  const { data, error, loading, refetch } = useFetchWithRefetch(query)
  return <div className="space-y-4" aria-busy={loading}>
    {loading && <p role="status">Loading persisted request events…</p>}
    {error && <p role="alert" className="route-preview text-destructive">Could not load request events. <Button variant="outline" onClick={refetch}>Retry</Button></p>}
    {data && <>
      <div className="flex flex-wrap items-center justify-between gap-3"><p role="status" className="text-sm">{data.items.length} events in this cursor window</p><div className="flex gap-2"><Button variant="outline" disabled={!canGoBack} onClick={onPrevious}>Back</Button><Button variant="outline" disabled={!data.hasNext} onClick={() => onNext(data.nextCursor)}>Next</Button></div></div>
      <Card><CardContent className="p-0"><div className="overflow-auto"><Table><TableHeader><TableRow><TableHead>Date / time (local)</TableHead><TableHead>Inbound path</TableHead><TableHead>Endpoint / group</TableHead><TableHead>Outcome</TableHead><TableHead>Client IP</TableHead><TableHead>HTTP status</TableHead><TableHead>Duration</TableHead></TableRow></TableHeader><TableBody>{data.items.map(event => <TableRow key={event.id}><TableCell className="whitespace-nowrap text-xs">{new Date(event.occurredAt).toLocaleString()}</TableCell><TableCell><code className="text-xs">{event.method} {event.requestPath}</code></TableCell><TableCell>{event.endpoint.name}<span className="block text-xs text-muted-foreground">{event.group.name}</span></TableCell><TableCell>{event.outcome}</TableCell><TableCell className="whitespace-nowrap text-xs">{event.clientIp ?? "—"}</TableCell><TableCell>{event.responseStatus ?? "—"}</TableCell><TableCell className="whitespace-nowrap">{event.durationMs == null ? "—" : `${event.durationMs.toFixed(1)} ms`}</TableCell></TableRow>)}{!data.items.length && <TableRow><TableCell colSpan={7}>No persisted events in this window.</TableCell></TableRow>}</TableBody></Table></div></CardContent></Card>
      <span className="text-xs text-muted-foreground" title={data.consistency} aria-label={data.consistency}>Pagination caveat</span>
    </>}
  </div>
}

export default function LogsTab({ navigation }) {
  const [filters, setFilters] = useState(emptyFilters)
  const [applied, setApplied] = useState(emptyFilters)
  const [window, setWindow] = useState(() => windowFor(emptyFilters.period))
  const [cursors, setCursors] = useState([null])
  const [cursorIndex, setCursorIndex] = useState(0)
  const configuration = useFetchWithRefetch("/api/management/operations/configuration")
  const endpoints = configuration.data?.flatMap(group => group.endpoints.map(endpoint => ({ ...endpoint, group }))) ?? []
  const reset = next => { setApplied(next); setWindow(windowFor(next.period)); setCursors([null]); setCursorIndex(0) }

  useEffect(() => {
    if (!navigation) return
    const next = { ...emptyFilters, ...navigation }
    setFilters(next)
    reset(next)
  }, [navigation])

  const query = useMemo(() => {
    const params = new URLSearchParams({ ...window, limit: applied.limit, failureOnly: String(applied.failureOnly) })
    ;["search", "status", "outcome", "groupId", "endpointId"].forEach(key => { if (applied[key]) params.set(key, applied[key]) })
    if (cursors[cursorIndex]) params.set("cursor", cursors[cursorIndex])
    return `/api/management/operations/events?${params}`
  }, [applied, cursorIndex, cursors, window])
  const field = key => ({ value: filters[key], onChange: event => setFilters(current => ({ ...current, [key]: event.target.value })), className: "border rounded-md bg-card px-3 py-2 text-sm" })
  const refresh = () => reset(applied)
  const next = cursor => { if (cursor) { setCursors(current => [...current.slice(0, cursorIndex + 1), cursor]); setCursorIndex(index => index + 1) } }

  return <div className="space-y-4">
    <form className="flex flex-wrap items-end gap-3" onSubmit={event => { event.preventDefault(); reset(filters) }}>
      <label className="grid gap-1 text-xs">Search path / endpoint<input type="search" maxLength={200} placeholder="Inbound path or endpoint…" {...field("search")} /></label>
      <label className="grid gap-1 text-xs">Time window<select {...field("period")}>{Object.keys(periods).map(period => <option key={period}>{period}</option>)}</select></label>
      <label className="grid gap-1 text-xs">Group<select {...field("groupId")}><option value="">All groups</option>{configuration.data?.map(group => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label>
      <label className="grid gap-1 text-xs">Endpoint<select {...field("endpointId")}><option value="">All endpoints</option>{endpoints.map(endpoint => <option key={endpoint.id} value={endpoint.id}>{endpoint.group.name} / {endpoint.name}</option>)}</select></label>
      <label className="grid gap-1 text-xs">Outcome<select {...field("outcome")}><option value="">All outcomes</option>{["upstream_response", "gateway_rejected", "network_failure", "gateway_failure", "client_disconnected"].map(outcome => <option key={outcome}>{outcome}</option>)}</select></label>
      <label className="grid gap-1 text-xs">HTTP status<input type="number" min={100} max={599} placeholder="Any" {...field("status")} /></label>
      <label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={filters.failureOnly} onChange={event => setFilters(current => ({ ...current, failureOnly: event.target.checked }))} />Failures only</label>
      <label className="grid gap-1 text-xs">Events / page<select {...field("limit")}>{[25, 50, 100].map(count => <option key={count}>{count}</option>)}</select></label>
      <Button type="submit">Apply filters</Button><Button type="button" variant="outline" onClick={refresh}>Refresh</Button>
    </form>
    <p className="text-xs text-muted-foreground">Inbound paths exclude query strings; known sensitive path values are redacted.</p>
    <EventPage key={query} query={query} canGoBack={cursorIndex > 0} onPrevious={() => setCursorIndex(index => index - 1)} onNext={next} />
  </div>
}
