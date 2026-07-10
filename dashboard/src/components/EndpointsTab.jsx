import { useState, useEffect, useCallback } from "react"
import { useToast } from "../hooks/useToast"
import { Button } from "./ui/Button"
import { Badge } from "./ui/Badge"
import { Card, CardContent } from "./ui/Card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/Table"
import { Skeleton } from "./ui/Skeleton"
import { Select as SelectInput } from "./ui/Select"
import { Plus, Pencil, Trash2, AlertTriangle, ShieldOff, ShieldCheck, Gauge, Ban } from "lucide-react"

// ── Shared: full endpoint payload builder (avoid clearing unset fields on PUT) ─
function buildFullPayload(ep, overrides) {
  return {
    groupId:            ep.groupId,
    name:               ep.name,
    pathPattern:        ep.pathPattern,
    destination:        ep.destination,
    removePrefix:       ep.removePrefix ?? null,
    requiresAuth:       ep.requiresAuth ?? false,
    isEnabled:          ep.isEnabled ?? true,
    rateLimitPerMinute: ep.rateLimitPerMinute ?? null,
    blockedIpRanges:    ep.blockedIpRanges ?? null,
    allowedIpRanges:    ep.allowedIpRanges ?? null,
    ...overrides,
  }
}

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

// ── Endpoint Modal (basic route config only) ─────────────────────────────────
function EndpointModal({ initial, groups, onClose, onSaved }) {
  const editing = !!initial
  const { toast } = useToast()
  const [form, setForm] = useState({
    groupId:      initial?.groupId      ?? (groups[0]?.id ?? ""),
    name:         initial?.name         ?? "",
    pathPattern:  initial?.pathPattern  ?? "",
    destination:  initial?.destination  ?? "",
    removePrefix: initial?.removePrefix ?? "",
    requiresAuth: initial?.requiresAuth ?? false,
    isEnabled:    initial?.isEnabled    ?? true,
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
      // For PUT: preserve existing policy fields to avoid clearing them
      const body = editing
        ? buildFullPayload(initial, {
            groupId:      form.groupId,
            name:         form.name.trim(),
            pathPattern:  form.pathPattern.trim(),
            destination:  form.destination.trim(),
            removePrefix: form.removePrefix.trim() || null,
            requiresAuth: form.requiresAuth,
            isEnabled:    form.isEnabled,
          })
        : {
            groupId:      form.groupId,
            name:         form.name.trim(),
            pathPattern:  form.pathPattern.trim(),
            destination:  form.destination.trim(),
            removePrefix: form.removePrefix.trim() || null,
            requiresAuth: form.requiresAuth,
          }
      const res = await fetch(url, {
        method,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
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

          {/* Downstream Auth (metadata) */}
          <label className="flex items-center gap-3 cursor-pointer select-none">
            <span className="relative">
              <input
                id="endpoint-requires-auth"
                name="requiresAuth"
                type="checkbox"
                className="sr-only peer"
                checked={form.requiresAuth}
                onChange={e => set("requiresAuth", e.target.checked)}
                aria-label="Service Requires Auth (metadata)"
              />
              <div className={`h-5 w-9 rounded-full transition-colors ${form.requiresAuth ? "bg-primary/30" : "bg-muted"}`} />
              <div className={`absolute top-[3px] h-3.5 w-3.5 rounded-full shadow transition-all ${
                form.requiresAuth ? "left-[18px] bg-primary" : "left-[3px] bg-muted-foreground"
              }`} />
            </span>
            <span className="text-sm">Service Requires Auth</span>
            <span className="text-xs text-muted-foreground">(downstream metadata — gateway always forwards auth headers)</span>
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

// ── Rate Limit Modal ─────────────────────────────────────────────────────────
function RateLimitModal({ endpoint, onClose, onSaved }) {
  const { toast } = useToast()
  const [value, setValue] = useState(endpoint.rateLimitPerMinute != null ? String(endpoint.rateLimitPerMinute) : "")
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState("")

  const submit = async e => {
    e.preventDefault()
    const parsed = value.trim() === "" ? null : Number(value)
    if (parsed !== null && (isNaN(parsed) || parsed < 0)) {
      setErr("Must be a positive number or empty to remove"); return
    }
    setSaving(true); setErr("")
    try {
      const res = await fetch(`/api/management/endpoints/${endpoint.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(buildFullPayload(endpoint, { rateLimitPerMinute: parsed })),
      })
      if (!res.ok) throw new Error(await res.text())
      toast({
        title: parsed == null
          ? `Rate limit removed from "${endpoint.name}"`
          : `Rate limit set to ${parsed}/min on "${endpoint.name}"`,
        variant: "success",
      })
      onSaved()
    } catch (ex) {
      const msg = ex.message || "Request failed"
      setErr(msg)
      toast({ title: "Failed to save rate limit", description: msg, variant: "destructive" })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border rounded-xl shadow-2xl w-full max-w-sm mx-4 overflow-hidden">
        <div className="flex items-center justify-between px-6 py-4 border-b">
          <div className="flex items-center gap-2">
            <Gauge className="w-4 h-4 text-blue-400" />
            <h2 className="text-sm font-semibold">Rate Limit</h2>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground transition-colors text-lg leading-none cursor-pointer" aria-label="Close dialog">✕</button>
        </div>
        <form onSubmit={submit} className="px-6 py-5 space-y-4">
          <p className="text-xs text-muted-foreground">
            Endpoint-level rate limit for <span className="text-foreground font-mono">{endpoint.name}</span>.
            Applies in addition to any group-level policy.
          </p>
          <div>
            <label htmlFor="rl-value" className="block text-xs font-medium text-muted-foreground mb-1">
              Requests / minute <span className="text-muted-foreground">(per IP — leave empty to remove)</span>
            </label>
            <input
              id="rl-value"
              type="number"
              min="0"
              value={value}
              onChange={e => setValue(e.target.value)}
              placeholder="e.g. 60"
              aria-label="Rate limit per minute"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors"
            />
          </div>
          {err && (
            <p className="text-xs text-destructive bg-destructive/10 border border-destructive/20 rounded-lg px-3 py-2 flex items-center gap-2">
              <AlertTriangle className="w-3.5 h-3.5 shrink-0" /> {err}
            </p>
          )}
          <div className="flex gap-3 pt-1">
            <Button type="button" variant="outline" onClick={onClose} className="flex-1">Cancel</Button>
            <Button type="submit" disabled={saving} className="flex-1">{saving ? "Saving…" : "Save"}</Button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Block IPs Modal (Endpoint) ─────────────────────────────────────────────────
function EndpointBlockIpsModal({ endpoint, onClose, onSaved }) {
  const { toast } = useToast()
  const [value, setValue] = useState(endpoint.blockedIpRanges ?? "")
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState("")

  const submit = async e => {
    e.preventDefault()
    setSaving(true); setErr("")
    try {
      const res = await fetch(`/api/management/endpoints/${endpoint.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(buildFullPayload(endpoint, { blockedIpRanges: value.trim() || null })),
      })
      if (!res.ok) throw new Error(await res.text())
      toast({
        title: value.trim()
          ? `Block list updated on "${endpoint.name}"`
          : `Block list cleared on "${endpoint.name}"`,
        variant: "success",
      })
      onSaved()
    } catch (ex) {
      const msg = ex.message || "Request failed"
      setErr(msg)
      toast({ title: "Failed to save block list", description: msg, variant: "destructive" })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border rounded-xl shadow-2xl w-full max-w-sm mx-4 overflow-hidden">
        <div className="flex items-center justify-between px-6 py-4 border-b">
          <div className="flex items-center gap-2">
            <ShieldOff className="w-4 h-4 text-red-400" />
            <h2 className="text-sm font-semibold">Block IPs — {endpoint.name}</h2>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground transition-colors text-lg leading-none cursor-pointer" aria-label="Close dialog">✕</button>
        </div>
        <form onSubmit={submit} className="px-6 py-5 space-y-4">
          <p className="text-xs text-muted-foreground">
            Endpoint-level block list. Applies <em>in addition to</em> any group-level block rules.
          </p>
          <div>
            <label htmlFor="ep-block-ip" className="block text-xs font-medium text-muted-foreground mb-1">
              Blocked IP ranges <span className="text-muted-foreground">(comma or newline separated — empty to clear)</span>
            </label>
            <textarea
              id="ep-block-ip"
              rows={4}
              value={value}
              onChange={e => setValue(e.target.value)}
              placeholder={"192.168.1.0/24\n10.0.0.1"}
              aria-label="Blocked IP ranges"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm font-mono placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors resize-y"
            />
          </div>
          {err && (
            <p className="text-xs text-destructive bg-destructive/10 border border-destructive/20 rounded-lg px-3 py-2 flex items-center gap-2">
              <AlertTriangle className="w-3.5 h-3.5 shrink-0" /> {err}
            </p>
          )}
          <div className="flex gap-3 pt-1">
            <Button type="button" variant="outline" onClick={onClose} className="flex-1">Cancel</Button>
            <Button type="submit" disabled={saving} className="flex-1">{saving ? "Saving…" : "Save"}</Button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Allow IPs Modal (Endpoint) ─────────────────────────────────────────────────
function EndpointAllowIpsModal({ endpoint, onClose, onSaved }) {
  const { toast } = useToast()
  const [value, setValue] = useState(endpoint.allowedIpRanges ?? "")
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState("")

  const submit = async e => {
    e.preventDefault()
    setSaving(true); setErr("")
    try {
      const res = await fetch(`/api/management/endpoints/${endpoint.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(buildFullPayload(endpoint, { allowedIpRanges: value.trim() || null })),
      })
      if (!res.ok) throw new Error(await res.text())
      toast({
        title: value.trim()
          ? `Allowlist updated on "${endpoint.name}"`
          : `Allowlist cleared on "${endpoint.name}"`,
        variant: "success",
      })
      onSaved()
    } catch (ex) {
      const msg = ex.message || "Request failed"
      setErr(msg)
      toast({ title: "Failed to save allowlist", description: msg, variant: "destructive" })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border rounded-xl shadow-2xl w-full max-w-sm mx-4 overflow-hidden">
        <div className="flex items-center justify-between px-6 py-4 border-b">
          <div className="flex items-center gap-2">
            <ShieldCheck className="w-4 h-4 text-amber-400" />
            <h2 className="text-sm font-semibold">Allow IPs — {endpoint.name}</h2>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground transition-colors text-lg leading-none cursor-pointer" aria-label="Close dialog">✕</button>
        </div>
        <form onSubmit={submit} className="px-6 py-5 space-y-4">
          <p className="text-xs text-muted-foreground">
            Endpoint-level allowlist (whitelist). When non-empty, only listed IPs are allowed through —
            in addition to group-level rules.
          </p>
          <div>
            <label htmlFor="ep-allow-ip" className="block text-xs font-medium text-muted-foreground mb-1">
              Allowed IP ranges <span className="text-muted-foreground">(comma or newline separated — empty to disable whitelist)</span>
            </label>
            <textarea
              id="ep-allow-ip"
              rows={4}
              value={value}
              onChange={e => setValue(e.target.value)}
              placeholder={"203.0.113.0/24\n10.0.0.0/8"}
              aria-label="Allowed IP ranges"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm font-mono placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors resize-y"
            />
          </div>
          {err && (
            <p className="text-xs text-destructive bg-destructive/10 border border-destructive/20 rounded-lg px-3 py-2 flex items-center gap-2">
              <AlertTriangle className="w-3.5 h-3.5 shrink-0" /> {err}
            </p>
          )}
          <div className="flex gap-3 pt-1">
            <Button type="button" variant="outline" onClick={onClose} className="flex-1">Cancel</Button>
            <Button type="submit" disabled={saving} className="flex-1">{saving ? "Saving…" : "Save"}</Button>
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
      // Send full payload to avoid clearing existing policy fields
      await fetch(`/api/management/endpoints/${endpoint.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(buildFullPayload(endpoint, { isEnabled: !endpoint.isEnabled })),
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

  // Build group name and enabled-state lookup
  const groupMeta  = id => groupList.find(g => g.id === id)
  const groupName  = id => groupMeta(id)?.name ?? id
  const groupOff   = id => groupMeta(id)?.isEnabled === false

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
          <h2 className="text-base font-semibold">API Routes</h2>
          <p className="text-xs text-muted-foreground mt-0.5">{epList.length} route{epList.length !== 1 ? "s" : ""}</p>
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
          <Plus className="w-4 h-4" /> New API Route
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
                  <TableHead className="text-center">Svc Auth</TableHead>
                  <TableHead className="text-center">Policy</TableHead>
                  <TableHead className="text-center">Enabled</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {epList.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={9} className="text-center py-12 text-muted-foreground text-xs">
                      No endpoints yet. Click <span className="text-primary">+ New Endpoint</span> to add one.
                    </TableCell>
                  </TableRow>
                )}
                {epList.map((ep, i) => {
                  const isGroupOff = groupOff(ep.groupId)
                  return (
                  <TableRow key={ep.id ?? i} className={`group/row${isGroupOff ? " opacity-60" : ""}`}>
                    <TableCell>
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <Badge variant="success" className="font-mono text-[11px]">
                          {groupName(ep.groupId)}
                        </Badge>
                        {isGroupOff && (
                          <span
                            title="This route's group is disabled — the route is unreachable regardless of its own enabled state"
                            className="inline-flex items-center gap-0.5 text-[10px] font-semibold px-1.5 py-0.5 rounded-full bg-orange-500/10 text-orange-400 border border-orange-500/25 leading-none"
                          >
                            <Ban className="w-2.5 h-2.5" />
                            Group off
                          </span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="font-medium font-mono text-sm">{ep.name}</TableCell>
                    <TableCell className="font-mono text-xs text-blue-400">{ep.pathPattern}</TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground max-w-[180px] truncate">{ep.destination}</TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {ep.removePrefix || <span className="text-muted-foreground/40">—</span>}
                    </TableCell>
                    <TableCell className="text-center">
                      {ep.requiresAuth ? (
                        <Badge variant="outline" className="text-[10px] gap-1 text-amber-400 border-amber-400/40">
                          Svc Auth
                        </Badge>
                      ) : (
                        <Badge variant="outline" className="text-[10px] gap-1 text-muted-foreground">
                          Svc Open
                        </Badge>
                      )}
                    </TableCell>
                    {/* Compact policy badges — clickable to edit */}
                    <TableCell className="text-center">
                      <div className="flex flex-wrap gap-1 justify-center">
                        {ep.rateLimitPerMinute > 0 && (
                          <button
                            onClick={() => setModal({ mode: "rateLimit", endpoint: ep })}
                            title="Edit rate limit"
                            className="cursor-pointer"
                          >
                            <Badge variant="outline" className="text-[10px] text-blue-400 border-blue-400/40 hover:border-blue-400 transition-colors">
                              RL:{ep.rateLimitPerMinute}
                            </Badge>
                          </button>
                        )}
                        {ep.blockedIpRanges && (
                          <button
                            onClick={() => setModal({ mode: "blockIps", endpoint: ep })}
                            title="Edit block list"
                            className="cursor-pointer"
                          >
                            <Badge variant="destructive" className="text-[10px] hover:opacity-80 transition-opacity">
                              Blocklist
                            </Badge>
                          </button>
                        )}
                        {ep.allowedIpRanges && (
                          <button
                            onClick={() => setModal({ mode: "allowIps", endpoint: ep })}
                            title="Edit allowlist"
                            className="cursor-pointer"
                          >
                            <Badge variant="warning" className="text-[10px] hover:opacity-80 transition-opacity">
                              Whitelist
                            </Badge>
                          </button>
                        )}
                        {!ep.rateLimitPerMinute && !ep.blockedIpRanges && !ep.allowedIpRanges && (
                          <span className="text-muted-foreground/40 text-[10px]">—</span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="text-center">
                      <div title={isGroupOff ? "Group is disabled — enable the group first to make this route callable" : undefined}>
                        <EnabledToggle endpoint={ep} onToggled={refresh} />
                      </div>
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex items-center justify-end gap-1 opacity-0 group-hover/row:opacity-100 transition-opacity">
                        {/* Policy icon actions */}
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setModal({ mode: "rateLimit", endpoint: ep })}
                          title="Rate limit"
                          className="h-7 px-2 text-blue-400 hover:text-blue-300 hover:bg-blue-400/10"
                        >
                          <Gauge className="w-3 h-3" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setModal({ mode: "blockIps", endpoint: ep })}
                          title="Block IPs"
                          className="h-7 px-2 text-red-400 hover:text-red-300 hover:bg-red-400/10"
                        >
                          <ShieldOff className="w-3 h-3" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setModal({ mode: "allowIps", endpoint: ep })}
                          title="Allow IPs"
                          className="h-7 px-2 text-amber-400 hover:text-amber-300 hover:bg-amber-400/10"
                        >
                          <ShieldCheck className="w-3 h-3" />
                        </Button>
                        <span className="w-px h-4 bg-border mx-0.5" />
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
                  )
                })}
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
      {modal?.mode === "rateLimit" && (
        <RateLimitModal
          endpoint={modal.endpoint}
          onClose={() => setModal(null)}
          onSaved={() => { setModal(null); refresh() }}
        />
      )}
      {modal?.mode === "blockIps" && (
        <EndpointBlockIpsModal
          endpoint={modal.endpoint}
          onClose={() => setModal(null)}
          onSaved={() => { setModal(null); refresh() }}
        />
      )}
      {modal?.mode === "allowIps" && (
        <EndpointAllowIpsModal
          endpoint={modal.endpoint}
          onClose={() => setModal(null)}
          onSaved={() => { setModal(null); refresh() }}
        />
      )}
    </div>
  )
}
