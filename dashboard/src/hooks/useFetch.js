import { useState, useEffect } from "react"
import { getAuthHeader, clearAuth } from "../lib/auth"

/**
 * F4: Data-fetching hook with JWT bearer auth.
 *
 * For /api/management/** URLs, attaches Authorization: Bearer <token>.
 * On 401/403: clears local auth (token invalid/expired) and sets authError=true
 *   so callers can redirect to login gracefully without toast spam.
 *
 * credentials="include" is kept as harmless but the JWT header is authoritative.
 */
export default function useFetch(url, interval = 0) {
  const [data,      setData]      = useState(null)
  const [error,     setError]     = useState(null)
  const [loading,   setLoading]   = useState(true)
  const [authError, setAuthError] = useState(false)

  useEffect(() => {
    if (!url) {
      setLoading(false)
      return
    }

    let cancelled = false
    const fetchData = async () => {
      try {
        const headers = url.includes("/api/management/") ? getAuthHeader() : {}
        const res = await fetch(url, { headers, credentials: "include" })
        if (res.status === 401 || res.status === 403) {
          // Token rejected – clear local session and surface authError
          clearAuth()
          if (!cancelled) { setAuthError(true); setData(null); setError(null) }
          return
        }
        if (!res.ok) throw new Error(res.statusText)
        const json = await res.json()
        if (!cancelled) { setData(json); setError(null); setAuthError(false) }
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

  return { data, error, loading, authError }
}
