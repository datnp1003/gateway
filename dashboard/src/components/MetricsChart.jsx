import { useEffect, useRef, useState } from "react"
import { Card, CardContent, CardHeader, CardTitle } from "./ui/Card"

// ponytail: native SVG for the existing bounded history; use a chart library only for interactive zoom.
// `domain` ({start,end} in epoch ms) fixes the x-axis to explicit bounds — the Overview Traffic chart passes
// the calendar-day window (00:00 → 00:00 next day, UTC) so points sit against the whole day, not first/last observed.
export default function MetricsChart({ title, samples, series, unit, domain }) {
  const container = useRef(null)
  const [width, setWidth] = useState(440)
  useEffect(() => {
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(220, entry.contentRect.width)))
    observer.observe(container.current)
    return () => observer.disconnect()
  }, [])
  const left = width < 400 ? 58 : 72
  const right = width - 12
  const timestamp = sample => sample.from ?? sample.timestamp
  const points = (samples ?? []).filter(sample => Number.isFinite(Date.parse(timestamp(sample))))
  const values = points.flatMap(point => series.map(([key]) => point[key]).filter(Number.isFinite))
  const start = domain ? domain.start : points.length ? Date.parse(timestamp(points[0])) : 0
  const end = domain ? domain.end : points.length ? Date.parse(timestamp(points.at(-1))) : 0
  const max = Math.max(unit === "s" ? 0.001 : 1, ...values)
  const xAt = ms => left + (end === start ? (right - left) / 2 : (ms - start) / (end - start) * (right - left))
  const x = point => xAt(Date.parse(timestamp(point)))
  const y = value => 168 - value / max * 140
  const dateTime = value => new Intl.DateTimeFormat(undefined, { month: "numeric", day: "numeric", hour: "numeric", minute: "2-digit" }).format(new Date(value))
  const hhmm = ms => new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(new Date(ms))
  const dayLabel = ms => new Intl.DateTimeFormat(undefined, { month: "numeric", day: "numeric" }).format(new Date(ms))
  const ticks = domain ? [0, .25, .5, .75, 1].map(f => start + f * (end - start)) : null
  return <Card><CardHeader><CardTitle>{title}</CardTitle></CardHeader><CardContent><div ref={container}>
    {!domain && !values.length ? <p role="status" className="h-48 flex items-center text-sm text-muted-foreground">No persisted proxy attempts in this period.</p> : <svg viewBox={`0 0 ${width} 235`} role="img" aria-label={`${title}: ${points.length} persisted time buckets, ${unit}; times shown in the browser time zone`} className="w-full h-[240px]">
      <title>{`${title} — persisted operations buckets`}</title>
      {[0, 0.5, 1].map(f => <g key={f}><line x1={left} x2={right} y1={y(max * f)} y2={y(max * f)} stroke="currentColor" opacity="0.15" /><text x={left - 6} y={y(max * f) + 4} textAnchor="end" fill="currentColor" fontSize="9">{`${(max * f).toFixed(unit === "s" ? 3 : 1)} ${unit}`}</text></g>)}
      {ticks?.map(ms => <line key={`grid${ms}`} x1={xAt(ms)} x2={xAt(ms)} y1={y(max)} y2={y(0)} stroke="currentColor" opacity="0.08" />)}
      {series.map(([key, label, color, seriesUnit]) => { const effectiveUnit = seriesUnit ?? unit; const line = points.filter(point => Number.isFinite(point[key])); return <g key={key}>{line.length > 1 && <polyline fill="none" stroke={color} strokeWidth="2" points={line.map(point => `${x(point)},${y(point[key])}`).join(" ")} />}{line.map((point, index) => <circle key={index} cx={x(point)} cy={y(point[key])} r="2.5" fill={color}><title>{`${new Date(timestamp(point)).toLocaleString()}: ${label} ${point[key].toFixed(effectiveUnit === "s" ? 3 : 2)} ${effectiveUnit}`}</title></circle>)}</g> })}
      {ticks
        ? ticks.map((ms, index) => { const boundary = index === 0 || index === ticks.length - 1; if (width < 400 && !boundary) return null; return <text key={`tick${ms}`} x={xAt(ms)} y="206" textAnchor={index === 0 ? "start" : index === ticks.length - 1 ? "end" : "middle"} fill="currentColor" fontSize="9">{boundary ? `${dayLabel(ms)} ${hhmm(ms)}` : hhmm(ms)}</text> })
        : <><text x={left} y="206" fill="currentColor" fontSize="9">{dateTime(start)}</text><text x={right} y="206" textAnchor="end" fill="currentColor" fontSize="9">{dateTime(end)}</text></>}
    </svg>}
    <div className="flex flex-wrap gap-4 text-xs">{series.map(([key, label, color, seriesUnit]) => <span key={key}><span aria-hidden="true" style={{ color }}>● </span>{label} ({seriesUnit ?? unit})</span>)}</div>
  </div></CardContent></Card>
}
