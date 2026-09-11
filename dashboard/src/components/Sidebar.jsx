import { LayoutDashboard, Route, FolderTree, Link2, Server, ScrollText } from "lucide-react"

const items = {
  Overview: [LayoutDashboard, "Overview"],
  Groups: [FolderTree, "Groups"],
  "API Routes": [Link2, "Endpoints"],
  Routes: [Route, "Active routes"],
  "Backend Targets": [Server, "Backend targets"],
  Logs: [ScrollText, "Logs"],
}

export default function Sidebar({ active, onSelect }) {
  return (
    <aside className="app-sidebar">
      <a href="#main-content" className="skip-link">Skip to content</a>
      <div className="brand"><span className="brand-mark"><Route size={23} /></span><strong>Gateway</strong></div>
      <nav aria-label="Main navigation">
        {Object.entries(items).map(([key, [Icon, label]]) => (
          <button key={key} aria-current={active === key ? "page" : undefined} onClick={() => onSelect(key)}>
            <Icon size={19} aria-hidden="true" /><span>{label}</span>
          </button>
        ))}
      </nav>
    </aside>
  )
}
