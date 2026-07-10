# 🌉 Gateway

[![Build Status](https://img.shields.io/badge/Build-passing-success?style=flat-square&logo=github-actions&logoColor=white)](#)
[![.NET Version](https://img.shields.io/badge/.NET-10.0-blue?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![React Version](https://img.shields.io/badge/React-19.0-61DAFB?style=flat-square&logo=react&logoColor=white)](https://react.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://opensource.org/licenses/MIT)

## 📖 Overview

**Gateway** is a production-ready API Gateway for the **English Learning** ecosystem. Built on ASP.NET Core 10 with **YARP (Yet Another Reverse Proxy)**, it dynamically routes client traffic to backend microservices, hosts a React 19 management dashboard, and secures the dashboard surface with Google OAuth + a Gateway-issued admin JWT.

Key design decisions:
- **Proxy traffic is auth pass-through.** Downstream services own authentication and authorization for their own APIs. Gateway strips the group path prefix and forwards `Authorization`/`Cookie` headers untouched.
- **Google OAuth is only for dashboard identity.** The login flow authenticates the person operating the dashboard, not end-users of the proxied services.
- **`/api/management/**` requires a Gateway admin JWT** (Bearer token). The SPA and `/health` are always public.

---

## 🏛️ Architecture & Data Flow

```text
                              ┌───────────────────────────┐
                              │   Clients (Mobile / Web)  │
                              └─────────────┬─────────────┘
                                            │ HTTPS requests
                                            ▼
┌────────────────────────────────────────────────────────────────────────────────┐
│ HOST / PRESENTATION LAYER  (src/Gateway)                                       │
│                                                                                │
│  Middleware Pipeline                                                           │
│  [Exception] → [Request Logging] → [EndpointAccessPolicy] → [CORS]            │
│  → [RateLimiter] → [Auth] → [Authorization]                                   │
│                                                                                │
│       ┌──────────────────────┐          ┌──────────────────────┐              │
│       │  Auth Endpoints      │          │  Management API      │              │
│       │  /api/auth/*         │          │  /api/management/**  │              │
│       │  (rate-limited,      │          │  (Bearer JWT +       │              │
│       │   public challenge)  │          │   rate-limited)      │              │
│       └──────────────────────┘          └──────────────────────┘              │
│                                                                                │
│       ┌──────────────────────┐          ┌──────────────────────┐              │
│       │  SPA (wwwroot)       │          │  YARP Reverse Proxy  │              │
│       │  Always anonymous    │          │  Pass-through auth   │              │
│       └──────────────────────┘          └──────────┬───────────┘              │
└──────────────────────────────────────────────────── │ ───────────────────────┘
                                                      │ Proxy Traffic
                                                      ▼
                               ┌───────────────────────────────────┐
                               │  Backend Microservice Clusters    │
                               │  (downstream services own auth)   │
                               │                                   │
                               │  /{group}/{endpoint}/{**rest}     │
                               │  → strip /{group} prefix          │
                               │  → forward to destination         │
                               └───────────────────────────────────┘
```

### Dashboard Login Flow

```
FE GET /api/auth/challenge
  → returns { url: "/api/auth/login?returnUrl=..." }

FE redirects browser to /api/auth/login
  → server issues Results.Challenge → Google OAuth

Google redirects to /api/auth/google/callback
  → allowlist check → mint one-time code (60s TTL)
  → redirect to SPA with ?auth=callback&code=<code>

FE POST /api/auth/exchange { code }
  → server consumes code → issues Gateway admin JWT
  → FE stores token in sessionStorage (never cookies)

FE attaches Authorization: Bearer <jwt> on every
  /api/management/** request
```

---

## 🚀 Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js v18+](https://nodejs.org/) and `npm`
- [Docker & Docker Compose](https://www.docker.com/) (optional)

### Local Setup

**1. Clone the repository:**
```bash
git clone https://github.com/datnp1003/gateway.git
cd gateway
```

**2. Configure secrets (required for Google OAuth):**

`appsettings.json` is git-ignored and local-only. Create it under `src/Gateway/` with the Authentication and optional Elasticsearch/ReverseProxy sections shown below. Do not copy `appsettings.Development.json`: it contains overrides only, not a complete base configuration.

**3. Build the solution:**
```bash
dotnet build Gateway.sln
```

**4. Install dashboard dependencies:**
```bash
cd dashboard && npm install && cd ..
```

**5. Run the gateway:**
```bash
dotnet run --project src/Gateway/Gateway.csproj
```
Gateway runs at `http://localhost:5075` (HTTP) and `https://localhost:7198` (HTTPS).

**6. Run the dashboard dev server:**
```bash
cd dashboard && npm run dev
```
Open `http://localhost:5173`. The Vite dev server proxies `/api` to `http://localhost:5075`.

**7. (Optional) Build the SPA into wwwroot:**
```bash
cd dashboard && npm run build
```
The built SPA is served by the gateway at `http://localhost:5075`.

---

## 📂 Project Structure

```text
gateway/
├── Gateway.sln                        # Solution configuration
├── Dockerfile                         # Multi-stage production build
├── docker-compose.yml                 # Orchestration for gateway + microservices
├── CHANGELOG.md                       # Project changelog
├── dashboard/                         # React 19 management SPA
│   ├── src/
│   │   ├── components/                # Reusable UI controls
│   │   ├── hooks/                     # Custom React hooks
│   │   ├── App.jsx                    # Dashboard root; manages auth state
│   │   ├── index.css                  # Styles (Tailwind CSS v4)
│   │   └── main.jsx                   # SPA entry point
│   ├── vite.config.js                 # Proxy /api → localhost:5075; outDir → wwwroot
│   └── package.json                   # Dependencies and scripts
├── src/
│   ├── Gateway/                       # Host project: pipeline, routing, middleware
│   │   ├── Authentication/
│   │   │   ├── ManagementAuth.cs      # Google OAuth + JWT wiring + auth endpoints
│   │   │   ├── AdminTokenService.cs   # Gateway admin JWT issuance
│   │   │   └── LoginCodeStore.cs      # In-memory one-time codes (60s TTL)
│   │   ├── Endpoints/
│   │   │   └── ManagementEndpoints.cs # /api/management/** CRUD + monitoring
│   │   ├── Middleware/
│   │   │   ├── EndpointAccessPolicyMiddleware.cs  # IP blocklist/allowlist + per-endpoint RL
│   │   │   ├── ExceptionHandlingMiddleware.cs
│   │   │   └── RequestLoggingMiddleware.cs
│   │   ├── Properties/launchSettings.json
│   │   ├── Program.cs                 # Service registration + middleware pipeline
│   │   ├── appsettings.json           # Local secrets (git-ignored)
│   │   └── appsettings.Development.json  # Tracked; minimal overrides for development
│   ├── Gateway.Domain/                # Entities, interfaces, models (no dependencies)
│   │   ├── Entities/
│   │   │   ├── ProxyGroup.cs          # Group: path, IP policies, endpoints[]
│   │   │   └── ProxyEndpoint.cs       # Endpoint: pathPattern, destination, IP policy, RL
│   │   ├── Interfaces/
│   │   │   ├── ILogBuffer.cs
│   │   │   ├── IMetricsTracker.cs
│   │   │   ├── IProxyConfigRepository.cs
│   │   │   └── IYarpConfigSyncService.cs
│   │   └── Models/                    # DTOs: RouteInfo, ClusterInfo, DashboardData
│   ├── Gateway.Infrastructure/        # EF Core, repositories, services
│   │   ├── Data/
│   │   │   ├── GatewayDbContext.cs    # SQLite EF Core context
│   │   │   └── SeedData.cs            # Schema patching + first-run appsettings seed
│   │   ├── Repositories/
│   │   │   └── ProxyConfigRepository.cs
│   │   └── Services/
│   │       ├── LogBuffer.cs
│   │       ├── MetricsTracker.cs
│   │       └── YarpConfigSyncService.cs  # DB → YARP InMemoryConfigProvider sync
│   └── Gateway.Application/           # Placeholder application layer
└── tests/
    └── Gateway.Tests/                 # xUnit integration tests (WebApplicationFactory)
        ├── DynamicProxyTests.cs
        ├── GroupAccessPolicyTests.cs
        ├── GroupKillSwitchTests.cs
        └── ManagementAuthTests.cs
```

---

## ✨ Features

- 🌉 **YARP Reverse Proxy (dynamic)**: Routes are built from SQLite at startup and updated in-memory on every CRUD operation — no restart needed. One cluster per endpoint; group path is stripped as the forwarding prefix.
- 🔐 **Dashboard Auth (Google OAuth → Admin JWT)**: FE-driven login flow. Google OAuth identifies the dashboard operator only. A one-time code is exchanged for a Gateway-issued JWT stored in sessionStorage; Bearer token is required on all `/api/management/**` calls.
- 🔓 **Proxy Auth Pass-Through**: YARP proxy routes carry no authorization policy. Downstream services validate their own tokens. `Authorization`/`Cookie` headers are forwarded untouched.
- 🛡️ **Per-Endpoint & Per-Group Access Policies**: Runtime IP blocklist, IP allowlist (exact IPs and IPv4 CIDRs; `::1` supported), and per-endpoint rate limits enforced by `EndpointAccessPolicyMiddleware` before YARP routing.
- 🚦 **Rate Limiting Policies**:
  - `management`: 100 req/min per IP (management API)
  - `auth`: 20 req/min per IP (auth endpoints)
  - `proxy`: 500 req/min per IP (proxied traffic, via YARP route metadata)
- 📂 **Structured Logging (Serilog)**: Console + daily rolling file. Optional Elasticsearch sink enabled via `Elasticsearch:Enabled` + `Elasticsearch:Url`. Dashboard traffic (`/api/management*`, `/health`, SPA assets) is excluded from the log buffer and metrics.
- 🔭 **OpenTelemetry Tracing**: ASP.NET Core instrumentation with console exporter.
- 💻 **React 19 Dashboard**: Groups tab, Endpoints tab with modal CRUD, enable/disable toggles, IP policy controls, rate-limit badges. Token stored in sessionStorage; FE owns login, denied, OAuth-error, and expired-session states; no refresh token is issued.
- 📈 **Metrics & Observability**: Total requests, req/sec, error rate, avg/P95 latency, active requests, top routes, status code breakdown, recent log buffer.

---

## 🔐 Authentication Details

### Proxy Traffic

Gateway applies **no authorization policy** to YARP proxy routes. Downstream services are responsible for validating their own bearer tokens or session cookies. Gateway forwards all `Authorization` and `Cookie` headers to destination untouched.

### Dashboard / Management API

Only `/api/management/**` requires authentication. The flow:

| Step | Who | What |
|------|-----|------|
| 1. Challenge | FE | `GET /api/auth/challenge` → returns login URL |
| 2. Login | Browser | `GET /api/auth/login` → `Results.Challenge` → Google |
| 3. Callback | Google → Gateway | `GET /api/auth/google/callback` → email allowlist → one-time code |
| 4. Exchange | FE | `POST /api/auth/exchange { code }` → admin JWT |
| 5. Store | FE | JWT in `sessionStorage` |
| 6. API calls | FE | `Authorization: Bearer <jwt>` on all `/api/management/**` |

The JWT is issued by `AdminTokenService` (HMAC-SHA256, configurable TTL, default 60 min). No refresh token, no server-side revocation for MVP.

**Dev bypass**: Set `Authentication:DevBypass=true` in `appsettings.Development.json`. The management API is effectively open; the FE still goes through the code→JWT flow against a `dev@local` identity.

---

## ⚙️ Configuration

### File Tracking

| File | Tracked in git | Purpose |
|------|---------------|---------|
| `appsettings.json` | **No** (git-ignored) | Local secrets: Google OAuth creds, JWT secret, Elasticsearch |
| `appsettings.Development.json` | **Yes** | Dev overrides: log levels, dev cluster addresses |

> [!IMPORTANT]
> `appsettings.json` is in `.gitignore`. Never commit real secrets. Use environment variables (`Authentication__Google__ClientId`, `Authentication__Jwt__Secret`, etc.) in CI/CD and production.

### Key Configuration Sections

**Google OAuth + Admin JWT** (in `appsettings.json` / environment):
```json
"Authentication": {
  "DevBypass": false,
  "AllowedEmails": "admin@example.com,another@example.com",
  "Google": {
    "ClientId": "<your-google-client-id>",
    "ClientSecret": "<your-google-client-secret>"
  },
  "Jwt": {
    "Issuer": "Gateway.Admin",
    "Audience": "Gateway.Dashboard",
    "Secret": "<min-32-byte-secret>",
    "AccessTokenMinutes": 60
  }
}
```

Google OAuth requires the authorized redirect URI:
```
http://localhost:5075/signin-google      ← dev (native)
https://your-production-domain/signin-google
```

**Elasticsearch** (optional, in `appsettings.json`):
```json
"Elasticsearch": {
  "Enabled": false,
  "Url": "http://localhost:9200",
  "IndexFormat": "gateway-logs-{0:yyyy.MM}"
}
```

**Initial proxy seed** (in `appsettings.json` → `ReverseProxy`):

On first run with an empty database, `SeedData.SeedFromAppSettingsAsync` reads the `ReverseProxy.Routes` and `ReverseProxy.Clusters` sections and creates a default group and endpoints in SQLite. Subsequent runs ignore these sections — the database is authoritative.

Example seed-only snippet:
```json
"ReverseProxy": {
  "Routes": {
    "auth-api": {
      "ClusterId": "auth-cluster",
      "Match": { "Path": "/api/auth/{**catch-all}" },
      "Transforms": [{ "PathRemovePrefix": "/api/auth" }]
    }
  },
  "Clusters": {
    "auth-cluster": {
      "Destinations": {
        "auth-service": { "Address": "http://localhost:5100/" }
      }
    }
  }
}
```

---

## 🗂️ Dynamic Proxy Structure

YARP routes are loaded from SQLite, not from `appsettings.json` at runtime. The data model is a **Group → Endpoint** hierarchy:

```
/{group.Path}{endpoint.PathPattern}/{**catch-all}
```

| Level | Fields | Example |
|-------|--------|---------|
| **Group** | `Path`, `BlockedIpRanges`, `AllowedIpRanges`, `IsEnabled` | path=`prot` |
| **Endpoint** | `PathPattern`, `Destination`, `RequiresAuth`, `RateLimitPerMinute`, `BlockedIpRanges`, `AllowedIpRanges`, `IsEnabled` | pathPattern=`/api/{**catch-all}` |
| **Full YARP Route** | auto-composed at sync | `/prot/api/{**catch-all}` → strips `/prot` → upstream |

### Example Structure

```
Group "prot"  (path="prot")
  ├── Endpoint "api"   (pathPattern="/api/{**catch-all}",   destination="http://localhost:5101/")
  ├── Endpoint "auth"  (pathPattern="/auth/{**catch-all}",  destination="http://localhost:5100/")
  └── Endpoint "cdn"   (pathPattern="/cdn/{**catch-all}",   destination="http://cdn-service/")

Group "edux"  (path="edux")
  ├── Endpoint "api"   (pathPattern="/api/{**catch-all}",   destination="http://localhost:5200/")
  └── Endpoint "cms"   (pathPattern="/cms/{**catch-all}",   destination="http://cms-service/")
```

YARP strips `/{group.Path}` so backends always receive clean paths. Each endpoint gets its own cluster (one-to-one mapping).

### Access Policy Evaluation (per request)

```
EndpointAccessPolicyMiddleware
  ├── Skip: /api/management*, /health, /assets, /@vite, /src, /
  ├── Find matching ProxyEndpoint by path
  ├── Group-level: BlockedIpRanges → 403
  ├── Group-level: AllowedIpRanges (if set, must match) → 403
  ├── Endpoint-level: BlockedIpRanges → 403
  ├── Endpoint-level: AllowedIpRanges (if set, must match) → 403
  └── Endpoint-level: RateLimitPerMinute (sliding window per IP) → 429
```

IP values are comma-separated and support exact IPs and IPv4 CIDR notation. `::1` is matched as localhost exact.

---

## 🔌 Management API Reference

All endpoints under `/api/management/**` require `Authorization: Bearer <admin-jwt>`.

### Monitoring

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Health check (public, plain text `Healthy`) |
| `GET` | `/api/management/health` | Uptime, request metrics (auth required) |
| `GET` | `/api/management/routes` | Active YARP routes |
| `GET` | `/api/management/clusters` | Active YARP clusters |
| `GET` | `/api/management/logs` | Recent log buffer (max 500, param `?count=`) |
| `GET` | `/api/management/metrics` | Request totals, latency, error rate |
| `GET` | `/api/management/metrics/history` | Metric snapshots (max 300, param `?count=`) |
| `GET` | `/api/management/dashboard` | Consolidated dashboard snapshot |

### Groups CRUD

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/management/groups` | List all groups (with endpoints) |
| `GET` | `/api/management/groups/{id}` | Get group by ID |
| `POST` | `/api/management/groups` | Create group |
| `PUT` | `/api/management/groups/{id}` | Update group |
| `DELETE` | `/api/management/groups/{id}` | Delete group |

### Endpoints CRUD

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/management/endpoints` | List endpoints (optional `?groupId=`) |
| `POST` | `/api/management/endpoints` | Create endpoint |
| `PUT` | `/api/management/endpoints/{id}` | Update endpoint |
| `DELETE` | `/api/management/endpoints/{id}` | Delete endpoint |

### Sync

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/management/sync` | Force YARP reload from database |

> [!NOTE]
> YARP syncs automatically on every CRUD mutation. `POST /sync` is a manual override for cases where the database was modified externally.

### Auth Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/auth/challenge` | Returns the login URL |
| `GET` | `/api/auth/login` | Initiates Google OAuth flow |
| `GET` | `/api/auth/google/callback` | OAuth callback; mints one-time code |
| `POST` | `/api/auth/exchange` | Exchanges code for admin JWT |
| `GET` | `/api/auth/me` | Returns current identity from Bearer JWT |
| `POST` | `/api/auth/logout` | No-op (stateless); FE discards token |

---

## 🛠️ Development

### Running Tests

```bash
dotnet test Gateway.sln
```

Tests use `WebApplicationFactory` with an isolated in-memory SQLite database per test class. The suite covers:

- **ManagementAuthTests**: challenge/login redirect, exchange flow (dev bypass + real JWT), `/me` endpoint, management API 401/403 enforcement, `DevBypass` behavior.
- **DynamicProxyTests**: group/endpoint CRUD, YARP sync, seed from appsettings.
- **GroupAccessPolicyTests**: blocklist → 403, allowlist miss → 403, allowlist hit → 200, CIDR matching, per-endpoint rate-limit → 429.
- **GroupKillSwitchTests**: enable/disable toggles for groups and endpoints.

### Dev Bypass Mode

Set in `appsettings.Development.json`:
```json
"Authentication": {
  "DevBypass": true
}
```

The management API skips JWT enforcement. The FE still obtains a JWT via the code flow, but the server accepts any or no token. **Do not enable in production.**

### Vite Proxy

`dashboard/vite.config.js` proxies `/api` to `http://localhost:5075`. If you run the gateway on a different port, update this file.

---

## 🐳 Docker Deployment

```yaml
# Build and run in detached mode
docker-compose up -d --build

# Follow gateway logs
docker-compose logs -f gateway

# Stop
docker-compose down
```

The gateway container exposes port `5000` to the host.

> [!WARNING]
> Set all secrets via environment variables, never hardcode in image layers:
> - `Authentication__Google__ClientId`
> - `Authentication__Google__ClientSecret`
> - `Authentication__Jwt__Secret` (min 32 bytes)
> - `Authentication__AllowedEmails`
> - `Elasticsearch__Url` (if enabled)

---

## 📄 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
