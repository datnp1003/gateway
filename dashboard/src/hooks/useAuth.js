import { useState, useEffect, useCallback } from "react"
import {
  getStoredAuth,
  storeAuth,
  clearAuth,
  isExpired,
  exchangeCode,
  getChallengeUrl,
} from "../lib/auth"
import { onSessionExpired } from "../lib/authEvents"

/**
 * F1 + F3: Auth state hook — JWT-based, session-scoped, no refresh tokens.
 *
 * State machine:
 *   • Page load: check sessionStorage (no network call).
 *     - Valid token   → authenticated = true, show dashboard.
 *     - No / expired  → authenticated = false, show login.
 *
 *   • URL contains ?auth=callback&code=… (after Google OAuth round-trip):
 *     - loading = true while calling /api/auth/exchange.
 *     - On success: storeAuth(), clear URL params, authenticated = true.
 *     - On failure: clearAuth(), loginError set, show login with error.
 *
 *   • URL contains ?auth=error (Google OAuth error / cancelled):
 *     - loginError set with a friendly message, show login.
 *
 *   • URL contains ?auth=denied (allowlist rejection):
 *     - clearAuth(), show access-denied UI.
 *     - The email is NOT read from the URL — it is not trusted/needed here.
 *
 *   • gateway:session-expired event (any management 401/403 while dashboard is
 *     mounted): transitions to sessionExpired = true, shows login screen.
 *
 * Exposes:
 *   { authenticated, loading, user, accessDenied, sessionExpired, loginError,
 *     login, logout }
 *
 * login()  — user-initiated only: fetch challenge URL → window.location.href.
 * logout() — clearAuth() + optional server-side POST, then show login.
 */
export default function useAuth() {
  // ── state ──────────────────────────────────────────────────────────────────
  const [authenticated,  setAuthenticated]  = useState(false)
  const [loading,        setLoading]        = useState(true)
  const [user,           setUser]           = useState(null)
  const [accessDenied,   setAccessDenied]   = useState(false)
  const [sessionExpired, setSessionExpired] = useState(false)
  const [loginError,     setLoginError]     = useState(null)

  // ── helpers ────────────────────────────────────────────────────────────────
  const setLoggedIn = useCallback((authData) => {
    setAuthenticated(true)
    setUser(authData.user)
    setAccessDenied(false)
    setSessionExpired(false)
    setLoginError(null)
  }, [])

  const setLoggedOut = useCallback((opts = {}) => {
    setAuthenticated(false)
    setUser(null)
    setAccessDenied(opts.accessDenied   ?? false)
    setSessionExpired(opts.sessionExpired ?? false)
    setLoginError(opts.loginError       ?? null)
  }, [])

  // ── F3: handle URL params on mount ────────────────────────────────────────
  useEffect(() => {
    const params    = new URLSearchParams(window.location.search)
    const authParam = params.get("auth")

    // Google OAuth error (e.g. user cancelled, provider error)
    if (authParam === "error") {
      clearAuth()
      window.history.replaceState(null, "", window.location.pathname)
      setLoggedOut({ loginError: "Sign-in was cancelled or an error occurred. Please try again." })
      setLoading(false)
      return
    }

    // Allowlist rejection — email is NOT read from URL (no URL dependency)
    if (authParam === "denied") {
      clearAuth()
      window.history.replaceState(null, "", window.location.pathname)
      setLoggedOut({ accessDenied: true })
      setLoading(false)
      return
    }

    // Code exchange after successful Google OAuth round-trip
    if (authParam === "callback") {
      const code = params.get("code")
      if (!code) {
        clearAuth()
        setLoggedOut({ loginError: "Missing exchange code in callback URL." })
        setLoading(false)
        return
      }
      // Clean URL immediately so the code is not re-used on refresh
      window.history.replaceState(null, "", window.location.pathname)
      setLoading(true)
      exchangeCode(code)
        .then((data) => {
          storeAuth(data)
          setLoggedIn(data)
        })
        .catch((err) => {
          clearAuth()
          setLoggedOut({ loginError: err.message || "Token exchange failed." })
        })
        .finally(() => setLoading(false))
      return
    }

    // Normal load — read sessionStorage; no network call
    const stored = getStoredAuth()
    if (stored && !isExpired(stored.expiresAt)) {
      setLoggedIn(stored)
    } else {
      if (stored) clearAuth() // evict expired token
      setLoggedOut()
    }
    setLoading(false)
  }, [setLoggedIn, setLoggedOut])

  // ── Session-expired signal from management API fetches ────────────────────
  // Any useFetch / useFetchWithRefetch that receives 401/403 dispatches the
  // gateway:session-expired event. We listen here to transition the whole
  // React app to the login screen without the dashboard staying mounted.
  useEffect(() => {
    if (!authenticated) return
    const cleanup = onSessionExpired(() => {
      // clearAuth() already called by the hook that fired the event
      setLoggedOut({ sessionExpired: true })
    })
    return cleanup
  }, [authenticated, setLoggedOut])

  // ── login: user-initiated only ────────────────────────────────────────────
  const login = useCallback(async () => {
    try {
      const url = await getChallengeUrl("/")
      window.location.href = url
    } catch (err) {
      setLoginError(err.message || "Could not reach auth service.")
    }
  }, [])

  // ── logout ────────────────────────────────────────────────────────────────
  const logout = useCallback(async () => {
    const stored = getStoredAuth()
    clearAuth()
    try {
      if (stored?.accessToken) {
        await fetch("/api/auth/logout", {
          method:  "POST",
          headers: { Authorization: `Bearer ${stored.accessToken}` },
        })
      }
    } catch {
      // ignore — session is already cleared locally
    }
    setLoggedOut()
  }, [setLoggedOut])

  return {
    authenticated,
    loading,
    user,
    accessDenied,
    sessionExpired,
    loginError,
    login,
    logout,
  }
}
