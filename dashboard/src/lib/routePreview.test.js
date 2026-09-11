import assert from "node:assert/strict"
import { routePreview } from "./routePreview.js"
const preview = prefix => routePreview("https://gateway.datnp.com", "9r", "/v1/{**catch-all}", "http://127.0.0.1:20128/", prefix)
assert.equal(preview("").incoming, "https://gateway.datnp.com/9r/v1/{**catch-all}")
assert.equal(preview("").upstream, "http://127.0.0.1:20128/v1/{**catch-all}")
assert.equal(preview("/9r/v1").upstream, "http://127.0.0.1:20128/{**catch-all}")
assert.equal(preview("/9").matches, false)
assert.equal(preview("/v1").upstream, "http://127.0.0.1:20128/9r/v1/{**catch-all}")
assert.equal(preview("/9R").matches, true)
assert.equal(routePreview("https://example.com/", "api", "/", "http://backend/base/", "").upstream, "http://backend/base/")
console.log("PASS: 7 route-preview assertions (default, override, segment boundary, casing, base path)")
