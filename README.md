# Event Ledger

A two-microservice financial transaction system built with ASP.NET Core 9 and C#.

## Architecture

```
Browser / Client ──→  Event Gateway API  ──(REST)──→  Account Service
                       (port 5000)                      (port 5001)
                       SQLite DB                        SQLite DB
```

### Event Gateway API (public-facing, port 5000)

Receives transaction events from clients. Handles idempotency, validates input, persists events to its own SQLite database, and synchronously calls the Account Service to apply each transaction.

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/events` | Submit a transaction event |
| `GET` | `/events/{id}` | Retrieve a single event by ID |
| `GET` | `/events?account={accountId}` | List events for an account (ordered by eventTimestamp) |
| `GET` | `/health` | Health check |

### Account Service (internal, port 5001)

Manages account balances and transaction history. Only called by the Gateway.

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/accounts/{accountId}/transactions` | Apply a transaction |
| `GET` | `/accounts/{accountId}/balance` | Get current balance |
| `GET` | `/accounts/{accountId}` | Account details and recent transactions |
| `GET` | `/health` | Health check |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker + Docker Compose](https://docs.docker.com/get-docker/) (for Docker mode)

## Running with Docker Compose (recommended)

```bash
docker-compose up --build
```

Both services start automatically. The Gateway waits for the Account Service health check to pass before starting.

## Running Manually

**Terminal 1 — Account Service:**
```bash
cd AccountService
dotnet run
# Listens on http://localhost:5001
```

**Terminal 2 — Event Gateway:**
```bash
cd EventGateway
dotnet run
# Listens on http://localhost:5000
```

## Running the Tests

```bash
dotnet test
```

Tests run without any external dependencies — each test spins up in-process `WebApplicationFactory` instances with isolated SQLite databases.

### Test coverage

| Area | Tests |
|------|-------|
| Idempotency | Submitting the same `eventId` twice returns 200 with original event |
| Out-of-order | Events returned in `eventTimestamp` order regardless of arrival order |
| Balance | `CREDIT − DEBIT` computed correctly with out-of-order arrivals |
| Validation | Missing fields, zero/negative amount, unknown type → 400 |
| Circuit breaker | After repeated Account Service failures, subsequent calls fail fast |
| Graceful degradation | `GET /events` endpoints work when Account Service is down |
| Trace propagation | `X-Trace-Id` header echoed back; propagated to Account Service |
| Integration | Full Gateway → Account Service request flow |
| Health checks | Both services report `healthy` with database connectivity |

## Example Request

```bash
curl -X POST http://localhost:5000/events \
  -H "Content-Type: application/json" \
  -H "X-Trace-Id: my-trace-001" \
  -d '{
    "eventId": "evt-001",
    "accountId": "acct-123",
    "type": "CREDIT",
    "amount": 150.00,
    "currency": "USD",
    "eventTimestamp": "2026-05-15T14:02:11Z",
    "metadata": { "source": "mainframe-batch", "batchId": "B-9042" }
  }'
```

## Resiliency Pattern: Circuit Breaker + Retry with Backoff

The Gateway uses **Polly** to wrap calls to the Account Service with two layered policies:

1. **Retry with exponential backoff** — retries up to 2 times on transient HTTP errors (5xx, timeouts), with exponential delay between attempts. Prevents hammering a temporarily slow Account Service.

2. **Circuit breaker** — after 3 consecutive failures, the circuit opens for 15 seconds. During this window, calls to the Account Service fail immediately without making any HTTP requests. After 15 seconds, the circuit enters half-open state and allows one probe request through to test recovery.

**Why this combination?** Retry alone can cause thundering-herd problems and exhaust the thread pool if the Account Service is fully down. The circuit breaker acts as a safety valve: once a failure threshold is crossed, it stops retries entirely and returns a fast error to the client. This protects both the Gateway and the Account Service from cascading overload during an outage.

**Graceful degradation:** `GET /events` and `GET /events/{id}` only read from the Gateway's local SQLite database and never call the Account Service, so they remain available even when the circuit is open.

## Observability

- **Structured JSON logs** — both services emit JSON-formatted logs including `traceId`, `service`, `timestamp`, and `logLevel`.
- **Distributed tracing** — the Gateway generates (or accepts) an `X-Trace-Id` header per request, propagates it to the Account Service via the same header, and returns it in the response. Both services log the trace ID on every relevant operation.
- **OpenTelemetry** — both services are instrumented with `OpenTelemetry.Instrumentation.AspNetCore` and export traces to the console exporter. To connect Jaeger or Zipkin, replace `AddConsoleExporter()` with the appropriate OTLP exporter.
- **Health endpoints** — `GET /health` on both services verifies database connectivity and returns structured JSON status.
