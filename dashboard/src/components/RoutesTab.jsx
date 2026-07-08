import useFetch from "../hooks/useFetch"

export default function RoutesTab() {
  const { data: routes, loading } = useFetch("/api/management/routes", 5000)
  if (loading) return <div className="text-gray-500 animate-pulse">Loading...</div>
  return (
    <div className="space-y-2">
      {routes?.map((r, i) => (
        <div key={i} className="bg-gray-900 rounded border border-gray-800 p-4">
          <div className="flex items-center gap-3">
            <span className="text-emerald-400 font-mono text-sm font-bold">{r.routeId}</span>
            <span className="px-2 py-0.5 text-xs rounded bg-gray-800 text-gray-400">→ {r.clusterId}</span>
            {r.authorizationPolicy && (
              <span className="px-2 py-0.5 text-xs rounded bg-amber-500/10 text-amber-400 border border-amber-500/20">🔒 {r.authorizationPolicy}</span>
            )}
            {!r.authorizationPolicy && (
              <span className="px-2 py-0.5 text-xs rounded bg-gray-800 text-gray-500">Public</span>
            )}
          </div>
          <div className="mt-2 font-mono text-xs text-gray-500">{r.matchPath}</div>
          {r.transforms?.length > 0 && (
            <div className="mt-1 flex gap-1">
              {r.transforms.map((t, j) => (
                <span key={j} className="px-2 py-0.5 text-xs rounded bg-blue-500/10 text-blue-400">{t}</span>
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  )
}
