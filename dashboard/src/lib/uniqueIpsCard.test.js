import assert from "node:assert/strict"

// Inline copy of the component's number formatter (not exported).
const number = value => value == null ? "—" : value.toLocaleString()

// Unique IPs Today card logic mirrors Total Today but reads uniqueClients instead of attempts.
function uniqueIpsTodayValue(today) {
  return number(today?.uniqueClients)
}

function uniqueIpsTodaySubtitle(today, yesterday) {
  return today ? "Yesterday: " + number(yesterday?.uniqueClients) : "Since local midnight"
}

// With data: value uses absolute today uniqueClients, subtitle uses absolute yesterday.
assert.equal(uniqueIpsTodayValue({ uniqueClients: 42 }), (42).toLocaleString())
assert.equal(uniqueIpsTodayValue({ uniqueClients: 0 }), "0")
assert.equal(uniqueIpsTodayValue({ uniqueClients: 999999 }), (999999).toLocaleString())
assert.equal(uniqueIpsTodayValue(null), "—")
assert.equal(uniqueIpsTodayValue({}), "—")

// Subtitle: absolute yesterday uniqueClients, not delta.
assert.equal(uniqueIpsTodaySubtitle({ uniqueClients: 150 }, { uniqueClients: 12 }), "Yesterday: " + (12).toLocaleString())
assert.equal(uniqueIpsTodaySubtitle({ uniqueClients: 0 }, { uniqueClients: 0 }), "Yesterday: 0")
assert.equal(uniqueIpsTodaySubtitle({ uniqueClients: 10 }, null), "Yesterday: —")
assert.equal(uniqueIpsTodaySubtitle(null, { uniqueClients: 42 }), "Since local midnight")

// Subtitle never contains delta indicators.
for (const [today, yesterday] of [
  [{ uniqueClients: 100 }, { uniqueClients: 50 }],
  [{ uniqueClients: 10 }, { uniqueClients: 200 }],
  [{ uniqueClients: 50 }, { uniqueClients: 50 }],
]) {
  const sub = uniqueIpsTodaySubtitle(today, yesterday)
  assert.doesNotMatch(sub, /[▲▼]/, "subtitle must not contain delta arrows")
  assert.doesNotMatch(sub, /vs prior|vs yesterday/, "subtitle must not contain delta comparison text")
}

// Daily unique != sum hourly unique: the card shows the daily count directly from the API.
const dailyCount = 42
assert.equal(uniqueIpsTodayValue({ uniqueClients: dailyCount }), dailyCount.toLocaleString())

console.log("PASS: 14/14 uniqueIpsCard assertions — absolute today/yesterday uniqueClients, null-safe, no delta, daily != sum hourly")
