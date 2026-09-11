import assert from "node:assert/strict"
import { createServer } from "vite"
import React from "react"
import { renderToStaticMarkup } from "react-dom/server"

const server = await createServer({ server: { middlewareMode: true }, appType: "custom" })
try {
  const { default: Chart } = await server.ssrLoadModule("/src/components/MetricsChart.jsx")
  const props = { title: "Traffic", series: [["requestsPerSecond", "Throughput", "blue"]], unit: "req/s" }
  const render = samples => renderToStaticMarkup(React.createElement(Chart, { ...props, samples }))
  assert.match(render([]), /No persisted proxy attempts in this period/)
  const markup = render([
    { timestamp: "2026-01-01T00:00:00Z", requestsPerSecond: 2 },
    { timestamp: "2026-01-01T00:00:10Z", requestsPerSecond: 4 },
  ])
  // Non-domain mode: x spans the plot box left→right (left=72 at SSR width 440, right=width-12=428).
  assert.match(markup, /points="72,98 428,28"/)
  assert.match(markup, /aria-label="Traffic: 2 persisted time buckets, req\/s; times shown in the browser time zone"/)
  assert.match(markup, /0\.0 req\/s/)
  assert.match(markup, /times shown in the browser time zone/)
  assert.match(markup, /Throughput 4.00 req\/s/)
  // Single point → centered: 72 + (428-72)/2 = 250.
  assert.match(render([{ timestamp: "2026-01-01T00:00:00Z", requestsPerSecond: 0 }]), /cx="250"/)
  assert.doesNotMatch(render([{ timestamp: "bad", requestsPerSecond: NaN }]), /NaN|polyline/)
  console.log("PASS: 8/8 metricsChart assertions — SVG coordinates, accessible label, units, tooltip, centered single point, empty/invalid data")
} finally {
  await server.close()
}
