import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { dirname, join } from "node:path"

const dir = dirname(fileURLToPath(import.meta.url))
const appSrc = readFileSync(join(dir, "..", "App.jsx"), "utf8")
const sidebarSrc = readFileSync(join(dir, "..", "components", "Sidebar.jsx"), "utf8")

// Backend Targets tab removed from App.jsx tabs array
assert.ok(!appSrc.includes('"Backend Targets"'), "App.jsx must not contain 'Backend Targets' tab")
assert.ok(!appSrc.includes("ClustersTab"), "App.jsx must not import or reference ClustersTab")

// Active routes preserved in App.jsx
assert.ok(appSrc.includes('"Routes"'), "App.jsx must contain 'Routes' tab")
assert.ok(appSrc.includes('"Active routes"'), "App.jsx must map Routes to 'Active routes'")
assert.ok(appSrc.includes("<RoutesTab"), "App.jsx must render RoutesTab")

// Backend Targets removed from Sidebar navigation
assert.ok(!sidebarSrc.includes('"Backend Targets"'), "Sidebar.jsx must not contain 'Backend Targets'")
assert.ok(!sidebarSrc.includes("Backend targets"), "Sidebar.jsx must not label 'Backend targets'")
assert.ok(!sidebarSrc.includes("Server"), "Sidebar.jsx must not import Server icon (was only for Backend Targets)")

// Active routes preserved in Sidebar
assert.ok(sidebarSrc.includes('"Active routes"'), "Sidebar.jsx must contain 'Active routes' label")

console.log("PASS: nav-removal regression (Backend Targets removed, Active routes preserved)")
