import { useState } from "react"
import Sidebar from "./components/Sidebar"
import Header from "./components/Header"
import OverviewTab from "./components/OverviewTab"
import RoutesTab from "./components/RoutesTab"
import GroupsTab from "./components/GroupsTab"
import EndpointsTab from "./components/EndpointsTab"
import LogsTab from "./components/LogsTab"
import LoginScreen from "./components/LoginScreen"
import { Toaster } from "./components/ui/Toaster"
import useAuth from "./hooks/useAuth"

const tabs = ["Overview", "Routes", "Groups", "API Routes", "Logs"]
const pageTitles = { "API Routes": "Endpoints", Routes: "Active routes" }

export default function App() {
  const [activeTab, setActiveTab] = useState("Overview")
  const [logNavigation, setLogNavigation] = useState(null)
  const {
    authenticated,
    loading,
    user,
    accessDenied,
    sessionExpired,
    loginError,
    login,
    logout,
  } = useAuth()

  // Show spinner while exchanging code (brief flash after Google redirect)
  if (loading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-muted-foreground">Signing you in…</p>
        </div>
      </div>
    )
  }

  // Show login gate if not authenticated (covers access-denied and session-expired states)
  if (!authenticated) {
    return (
      <LoginScreen
        accessDenied={accessDenied}
        sessionExpired={sessionExpired}
        loginError={loginError}
        onLogin={login}
        onSignOut={logout}
      />
    )
  }

  return (
    <div className="app-shell bg-background text-foreground">
      <Sidebar tabs={tabs} active={activeTab} onSelect={tab => { setActiveTab(tab); if (tab !== "Logs") setLogNavigation(null) }} />
      <div className="app-workspace">
        <Header title={pageTitles[activeTab] ?? activeTab} user={user} onLogout={logout} />
        <main id="main-content" tabIndex={-1} className="workspace-content">
          {activeTab === "Overview"        && <OverviewTab onOpenLogs={filters => { setLogNavigation({ ...filters, nonce: Date.now() }); setActiveTab("Logs") }} />}
          {activeTab === "Routes"          && <RoutesTab />}
          {activeTab === "Groups"          && <GroupsTab />}
          {activeTab === "API Routes"      && <EndpointsTab />}
          {activeTab === "Logs"            && <LogsTab navigation={logNavigation} />}
        </main>
      </div>
      <Toaster />
    </div>
  )
}
