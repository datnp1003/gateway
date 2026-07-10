import { useState } from "react"
import Sidebar from "./components/Sidebar"
import Header from "./components/Header"
import OverviewTab from "./components/OverviewTab"
import RoutesTab from "./components/RoutesTab"
import GroupsTab from "./components/GroupsTab"
import EndpointsTab from "./components/EndpointsTab"
import ClustersTab from "./components/ClustersTab"
import LogsTab from "./components/LogsTab"
import { Toaster } from "./components/ui/Toaster"

const tabs = ["Overview", "Routes", "Groups", "API Routes", "Backend Targets", "Logs"]

export default function App() {
  const [activeTab, setActiveTab] = useState("Overview")

  return (
    <div className="flex h-screen bg-background text-foreground">
      <Sidebar tabs={tabs} active={activeTab} onSelect={setActiveTab} />
      <div className="flex-1 flex flex-col overflow-hidden">
        <Header title={activeTab} />
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
