import { useEffect, useState } from "react"
import { Clock } from "lucide-react"
import { Badge } from "./ui/Badge"

export default function Header({ title }) {
  const [uptime, setUptime] = useState("")
  const [isOnline, setIsOnline] = useState(true)

  useEffect(() => {
    const fetchUptime = async () => {
      try {
        const res = await fetch("/api/management/health")
        const data = await res.json()
        const match = data.uptime?.match(/^(\d+)/)
        if (match) {
          const totalMs = parseInt(match[1]) / 10000
          const h = Math.floor(totalMs / 3600000)
          const m = Math.floor((totalMs % 3600000) / 60000)
          setUptime(`${h}h ${m}m`)
        }
        setIsOnline(true)
      } catch {
        setUptime("—")
        setIsOnline(false)
      }
    }
    fetchUptime()
    const interval = setInterval(fetchUptime, 5000)
    return () => clearInterval(interval)
  }, [])

  return (
    <header className="bg-card border-b px-4 md:px-6 py-3 flex items-center justify-between">
      <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground md:ml-0 ml-10">{title}</h2>
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Clock className="w-3.5 h-3.5" />
          <span className="hidden sm:inline">Uptime:</span>
          <span className="font-mono">{uptime}</span>
        </div>
        <Badge variant={isOnline ? "success" : "destructive"} className="text-[10px] gap-1">
          <span className={`inline-block w-1.5 h-1.5 rounded-full ${isOnline ? "bg-emerald-400 animate-pulse" : "bg-red-400"}`} />
          {isOnline ? "Online" : "Offline"}
        </Badge>
      </div>
    </header>
  )
}
