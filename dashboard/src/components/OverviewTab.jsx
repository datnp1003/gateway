import useFetch from "../hooks/useFetch"

function StatCard({ label, value, color = "emerald" }) {
  const colors = {
    emerald: "text-emerald-400 border-emerald-500/20 bg-emerald-500/5",
    blue: "text-blue-400 border-blue-500/20 bg-blue-500/5",
    amber: "text-amber-400 border-amber-500/20 bg-amber-500/5",
    red: "text-red-400 border-red-500/20 bg-red-500/5",
  }
  return (
    <div className={`p-4 rounded-lg border ${colors[color]}`}>
      <div className="text-xs uppercase tracking-wide opacity-60">{label}</div>
      <div className="text-2xl font-bold mt-1">{value}</div>
    </div>
  )
}

function StatusCodeBar({ statusCodes }) {
  if (!statusCodes || Object.keys(statusCodes).length === 0) return null
  const total = Object.values(statusCodes).reduce((a, b) => a + b, 0)
  const map = { "200": "bg-emerald-500", "401": "bg-amber-500", "404": "bg-gray-500", "502": "bg-red-500", "500": "bg-red-600" }
  return (
    <div className="mt-2">
      <div className="flex h-2 rounded overflow-hidden bg-gray-800">
        {Object.entries(statusCodes).map(([code, count]) => (
          <div key={code} className={map[code.substring(0,3)] || "bg-blue-500"}
            style={{ width: `${(count / total) * 100}%` }} title={`${code}: ${count}`} />
        ))}
      </div>
      <div className="flex gap-3 mt-1 text-xs text-gray-500">
        {Object.entries(statusCodes).map(([code, count]) => (
          <span key={code}>{code}: {count}</span>
        ))}
      </div>
    </div>
  )
}

export default function OverviewTab() {
  const { data: db, loading } = useFetch("/api/management/dashboard", 3000)

  if (loading) return <div className="text-gray-500 animate-pulse">Loading...</div>
  if (!db) return <div className="text-red-400">Failed to load</div>

  const errCount = db.recentLogs?.filter(l => l.level === "Error").length || 0

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-4 gap-4">
        <StatCard label="Total Requests" value={db.totalRequests || 0} color="emerald" />
        <StatCard label="Routes" value={db.routes?.length || 0} color="blue" />
        <StatCard label="Clusters" value={db.clusters?.length || 0} color="blue" />
        <StatCard label="Errors (recent)" value={errCount} color={errCount > 0 ? "red" : "emerald"} />
      </div>
      <div className="bg-gray-900 rounded-lg border border-gray-800 p-4">
        <h3 className="text-sm font-medium text-gray-400 mb-3">Status Code Distribution</h3>
        <StatusCodeBar statusCodes={db.recentLogs?.reduce((acc, l) => {
          if (l.statusCode) { acc[l.statusCode] = (acc[l.statusCode] || 0) + 1 }
          return acc
        }, {})} />
      </div>
      <div className="bg-gray-900 rounded-lg border border-gray-800 p-4">
        <h3 className="text-sm font-medium text-gray-400 mb-3">Recent Activity</h3>
        <div className="space-y-1 max-h-64 overflow-auto">
          {db.recentLogs?.slice(-20).reverse().map((log, i) => (
            <div key={i} className="flex items-center gap-3 text-xs font-mono">
              <span className={`w-12 shrink-0 ${
                log.level === "Error" ? "text-red-400" :
                log.level === "Warning" ? "text-amber-400" : "text-gray-500"
              }`}>{log.level}</span>
              <span className="text-gray-300 truncate flex-1">{log.message}</span>
              {log.statusCode && (
                <span className={`w-8 text-right ${
                  log.statusCode >= 500 ? "text-red-400" :
                  log.statusCode >= 400 ? "text-amber-400" : "text-emerald-400"
                }`}>{log.statusCode}</span>
              )}
              {log.durationMs != null && <span className="w-16 text-right text-gray-600">{log.durationMs.toFixed(0)}ms</span>}
            </div>
          ))}
          {(!db.recentLogs || db.recentLogs.length === 0) && (
            <div className="text-gray-600 text-xs">No requests yet</div>
          )}
        </div>
      </div>
    </div>
  )
}
