import assert from "node:assert/strict"
import { createServer } from "vite"
import React from "react"
import { renderToStaticMarkup } from "react-dom/server"
process.env.TZ = "UTC" // Deterministic UTC fixture; production labels use browser-local time.

// Focused tests for the calendar-day Traffic axis: fixed 00:00->00:00 bounds, x placement against those
// bounds (not first/last observed), boundary + intermediate tick labels, day rollover, and sparse/empty data.
const server = await createServer({ server: { middlewareMode: true }, appType: "custom" })
try {
  const { default: Chart } = await server.ssrLoadModule("/src/components/MetricsChart.jsx")
  const day = { start: Date.parse("2026-09-11T00:00:00Z"), end: Date.parse("2026-09-12T00:00:00Z") }
  const base = { title: "Traffic — Today (UTC)", series: [["attempts", "Requests", "blue"]], unit: "req", domain: day }
  const render = samples => renderToStaticMarkup(React.createElement(Chart, { ...base, samples }))

  // A single 06:00 bucket sits at 1/4 of the axis (x = 72 + 0.25*(428-72) = 161), NOT centered/at the left edge.
  const one = render([{ from: "2026-09-11T06:00:00Z", attempts: 3 }])
  assert.match(one, /cx="161"/, "06:00 bucket placed against fixed day bounds")
  assert.doesNotMatch(one, /No persisted proxy attempts/, "fixed domain shows a partial day, not the empty message")

  // Two sparse buckets keep absolute placement (10:00 -> x=220.83.., 22:00 -> x=398.16..), independent of each other.
  const sparse = render([
    { from: "2026-09-11T10:00:00Z", attempts: 5 },
    { from: "2026-09-11T22:00:00Z", attempts: 5 },
  ])
  assert.match(sparse, /points="220\./, "first sparse point anchored to 10:00 of the day")
  assert.match(sparse, /398\./, "second sparse point anchored to 22:00 of the day")

  // Tick labels: both boundary 00:00 (with date) plus 06/12/18 present; axis declared UTC.
  assert.match(one, /times shown in the browser time zone/)
  for (const t of ["9\\/11 00:00", "06:00", "12:00", "18:00", "9\\/12 00:00"]) assert.match(one, new RegExp(t), `tick ${t}`)

  // Empty day: still a full-bounds axis with both boundary labels, no fabricated points/polyline/counts.
  const empty = render([])
  assert.match(empty, /9\/11 00:00/)
  assert.match(empty, /9\/12 00:00/)
  assert.doesNotMatch(empty, /polyline|<circle/, "no fabricated traffic on an empty day")

  // Day rollover: a different domain relabels boundaries to that day (12/31 -> 1/1), no leakage from the prior day.
  const nye = renderToStaticMarkup(React.createElement(Chart, { ...base, domain: { start: Date.parse("2026-12-31T00:00:00Z"), end: Date.parse("2027-01-01T00:00:00Z") }, samples: [{ from: "2026-12-31T12:00:00Z", attempts: 1 }] }))
  assert.match(nye, /12\/31 00:00/)
  assert.match(nye, /1\/1 00:00/)
  assert.doesNotMatch(nye, /9\/11/)

  console.log("PASS: fixed calendar-day bounds, x placement, tick labels, rollover, sparse/empty data")
} finally {
  await server.close()
}
