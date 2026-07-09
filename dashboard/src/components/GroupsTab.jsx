import { useState, useEffect, useCallback } from "react"
import { useToast } from "../hooks/useToast"
import { Button } from "./ui/Button"
import { Badge } from "./ui/Badge"
import { Card, CardContent } from "./ui/Card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./ui/Table"
import { Skeleton } from "./ui/Skeleton"
import { Plus, Pencil, Trash2, AlertTriangle } from "lucide-react"

// ── useFetch with manual refetch ──────────────────────────────────────────────
function useFetchWithRefetch(url, interval) {
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)

  const refetch = useCallback(() => setTick(t => t + 1), [])

  useEffect(() => {
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

// ── Group Modal ───────────────────────────────────────────────────────────────
function GroupModal({ initial, onClose, onSaved }) {
  const editing = !!initial
  const { toast } = useToast()
  const [form, setForm] = useState({ name: initial?.name ?? "", path: initial?.path ?? "", description: initial?.description ?? "" })
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState("")

  const set = (k, v) => setForm(f => ({ ...f, [k]: v }))

  const submit = async e => {
    e.preventDefault()
    if (!form.name.trim()) { setErr("Name is required"); return }
    if (!form.path.trim()) { setErr("Path is required"); return }
    if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(form.path.trim())) { setErr("Path must be lowercase slug format (e.g. my-group)"); return }
    setSaving(true); setErr("")
    try {
      const method = editing ? "PUT" : "POST"
      const url = editing ? `/api/management/groups/${initial.id}` : "/api/management/groups"
      const res = await fetch(url, {
        method,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(
          editing
            ? { name: form.name.trim(), path: form.path.trim(), description: form.description.trim(), isEnabled: initial.isEnabled }
            : { name: form.name.trim(), path: form.path.trim(), description: form.description.trim() }
        ),
      })
      if (!res.ok) throw new Error(await res.text())
      const groupName = form.name.trim()
      toast({
        title: editing ? `Group "${groupName}" updated` : `Group "${groupName}" created`,
        variant: "success",
      })
      onSaved()
    } catch (ex) {
      const msg = ex.message || "Request failed"
      setErr(msg)
      toast({
        title: editing ? "Failed to update group" : "Failed to create group",
        description: msg,
        variant: "destructive",
      })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border rounded-xl shadow-2xl w-full max-w-md mx-4 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b">
          <h2 className="text-sm font-semibold">
            {editing ? "Edit Group" : "New Group"}
          </h2>
          <button
            onClick={onClose}
            className="text-muted-foreground hover:text-foreground transition-colors text-lg leading-none cursor-pointer"
            aria-label="Close dialog"
          >✕</button>
        </div>

        {/* Form */}
        <form onSubmit={submit} className="px-6 py-5 space-y-4">
          <div>
            <label htmlFor="group-name" className="block text-xs font-medium text-muted-foreground mb-1">
              Name <span className="text-destructive">*</span>
            </label>
            <input
              id="group-name"
              name="name"
              type="text"
              value={form.name}
              onChange={e => set("name", e.target.value)}
              placeholder="my-group"
              aria-label="Group name"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors"
            />
          </div>
          <div>
            <label htmlFor="group-path" className="block text-xs font-medium text-muted-foreground mb-1">
              Path <span className="text-destructive">*</span>
            </label>
            <input
              id="group-path"
              name="path"
              type="text"
              value={form.path}
              onChange={e => set("path", e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "-").replace(/-+/g, "-").replace(/^-|-$/g, ""))}
              placeholder="my-group"
              aria-label="Group path"
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm font-mono placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors"
            />
            <p className="text-xs text-muted-foreground mt-1">Lowercase letters, numbers and hyphens only</p>
          </div>
          <div>
            <label htmlFor="group-description" className="block text-xs font-medium text-muted-foreground mb-1">Description</label>
            <textarea
              id="group-description"
              name="description"
              value={form.description}
              onChange={e => set("description", e.target.value)}
              placeholder="Optional description…"
              aria-label="Group description"
              rows={3}
              className="w-full bg-input border rounded-lg px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring transition-colors resize-none"
            />
          </div>
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
function DeleteDialog({ group, onClose, onDeleted }) {
  const [deleting, setDeleting] = useState(false)
  const { toast } = useToast()

  const confirm = async () => {
    setDeleting(true)
    try {
      const res = await fetch(`/api/management/groups/${group.id}`, { method: "DELETE" })
      if (!res.ok) throw new Error(await res.text())
      toast({
        title: `Group "${group.name}" deleted`,
        variant: "success",
      })
      onDeleted()
    } catch (ex) {
      toast({
        title: "Failed to delete group",
        description: ex.message || "Request failed",
        variant: "destructive",
      })
      setDeleting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-card border rounded-xl shadow-2xl w-full max-w-sm mx-4 p-6 space-y-4">
        <h2 className="text-sm font-semibold">Delete Group</h2>
        <p className="text-sm text-muted-foreground">
          Are you sure you want to delete <span className="text-foreground font-mono">{group.name}</span>? This action cannot be undone.
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

// ── Toggle ────────────────────────────────────────────────────────────────────
function EnabledToggle({ group, onToggled }) {
  const [busy, setBusy] = useState(false)
  const { toast } = useToast()

  const toggle = async () => {
    setBusy(true)
    try {
      await fetch(`/api/management/groups/${group.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ isEnabled: !group.isEnabled }),
      })
      onToggled()
    } catch {
      toast({
        title: "Failed to toggle group",
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
      title={group.isEnabled ? "Enabled – click to disable" : "Disabled – click to enable"}
      className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50 cursor-pointer ${
        group.isEnabled ? "bg-primary/30" : "bg-muted"
      }`}
    >
      <span className={`inline-block h-3.5 w-3.5 rounded-full shadow transition-transform ${
        group.isEnabled ? "translate-x-[18px] bg-primary" : "translate-x-[2px] bg-muted-foreground"
      }`} />
    </button>
  )
}

// ── Table skeleton ────────────────────────────────────────────────────────────
function TableSkeleton() {
  return (
    <Card className="overflow-hidden">
      <CardContent className="p-0">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="flex items-center gap-4 px-5 py-3 border-b last:border-0">
            <Skeleton className="h-4 w-28" />
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 flex-1 max-w-xs" />
            <Skeleton className="h-5 w-9 rounded-full" />
            <Skeleton className="h-5 w-10 rounded-full" />
            <Skeleton className="h-8 w-28 rounded-md" />
          </div>
        ))}
      </CardContent>
    </Card>
  )
}

// ── Main tab ──────────────────────────────────────────────────────────────────
export default function GroupsTab() {
  const { data: groups, loading, refetch } = useFetchWithRefetch("/api/management/groups", 5000)
  const [modal, setModal] = useState(null)
  const [toDelete, setToDelete] = useState(null)

  const refresh = useCallback(() => refetch?.(), [refetch])

  if (loading && !groups) {
    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <div>
            <Skeleton className="h-5 w-20 mb-1" />
            <Skeleton className="h-3 w-12" />
          </div>
          <Skeleton className="h-9 w-32 rounded-md" />
        </div>
        <TableSkeleton />
      </div>
    )
  }

  const list = groups ?? []

  return (
    <div className="space-y-4">
      {/* Header bar */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold">Groups</h2>
          <p className="text-xs text-muted-foreground mt-0.5">{list.length} group{list.length !== 1 ? "s" : ""}</p>
        </div>
        <Button onClick={() => setModal({ mode: "create" })}>
          <Plus className="w-4 h-4" /> New Group
        </Button>
      </div>

      {/* Table */}
      <Card className="overflow-hidden">
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Path</TableHead>
                <TableHead>Description</TableHead>
                <TableHead className="text-center">Enabled</TableHead>
                <TableHead className="text-center">Endpoints</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {list.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6} className="text-center py-12 text-muted-foreground text-xs">
                    No groups yet. Click <span className="text-primary">+ New Group</span> to add one.
                  </TableCell>
                </TableRow>
              )}
              {list.map((g, i) => (
                <TableRow key={g.id ?? i} className="group/row">
                  <TableCell className="font-mono text-primary font-medium">{g.name}</TableCell>
                  <TableCell className="font-mono text-muted-foreground">{g.path || <span className="text-muted-foreground/40">—</span>}</TableCell>
                  <TableCell className="text-muted-foreground max-w-xs truncate">{g.description || <span className="text-muted-foreground/40">—</span>}</TableCell>
                  <TableCell className="text-center">
                    <EnabledToggle group={g} onToggled={refresh} />
                  </TableCell>
                  <TableCell className="text-center">
                    <Badge variant="secondary" className="font-mono">
                      {g.endpointCount ?? g.endpoints?.length ?? 0}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    <div className="flex items-center justify-end gap-2 opacity-0 group-hover/row:opacity-100 transition-opacity">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => setModal({ mode: "edit", group: g })}
                      >
                        <Pencil className="w-3 h-3" /> Edit
                      </Button>
                      <Button
                        variant="destructive"
                        size="sm"
                        onClick={() => setToDelete(g)}
                      >
                        <Trash2 className="w-3 h-3" /> Delete
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {/* Modals */}
      {modal?.mode === "create" && (
        <GroupModal onClose={() => setModal(null)} onSaved={() => { setModal(null); refresh() }} />
      )}
      {modal?.mode === "edit" && (
        <GroupModal initial={modal.group} onClose={() => setModal(null)} onSaved={() => { setModal(null); refresh() }} />
      )}
      {toDelete && (
        <DeleteDialog group={toDelete} onClose={() => setToDelete(null)} onDeleted={() => { setToDelete(null); refresh() }} />
      )}
    </div>
  )
}
