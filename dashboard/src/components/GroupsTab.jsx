import { useState, useEffect, useCallback } from "react"
import useFetch from "../hooks/useFetch"

// ── Modal ────────────────────────────────────────────────────────────────────
function GroupModal({ initial, onClose, onSaved }) {
  const editing = !!initial
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
      onSaved()
    } catch (ex) {
      setErr(ex.message || "Request failed")
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-gray-900 border border-gray-800 rounded-xl shadow-2xl w-full max-w-md mx-4 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-800">
          <h2 className="text-sm font-semibold text-gray-100">
            {editing ? "Edit Group" : "New Group"}
          </h2>
          <button onClick={onClose} className="text-gray-500 hover:text-gray-300 transition-colors text-lg leading-none">✕</button>
        </div>

        {/* Form */}
        <form onSubmit={submit} className="px-6 py-5 space-y-4">
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">
              Name <span className="text-red-400">*</span>
            </label>
            <input
              type="text"
              value={form.name}
              onChange={e => set("name", e.target.value)}
              placeholder="my-group"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 placeholder-gray-600 focus:outline-none focus:border-emerald-500/60 transition-colors"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">
              Path <span className="text-red-400">*</span>
            </label>
            <input
              type="text"
              value={form.path}
              onChange={e => set("path", e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "-").replace(/-+/g, "-").replace(/^-|-$/g, ""))}
              placeholder="my-group"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 placeholder-gray-600 focus:outline-none focus:border-emerald-500/60 transition-colors font-mono"
            />
            <p className="text-xs text-gray-600 mt-1">Lowercase letters, numbers and hyphens only</p>
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">Description</label>
            <textarea
              value={form.description}
              onChange={e => set("description", e.target.value)}
              placeholder="Optional description…"
              rows={3}
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 placeholder-gray-600 focus:outline-none focus:border-emerald-500/60 transition-colors resize-none"
            />
          </div>
          {err && <p className="text-xs text-red-400 bg-red-500/10 border border-red-500/20 rounded-lg px-3 py-2">{err}</p>}
          <div className="flex gap-3 pt-1">
            <button type="button" onClick={onClose}
              className="flex-1 px-4 py-2 text-sm rounded-lg border border-gray-700 text-gray-400 hover:text-gray-200 hover:border-gray-600 transition-colors">
              Cancel
            </button>
            <button type="submit" disabled={saving}
              className="flex-1 px-4 py-2 text-sm rounded-lg bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 hover:bg-emerald-500/20 transition-colors disabled:opacity-50 font-medium">
              {saving ? "Saving…" : editing ? "Update" : "Create"}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Delete confirm ────────────────────────────────────────────────────────────
