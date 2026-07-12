/**
 * F1: Centralised auth helpers for the Gateway admin dashboard.
 *
 * Storage keys (sessionStorage – expires when tab/session ends):
 *   gateway.admin.accessToken
 *   gateway.admin.expiresAt
 *   gateway.admin.user       (JSON)
 *
 * Login flow:
 *   1. getChallengeUrl()         – GET /api/auth/challenge → { url }
 *   2. window.location.href = url  – browser follows Google OAuth
 *   3. Gateway redirects back to  /?auth=callback&code=<one-time-code>
 *   4. exchangeCode(code)         – POST /api/auth/exchange → { accessToken, expiresAt, user }
 *   5. storeAuth(...)             – persist in sessionStorage
 *   6. Every management fetch uses getAuthHeader()
 */

const KEYS = {
  token:     "gateway.admin.accessToken",
  expiresAt: "gateway.admin.expiresAt",
  user:      "gateway.admin.user",
}

// ── Storage ───────────────────────────────────────────────────────────────────

/** Returns { accessToken, expiresAt, user } or null if nothing is stored. */
export function getStoredAuth() {
  const token = sessionStorage.getItem(KEYS.token)
  if (!token) return null
  try {
    return {
      accessToken: token,
      expiresAt:   sessionStorage.getItem(KEYS.expiresAt),
      user:        JSON.parse(sessionStorage.getItem(KEYS.user) || "null"),
    }
  } catch {
    return null
  }
}

/** Persist the exchange response to sessionStorage. */
export function storeAuth({ accessToken, expiresAt, user }) {
  sessionStorage.setItem(KEYS.token,     accessToken)
  sessionStorage.setItem(KEYS.expiresAt, expiresAt)
  sessionStorage.setItem(KEYS.user,      JSON.stringify(user))
}

/** Remove all auth data from sessionStorage (logout / token expired). */
export function clearAuth() {
  sessionStorage.removeItem(KEYS.token)
  sessionStorage.removeItem(KEYS.expiresAt)
  sessionStorage.removeItem(KEYS.user)
}

// ── Expiry check ──────────────────────────────────────────────────────────────

/** Returns true if expiresAt (ISO string or null) is in the past or missing. */
export function isExpired(expiresAt) {
  if (!expiresAt) return true
  return Date.parse(expiresAt) <= Date.now()
}

// ── Auth header ───────────────────────────────────────────────────────────────

/**
 * Returns { Authorization: "Bearer <token>" } or {} if no valid token.
 * Clears stale token automatically.
 */
export function getAuthHeader() {
  const stored = getStoredAuth()
  if (!stored) return {}
  if (isExpired(stored.expiresAt)) {
    clearAuth()
    return {}
  }
  return { Authorization: `Bearer ${stored.accessToken}` }
}

// ── API calls ─────────────────────────────────────────────────────────────────

/**
 * GET /api/auth/challenge?returnUrl=<returnUrl>
 * Returns the Google OAuth redirect URL without triggering the redirect itself.
 * Only called after explicit user action.
 */
export async function getChallengeUrl(returnUrl = "/") {
  const params = new URLSearchParams({ returnUrl })
  const res = await fetch(`/api/auth/challenge?${params}`)
  if (!res.ok) throw new Error(`Challenge failed: ${res.status}`)
  const data = await res.json()
  return data.url
}

/**
 * POST /api/auth/exchange  { code }
 * Returns { accessToken, expiresAt, user } on success, throws on failure.
 */
export async function exchangeCode(code) {
  const res = await fetch("/api/auth/exchange", {
    method:  "POST",
    headers: { "Content-Type": "application/json" },
    body:    JSON.stringify({ code }),
  })
  if (!res.ok) {
    const body = await res.text()
    throw new Error(body || `Exchange failed: ${res.status}`)
  }
  return res.json()
}
