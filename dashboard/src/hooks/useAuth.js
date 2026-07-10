import { useState, useEffect, useCallback } from "react"
import {
  getStoredAuth,
  storeAuth,
  clearAuth,
  isExpired,
  exchangeCode,
  getChallengeUrl,
} from "../lib/auth"

/**
 * F1 + F3: Auth state hook – JWT-based, no cookie session.
 *
 * State machine:
 *   • Page load: check sessionStorage only (no network call).
 *     - Valid token   → authenticated = true, show dashboard.
 *     - No / expired  → authenticated = false, show login.
 *   • URL contains ?auth=callback&code=… (after Google OAuth round-trip):
 *     - loading = true while calling /api/auth/exchange.
 *     - On success: storeAuth(), clear URL params, authenticated = true.
 *     - On failure: clearAuth(), loginError set, show login with error.
 *   • URL contains ?auth=denied&email=… (allowlist rejection):
 *     - clearAuth(), show access-denied UI with email.
 *
 * Exposes:
 *   { authenticated, loading, user, accessDenied, deniedEmail, loginError, login, logout }
 *
 * login()  – user-initiated only: fetch challenge URL → window.location.href.
 * logout() – clearAuth() + optional server-side POST + redirect to login.
 */
export default function useAuth() {
  // ── state ──────────────────────────────────────────────────────────────────
  const [authenticated, setAuthenticated] = useState(false)
  const [loading,       setLoading]       = useState(true)
  const [user,          setUser]          = useState(null)
  const [accessDenied,  setAccessDenied]  = useState(false)
  const [deniedEmail,   setDeniedEmail]   = useState(null)
  const [loginError,    setLoginError]    = useState(null)

  // ── helpers ────────────────────────────────────────────────────────────────
  const setLoggedIn = useCallback((authData) => {
    setAuthenticated(true)
    setUser(authData.user)
    setAccessDenied(false)
    setDeniedEmail(null)
    setLoginError(null)
  }, [])

  const setLoggedOut = useCallback((opts = {}) => {
    setAuthenticated(false)
    setUser(null)
    setAccessDenied(opts.accessDenied ?? false)
    setDeniedEmail(opts.deniedEmail   ?? null)
    setLoginError(opts.loginError     ?? null)
  }, [])

  // ── F3: handle URL params on mount ────────────────────────────────────────
  useEffect(() => {
    const params  = new URLSearchParams(window.location.search)
    const authParam = params.get("auth")

    if (authParam === "denied") {
      // Access denied – allowlist rejection
      const email = params.get("email") || null
      clearAuth()
      // Clean URL
      window.history.replaceState(null, "", window.location.pathname)
      setLoggedOut({ accessDenied: true, deniedEmail: email })
      setLoading(false)
      return
    }

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
      // Exchange code for JWT
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

    // Normal load – read sessionStorage
    const stored = getStoredAuth()
    if (stored && !isExpired(stored.expiresAt)) {
      setLoggedIn(stored)
    } else {
      if (stored) clearAuth() // evict expired
      setLoggedOut()
    }
    setLoading(false)
  }, [setLoggedIn, setLoggedOut])

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
      // ignore – session is already cleared locally
    }
    setLoggedOut()
  }, [setLoggedOut])

  return {
    authenticated,
    loading,
    user,
    accessDenied,
    deniedEmail,
    loginError,
    // devBypass kept for compatibility – not needed in JWT flow but harmless
    devBypass: false,
    login,
    logout,
    // refresh kept for API compatibility with App.jsx (calls logout)
    refresh: logout,
  }
}
