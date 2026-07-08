import { useState, useEffect, useCallback } from "react"

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
      } catch (_) {}
      finally { if (!cancelled) setLoading(false) }
    }
    run()
    const timer = interval > 0 ? setInterval(run, interval) : null
    return () => { cancelled = true; timer && clearInterval(timer) }
  }, [url, interval, tick])

  return { data, loading, refetch }
}

// ── Endpoint Modal ────────────────────────────────────────────────────────────
function EndpointModal({ initial, groups, onClose, onSaved }) {
  const editing = !!initial
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
      onSaved()
    } catch (ex) {
      setErr(ex.message || "Request failed")
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-gray-900 border border-gray-800 rounded-xl shadow-2xl w-full max-w-lg mx-4 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-800">
          <h2 className="text-sm font-semibold text-gray-100">
            {editing ? "Edit Endpoint" : "New Endpoint"}
          </h2>
          <button onClick={onClose} className="text-gray-500 hover:text-gray-300 transition-colors text-lg leading-none">✕</button>
        </div>

        {/* Form */}
        <form onSubmit={submit} className="px-6 py-5 space-y-4 max-h-[80vh] overflow-y-auto">
          {/* Group */}
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">
              Group <span className="text-red-400">*</span>
            </label>
            <select
              value={form.groupId}
              onChange={e => set("groupId", e.target.value)}
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 focus:outline-none focus:border-emerald-500/60 transition-colors"
            >
              {groups.map(g => (
                <option key={g.id} value={g.id}>{g.name}</option>
              ))}
            </select>
          </div>

          {/* Name */}
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">
              Name <span className="text-red-400">*</span>
            </label>
            <input
              type="text"
              value={form.name}
              onChange={e => set("name", e.target.value)}
              placeholder="my-endpoint"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 placeholder-gray-600 focus:outline-none focus:border-emerald-500/60 transition-colors"
            />
          </div>

          {/* Path Pattern */}
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">
              Path Pattern <span className="text-red-400">*</span>
            </label>
            <input
              type="text"
              value={form.pathPattern}
              onChange={e => set("pathPattern", e.target.value)}
              placeholder="/api/v1/{**}"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 font-mono placeholder-gray-600 focus:outline-none focus:border-emerald-500/60 transition-colors"
            />
          </div>

          {/* Destination */}
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">
              Destination <span className="text-red-400">*</span>
            </label>
            <input
              type="text"
              value={form.destination}
              onChange={e => set("destination", e.target.value)}
              placeholder="http://backend:8080"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 font-mono placeholder-gray-600 focus:outline-none focus:border-emerald-500/60 transition-colors"
            />
          </div>

          {/* Remove Prefix */}
          <div>
            <label className="block text-xs font-medium text-gray-400 mb-1">Remove Prefix <span className="text-gray-600">(optional)</span></label>
            <input
              type="text"
              value={form.removePrefix}
              onChange={e => set("removePrefix", e.target.value)}
              placeholder="/api/v1"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-100 font-mono placeholder-gray-600 focus:outline-none focus:border-emerald-500/60 transition-colors"
            />
          </div>

          {/* Requires Auth */}
          <label className="flex items-center gap-3 cursor-pointer select-none">
            <span className="relative">
              <input
                type="checkbox"
                className="sr-only peer"
                checked={form.requiresAuth}
                onChange={e => set("requiresAuth", e.target.checked)}
              />
              <div className={`h-5 w-9 rounded-full transition-colors ${form.requiresAuth ? "bg-emerald-500/30" : "bg-gray-700"}`} />
              <div className={`absolute top-[3px] h-3.5 w-3.5 rounded-full shadow transition-all ${
                form.requiresAuth ? "left-[18px] bg-emerald-400" : "left-[3px] bg-gray-500"
              }`} />
            </span>
            <span className="text-sm text-gray-300">Requires Authentication</span>
            <span className="text-sm">{form.requiresAuth ? "🔒" : "🔓"}</span>
          </label>

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
function DeleteDialog({ endpoint, onClose, onDeleted }) {
  const [deleting, setDeleting] = useState(false)

  const confirm = async () => {
    setDeleting(true)
    await fetch(`/api/management/endpoints/${endpoint.id}`, { method: "DELETE" })
    setDeleting(false)
    onDeleted()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
      <div className="bg-gray-900 border border-gray-800 rounded-xl shadow-2xl w-full max-w-sm mx-4 p-6 space-y-4">
        <h2 className="text-sm font-semibold text-gray-100">Delete Endpoint</h2>
        <p className="text-sm text-gray-400">
          Are you sure you want to delete <span className="text-gray-100 font-mono">{endpoint.name}</span>? This action cannot be undone.
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

// ── Enabled Toggle ────────────────────────────────────────────────────────────
function EnabledToggle({ endpoint, onToggled }) {
  const [busy, setBusy] = useState(false)

  const toggle = async () => {
    setBusy(true)
    await fetch(`/api/management/endpoints/${endpoint.id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ isEnabled: !endpoint.isEnabled }),
    })
    setBusy(false)
    onToggled()
  }

  return (
    <button
      onClick={toggle}
      disabled={busy}
      title={endpoint.isEnabled ? "Enabled – click to disable" : "Disabled – click to enable"}
      className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none disabled:opacity-50 ${
        endpoint.isEnabled ? "bg-emerald-500/30" : "bg-gray-700"
      }`}
    >
      <span className={`inline-block h-3.5 w-3.5 rounded-full shadow transition-transform ${
        endpoint.isEnabled ? "translate-x-[18px] bg-emerald-400" : "translate-x-[2px] bg-gray-500"
      }`} />
    </button>
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
    return <div className="text-gray-500 animate-pulse">Loading endpoints…</div>
  }

  return (
    <div className="space-y-4">
      {/* Header bar */}
      <div className="flex items-center gap-4 flex-wrap">
        <div className="flex-1 min-w-0">
          <h2 className="text-base font-semibold text-gray-100">Endpoints</h2>
          <p className="text-xs text-gray-500 mt-0.5">{epList.length} endpoint{epList.length !== 1 ? "s" : ""}</p>
        </div>

        {/* Group filter */}
        <div className="flex items-center gap-2">
          <label className="text-xs text-gray-500 shrink-0">Filter by group</label>
          <select
            value={selectedGroupId}
            onChange={e => setSelectedGroupId(e.target.value)}
            className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-sm text-gray-100 focus:outline-none focus:border-emerald-500/60 transition-colors"
          >
            <option value="all">All groups</option>
            {groupList.map(g => (
              <option key={g.id} value={g.id}>{g.name}</option>
            ))}
          </select>
        </div>

        <button
          onClick={() => setModal({ mode: "create" })}
          className="flex items-center gap-2 px-4 py-2 text-sm rounded-lg bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 hover:bg-emerald-500/20 transition-colors font-medium shrink-0"
        >
          <span className="text-base leading-none">＋</span> New Endpoint
        </button>
      </div>

      {/* Table */}
      <div className="bg-gray-900 rounded-xl border border-gray-800 overflow-hidden overflow-x-auto">
        <table className="w-full text-sm min-w-[900px]">
          <thead>
            <tr className="border-b border-gray-800 text-xs text-gray-500 uppercase tracking-wider">
              <th className="text-left px-4 py-3 font-medium">Group</th>
              <th className="text-left px-4 py-3 font-medium">Name</th>
              <th className="text-left px-4 py-3 font-medium">Path Pattern</th>
              <th className="text-left px-4 py-3 font-medium">Destination</th>
              <th className="text-left px-4 py-3 font-medium">Remove Prefix</th>
              <th className="text-center px-4 py-3 font-medium">Auth</th>
              <th className="text-center px-4 py-3 font-medium">Enabled</th>
              <th className="text-right px-4 py-3 font-medium">Actions</th>
            </tr>
          </thead>
          <tbody>
            {epList.length === 0 && (
              <tr>
                <td colSpan={8} className="text-center py-12 text-gray-600 text-xs">
                  No endpoints yet. Click <span className="text-emerald-500">+ New Endpoint</span> to add one.
                </td>
              </tr>
            )}
            {epList.map((ep, i) => (
              <tr key={ep.id ?? i}
                className="border-b border-gray-800/60 hover:bg-gray-800/30 transition-colors group/row">
                {/* Group */}
                <td className="px-4 py-3">
                  <span className="px-2 py-0.5 text-xs rounded-full bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 font-mono">
                    {groupName(ep.groupId)}
                  </span>
                </td>
                {/* Name */}
                <td className="px-4 py-3 font-medium text-gray-200 font-mono">{ep.name}</td>
                {/* Path Pattern */}
                <td className="px-4 py-3 font-mono text-xs text-blue-300">{ep.pathPattern}</td>
                {/* Destination */}
                <td className="px-4 py-3 font-mono text-xs text-gray-400 max-w-[180px] truncate">{ep.destination}</td>
                {/* Remove Prefix */}
                <td className="px-4 py-3 font-mono text-xs text-gray-500">
                  {ep.removePrefix || <span className="text-gray-700">—</span>}
                </td>
                {/* Auth badge */}
                <td className="px-4 py-3 text-center">
                  {ep.requiresAuth ? (
                    <span className="px-2 py-0.5 text-xs rounded-full bg-amber-500/10 border border-amber-500/20 text-amber-400">🔒 Auth</span>
                  ) : (
                    <span className="px-2 py-0.5 text-xs rounded-full bg-gray-800 border border-gray-700 text-gray-500">🔓 Public</span>
                  )}
                </td>
                {/* Enabled toggle */}
                <td className="px-4 py-3 text-center">
                  <EnabledToggle endpoint={ep} onToggled={refresh} />
                </td>
                {/* Actions */}
                <td className="px-4 py-3 text-right">
                  <div className="flex items-center justify-end gap-2 opacity-0 group-hover/row:opacity-100 transition-opacity">
                    <button
                      onClick={() => setModal({ mode: "edit", endpoint: ep })}
                      className="px-3 py-1 text-xs rounded-lg border border-gray-700 text-gray-400 hover:text-gray-200 hover:border-gray-500 transition-colors"
                    >
                      Edit
                    </button>
                    <button
                      onClick={() => setToDelete(ep)}
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
