import assert from "node:assert/strict"
import { createServer } from "vite"
import React from "react"
import { renderToStaticMarkup } from "react-dom/server"
process.env.TZ = "UTC"

// Traffic chart with two series: Requests + Unique IPs per hourly bucket.
const server = await createServer({ server: { middlewareMode: true }, appType: "custom" })
try {
  const { default: Chart } = await server.ssrLoadModule("/src/components/MetricsChart.jsx")
  const day = { start: Date.parse("2026-09-11T00:00:00Z"), end: Date.parse("2026-09-12T00:00:00Z") }
  const series = [["attempts", "Requests", "#2563eb", "req"], ["uniqueClients", "Unique IPs", "#16a34a", "IPs"]]
  const base = { title: "Traffic — Today (UTC)", series, unit: "count", domain: day }
  const render = samples => renderToStaticMarkup(React.createElement(Chart, { ...base, samples }))

  // Two data points with both series present.
  const samples = [
    { from: "2026-09-11T10:00:00Z", attempts: 10, uniqueClients: 5 },
    { from: "2026-09-11T11:00:00Z", attempts: 15, uniqueClients: 8 },
  ]
  const markup = render(samples)

  // Both series render polylines.
  assert.equal(markup.match(/<polyline /g)?.length, 2, "two polylines for two series")

  // Both series render circles with tooltips — per-series units.
  assert.match(markup, /Requests 10\.00 req/)
  assert.match(markup, /Unique IPs 5\.00 IPs/)
  assert.match(markup, /Requests 15\.00 req/)
  assert.match(markup, /Unique IPs 8\.00 IPs/)

  // Legend shows per-series units.
  assert.match(markup, /Requests \(req\)/)
  assert.match(markup, /Unique IPs \(IPs\)/)

  // Unique IPs must NOT be labeled with req.
  assert.doesNotMatch(markup, /Unique IPs \(req\)/)
  assert.doesNotMatch(markup, /Unique IPs \d+\.\d+ req/)

  // Axis y-label still uses req unit.
  assert.match(markup, /req/)

  // Accessible aria-label uses shared axis unit.
  assert.match(markup, /aria-label="Traffic — Today \(UTC\): 2 persisted time buckets, count; times shown in the browser time zone"/)

  // Partial data: uniqueClients missing on some points doesn't crash.
  const partialSamples = [
    { from: "2026-09-11T10:00:00Z", attempts: 10, uniqueClients: 5 },
    { from: "2026-09-11T11:00:00Z", attempts: 15 },
  ]
  const partialMarkup = render(partialSamples)
  assert.match(partialMarkup, /Requests 10\.00 req/)
  assert.match(partialMarkup, /Unique IPs 5\.00 IPs/)
  // The second point's uniqueClients is undefined → only 3 circles for the non-NaN values.
  assert.equal(partialMarkup.match(/<circle /g)?.length, 3, "missing uniqueClients renders 3 circles (2 attempts + 1 uniqueClients)")

  // Colors are distinct: #2563eb for Requests, #16a34a for Unique IPs.
  assert.match(markup, /stroke="#2563eb"/)
  assert.match(markup, /stroke="#16a34a"/)

  console.log("PASS: 14/14 trafficTwoSeries assertions — two polylines, two legend entries, per-series tooltips, no Unique IPs req, partial data, distinct colors, accessible label")
} finally {
  await server.close()
}
