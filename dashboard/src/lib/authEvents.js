/**
 * Lightweight session-expired bus.
 *
 * Any management fetch that receives 401 or 403 calls signalSessionExpired().
 * useAuth subscribes on mount and transitions the whole React app to
 * logged-out state when it fires — no polling, no refresh tokens.
 *
 * We use a plain custom DOM event so the signal works across any hook
 * or component without React context threading.
 */

const SESSION_EXPIRED_EVENT = "gateway:session-expired"

/**
 * Fire the session-expired signal.
 * Called by useFetch / useFetchWithRefetch on any management API 401/403.
 */
export function signalSessionExpired() {
  window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT))
}

/**
 * Subscribe to the session-expired signal.
 * Returns an unsubscribe function suitable for useEffect cleanup.
 *
 * @param {() => void} handler
 * @returns {() => void} cleanup
 */
export function onSessionExpired(handler) {
  window.addEventListener(SESSION_EXPIRED_EVENT, handler)
  return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler)
}
