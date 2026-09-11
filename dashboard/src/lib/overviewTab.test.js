import assert from "node:assert/strict"
import { createServer } from "vite"
import React from "react"
import { renderToStaticMarkup } from "react-dom/server"

const server = await createServer({ server: { middlewareMode: true }, appType: "custom" })
try {
  const { default: OverviewTab } = await server.ssrLoadModule("/src/components/OverviewTab.jsx")
  const markup = renderToStaticMarkup(React.createElement(OverviewTab, { onOpenLogs: () => {} }))
  // Real API-backed information architecture: four metric cards + four sections.
  assert.match(markup, /Requests \/ min/)
  assert.match(markup, /Total Today/)
  assert.match(markup, /Error Rate/)
  assert.match(markup, /Avg Latency/)
  assert.match(markup, /Recent Endpoints — Last 5 min/)
  // Endpoint Groups now occupies the panel beside Traffic; title is exactly "Endpoint Groups" (no "Recent minute").
  assert.match(markup, /<[^>]*>Endpoint Groups<\/[^>]*>/)
  assert.doesNotMatch(markup, /Endpoint Groups — Recent minute/)
  // Error Breakdown is removed from the UI position and not duplicated anywhere.
  assert.doesNotMatch(markup, /Error breakdown/i)
  assert.doesNotMatch(markup, /Network failures \(no HTTP status\)/)
  // Exactly one Endpoint Groups panel (no silent duplication).
  assert.equal(markup.match(/>Endpoint Groups</g)?.length, 1)
  // Traffic panel keeps its calendar-day domain: UTC title, UTC-labelled axis, fixed 00:00→24:00 hour ticks.
  assert.match(markup, /Traffic — Today \(UTC\)/)
  assert.match(markup, /times shown in UTC/)
  assert.match(markup, /<text[^>]*>06:00<\/text>/)
  assert.match(markup, /<text[^>]*>12:00<\/text>/)
  assert.match(markup, /<text[^>]*>18:00<\/text>/)
  assert.match(markup, /UTC windows/)
  // Data-driven, not demo/static: with no fetched data the cards show em-dash placeholders and the loading status,
  // never fabricated sample figures or mock content.
  assert.match(markup, /Loading operations overview…/)
  assert.match(markup, /metric-card__value">—</)
  assert.doesNotMatch(markup, /Demo|Sample data|Mock|Lorem|example\.com|\bfoo\b|123,456/)
  assert.doesNotMatch(markup, /Destination|p95/)
  assert.doesNotMatch(markup, /Prior equivalent window|Exact UTC window|Persisted attempts per server bucket|Average and PostgreSQL p95/)
  const { default: MetricsChart } = await server.ssrLoadModule("/src/components/MetricsChart.jsx")
  const chart = renderToStaticMarkup(React.createElement(MetricsChart, { title: "Latency", samples: [{ from: "2026-01-01T00:00:00Z", seconds: 0.077 }], series: [["seconds", "p95", "blue"]], unit: "s" }))
  assert.match(chart, /p95 0\.077 s/)
  assert.match(chart, /0\.077 s/)
  assert.doesNotMatch(chart, / ms/)
  console.log("PASS: 24/24 overviewTab assertions — Endpoint Groups relocated beside Traffic, no Error Breakdown / no duplicate, Traffic calendar-day domain, no demo/static content, seconds chart precision")
} finally {
  await server.close()
}
