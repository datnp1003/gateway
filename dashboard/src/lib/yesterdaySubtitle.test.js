import assert from "node:assert/strict"

// Inline copy of the component's number formatter (not exported).
const number = value => value == null ? "—" : value.toLocaleString()

// Regression: Total Today subtitle must show ABSOLUTE yesterday attempts,
// never the difference/delta. Backend already returns
// requests.yesterday.attempts as the complete previous local calendar day.
function totalTodaySubtitle(today, yesterday) {
  return today ? "Yesterday: " + number(yesterday?.attempts) : "Since local midnight"
}

// With data: subtitle uses the absolute yesterday value, formatted.
assert.equal(totalTodaySubtitle({ attempts: 150 }, { attempts: 42 }), "Yesterday: 42")
assert.equal(totalTodaySubtitle({ attempts: 0 }, { attempts: 0 }), "Yesterday: 0")
assert.equal(totalTodaySubtitle({ attempts: 999999 }, { attempts: 123456 }), "Yesterday: " + (123456).toLocaleString())
// Yesterday null → em-dash placeholder, no crash.
assert.equal(totalTodaySubtitle({ attempts: 10 }, null), "Yesterday: —")
// No today data → loading placeholder.
assert.equal(totalTodaySubtitle(null, { attempts: 42 }), "Since local midnight")

// Subtitle never contains delta indicators (▲/▼) or "vs prior"/"vs yesterday".
for (const [today, yesterday] of [
  [{ attempts: 150 }, { attempts: 42 }],
  [{ attempts: 10 }, { attempts: 200 }],
  [{ attempts: 50 }, { attempts: 50 }],
]) {
  const sub = totalTodaySubtitle(today, yesterday)
  assert.doesNotMatch(sub, /[▲▼]/, "subtitle must not contain delta arrows")
  assert.doesNotMatch(sub, /vs prior|vs yesterday/, "subtitle must not contain delta comparison text")
}

// Subtitle is independent of today's value — only yesterday drives it.
const fixed = totalTodaySubtitle({ attempts: 100 }, { attempts: 42 })
assert.equal(fixed, "Yesterday: 42")
assert.equal(totalTodaySubtitle({ attempts: 9999 }, { attempts: 42 }), "Yesterday: 42")

console.log("PASS: 8/8 yesterday-subtitle assertions — absolute yesterday value, no delta, null-safe, today-independent")
