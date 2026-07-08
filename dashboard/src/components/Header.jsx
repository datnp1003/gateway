import { useEffect, useState } from "react"

export default function Header({ title }) {
  const [uptime, setUptime] = useState("")

  useEffect(() => {
    const fetchUptime = async () => {
      try {
        const res = await fetch("/api/management/health")
        const data = await res.json()
        const match = data.uptime.match(/^(\d+)/)
        if (match) {
          const totalMs = parseInt(match[1]) / 10000
          const h = Math.floor(totalMs / 3600000)
          const m = Math.floor((totalMs % 3600000) / 60000)
          setUptime(`${h}h ${m}m`)
        }
      } catch { setUptime("—") }
    }
    fetchUptime()
    const interval = setInterval(fetchUptime, 5000)
    return () => clearInterval(interval)
  }, [])

  return (
    <header className="bg-gray-900 border-b border-gray-800 px-6 py-3 flex items-center justify-between">
      <h2 className="text-sm font-semibold text-gray-300 uppercase tracking-wide">{title}</h2>
      <span className="text-xs text-gray-500">Uptime: {uptime}</span>
    </header>
  )
}
