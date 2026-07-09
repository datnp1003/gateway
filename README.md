# 🌉 Gateway

[![Build Status](https://img.shields.io/badge/Build-passing-success?style=flat-square&logo=github-actions&logoColor=white)](#)
[![.NET Version](https://img.shields.io/badge/.NET-10.0-blue?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![React Version](https://img.shields.io/badge/React-19.0-61DAFB?style=flat-square&logo=react&logoColor=white)](https://react.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://opensource.org/licenses/MIT)

## 📖 Overview

**Gateway** is a production-ready, high-performance API Gateway designed for the **English Learning** ecosystem. Built on ASP.NET Core 10 using **YARP (Yet Another Reverse Proxy)**, it functions as a centralized gateway for managing client requests and routing them to 6 backend microservices:

1. **Authentication Service (`auth-service`)**: Handles client authentication, user registration, and JWT token issuance on port `5100`.
2. **Learning Service (`learning-service`)**: Manages core learning curricula, modules, and courses on port `5101`.
3. **AI Service (`ai-service`)**: Leverages generative AI for translation, grammar check, and chat features on port `5102`.
4. **Writing Service (`writing-service`)**: Manages written essays, submissions, and grading pipelines on port `5103`.
5. **Speaking Service (`speaking-service`)**: Manages spoken responses, audio evaluations, and pronunciations on port `5104`.
6. **Gamification Service (`gamification-service`)**: Drives system incentives, points, leaderboards, and accomplishments on port `5105`.

Beyond simple request routing, Gateway acts as the security boundary and observer of the microservice ecosystem, handling CORS policies, JWT validation, rate limiting, request logging, tracing, metrics tracking, and serving an interactive React 19 real-time management dashboard.

---

## 🏛️ Architecture & Data Flow

This project follows the principles of **Clean Architecture**, partitioning concerns across 4 logical layers to guarantee decoupleability, testability, and clarity.

```text
                                  ┌───────────────────────────┐
                                  │   Clients (Mobile / Web)  │
                                  └─────────────┬─────────────┘
                                                │ HTTPS requests
                                                ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ 1. PRESENTATION / HOST LAYER                                                           │
│    Project: src/Gateway                                                                │
│                                                                                        │
│    ┌──────────────────────────────────────────────────────────────────────────────┐    │
│    │ Middleware Pipeline                                                          │    │
│    │  [Exception Handling] ──► [Request Logging] ──► [CORS] ──► [Auth] ──► [Limits]│    │
│    └──────────────────────────────────────┬───────────────────────────────────────┘    │
│                                           │ Evaluated Request                          │
│                    ┌──────────────────────┴──────────────────────┐                     │
│                    ▼                                             ▼                     │
│         ┌─────────────────────┐                       ┌─────────────────────┐          │
│         │   Management API    │                       │ YARP Reverse Proxy  │          │
│         │ (/api/management/*) │                       │  (Reverse Routing)  │          │
│         └──────────┬──────────┘                       └──────────┬──────────┘          │
└────────────────────┼─────────────────────────────────────────────┼─────────────────────┘
                     │ Query Metrics / Logs                        │ Proxy Traffic
                     ▼                                             ▼
┌────────────────────────────────────────────────────────┐   ┌───────────────────────────┐
│ 2. APPLICATION LAYER                                   │   │    Microservice Clusters  │
│    Project: src/Gateway.Application                    │   │                           │
│                                                        │   │  ┌─────────────────────┐  │
│  - App Interfaces, Queries, and DTOs                   │   │  │    /api/auth/*      │──► Port 5100 (Auth Service)
└────────────────────┬───────────────────────────────────┘   │  ├─────────────────────┤  │
                     │ Implements interfaces                 │  │    /api/learning/*  │──► Port 5101 (Learning Service)
                     ▼                                       │  ├─────────────────────┤  │
┌────────────────────────────────────────────────────────┐   │  │    /api/ai/*        │──► Port 5102 (AI Service)
│ 3. INFRASTRUCTURE LAYER                                │   │  ├─────────────────────┤  │
│    Project: src/Gateway.Infrastructure                 │   │  │    /api/writing/*   │──► Port 5103 (Writing Service)
│                                                        │   │  ├─────────────────────┤  │
│  - LogBuffer (ILogBuffer implementation)               │   │  │    /api/speaking/*  │──► Port 5104 (Speaking Service)
│  - MetricsTracker (IMetricsTracker implementation)     │   │  ├─────────────────────┤  │
│  - Serilog Sinks & OpenTelemetry Trace Providers       │   │  │    /api/gamification│──► Port 5105 (Gamification Service)
└────────────────────┬───────────────────────────────────┘   │  └─────────────────────┘  │
                     │ Uses abstractions                     └───────────────────────────┘
                     ▼
┌────────────────────────────────────────────────────────┐
│ 4. DOMAIN LAYER                                        │
│    Project: src/Gateway.Domain                         │
│                                                        │
│  - Abstractions: ILogBuffer, IMetricsTracker             │
│  - Entities: RouteInfo, ClusterInfo, DashboardData     │
└────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start

Get the gateway and dashboard up and running in minutes.

### 📋 Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js v18+](https://nodejs.org/) & `npm`
- [Docker & Docker Compose](https://www.docker.com/) (optional, for full containerized setup)

### ⚙️ Step-by-Step Instructions

1. **Clone the repository:**
   ```bash
   git clone https://github.com/your-username/gateway.git
   cd gateway
   ```

2. **Restore and Build the Solution:**
   Use the .NET CLI to compile all C# projects configured inside [Gateway.sln](file:///home/datnguyen/gateway/Gateway.sln):
   ```bash
   dotnet build Gateway.sln
   ```

3. **Install Dashboard Dependencies:**
   Install Node packages required by the Vite-based React management application inside the [dashboard/](file:///home/datnguyen/gateway/dashboard) directory:
   ```bash
   cd dashboard
   npm install
   cd ..
   ```

4. **Run the gateway natively:**
   Launch the proxy server in development mode.
   ```bash
   dotnet run --project src/Gateway/Gateway.csproj
   ```
   The gateway exposes a secure HTTPS endpoint at `https://localhost:7198` and an HTTP endpoint at `http://localhost:5075` as configured in [launchSettings.json](file:///home/datnguyen/gateway/src/Gateway/Properties/launchSettings.json).

5. **Open the Dashboard:**
   - **Development Dashboard:** Start the Vite dev server inside [dashboard/](file:///home/datnguyen/gateway/dashboard):
     ```bash
     cd dashboard
     npm run dev
     ```
     Open `http://localhost:5173` in your browser. The Vite server automatically proxies management API calls to the gateway.
   - **Production Built Dashboard:** Once built, the React dashboard is hosted natively by the gateway. Build it and open `http://localhost:5075` (or the configured gateway URL):
     ```bash
     cd dashboard
     npm run build
     ```

---

## 📂 Project Structure

Below is the directory structure layout for the solution, mapping folders, projects, and key files:

```text
gateway/
├── Gateway.sln                     # [Gateway.sln](file:///home/datnguyen/gateway/Gateway.sln) solution configuration
├── Dockerfile                      # Multi-stage [Dockerfile](file:///home/datnguyen/gateway/Dockerfile) for production builds
├── docker-compose.yml              # [docker-compose.yml](file:///home/datnguyen/gateway/docker-compose.yml) orchestrating gateway & microservices
├── dashboard/                      # React Admin Panel UI ([dashboard/](file:///home/datnguyen/gateway/dashboard))
│   ├── public/                     # Static client-side assets
│   ├── src/                        # React source files
│   │   ├── assets/                 # SVGs and styling resources
│   │   ├── components/             # Reusable UI controls and indicators
│   │   ├── hooks/                  # Custom React hooks (e.g. status polling)
│   │   ├── App.jsx                 # Main layout and dashboard root ([App.jsx](file:///home/datnguyen/gateway/dashboard/src/App.jsx))
│   │   ├── index.css               # Main styling rules
│   │   └── main.jsx                # SPA entry bootstrapper ([main.jsx](file:///home/datnguyen/gateway/dashboard/src/main.jsx))
│   ├── vite.config.js              # [vite.config.js](file:///home/datnguyen/gateway/dashboard/vite.config.js) configuration containing path proxying & build outDir rules
│   └── package.json                # Project dependencies, scripts, Oxlint configurations ([package.json](file:///home/datnguyen/gateway/dashboard/package.json))
├── src/                            # Backend source code
│   ├── Gateway/                    # Host project, pipelines, routing middlewares ([src/Gateway/](file:///home/datnguyen/gateway/src/Gateway))
│   │   ├── Endpoints/              # Management API Minimal endpoints ([ManagementEndpoints.cs](file:///home/datnguyen/gateway/src/Gateway/Endpoints/ManagementEndpoints.cs))
│   │   ├── Middleware/             # Custom exception-handling and logging middlewares
│   │   │   ├── [ExceptionHandlingMiddleware.cs](file:///home/datnguyen/gateway/src/Gateway/Middleware/ExceptionHandlingMiddleware.cs)
│   │   │   └── [RequestLoggingMiddleware.cs](file:///home/datnguyen/gateway/src/Gateway/Middleware/RequestLoggingMiddleware.cs)
│   │   ├── Properties/             # Runtime configurations like [launchSettings.json](file:///home/datnguyen/gateway/src/Gateway/Properties/launchSettings.json)
│   │   ├── Program.cs              # Global bootstrapper configuring services & HTTP request pipelines ([Program.cs](file:///home/datnguyen/gateway/src/Gateway/Program.cs))
│   │   └── appsettings.json        # Main configuration file containing JWT and YARP routes ([appsettings.json](file:///home/datnguyen/gateway/src/Gateway/appsettings.json))
│   ├── Gateway.Domain/             # Pure abstractions, configuration mapping records, interfaces ([src/Gateway.Domain/](file:///home/datnguyen/gateway/src/Gateway.Domain))
│   │   ├── Interfaces/             # Core interfaces
│   │   │   ├── [ILogBuffer.cs](file:///home/datnguyen/gateway/src/Gateway.Domain/Interfaces/ILogBuffer.cs)
│   │   │   └── [IMetricsTracker.cs](file:///home/datnguyen/gateway/src/Gateway.Domain/Interfaces/IMetricsTracker.cs)
│   │   └── Models/                 # Core record models
│   │       ├── [ClusterInfo.cs](file:///home/datnguyen/gateway/src/Gateway.Domain/Models/ClusterInfo.cs) (and DestinationInfo)
│   │       ├── [DashboardData.cs](file:///home/datnguyen/gateway/src/Gateway.Domain/Models/DashboardData.cs)
│   │       └── [RouteInfo.cs](file:///home/datnguyen/gateway/src/Gateway.Domain/Models/RouteInfo.cs)
│   ├── Gateway.Infrastructure/     # Service implementations of domain interfaces ([src/Gateway.Infrastructure/](file:///home/datnguyen/gateway/src/Gateway.Infrastructure))
│   │   └── Services/               # Concrete memory buffer and metrics tracker service implementations
│   │       ├── [LogBuffer.cs](file:///home/datnguyen/gateway/src/Gateway.Infrastructure/Services/LogBuffer.cs)
│   │       └── [MetricsTracker.cs](file:///home/datnguyen/gateway/src/Gateway.Infrastructure/Services/MetricsTracker.cs)
│   └── Gateway.Application/        # Business Logic Core placeholder library ([src/Gateway.Application/](file:///home/datnguyen/gateway/src/Gateway.Application))
└── tests/                          # Automated tests folder
    └── Gateway.Tests/              # xUnit-based integration tests project ([tests/Gateway.Tests/](file:///home/datnguyen/gateway/tests/Gateway.Tests))
        ├── [HealthCheckTests.cs](file:///home/datnguyen/gateway/tests/Gateway.Tests/HealthCheckTests.cs) - Integrations validating health and routing status
        └── [ProxyTests.cs](file:///home/datnguyen/gateway/tests/Gateway.Tests/ProxyTests.cs) - Proxy authentication policy behavior validations
```

---

## ✨ Features

- 🌉 **YARP Reverse Proxy Routing**: Declarative, high-performance HTTP proxy routing using Microsoft YARP. Supports prefix-trimming path transformations, load-balancing, and connection forwarding.
- 🔐 **Global JWT Authentication**: Centralized access checking at the gateway boundary. Validates incoming JWT headers using HMAC-SHA256 tokens and enforces standard authorization policies.
- 🚦 **Dynamic Rate Limiting**: Built-in ASP.NET Core rate limiting middleware executing fixed-window policy rules:
  - `fixed` policy (general APIs): 100 requests per minute with a queue limit of 10.
  - `auth` policy (authentication routes): 20 requests per minute with a queue limit of 5.
- 📂 **Structured Diagnostics Logging (Serilog)**: Integrates Serilog configured with dual outputs (Console and daily-rolling file logs under `/logs/`). Includes custom [RequestLoggingMiddleware](file:///home/datnguyen/gateway/src/Gateway/Middleware/RequestLoggingMiddleware.cs) recording timing metrics and endpoint responses.
- 🔭 **OpenTelemetry Observability**: Telemetry tracing configured via OpenTelemetry ASP.NET Core instrumentation, allowing trace exportation and system tracking.
- 🛠️ **Real-time Management API**: Exposes specialized endpoints under `/api/management/` providing insight into the reverse proxy state, log buffers, and gateway health metrics.
- 💻 **Interactive React Dashboard**: Built on React 19 and Tailwind CSS v4, the dashboard displays metrics, cluster/route lists, and real-time logs fetched straight from memory using custom React components.
- 🧱 **Endpoint Access Policies**: Configure per-endpoint rate limits, blocklisted IPs/CIDRs, and allowlisted IPs/CIDRs from the dashboard.
- 🔎 **Elasticsearch Log Sink**: Optional Serilog sink for long-term searchable logs while the dashboard keeps a small in-memory live log buffer.

---

## 🔌 API Endpoints

The Gateway exposes management endpoints for monitoring proxy health, active routing, and live request statistics.

| Method | Endpoint | Description | Response Model | Auth Required |
|:---|:---|:---|:---|:---|
| `GET` | `/health` | Check overall application health status. | Plain text (`Healthy`) | No |
| `GET` | `/api/management/health` | Retrieves detailed system statistics, uptime, and request metrics. | JSON | No |
| `GET` | `/api/management/routes` | Returns the list of active YARP configured routes, target clusters, and auth policies. | Array of [RouteInfo](file:///home/datnguyen/gateway/src/Gateway.Domain/Models/RouteInfo.cs) | No |
| `GET` | `/api/management/clusters` | Returns the list of destination clusters, and their configured backend addresses. | Array of [ClusterInfo](file:///home/datnguyen/gateway/src/Gateway.Domain/Models/ClusterInfo.cs) | No |
| `GET` | `/api/management/logs` | Fetches recent entries cached in the gateway's rolling memory buffer (max 500 logs). | Array of JSON strings | No |
| `GET` | `/api/management/metrics` | Retrieves gateway request count totals and system performance measurements. | JSON | No |
| `GET` | `/api/management/dashboard` | Consolidated overview designed for the dashboard (includes system metrics, routes, clusters, and recent logs). | [DashboardData](file:///home/datnguyen/gateway/src/Gateway.Domain/Models/DashboardData.cs) | No |

> [!NOTE]
> The management API paths are public to facilitate simple scraping by Prometheus or other external telemetry targets. If deploying to production, secure these endpoints by adding appropriate network constraints or custom security policy rules.

---

## ⚙️ Configuration

The Gateway's settings are configured in [appsettings.json](file:///home/datnguyen/gateway/src/Gateway/appsettings.json).

### 🔑 JWT Configuration
Controls how the gateway validates bearer tokens passed by clients:
```json
"Jwt": {
  "Issuer": "EnglishLearning.Auth",
  "Audience": "EnglishLearning.Gateway",
  "Secret": "PLACEHOLDER_KEY_AT_LEAST_32_CHARS_LONG_CHANGE_IN_ENV"
}
```

### 🌉 Reverse Proxy (YARP) Configuration
Declares the backend services routing setup. Below is an example routing configuration mapping `/api/auth/*` requests to the authentication service cluster:
```json
"ReverseProxy": {
  "Routes": {
    "auth-api": {
      "ClusterId": "auth-cluster",
      "Match": {
        "Path": "/api/auth/{**catch-all}"
      },
      "Transforms": [
        { "PathRemovePrefix": "/api/auth" }
      ]
    }
  },
  "Clusters": {
    "auth-cluster": {
      "Destinations": {
        "auth-service": {
          "Address": "http://localhost:5100/"
        }
      }
    }
  }
}
```

---

## 🛠️ Development

Follow these steps to work on the Gateway and the Management Dashboard simultaneously:

### 1. Run the Gateway Natively
Start the gateway using the .NET CLI. By default, this will run on ports `5075` (HTTP) and `7198` (HTTPS):
```bash
dotnet run --project src/Gateway/Gateway.csproj
```

### 2. Run the React Dashboard Dev Server
The React project uses Vite for Hot Module Replacement (HMR). Go to [dashboard/](file:///home/datnguyen/gateway/dashboard) and run the dev server:
```bash
cd dashboard
npm run dev
```
This runs Vite on `http://localhost:5173`. According to [vite.config.js](file:///home/datnguyen/gateway/dashboard/vite.config.js), any request to `/api` is proxied to `http://localhost:5000` (which is mapped to the Docker Compose host port). During native local development, you can modify `vite.config.js` to point to `http://localhost:5075` to proxy directly to the natively running C# Gateway.

### 3. Deploy/Build the SPA Assets
To compile the dashboard assets and host them directly through the Gateway:
```bash
cd dashboard
npm run build
```
This compiles the files into [src/Gateway/wwwroot](file:///home/datnguyen/gateway/src/Gateway/wwwroot), which is served automatically by the ASP.NET Core pipeline via `app.UseStaticFiles()` (configured in [Program.cs](file:///home/datnguyen/gateway/src/Gateway/Program.cs)).

### 4. Running Automated Tests
Run integration tests located in [tests/Gateway.Tests](file:///home/datnguyen/gateway/tests/Gateway.Tests) using:
```bash
dotnet test
```
These tests utilize `WebApplicationFactory` to spin up the Gateway in-memory, asserting that public routes forward correctly, protected routes return `401 Unauthorized` without a valid token, and the `/health` endpoint is functioning.

---

## 🐳 Docker Deployment

The gateway comes equipped with container configurations for both build and deployment orchestration.

### Dockerfile
The project uses a multi-stage [Dockerfile](file:///home/datnguyen/gateway/Dockerfile) referencing:
- `mcr.microsoft.com/dotnet/sdk:10.0` as the build environment.
- `mcr.microsoft.com/dotnet/aspnet:10.0` as the final ASP.NET runtime, exposing port `8080` (mapped internally to `ASPNETCORE_URLS=http://+:8080`).

### Docker Compose
To build and spin up the gateway along with the associated auth-service, learning-service, and ai-service clusters, use [docker-compose.yml](file:///home/datnguyen/gateway/docker-compose.yml):

```yaml
# Build and run the entire gateway stack in detached mode
docker-compose up -d --build

# Verify all services are up and running
docker-compose ps

# Follow logs from the gateway service container
docker-compose logs -f gateway

# Stop and remove containers
docker-compose down
```

The gateway container will expose port `5000` to the host machine. You can access the gateway APIs or the embedded dashboard at `http://localhost:5000`.

> [!WARNING]
> Before running docker-compose in a staging or production environment, make sure to replace the default JWT secret key in the environment variables (e.g. using `Jwt__Secret`).

---

## 📅 Changelog

### [v2.1.0] - 2026-07-09
#### Added
- 🚦 Per-endpoint access policy fields: `RateLimitPerMinute`, `BlockedIpRanges`, `AllowedIpRanges`
- 🛡️ Runtime endpoint policy middleware: blocklist → allowlist → rate-limit enforcement
- 🌐 IP matching supports exact IPs and IPv4 CIDR ranges; `::1` localhost exact match supported
- 🎛️ Dashboard endpoint policy controls and badges: `RL: N/min`, `Blocklist`, `Whitelist`
- 🔎 Elasticsearch logging enabled via `Elasticsearch:Enabled` + `Elasticsearch:Url`
- 📈 P95 latency metric exposed through management metrics/dashboard APIs
- 🧩 Startup schema patching for existing SQLite DBs when endpoint policy columns are missing

#### Changed
- 🧹 Dashboard/self traffic is excluded from `LogBuffer` and dashboard metrics (`/api/management*`, `/health`, SPA assets)
- 🛠️ Vite dev proxy now targets the native Gateway port `http://localhost:5075`
- 🧾 Serilog keeps Console/File logging and can additionally write to Elasticsearch when enabled

#### Verified
- ✅ `dotnet build Gateway.sln`
- ✅ `dotnet test Gateway.sln` — 11/11 passed
- ✅ `dashboard npm run build`
- ✅ Runtime checks: blocklist returns `403`, whitelist miss returns `403`, per-endpoint rate-limit returns `429`, proxy traffic still logs, self-traffic does not inflate metrics/logs

### [v2.0.0] - 2026-07-08
#### Added
- 🗂️ Group → Endpoint hierarchy: `/{group.Path}{endpoint.PathPattern}/{**catch-all}`
- 💾 SQLite persistence with EF Core for dynamic proxy config
- 🔄 YARP InMemoryConfigProvider — routes update runtime, no restart needed
- 🌱 Auto-seed from `appsettings.json` on first run
- ⚡ CRUD REST API: `/api/management/groups`, `/api/management/endpoints`, `/api/management/sync`
- 🎛️ Dashboard: Groups tab + Endpoints tab with modal CRUD, enable/disable toggles
- ✅ 11 integration tests (dynamic CRUD, seed, proxy, auth)
#### Changed
- Group entity now has required `Path` field for URL namespace
- Endpoint `PathPattern` is relative (e.g. `/api/{**catch-all}`), full route auto-combined
- YARP config source: `LoadFromConfig` → `InMemoryConfigProvider`

### [v1.0.0] - 2026-07-08
#### Added
- 🚀 Initial project release with all features.
- 🌉 YARP reverse proxy configurations routing traffic to 6 microservices (`auth`, `learning`, `ai`, `writing`, `speaking`, `gamification`).
- 🔐 JWT validation handler integrating token security at the API gateway layer.
- 🚦 Fixed-window and auth-window Rate Limiting policies.
- 📝 Diagnostic logging via Serilog and tracing instrumentation via OpenTelemetry.
- 🛠️ Management REST API providing routes, clusters, buffer logs, and metrics metadata.
- 💻 Real-time SPA dashboard created with React 19, Tailwind CSS v4, and Vite.
- 🧪 xUnit integration tests validating route authentication and gateway health.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 🗂️ Dynamic Proxy Structure

Gateway uses a **Group → Endpoint** hierarchy persisted in SQLite:

```
gateway.datnp.com/{group-path}/{endpoint-path}/{**catch-all}
```

| Level | Description | Example |
|-------|-------------|---------|
| **Group** | Project/tenant namespace, defines URL prefix | `prot`, `edux` |
| **Endpoint** | Service route within a group | `/api/{**catch-all}`, `/auth/{**catch-all}` |
| **Full Route** | `/{group.Path}{endpoint.PathPattern}` | `/prot/api/users` → `/api/users` (backend) |

### Example

```
Group "prot" path="prot"
├── Endpoint "api"   path="/api/{**catch-all}"   → gateway.datnp.com/prot/api/...
├── Endpoint "auth"  path="/auth/{**catch-all}"  → gateway.datnp.com/prot/auth/...
└── Endpoint "cdn"   path="/cdn/{**catch-all}"    → gateway.datnp.com/prot/cdn/...

Group "edux" path="edux"
├── Endpoint "api"   path="/api/{**catch-all}"   → gateway.datnp.com/edux/api/...
└── Endpoint "cms"   path="/cms/{**catch-all}"    → gateway.datnp.com/edux/cms/...
```

YARP automatically strips `/{group.Path}` prefix so backends receive clean paths.
