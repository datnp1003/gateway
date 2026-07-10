# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Admin authentication** — Google OAuth identity provider for dashboard operators only; proxy routes remain auth pass-through. Login flow: FE GET `/api/auth/challenge` → browser redirect to Google → `/api/auth/google/callback` mints a 60-second one-time code → FE POST `/api/auth/exchange` consumes code and receives a Gateway-issued admin JWT.
- **`AdminTokenService`** — HMAC-SHA256 JWT signed with `Authentication:Jwt:Secret`; configurable TTL via `Authentication:Jwt:AccessTokenMinutes` (default 60 min).
- **`LoginCodeStore`** — in-memory single-use codes bridging the OAuth redirect and token exchange so the JWT never appears in a URL.
- **`ManagementAuth.cs`** extension registering JwtBearer (default scheme), Google OAuth (external cookie scheme, 5-min TTL), and the `ManagementOnly` authorization policy.
- **`DevBypass` mode** — `Authentication:DevBypass=true` (Development only) skips Google entirely; management API accepts any/no token for local iteration.
- **Auth endpoints** under `/api/auth/*` (rate-limited, public): `challenge`, `login`, `google/callback`, `exchange`, `me`, `logout`.
- **Dashboard JWT storage in `sessionStorage`** — token is never written to cookies; FE owns login/denied/expired states.
- **Group-level IP policies** — `BlockedIpRanges` and `AllowedIpRanges` fields on `ProxyGroup` entity; evaluated by `EndpointAccessPolicyMiddleware` before endpoint-level policies.
- **Endpoint-per-cluster YARP mapping** — each `ProxyEndpoint` gets its own `ClusterConfig`; no cross-endpoint load-balancing within a group.
- **Proxy auth pass-through** — `YarpConfigSyncService` generates `RouteConfig` with no `AuthorizationPolicy`; downstream services validate their own tokens.
- **`ManagementAuthTests`** — integration tests covering challenge redirect, exchange flow (dev bypass + real JWT), `/me`, management API 401/403 enforcement.
- **`GroupAccessPolicyTests`** — integration tests covering blocklist → 403, allowlist miss → 403, CIDR matching, per-endpoint rate-limit → 429.
- **`GroupKillSwitchTests`** — integration tests covering group/endpoint enable/disable toggles.
- **SQLite native bundle vulnerability upgrade** — upgraded to a patched version addressing known CVEs.
- **Dashboard updates** — login page, auth state banner, session-expiry handling, and friendly OAuth/access-denied errors.

### Changed
- `appsettings.json` added to `.gitignore`; `appsettings.Development.json` remains tracked as the minimal dev config template.
- JWT configuration key path changed from `Jwt:*` to `Authentication:Jwt:*` (issuer, audience, secret, TTL).
- Management API (`/api/management/**`) now requires `Authorization: Bearer <admin-jwt>`; previously documented as public.

---

## [2.1.0] — 2026-07-09

### Added
- Per-endpoint access policy fields: `RateLimitPerMinute`, `BlockedIpRanges`, `AllowedIpRanges` on `ProxyEndpoint`.
- Runtime `EndpointAccessPolicyMiddleware`: blocklist → allowlist → per-IP rate-limit enforcement; skips `/api/management*`, `/health`, and SPA asset paths.
- IP matching supports exact IPs and IPv4 CIDR ranges; `::1` localhost exact match.
- Dashboard endpoint policy controls and badges: `RL: N/min`, `Blocklist`, `Whitelist`.
- Elasticsearch logging sink enabled via `Elasticsearch:Enabled` and `Elasticsearch:Url`.
- P95 latency metric exposed through `/api/management/metrics` and `/api/management/dashboard`.
- Startup schema patching (`SeedData.EnsureSchemaAsync`) for existing SQLite databases missing endpoint policy columns.

### Changed
- Dashboard/self traffic excluded from `LogBuffer` and metrics (`/api/management*`, `/health`, SPA assets).
- Vite dev proxy target corrected to `http://localhost:5075` (native gateway port).
- Serilog retains Console/File sinks; Elasticsearch sink is additive when enabled.

### Verified
- `dotnet build Gateway.sln` — passes.
- `dotnet test Gateway.sln` — 11/11 passed.
- `dashboard npm run build` — passes.
- Runtime: blocklist → `403`, allowlist miss → `403`, per-endpoint rate-limit → `429`, proxy traffic logs correctly, self-traffic excluded from metrics/logs.

---

## [2.0.0] — 2026-07-08

### Added
- **Group → Endpoint hierarchy**: YARP routes composed as `/{group.Path}{endpoint.PathPattern}/{**catch-all}`.
- **SQLite persistence** with EF Core (`GatewayDbContext`) for dynamic proxy configuration.
- **YARP `InMemoryConfigProvider`**: routes update at runtime without restart.
- **Auto-seed from `appsettings.json`**: on first run with an empty database, routes and clusters from `ReverseProxy` config are imported as a default group and endpoints.
- **CRUD REST API**: `/api/management/groups`, `/api/management/endpoints`, `/api/management/sync`.
- **Dashboard Groups and Endpoints tabs**: modal CRUD forms, enable/disable toggles.
- 11 integration tests covering dynamic CRUD, seed, proxy, and auth scenarios.

### Changed
- `ProxyGroup` entity now has a required `Path` field for the URL namespace prefix.
- `ProxyEndpoint.PathPattern` is relative (e.g. `/api/{**catch-all}`); full route is auto-composed by `YarpConfigSyncService`.
- YARP configuration source changed from `LoadFromConfig` (static `appsettings.json`) to `InMemoryConfigProvider` (database-driven).

---

## [1.0.0] — 2026-07-08

### Added
- Initial release.
- YARP reverse proxy routing traffic to 6 microservices: `auth` (5100), `learning` (5101), `ai` (5102), `writing` (5103), `speaking` (5104), `gamification` (5105).
- Fixed-window rate limiting: `fixed` policy (100 req/min, queue 10) and `auth` policy (20 req/min, queue 5).
- Structured logging via Serilog (Console + daily rolling file) and tracing via OpenTelemetry.
- Management REST API: `/api/management/routes`, `/api/management/clusters`, `/api/management/logs`, `/api/management/metrics`, `/api/management/health`, `/api/management/dashboard`.
- React 19 real-time management dashboard built with Tailwind CSS v4 and Vite.
- `RequestLoggingMiddleware` recording timing and endpoint responses.
- xUnit integration tests validating route authentication and gateway health.

[Unreleased]: https://github.com/datnp1003/gateway/compare/v2.1.0...HEAD
[2.1.0]: https://github.com/datnp1003/gateway/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/datnp1003/gateway/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/datnp1003/gateway/releases/tag/v1.0.0