function DeleteDialog({ group, onClose, onDeleted }) {
  const [deleting, setDeleting] = useState(false)

  const confirm = async () => {
    setDeleting(true)
    await fetch(`/api/management/groups/${group.id}`, { method: "DELETE" })
    setDeleting(false)
    onDeleted()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-gray-900 border border-gray-800 rounded-xl shadow-2xl w-full max-w-sm mx-4 p-6 space-y-4">
        <h2 className="text-sm font-semibold text-gray-100">Delete Group</h2>
        <p className="text-sm text-gray-400">
          Are you sure you want to delete <span className="text-gray-100 font-mono">{group.name}</span>? This action cannot be undone.
        </p>
        <div className="flex gap-3">
          <button onClick={onClose}
            className="flex-1 px-4 py-2 text-sm rounded-lg border border-gray-700 text-gray-400 hover:text-gray-200 hover:border-gray-600 transition-colors">
            Cancel
          </button>
          <button onClick={confirm} disabled={deleting}
            className="flex-1 px-4 py-2 text-sm rounded-lg bg-red-500/10 border border-red-500/30 text-red-400 hover:bg-red-500/20 transition-colors disabled:opacity-50 font-medium">
            {deleting ? "Deleting…" : "Delete"}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Toggle ────────────────────────────────────────────────────────────────────
function EnabledToggle({ group, onToggled }) {
  const [busy, setBusy] = useState(false)

  const toggle = async () => {
    setBusy(true)
    await fetch(`/api/management/groups/${group.id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ isEnabled: !group.isEnabled }),
    })
    setBusy(false)
    onToggled()
  }

  return (
    <button
      onClick={toggle}
      disabled={busy}
      title={group.isEnabled ? "Enabled – click to disable" : "Disabled – click to enable"}
      className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none disabled:opacity-50 ${
        group.isEnabled ? "bg-emerald-500/30" : "bg-gray-700"
      }`}
    >
      <span className={`inline-block h-3.5 w-3.5 rounded-full shadow transition-transform ${
        group.isEnabled ? "translate-x-[18px] bg-emerald-400" : "translate-x-[2px] bg-gray-500"
      }`} />
    </button>
  )
}

// ── Main tab ──────────────────────────────────────────────────────────────────
export default function GroupsTab() {
  const { data: groups, loading, refetch } = useFetchWithRefetch("/api/management/groups", 5000)
  const [modal, setModal] = useState(null)   // null | { mode: "create" | "edit", group? }
  const [toDelete, setToDelete] = useState(null)

  const refresh = useCallback(() => refetch?.(), [refetch])

  if (loading && !groups) {
    return <div className="text-gray-500 animate-pulse">Loading groups…</div>
  }

  const list = groups ?? []

  return (
    <div className="space-y-4">
      {/* Header bar */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold text-gray-100">Groups</h2>
          <p className="text-xs text-gray-500 mt-0.5">{list.length} group{list.length !== 1 ? "s" : ""}</p>
        </div>
        <button
          onClick={() => setModal({ mode: "create" })}
          className="flex items-center gap-2 px-4 py-2 text-sm rounded-lg bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 hover:bg-emerald-500/20 transition-colors font-medium"
        >
          <span className="text-base leading-none">＋</span> New Group
        </button>
      </div>

      {/* Table */}
      <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-800 text-xs text-gray-500 uppercase tracking-wider">
              <th className="text-left px-5 py-3 font-medium">Name</th>
              <th className="text-left px-5 py-3 font-medium">Path</th>
              <th className="text-left px-5 py-3 font-medium">Description</th>
              <th className="text-center px-5 py-3 font-medium">Enabled</th>
              <th className="text-center px-5 py-3 font-medium">Endpoints</th>
              <th className="text-right px-5 py-3 font-medium">Actions</th>
            </tr>
          </thead>
          <tbody>
            {list.length === 0 && (
              <tr>
                <td colSpan={6} className="text-center py-12 text-gray-600 text-xs">
                  No groups yet. Click <span className="text-emerald-500">+ New Group</span> to add one.
                </td>
              </tr>
            )}
            {list.map((g, i) => (
              <tr key={g.id ?? i}
                className="border-b border-gray-800/60 hover:bg-gray-800/30 transition-colors group/row">
                <td className="px-5 py-3 font-mono text-emerald-400 font-medium">{g.name}</td>
                <td className="px-5 py-3 font-mono text-gray-400">{g.path || <span className="text-gray-700">—</span>}</td>
                <td className="px-5 py-3 text-gray-400 max-w-xs truncate">{g.description || <span className="text-gray-700">—</span>}</td>
                <td className="px-5 py-3 text-center">
                  <EnabledToggle group={g} onToggled={refresh} />
                </td>
                <td className="px-5 py-3 text-center">
                  <span className="px-2 py-0.5 text-xs rounded-full bg-blue-500/10 border border-blue-500/20 text-blue-400">
                    {g.endpointCount ?? g.endpoints?.length ?? 0}
                  </span>
                </td>
                <td className="px-5 py-3 text-right">
                  <div className="flex items-center justify-end gap-2 opacity-0 group-hover/row:opacity-100 transition-opacity">
                    <button
                      onClick={() => setModal({ mode: "edit", group: g })}
                      className="px-3 py-1 text-xs rounded-lg border border-gray-700 text-gray-400 hover:text-gray-200 hover:border-gray-500 transition-colors"
                    >
                      Edit
                    </button>
                    <button
                      onClick={() => setToDelete(g)}
                      className="px-3 py-1 text-xs rounded-lg border border-red-500/20 bg-red-500/5 text-red-400 hover:bg-red-500/15 transition-colors"
                    >
                      Delete
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

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
      } catch (_) {}
      finally { if (!cancelled) setLoading(false) }
    }
    run()
    const timer = interval > 0 ? setInterval(run, interval) : null
    return () => { cancelled = true; timer && clearInterval(timer) }
  }, [url, interval, tick])

  return { data, loading, refetch }
}
