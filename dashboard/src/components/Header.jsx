import { LogOut } from "lucide-react"

/**
 * F5: Header – displays current user and a logout button.
 * onLogout: calls clearAuth() + optional server logout (handled in useAuth),
 *   then shows login screen (no hard page reload needed).
 */
export default function Header({ title, user, onLogout }) {
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
      <h1 className="text-lg font-semibold tracking-tight">{title}</h1>
      <div className="flex items-center gap-3">
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
              aria-label="Sign out"
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
