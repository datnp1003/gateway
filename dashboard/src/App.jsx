import { useState } from "react"
import Sidebar from "./components/Sidebar"
import Header from "./components/Header"
import OverviewTab from "./components/OverviewTab"
import RoutesTab from "./components/RoutesTab"
import GroupsTab from "./components/GroupsTab"
import EndpointsTab from "./components/EndpointsTab"
import ClustersTab from "./components/ClustersTab"
import LogsTab from "./components/LogsTab"
import LoginScreen from "./components/LoginScreen"
import { Toaster } from "./components/ui/Toaster"
import useAuth from "./hooks/useAuth"

const tabs = ["Overview", "Routes", "Groups", "API Routes", "Backend Targets", "Logs"]

export default function App() {
  const [activeTab, setActiveTab] = useState("Overview")
  const {
    authenticated,
    loading,
    user,
    accessDenied,
    deniedEmail,
    loginError,
    login,
    logout,
  } = useAuth()

  // F3: Show spinner while exchanging code (brief flash after Google redirect)
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

  // F2/F3: Show login gate if not authenticated (also handles access-denied state)
  if (!authenticated) {
    return (
      <LoginScreen
        accessDenied={accessDenied}
        deniedEmail={deniedEmail}
        loginError={loginError}
        onLogin={login}
        onSignOut={logout}
      />
    )
  }

  return (
    <div className="flex h-screen bg-background text-foreground">
      <Sidebar tabs={tabs} active={activeTab} onSelect={setActiveTab} />
      <div className="flex-1 flex flex-col overflow-hidden">
        <Header title={activeTab} user={user} onLogout={logout} />
        <main className="flex-1 overflow-auto p-4 md:p-6">
          {activeTab === "Overview"        && <OverviewTab />}
          {activeTab === "Routes"          && <RoutesTab />}
          {activeTab === "Groups"          && <GroupsTab />}
          {activeTab === "API Routes"      && <EndpointsTab />}
          {activeTab === "Backend Targets" && <ClustersTab />}
          {activeTab === "Logs"            && <LogsTab />}
        </main>
      </div>
      <Toaster />
    </div>
  )
}
