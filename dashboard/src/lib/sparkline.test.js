import assert from "node:assert/strict"
import { createServer } from "vite"
import React from "react"
import { renderToStaticMarkup } from "react-dom/server"

const server = await createServer({ server: { middlewareMode: true }, appType: "custom" })
try {
  const { default: Sparkline } = await server.ssrLoadModule("/src/components/Sparkline.jsx")
  // 1h UTC domain (60 one-minute buckets); render is domain-anchored, not first/last-observed.
  const domain = { start: Date.parse("2026-01-01T00:00:00Z"), end: Date.parse("2026-01-01T01:00:00Z") }
  const render = buckets => renderToStaticMarkup(React.createElement(Sparkline, { buckets, domain, label: "orders — last hour" }))

  // Empty group → honest empty state, no SVG, no fabricated bars.
  const empty = render([])
  assert.match(empty, /No attempts in the last hour/)
  assert.doesNotMatch(empty, /<svg|<rect/)
  // All-zero buckets are also empty (no observed attempts): honest, not a flat fake line.
  assert.match(render([{ from: "2026-01-01T00:00:00Z", attempts: 0 }]), /No attempts in the last hour/)

  const markup = render([
    { from: "2026-01-01T00:00:00Z", attempts: 2 },
    { from: "2026-01-01T00:30:00Z", attempts: 4 },
  ])
  // Accessible label states totals, bucket count, window and peak; a <title> mirrors it.
  assert.match(markup, /aria-label="orders — last hour: 6 req across 2 one-minute buckets in the last hour \(UTC\), peak 4 per minute"/)
  assert.match(markup, /role="img"/)
  assert.match(markup, /<title>orders — last hour: 6 req last hour, peak 4\/min<\/title>/)
  // Two bars, coordinate-mapped by domain: first at x=start (PAD=2), peak bar full height (H-2*PAD = 32).
  assert.equal(markup.match(/<rect /g).length, 2)
  assert.match(markup, /x="2"/)
  assert.match(markup, /height="32"/)
  // Mobile-safe: scales to container, no fixed pixel width that could overflow a narrow panel.
  assert.match(markup, /viewBox="0 0 240 36"/)
  assert.match(markup, /preserveAspectRatio="none"/)
  assert.doesNotMatch(markup, /width="240px"/)
  // Sparse/invalid points are dropped, never coerced to NaN coordinates.
  assert.doesNotMatch(render([{ from: "bad", attempts: NaN }, { from: "2026-01-01T00:10:00Z", attempts: 3 }]), /NaN/)
  console.log("PASS: 13/13 sparkline assertions — 1h UTC domain, empty/zero honest state, accessible label+title, coordinate mapping, mobile-safe scaling, invalid-point drop")
} finally {
  await server.close()
}
