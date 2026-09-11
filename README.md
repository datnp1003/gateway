# Gateway

ASP.NET Core 10 API gateway built on YARP, PostgreSQL and a React 19 management dashboard.

## What it does

- Loads groups and endpoints from PostgreSQL and refreshes YARP routes after management CRUD changes.
- Passes proxy authentication headers through to downstream services.
- Protects `/api/management/**` with Google OAuth, an email allowlist and a Gateway-issued admin JWT.
- Enforces group/endpoint IP policies, enable switches and endpoint rate limits.
- Persists proxy attempts for the operations dashboard and retains them for 30 days.
- Serves the built dashboard and public `/health` endpoint from the same process.

## Requirements

- .NET 10 SDK
- Node.js 24 and npm
- PostgreSQL 17
- Docker with Compose v2, if using the container workflow

## Configuration

Configuration uses standard ASP.NET Core keys. Keep secrets outside git.

| Environment variable | Required in Production | Purpose |
|---|---:|---|
| `ConnectionStrings__Gateway` | yes | PostgreSQL connection string |
| `Authentication__Jwt__Secret` | yes | Admin JWT signing secret; at least 32 bytes |
| `Authentication__Jwt__Issuer` | no | Defaults to `Gateway.Admin` |
| `Authentication__Jwt__Audience` | no | Defaults to `Gateway.Dashboard` |
| `Authentication__Google__ClientId` | yes | Google OAuth client ID |
| `Authentication__Google__ClientSecret` | yes | Google OAuth client secret |
| `Authentication__AllowedEmails` | yes | Comma-separated dashboard admin emails |
| `Elasticsearch__Enabled` | no | Enables the optional Elasticsearch log sink |
| `Elasticsearch__Url` | when enabled | Elasticsearch endpoint |

Google OAuth callback URL:

```text
http://localhost:5075/signin-google
https://your-gateway-host/signin-google
```

The application applies checked-in EF Core migrations during startup. Use a database role with schema DDL permissions. On an empty database, optional `ReverseProxy` configuration seeds the first group/endpoints; PostgreSQL is authoritative afterward.

## Local development

```bash
npm ci --prefix dashboard
npm run build --prefix dashboard

export ConnectionStrings__Gateway='Host=localhost;Port=5432;Database=gateway;Username=gateway;Password=local-password'
dotnet run --project src/Gateway/Gateway.csproj
```

The launch profile serves HTTP on `http://localhost:5075`. For dashboard hot reload:

```bash
npm run dev --prefix dashboard
```

Vite serves `http://localhost:5173` and proxies `/api` to `http://localhost:5075`.

Development-only auth bypass is controlled by `Authentication:DevBypass=true`. It is ignored outside the Development environment.

## Docker Compose

Create an untracked `.env`:

```dotenv
POSTGRES_PASSWORD=replace-me
JWT_SECRET=replace-with-at-least-32-bytes
GOOGLE_CLIENT_ID=replace-me
GOOGLE_CLIENT_SECRET=replace-me
GATEWAY_ALLOWED_EMAILS=admin@example.com
```

Validate and start:

```bash
docker compose config
docker compose up -d --build
docker compose ps
docker compose logs -f gateway
```

Open `http://localhost:5000`. Stop containers without deleting PostgreSQL data:

```bash
docker compose down
```

The image builds the dashboard from `dashboard/package-lock.json`, publishes the .NET application, copies the generated SPA into `wwwroot`, and listens on container port `8080`. Compose runs only Gateway and PostgreSQL; proxied upstream services are external and are configured through the dashboard.

## Tests

Backend integration tests require an isolated PostgreSQL server. The repository harness creates a temporary PostgreSQL 17 container:

```bash
bash scripts/test-postgres.sh
```

Dashboard checks and production build:

```bash
npm ci --prefix dashboard
node --test dashboard/src/lib/*.test.js
npm run build --prefix dashboard
```

## Runtime endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/health` | public | Process health |
| `GET` | `/api/auth/challenge` | public, rate-limited | Starts dashboard login |
| `POST` | `/api/auth/exchange` | public, rate-limited | Exchanges a one-time code for an admin JWT |
| `GET` | `/api/auth/me` | admin JWT | Current dashboard identity |
| `GET` | `/api/management/operations/overview` | admin JWT | Overview metrics, UTC traffic and top endpoint groups |
| `GET` | `/api/management/operations/summary` | admin JWT | Windowed aggregate and ranking data |
| `GET` | `/api/management/operations/events` | admin JWT | Filtered persisted proxy attempts |
| `GET` | `/api/management/operations/configuration` | admin JWT | Group/endpoint configuration for operations filters |
| `GET` | `/api/management/groups` | admin JWT | Groups with endpoints |
| `POST/PUT/DELETE` | `/api/management/groups[/<id>]` | admin JWT | Group mutations |
| `GET/POST/PUT/DELETE` | `/api/management/endpoints[/<id>]` | admin JWT | Endpoint reads and mutations |
| `POST` | `/api/management/sync` | admin JWT | Force DB-to-YARP synchronization |

Proxy routes are generated from configured group and endpoint paths. Downstream services remain responsible for authenticating proxied requests.

## Project layout

```text
dashboard/                    React management SPA
src/Gateway/                  ASP.NET Core host, auth, middleware and endpoints
src/Gateway.Domain/           Entities, interfaces and models
src/Gateway.Infrastructure/   EF Core, PostgreSQL repository and operations services
tests/Gateway.Tests/          Integration and persistence tests
scripts/test-postgres.sh      Isolated PostgreSQL test harness
Dockerfile                    Dashboard + .NET multi-stage image
docker-compose.yml            Gateway + PostgreSQL local stack
```

## Production notes

- Terminate TLS at a reverse proxy and forward the original scheme/host.
- Back up PostgreSQL before deploying migrations.
- Mount `/app/logs` if file logs must survive container replacement.
- Never bake OAuth, JWT or database credentials into the image.
- Verify `/health`, dashboard login, one real proxy route and authenticated operations endpoints after deployment.

## License

MIT — see [LICENSE](LICENSE).
