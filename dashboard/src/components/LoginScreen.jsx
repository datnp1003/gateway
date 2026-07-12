import { useState } from "react"
import { Shield, ShieldAlert, LogIn, LogOut, Loader2, AlertCircle, Clock } from "lucide-react"

/**
 * F2: Auth gate screen — JWT flow, no refresh tokens.
 *
 * Props:
 *   accessDenied  {bool}         — true when Google identity was rejected by allowlist
 *   sessionExpired {bool}        — true when a management API 401/403 ended the session
 *   loginError    {string|null}  — error from code exchange, challenge call, or ?auth=error
 *   onLogin       {async fn}     — user-initiated login: fetch challenge → redirect
 *   onSignOut     {async fn}     — clears local auth, returns to login page
 *
 * No auto-redirect on load. Login is only triggered by the button.
 */
export default function LoginScreen({
  accessDenied   = false,
  sessionExpired = false,
  loginError     = null,
  onLogin,
  onSignOut,
}) {
  const [signingIn,  setSigningIn]  = useState(false)
  const [signingOut, setSigningOut] = useState(false)

  // ── Session expired (management API returned 401/403) ─────────────────────
  if (sessionExpired) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center p-4">
        <div className="w-full max-w-sm">
          {/* Icon */}
          <div className="flex justify-center mb-6">
            <div className="w-14 h-14 rounded-2xl bg-amber-500/10 border border-amber-500/20 flex items-center justify-center">
              <Clock className="w-7 h-7 text-amber-400" />
            </div>
          </div>

          {/* Card */}
          <div className="bg-card border border-border rounded-xl p-8 shadow-xl shadow-black/40 text-center space-y-4">
            <div>
              <h1 className="text-xl font-bold text-foreground">Session Expired</h1>
              <p className="text-sm text-muted-foreground mt-1.5 leading-relaxed">
                Your session is no longer valid. Please sign in again to continue.
              </p>
            </div>

            <button
              id="btn-sign-in-again"
              onClick={async () => {
                setSigningIn(true)
                try { await onLogin?.() } finally { setSigningIn(false) }
              }}
              disabled={signingIn}
              className="flex items-center justify-center gap-2.5 w-full px-4 py-2.5 rounded-lg
                         bg-primary text-primary-foreground font-medium text-sm
                         hover:opacity-90 active:scale-[0.98] transition-all duration-150 cursor-pointer
                         disabled:opacity-60 disabled:cursor-not-allowed"
            >
              {signingIn
                ? <Loader2 className="w-4 h-4 animate-spin" />
                : <LogIn className="w-4 h-4" />}
              {signingIn ? "Redirecting…" : "Sign in again"}
            </button>
          </div>
        </div>
      </div>
    )
  }

  // ── Access denied ──────────────────────────────────────────────────────────
  if (accessDenied) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center p-4">
        <div className="w-full max-w-sm">
          {/* Icon */}
          <div className="flex justify-center mb-6">
            <div className="w-14 h-14 rounded-2xl bg-destructive/10 border border-destructive/20 flex items-center justify-center">
              <ShieldAlert className="w-7 h-7 text-destructive" />
            </div>
          </div>

          {/* Card */}
          <div className="bg-card border border-border rounded-xl p-8 shadow-xl shadow-black/40 text-center space-y-4">
            <div>
              <h1 className="text-xl font-bold text-foreground">Access Denied</h1>
              <p className="text-sm text-muted-foreground mt-1.5 leading-relaxed">
                Your Google account is not on the allowlist for this dashboard.
              </p>
            </div>

            <p className="text-xs text-muted-foreground leading-relaxed">
              To request access, contact your administrator. You can sign out and try a different account.
            </p>

            {/* Sign out — user-initiated only */}
            <button
              id="btn-sign-out"
              onClick={async () => {
                setSigningOut(true)
                try { await onSignOut?.() } finally { setSigningOut(false) }
              }}
              disabled={signingOut}
              className="flex items-center justify-center gap-2.5 w-full px-4 py-2.5 rounded-lg
                         bg-destructive/10 text-destructive border border-destructive/20 font-medium text-sm
                         hover:bg-destructive/20 active:scale-[0.98] transition-all duration-150 cursor-pointer
                         disabled:opacity-60 disabled:cursor-not-allowed"
            >
              {signingOut
                ? <Loader2 className="w-4 h-4 animate-spin" />
                : <LogOut className="w-4 h-4" />}
              Sign out
            </button>

            {/* Try a different account */}
            <button
              id="btn-try-other-account"
              onClick={async () => {
                setSigningIn(true)
                try { await onLogin?.() } finally { setSigningIn(false) }
              }}
              disabled={signingIn}
              className="flex items-center justify-center gap-2.5 w-full px-4 py-2.5 rounded-lg
                         bg-secondary text-foreground border border-border font-medium text-sm
                         hover:bg-secondary/80 active:scale-[0.98] transition-all duration-150 cursor-pointer
                         disabled:opacity-60 disabled:cursor-not-allowed"
            >
              {signingIn
                ? <Loader2 className="w-4 h-4 animate-spin" />
                : <LogIn className="w-4 h-4" />}
              Try a different account
            </button>
          </div>
        </div>
      </div>
    )
  }

  // ── Normal login gate ──────────────────────────────────────────────────────
  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        {/* Logo / icon */}
        <div className="flex justify-center mb-6">
          <div className="w-14 h-14 rounded-2xl bg-primary/10 border border-primary/20 flex items-center justify-center">
            <Shield className="w-7 h-7 text-primary" />
          </div>
        </div>

        {/* Card */}
        <div className="bg-card border border-border rounded-xl p-8 shadow-xl shadow-black/40 text-center space-y-4">
          <div>
            <h1 className="text-xl font-bold text-foreground">Gateway Management</h1>
            <p className="text-sm text-muted-foreground mt-1.5 leading-relaxed">
              Sign in to access the admin dashboard.
              Proxied API routes remain pass-through and are not affected.
            </p>
          </div>

          {/* Error from code exchange, challenge call, or ?auth=error */}
          {loginError && (
            <div className="flex items-start gap-2.5 rounded-lg bg-destructive/10 border border-destructive/20 px-4 py-3 text-left">
              <AlertCircle className="w-4 h-4 text-destructive mt-0.5 shrink-0" />
              <p className="text-xs text-destructive leading-relaxed">{loginError}</p>
            </div>
          )}

          {/* Sign in — user-initiated only; fetches challenge URL then redirects */}
          <button
            id="btn-sign-in-google"
            onClick={async () => {
              setSigningIn(true)
              // onLogin fetches the challenge URL and sets window.location.href.
              // If it throws (network error) the spinner stops and loginError is
              // set by useAuth, causing a re-render with the error shown above.
              try { await onLogin?.() } finally { setSigningIn(false) }
            }}
            disabled={signingIn}
            className="flex items-center justify-center gap-2.5 w-full px-4 py-2.5 rounded-lg
                       bg-primary text-primary-foreground font-medium text-sm
                       hover:opacity-90 active:scale-[0.98] transition-all duration-150 cursor-pointer
                       disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {signingIn
              ? <Loader2 className="w-4 h-4 animate-spin" />
              : <LogIn className="w-4 h-4" />}
            {signingIn ? "Redirecting…" : "Sign in with Google"}
          </button>

          <p className="text-[11px] text-muted-foreground">
            Only allowlisted Google accounts can sign in.
          </p>
        </div>
      </div>
    </div>
  )
}
