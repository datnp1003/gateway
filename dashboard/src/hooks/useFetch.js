import { useState, useEffect } from "react"

export default function useFetch(url, interval = 0) {
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!url) {
      setLoading(false)
      return
    }

    let cancelled = false
    const fetchData = async () => {
      try {
        const res = await fetch(url)
        if (!res.ok) throw new Error(res.statusText)
        const json = await res.json()
        if (!cancelled) { setData(json); setError(null) }
      } catch (err) {
        if (!cancelled) setError(err.message)
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    fetchData()
    const timer = interval > 0 ? setInterval(fetchData, interval) : null
    return () => { cancelled = true; if (timer) clearInterval(timer) }
  }, [url, interval])

  return { data, error, loading }
}
