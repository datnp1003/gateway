import { useState } from "react"
import useFetch from "../hooks/useFetch"

const levelColors = {
  Error: "text-red-400 bg-red-500/5 border-red-500/20",
  Warning: "text-amber-400 bg-amber-500/5 border-amber-500/20",
  Info: "text-gray-400 bg-gray-800/50 border-gray-800",
}

export default function LogsTab() {
  const [filter, setFilter] = useState("All")
  const { data: logs, loading } = useFetch("/api/management/logs?count=200", 2000)
  if (loading) return <div className="text-gray-500 animate-pulse">Loading...</div>
  const filtered = logs?.filter(l => filter === "All" || l.level === filter) || []

  return (
    <div>
      <div className="flex gap-2 mb-4">
        {["All", "Error", "Warning", "Info"].map(f => (
          <button key={f} onClick={() => setFilter(f)}
            className={`px-3 py-1 text-xs rounded border transition-colors ${
              filter === f ? "bg-emerald-500/10 text-emerald-400 border-emerald-500/30"
                : "border-gray-800 text-gray-500 hover:text-gray-300"
            }`}
          >{f}</button>
        ))}
        <span className="ml-auto text-xs text-gray-600">{filtered.length} entries</span>
      </div>
      <div className="bg-gray-900 rounded-lg border border-gray-800 overflow-hidden">
        <div className="max-h-[calc(100vh-200px)] overflow-auto">
          {filtered.slice().reverse().map((log, i) => (
            <div key={i} className={`flex items-start gap-3 px-4 py-2 font-mono text-xs border-b border-gray-800/50 ${levelColors[log.level] || ""}`}>
              <span className="w-20 shrink-0 text-gray-600">{new Date(log.timestamp).toLocaleTimeString()}</span>
              <span className="w-14 shrink-0 font-bold">{log.level}</span>
              <span className="text-gray-300 flex-1 truncate">{log.message}</span>
              {log.statusCode && (
                <span className={`w-10 text-right shrink-0 ${log.statusCode >= 500 ? "text-red-400" : log.statusCode >= 400 ? "text-amber-400" : "text-emerald-400"}`}>{log.statusCode}</span>
              )}
              {log.durationMs != null && (
                <span className="w-20 text-right text-gray-600 shrink-0">{log.durationMs.toFixed(1)}ms</span>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
