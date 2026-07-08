export default function Sidebar({ tabs, active, onSelect }) {
  return (
    <aside className="w-56 bg-gray-900 border-r border-gray-800 flex flex-col">
      <div className="p-4 border-b border-gray-800">
        <h1 className="text-lg font-bold text-emerald-400">⚡ Gateway</h1>
        <p className="text-xs text-gray-500">Admin Dashboard</p>
      </div>
      <nav className="flex-1 p-2">
        {tabs.map(tab => (
          <button
            key={tab}
            onClick={() => onSelect(tab)}
            className={`w-full text-left px-3 py-2 rounded text-sm mb-1 transition-colors ${
              active === tab
                ? "bg-emerald-500/10 text-emerald-400 border border-emerald-500/20"
                : "text-gray-400 hover:text-gray-200 hover:bg-gray-800"
            }`}
          >
            {tab}
          </button>
        ))}
      </nav>
      <div className="p-4 border-t border-gray-800 text-xs text-gray-600">
        NET 10 + YARP
      </div>
    </aside>
  )
}
