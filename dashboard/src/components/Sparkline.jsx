// ponytail: compact native-SVG sparkline for the fixed 1h minute-bucket window; no chart dep, no ResizeObserver.
// `domain` ({start,end} epoch ms) fixes the x-axis to the API's advertised groupsHourly window so bars align to real UTC minutes.
// Empty/zero-only series render an honest empty state — no fabricated points. viewBox scales to any panel width.
const W = 240, H = 36, PAD = 2
export default function Sparkline({ buckets, domain, label, unit = "req" }) {
  const points = (buckets ?? []).filter(bucket => Number.isFinite(Date.parse(bucket.from)) && Number.isFinite(bucket.attempts))
  const max = Math.max(0, ...points.map(bucket => bucket.attempts))
  const observed = points.reduce((sum, bucket) => sum + bucket.attempts, 0)
  if (!observed) return <p className="group-spark-empty" role="status">No attempts in the last hour.</p>
  const span = domain.end - domain.start || 1
  const bw = Math.max(1, (W - PAD * 2) / points.length)
  const x = ms => PAD + (ms - domain.start) / span * (W - PAD * 2)
  const h = value => value ? Math.max(1, value / max * (H - PAD * 2)) : 0
  return <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="group-spark" role="img"
      aria-label={`${label}: ${observed} ${unit} across ${points.length} one-minute buckets in the last hour (UTC), peak ${max} per minute`}>
    <title>{`${label}: ${observed} ${unit} last hour, peak ${max}/min`}</title>
    {points.map((bucket, index) => { const bx = x(Date.parse(bucket.from)); const bh = h(bucket.attempts)
      return <rect key={index} x={bx} y={H - PAD - bh} width={Math.max(0.5, bw - 0.5)} height={bh} fill="#2563eb">
        <title>{`${bucket.attempts} ${unit}`}</title></rect> })}
  </svg>
}
