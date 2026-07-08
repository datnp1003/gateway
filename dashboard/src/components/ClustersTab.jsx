import useFetch from "../hooks/useFetch"

export default function ClustersTab() {
  const { data: clusters, loading } = useFetch("/api/management/clusters", 5000)
  if (loading) return <div className="text-gray-500 animate-pulse">Loading...</div>
  return (
    <div className="space-y-4">
      {clusters?.map((c, i) => (
        <div key={i} className="bg-gray-900 rounded border border-gray-800 p-4">
          <h3 className="text-sm font-mono font-bold text-emerald-400 mb-3">{c.clusterId}</h3>
          <div className="grid grid-cols-2 gap-2">
            {c.destinations?.map((d, j) => (
              <div key={j} className="flex items-center justify-between p-2 bg-gray-800/50 rounded">
                <div>
                  <span className="text-sm font-mono text-gray-300">{d.name}</span>
                  <div className="text-xs text-gray-500">{d.address}</div>
                </div>
                <span className="w-2 h-2 rounded-full bg-gray-500" />
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}
