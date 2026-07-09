import { useState, useEffect, useCallback } from "react"
import { useToast } from "../hooks/useToast"
import { Button } from "./ui/Button"
import { Badge } from "./ui/Badge"
import { Card, CardContent } from "./ui/Card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/Table"
import { Skeleton } from "./ui/Skeleton"
import { Select as SelectInput } from "./ui/Select"
import { Plus, Pencil, Trash2, Lock, Unlock, AlertTriangle } from "lucide-react"

// ── useFetch with manual refetch ──────────────────────────────────────────────
function useFetchWithRefetch(url, interval) {
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)

  const refetch = useCallback(() => setTick(t => t + 1), [])

  useEffect(() => {
    if (!url) { setLoading(false); return }
    let cancelled = false
    const run = async () => {
      try {
        const res = await fetch(url)
        if (!res.ok) throw new Error(res.statusText)
        const json = await res.json()
        if (!cancelled) setData(json)
      } catch {
        // ignore fetch errors
      }
      finally { if (!cancelled) setLoading(false) }
    }
    run()
    const timer = interval > 0 ? setInterval(run, interval) : null
    return () => { cancelled = true; if (timer) clearInterval(timer) }
  }, [url, interval, tick])

  return { data, loading, refetch }
}

// ── Endpoint Modal ────────────────────────────────────────────────────────────
function EndpointModal({ initial, groups, onClose, onSaved }) {
  const editing = !!initial
  const { toast } = useToast()
  const [form, setForm] = useState({
    groupId:       initial?.groupId       ?? (groups[0]?.id ?? ""),
    name:          initial?.name          ?? "",
    pathPattern:   initial?.pathPattern   ?? "",
    destination:   initial?.destination   ?? "",
    removePrefix:  initial?.removePrefix  ?? "",
    requiresAuth:  initial?.requiresAuth  ?? false,
  })
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState("")

  const set = (k, v) => setForm(f => ({ ...f, [k]: v }))

  const submit = async e => {
    e.preventDefault()
    if (!form.name.trim())        { setErr("Name is required"); return }
    if (!form.pathPattern.trim()) { setErr("Path Pattern is required"); return }
    if (!form.destination.trim()) { setErr("Destination is required"); return }
    if (!form.groupId)            { setErr("Group is required"); return }
    setSaving(true); setErr("")
    try {
      const method = editing ? "PUT" : "POST"
      const url = editing ? `/api/management/endpoints/${initial.id}` : "/api/management/endpoints"
      const res = await fetch(url, {
        method,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          groupId:      form.groupId,
          name:         form.name.trim(),
          pathPattern:  form.pathPattern.trim(),
          destination:  form.destination.trim(),
          removePrefix: form.removePrefix.trim() || null,
          requiresAuth: form.requiresAuth,
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      const endpointName = form.name.trim()
      toast({
        title: editing ? `Endpoint "${endpointName}" updated` : `Endpoint "${endpointName}" created`,
        variant: "success",
      })
      onSaved()
    } catch (ex) {
      const msg = ex.message || "Request failed"
      setErr(msg)
      toast({
        title: editing ? "Failed to update endpoint" : "Failed to create endpoint",
        description: msg,
        variant: "destructive",
      })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border rounded-xl shadow-2xl w-full max-w-lg mx-4 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b">
          <h2 className="text-sm font-semibold">
            {editing ? "Edit Endpoint" : "New Endpoint"}
          </h2>
          <button
            onClick={onClose}
            className="text-muted-foreground hover:text-foreground transition-colors text-lg leading-none cursor-pointer"
            aria-label="Close dialog"
          >✕</button>
        </div>

        {/* Form */}
        <form onSubmit={submit} className="px-6 py-5 space-y-4 max-h-[80vh] overflow-y-auto">
          {/* Group */}
          <div>
            <label htmlFor="endpoint-group" className="block text-xs font-medium text-muted-foreground mb-1">
              Group <span className="text-destructive">*</span>
            </label>
            <SelectInput
              id="endpoint-group"
              name="groupId"
              value={form.groupId}
              onChange={e => set("groupId", e.target.value)}
              aria-label="Endpoint group"
            >
              {groups.map(g => (
                <option key={g.id} value={g.id}>{g.name}</option>
              ))}
            </SelectInput>
          </div>

          {/* Name */}
          <div>
            <label htmlFor="endpoint-name" className="block text-xs font-medium text-muted-foreground mb-1">
              Name <span className="text-destructive">*</span>
            </label>
            <input
              id="endpoint-name"
              name="name"
              type="text"
              value={form.name}
              onChange={e => set("name", e.target.value)}
              placeholder="my-endpoint"
              aria-label="Endpoint name"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors"
            />
          </div>

          {/* Path Pattern */}
          <div>
            <label htmlFor="endpoint-path-pattern" className="block text-xs font-medium text-muted-foreground mb-1">
              Path Pattern <span className="text-destructive">*</span>
            </label>
            <input
              id="endpoint-path-pattern"
              name="pathPattern"
              type="text"
              value={form.pathPattern}
              onChange={e => set("pathPattern", e.target.value)}
              placeholder="/api/v1/{**}"
              aria-label="Path Pattern"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm font-mono placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors"
            />
          </div>

          {/* Destination */}
          <div>
            <label htmlFor="endpoint-destination" className="block text-xs font-medium text-muted-foreground mb-1">
              Destination <span className="text-destructive">*</span>
            </label>
            <input
              id="endpoint-destination"
              name="destination"
              type="text"
              value={form.destination}
              onChange={e => set("destination", e.target.value)}
              placeholder="http://backend:8080"
              aria-label="Destination"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm font-mono placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors"
            />
          </div>

          {/* Remove Prefix */}
          <div>
            <label htmlFor="endpoint-remove-prefix" className="block text-xs font-medium text-muted-foreground mb-1">
              Remove Prefix <span className="text-muted-foreground">(optional)</span>
            </label>
            <input
              id="endpoint-remove-prefix"
              name="removePrefix"
              type="text"
              value={form.removePrefix}
              onChange={e => set("removePrefix", e.target.value)}
              placeholder="/api/v1"
              aria-label="Remove Prefix"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm font-mono placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors"
            />
          </div>

          {/* Requires Auth */}
          <label className="flex items-center gap-3 cursor-pointer select-none">
            <span className="relative">
              <input
                id="endpoint-requires-auth"
                name="requiresAuth"
                type="checkbox"
                className="sr-only peer"
                checked={form.requiresAuth}
                onChange={e => set("requiresAuth", e.target.checked)}
                aria-label="Requires Authentication"
              />
              <div className={`h-5 w-9 rounded-full transition-colors ${form.requiresAuth ? "bg-primary/30" : "bg-muted"}`} />
              <div className={`absolute top-[3px] h-3.5 w-3.5 rounded-full shadow transition-all ${
                form.requiresAuth ? "left-[18px] bg-primary" : "left-[3px] bg-muted-foreground"
              }`} />
            </span>
            <span className="text-sm">Requires Authentication</span>
            <span className="text-sm">{form.requiresAuth ? <Lock className="w-4 h-4 text-amber-400" /> : <Unlock className="w-4 h-4 text-muted-foreground" />}</span>
          </label>

          {err && (
            <p className="text-xs text-destructive bg-destructive/10 border border-destructive/20 rounded-lg px-3 py-2 flex items-center gap-2">
              <AlertTriangle className="w-3.5 h-3.5 shrink-0" /> {err}
            </p>
          )}

          <div className="flex gap-3 pt-1">
            <Button type="button" variant="outline" onClick={onClose} className="flex-1">
              Cancel
            </Button>
            <Button type="submit" disabled={saving} className="flex-1">
              {saving ? "Saving…" : editing ? "Update" : "Create"}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Delete confirm ────────────────────────────────────────────────────────────
function DeleteDialog({ endpoint, onClose, onDeleted }) {
  const [deleting, setDeleting] = useState(false)
  const { toast } = useToast()

  const confirm = async () => {
    setDeleting(true)
    try {
      const res = await fetch(`/api/management/endpoints/${endpoint.id}`, { method: "DELETE" })
      if (!res.ok) throw new Error(await res.text())
      toast({
        title: `Endpoint "${endpoint.name}" deleted`,
        variant: "success",
      })
      onDeleted()
    } catch (ex) {
      toast({
        title: "Failed to delete endpoint",
        description: ex.message || "Request failed",
        variant: "destructive",
      })
      setDeleting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border rounded-xl shadow-2xl w-full max-w-sm mx-4 p-6 space-y-4">
        <h2 className="text-sm font-semibold">Delete Endpoint</h2>
        <p className="text-sm text-muted-foreground">
          Are you sure you want to delete <span className="text-foreground font-mono">{endpoint.name}</span>? This action cannot be undone.
        </p>
        <div className="flex gap-3">
          <Button variant="outline" onClick={onClose} className="flex-1">
            Cancel
          </Button>
          <Button variant="destructive" onClick={confirm} disabled={deleting} className="flex-1">
            {deleting ? "Deleting…" : "Delete"}
          </Button>
        </div>
      </div>
    </div>
  )
}

// ── Enabled Toggle ────────────────────────────────────────────────────────────
function EnabledToggle({ endpoint, onToggled }) {
  const [busy, setBusy] = useState(false)
  const { toast } = useToast()

  const toggle = async () => {
    setBusy(true)
    try {
      await fetch(`/api/management/endpoints/${endpoint.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ isEnabled: !endpoint.isEnabled }),
      })
      onToggled()
    } catch {
      toast({
        title: "Failed to toggle endpoint",
        variant: "destructive",
      })
    } finally {
      setBusy(false)
    }
  }

  return (
    <button
      onClick={toggle}
      disabled={busy}
      title={endpoint.isEnabled ? "Enabled – click to disable" : "Disabled – click to enable"}
      className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50 cursor-pointer ${
        endpoint.isEnabled ? "bg-primary/30" : "bg-muted"
      }`}
    >
      <span className={`inline-block h-3.5 w-3.5 rounded-full shadow transition-transform ${
        endpoint.isEnabled ? "translate-x-[18px] bg-primary" : "translate-x-[2px] bg-muted-foreground"
      }`} />
    </button>
  )
}

// ── Loading skeleton ──
function TableSkeleton() {
  return (
    <Card className="overflow-hidden">
      <CardContent className="p-0">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex items-center gap-4 px-4 py-3 border-b last:border-0">
            <Skeleton className="h-5 w-16 rounded-full" />
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-4 flex-1" />
            <Skeleton className="h-4 w-20" />
            <Skeleton className="h-5 w-12 rounded-full" />
            <Skeleton className="h-5 w-9 rounded-full" />
            <Skeleton className="h-8 w-16 rounded-md" />
          </div>
        ))}
      </CardContent>
    </Card>
  )
}

// ── Main tab ──────────────────────────────────────────────────────────────────
export default function EndpointsTab() {
  const [selectedGroupId, setSelectedGroupId] = useState("all")
  const [modal, setModal]     = useState(null)
  const [toDelete, setToDelete] = useState(null)

  // Groups list (fetch once for dropdown)
  const { data: groups } = useFetchWithRefetch("/api/management/groups", 0)

  // Endpoints – reactive to selected group
  const epUrl = selectedGroupId === "all"
    ? "/api/management/endpoints"
    : `/api/management/endpoints?groupId=${selectedGroupId}`
  const { data: endpoints, loading, refetch } = useFetchWithRefetch(epUrl, 5000)

  const refresh = useCallback(() => refetch?.(), [refetch])

  const groupList = groups ?? []
  const epList    = endpoints ?? []

  // Build group name lookup
  const groupName = id => groupList.find(g => g.id === id)?.name ?? id

  if (loading && !endpoints) {
    return (
      <div className="space-y-4">
        <div className="flex items-center gap-4">
          <div>
            <Skeleton className="h-5 w-24 mb-1" />
            <Skeleton className="h-3 w-16" />
          </div>
          <div className="flex-1" />
          <Skeleton className="h-9 w-36 rounded-md" />
        </div>
        <TableSkeleton />
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Header bar */}
      <div className="flex items-center gap-4 flex-wrap">
        <div className="flex-1 min-w-0">
          <h2 className="text-base font-semibold">Endpoints</h2>
          <p className="text-xs text-muted-foreground mt-0.5">{epList.length} endpoint{epList.length !== 1 ? "s" : ""}</p>
        </div>

        {/* Group filter */}
        <div className="flex items-center gap-2">
          <label htmlFor="endpoint-group-filter" className="text-xs text-muted-foreground shrink-0">Filter by group</label>
          <SelectInput
            id="endpoint-group-filter"
            name="groupFilter"
            value={selectedGroupId}
            onChange={e => setSelectedGroupId(e.target.value)}
            aria-label="Filter endpoints by group"
          >
            <option value="all">All groups</option>
            {groupList.map(g => (
              <option key={g.id} value={g.id}>{g.name}</option>
            ))}
          </SelectInput>
        </div>

        <Button onClick={() => setModal({ mode: "create" })} className="shrink-0">
          <Plus className="w-4 h-4" /> New Endpoint
        </Button>
      </div>

      {/* Table */}
      <Card className="overflow-hidden">
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Group</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Path Pattern</TableHead>
                  <TableHead>Destination</TableHead>
                  <TableHead>Remove Prefix</TableHead>
                  <TableHead className="text-center">Auth</TableHead>
                  <TableHead className="text-center">Enabled</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {epList.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={8} className="text-center py-12 text-muted-foreground text-xs">
                      No endpoints yet. Click <span className="text-primary">+ New Endpoint</span> to add one.
                    </TableCell>
                  </TableRow>
                )}
                {epList.map((ep, i) => (
                  <TableRow key={ep.id ?? i} className="group/row">
                    <TableCell>
                      <Badge variant="success" className="font-mono text-[11px]">
                        {groupName(ep.groupId)}
                      </Badge>
                    </TableCell>
                    <TableCell className="font-medium font-mono text-sm">{ep.name}</TableCell>
                    <TableCell className="font-mono text-xs text-blue-400">{ep.pathPattern}</TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground max-w-[180px] truncate">{ep.destination}</TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {ep.removePrefix || <span className="text-muted-foreground/40">—</span>}
                    </TableCell>
                    <TableCell className="text-center">
                      {ep.requiresAuth ? (
                        <Badge variant="warning" className="text-[10px] gap-1">
                          <Lock className="w-3 h-3" /> Auth
                        </Badge>
                      ) : (
                        <Badge variant="outline" className="text-[10px] gap-1 text-muted-foreground">
                          <Unlock className="w-3 h-3" /> Public
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-center">
                      <EnabledToggle endpoint={ep} onToggled={refresh} />
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex items-center justify-end gap-2 opacity-0 group-hover/row:opacity-100 transition-opacity">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setModal({ mode: "edit", endpoint: ep })}
                        >
                          <Pencil className="w-3 h-3" /> Edit
                        </Button>
                        <Button
                          variant="destructive"
                          size="sm"
                          onClick={() => setToDelete(ep)}
                        >
                          <Trash2 className="w-3 h-3" /> Delete
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      {/* Modals */}
      {modal?.mode === "create" && (
        <EndpointModal
          groups={groupList}
          onClose={() => setModal(null)}
          onSaved={() => { setModal(null); refresh() }}
        />
      )}
      {modal?.mode === "edit" && (
        <EndpointModal
          initial={modal.endpoint}
          groups={groupList}
          onClose={() => setModal(null)}
          onSaved={() => { setModal(null); refresh() }}
        />
      )}
      {toDelete && (
        <DeleteDialog
          endpoint={toDelete}
          onClose={() => setToDelete(null)}
          onDeleted={() => { setToDelete(null); refresh() }}
        />
      )}
    </div>
  )
}
