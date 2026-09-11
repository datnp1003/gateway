// ponytail: preview the route template, not a YARP matcher; parameters remain visible.
export function routePreview(origin, groupPath, pattern, destination, removePrefix) {
  const path = `/${groupPath.replace(/^\/+/, "")}${pattern}`
  const prefix = removePrefix.trim() || `/${groupPath.replace(/^\/+/, "")}`
  const matches = path.toLowerCase() === prefix.toLowerCase() || path.toLowerCase().startsWith(`${prefix.toLowerCase()}/`)
  const forwarded = matches ? path.slice(prefix.length) || "/" : path
  return { incoming: origin.replace(/\/$/, "") + path, upstream: destination.replace(/\/+$/, "") + forwarded, prefix, matches }
}
