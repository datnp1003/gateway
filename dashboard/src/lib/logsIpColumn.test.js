import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { dirname, join } from "node:path"

const dir = dirname(fileURLToPath(import.meta.url))
const logsSrc = readFileSync(join(dir, "..", "components", "LogsTab.jsx"), "utf8")

// Client IP column header present in table
assert.ok(logsSrc.includes(">Client IP<"), "LogsTab must contain a 'Client IP' table header")

// clientIp rendered with em-dash fallback for missing values
assert.ok(logsSrc.includes('event.clientIp ?? "—"'), "LogsTab must render clientIp with em-dash fallback when absent")

// colSpan updated from 6 to 7 for the empty-state row
assert.ok(logsSrc.includes("colSpan={7}"), "LogsTab empty-state row colSpan must be 7 (was 6)")

// Existing columns preserved: Date/time, Inbound path, Endpoint/group, Outcome, HTTP status, Duration
assert.ok(logsSrc.includes(">Date / time (local)<"), "Date/time column must be preserved")
assert.ok(logsSrc.includes(">Inbound path<"), "Inbound path column must be preserved")
assert.ok(logsSrc.includes(">Endpoint / group<"), "Endpoint/group column must be preserved")
assert.ok(logsSrc.includes(">Outcome<"), "Outcome column must be preserved")
assert.ok(logsSrc.includes(">HTTP status<"), "HTTP status column must be preserved")
assert.ok(logsSrc.includes(">Duration<"), "Duration column must be preserved")

// IP appears after Outcome, before HTTP status (column ordering)
const outcomePos = logsSrc.indexOf(">Outcome<")
const ipPos = logsSrc.indexOf(">Client IP<")
const statusPos = logsSrc.indexOf(">HTTP status<")
assert.ok(outcomePos < ipPos && ipPos < statusPos, "Client IP column must appear between Outcome and HTTP status")

console.log("PASS: 10/10 logsIpColumn assertions — Client IP column added, em-dash fallback, colSpan=7, all existing columns and ordering preserved")
