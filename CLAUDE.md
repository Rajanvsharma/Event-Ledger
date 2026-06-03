# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~SubmitEvent_Idempotency"

# Run a single test class
dotnet test --filter "ClassName~ResiliencyTests"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Run AccountService locally (port 5001)
cd AccountService && dotnet run

# Run EventGateway locally (port 5000) — start AccountService first
cd EventGateway && dotnet run

# Run both via Docker
docker-compose up --build
```

## Architecture

Two independently-deployable ASP.NET Core 9 services, each with its own SQLite database. They share no in-process state.

```
Client → EventGateway (5000) → AccountService (5001)
          event-gateway.db        account-service.db
```

**Request flow for `POST /events`:**
1. `TracingMiddleware` generates/accepts `X-Trace-Id` and stores it in `HttpContext.Items["TraceId"]`
2. `EventsController` checks for duplicate `eventId` in its local DB (idempotency gate)
3. If new: persists the `EventRecord` to `EventDbContext`, then calls `AccountServiceClient`
4. `AccountServiceClient` forwards the transaction with `X-Trace-Id` header via a Polly-wrapped `HttpClient`
5. `AccountsController` checks for duplicate `eventId` again (second idempotency gate) before writing to `AccountDbContext`

**Idempotency is enforced at both layers** — unique index on `EventId` in both DBs, plus an explicit lookup-before-write in both controllers.

**GET endpoints** (`GET /events/{id}`, `GET /events?account=`) only read from the Gateway's local DB and never call AccountService. This is the graceful degradation path.

## Polly policy ordering (EventGateway/Program.cs)

Policies are applied outermost-first. The chain is:

```
retryPolicy (2 retries, exponential backoff)
  └─ circuitBreakerPolicy (opens after 3 failures, holds 15 s)
       └─ primary HTTP handler
```

A single `POST /events` attempt can therefore make up to 3 HTTP calls to AccountService before the circuit opens. After the circuit opens, subsequent calls fail immediately without hitting the handler.

## Test infrastructure

Tests use `WebApplicationFactory<TProgram>` with isolated per-test SQLite databases (temp file, deleted on dispose). No mocks — the AccountService test server is wired directly as the primary HTTP handler for the Gateway's `AccountServiceClient`.

- `GatewayWebFactory` — replaces `EventDbContext` and overrides `AccountServiceClient`'s primary handler
- `AccountWebFactory` — replaces `AccountDbContext` only
- `AlwaysFailingHandler` — returns 503 on every call; used to trigger the Polly circuit breaker in resiliency tests

To wire the two factories together in an integration test, call `_accountFactory.CreateClient()` first (triggers `Server` initialization), then pass `_accountFactory.Server.CreateHandler()` as `GatewayWebFactory.AccountHandler`.

## Configuration

| Key | Default | Where set |
|-----|---------|-----------|
| `DatabasePath` | `event-gateway.db` / `account-service.db` | `appsettings.json`, env var, or Docker volume |
| `AccountService:BaseUrl` | `http://localhost:5001/` | `appsettings.json` or `AccountService__BaseUrl` env var |
| `Urls` | `http://localhost:5000` / `http://localhost:5001` | `appsettings.json` or `ASPNETCORE_URLS` env var |

## Entry points for WebApplicationFactory

Both `Program.cs` files declare a `public partial class` at the bottom (`EventGatewayProgram`, `AccountServiceProgram`) so tests can reference them as the `TProgram` type argument.
