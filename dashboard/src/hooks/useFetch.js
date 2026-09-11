import { useState, useEffect, useCallback } from "react"
import { getAuthHeader, clearAuth } from "../lib/auth"
import { signalSessionExpired } from "../lib/authEvents"

/**
 * F4: Data-fetching hook with JWT bearer auth.
 *
 * For /api/management/** URLs, attaches Authorization: Bearer <token>.
 *
 * On 401/403: clears local auth and dispatches the gateway:session-expired
 * event so useAuth can unmount the whole dashboard and show the login screen.
 * No toast spam; no refresh tokens.
 *
 * credentials="include" is kept as harmless but the JWT header is authoritative.
 */
export default function useFetch(url, interval = 0) {
  const [data,    setData]    = useState(null)
  const [error,   setError]   = useState(null)
  const [loading, setLoading] = useState(true)

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
          // Session no longer valid — clear local state and signal the app.
          clearAuth()
          signalSessionExpired()
          return
        }
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

/**
 * Variant of useFetch that exposes a manual refetch trigger and is used by
 * tabs that need to refresh after a mutation (Groups, Endpoints).
 *
 * Same 401/403 behaviour: clears auth + signals session expiry.
 */
export function useFetchWithRefetch(url, interval = 0) {
  const [data,    setData]    = useState(null)
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)
  const [tick,    setTick]    = useState(0)

  const refetch = useCallback(() => setTick(t => t + 1), [])

  useEffect(() => {
    if (!url) { setLoading(false); return }
    let cancelled = false
    const run = async () => {
      try {
        const res = await fetch(url, { headers: getAuthHeader(), credentials: "include" })
        if (res.status === 401 || res.status === 403) {
          clearAuth()
          signalSessionExpired()
          return
        }
        if (!res.ok) throw new Error(res.statusText)
        const json = await res.json()
        if (!cancelled) { setData(json); setError(null) }
      } catch (err) {
        if (!cancelled) setError(err.message || "Unable to load data")
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    run()
    const timer = interval > 0 ? setInterval(run, interval) : null
    return () => { cancelled = true; if (timer) clearInterval(timer) }
  }, [url, interval, tick])

  return { data, loading, error, refetch }
}
