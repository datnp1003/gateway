import { useState } from "react"
import { LayoutDashboard, Route, FolderTree, Link2, Server, ScrollText, Menu, X } from "lucide-react"

const tabIcons = {
  Overview: LayoutDashboard,
  Routes: Route,
  Groups: FolderTree,
  Endpoints: Link2,
  Clusters: Server,
  Logs: ScrollText,
}

export default function Sidebar({ tabs, active, onSelect }) {
  const [mobileOpen, setMobileOpen] = useState(false)

  const nav = (
    <nav className="flex-1 p-2 space-y-0.5">
      {tabs.map(tab => {
        const Icon = tabIcons[tab] || LayoutDashboard
        return (
          <button
            key={tab}
            onClick={() => { onSelect(tab); setMobileOpen(false) }}
            className={`w-full text-left px-3 py-2 rounded-md text-sm transition-colors flex items-center gap-2.5 cursor-pointer ${
              active === tab
                ? "bg-primary/10 text-primary border border-primary/20"
                : "text-muted-foreground hover:text-foreground hover:bg-muted"
            }`}
          >
            <Icon className="w-4 h-4 shrink-0" />
            <span className="truncate">{tab}</span>
          </button>
        )
      })}
    </nav>
  )

  return (
    <>
      {/* Mobile hamburger */}
      <button
        className="fixed top-3 left-3 z-50 md:hidden p-2 rounded-md bg-card border text-muted-foreground cursor-pointer"
        onClick={() => setMobileOpen(o => !o)}
        aria-label={mobileOpen ? "Close menu" : "Open menu"}
      >
        {mobileOpen ? <X className="w-4 h-4" /> : <Menu className="w-4 h-4" />}
      </button>

      {/* Mobile overlay */}
      {mobileOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/50 md:hidden"
          onClick={() => setMobileOpen(false)}
        />
      )}

      {/* Sidebar */}
      <aside className={`
        fixed md:relative z-40 w-60 h-screen bg-card border-r flex flex-col transition-transform duration-200
        ${mobileOpen ? "translate-x-0" : "-translate-x-full md:translate-x-0"}
      `}>
        <div className="p-4 border-b flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-primary/10 border border-primary/20 flex items-center justify-center">
            <svg className="w-5 h-5 text-primary" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
            </svg>
          </div>
          <div className="min-w-0">
            <h1 className="text-sm font-bold text-primary truncate">Gateway</h1>
            <p className="text-[10px] text-muted-foreground">Admin Dashboard</p>
          </div>
        </div>
        {nav}
        <div className="p-4 border-t text-[10px] text-muted-foreground">
          NET 10 + YARP
        </div>
      </aside>
    </>
  )
}
