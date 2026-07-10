import { useEffect, useState } from "react"
import { Clock, LogOut } from "lucide-react"
import { Badge } from "./ui/Badge"
import { getAuthHeader } from "../lib/auth"

/**
 * F5: Header – displays current user and a logout button.
 * onLogout: calls clearAuth() + optional server logout (handled in useAuth),
 *   then shows login screen (no hard page reload needed).
 */
export default function Header({ title, user, onLogout }) {
  const [uptime,   setUptime]   = useState("")
  const [isOnline, setIsOnline] = useState(true)

  useEffect(() => {
    const fetchUptime = async () => {
      try {
        const res  = await fetch("/api/management/health", {
          headers:     getAuthHeader(),
          credentials: "include",
        })
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

  const handleLogout = async () => {
    // useAuth's logout handles clearAuth + optional server POST
    await onLogout?.()
  }

  const displayName = user?.name || user?.email || null
  const shortEmail  = user?.email
    ? user.email.length > 22 ? user.email.slice(0, 20) + "…" : user.email
    : null

  return (
    <header className="bg-card border-b px-4 md:px-6 py-3 flex items-center justify-between">
      <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground md:ml-0 ml-10">{title}</h2>
      <div className="flex items-center gap-3">
        {/* Uptime */}
        <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Clock className="w-3.5 h-3.5" />
          <span className="hidden sm:inline">Uptime:</span>
          <span className="font-mono">{uptime}</span>
        </div>

        {/* Online badge */}
        <Badge variant={isOnline ? "success" : "destructive"} className="text-[10px] gap-1">
          <span className={`inline-block w-1.5 h-1.5 rounded-full ${isOnline ? "bg-emerald-400 animate-pulse" : "bg-red-400"}`} />
          {isOnline ? "Online" : "Offline"}
        </Badge>

        {/* User + logout */}
        {user && (
          <div className="flex items-center gap-2 border-l border-border pl-3 ml-1">
            <span className="text-xs text-muted-foreground hidden sm:block" title={user.email}>
              {displayName ?? shortEmail}
            </span>
            <button
              id="btn-header-logout"
              onClick={handleLogout}
              title="Sign out"
              className="flex items-center gap-1 px-2 py-1 rounded text-xs text-muted-foreground
                         hover:text-foreground hover:bg-secondary transition-colors duration-150"
            >
              <LogOut className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Sign out</span>
            </button>
          </div>
        )}
      </div>
    </header>
  )
}
