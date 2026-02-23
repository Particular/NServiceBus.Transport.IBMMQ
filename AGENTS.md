# NServiceBus.IBMMQ

NServiceBus transport implementation for IBM MQ using the managed .NET client (`IBMMQDotnetClient`).

## Prerequisites

- .NET 10 SDK
- IBM MQ queue manager accessible (default: localhost:1414, queue manager `QM1`)
- For integration/acceptance/transport tests: a running IBM MQ instance (e.g. via Docker)

## Solution

The solution file is at `src/NServiceBus.Transport.IbmMq.slnx`.

## Building

```bash
dotnet build src/NServiceBus.Transport.IbmMq.slnx
```

## Running Tests

### Unit tests (no MQ instance required)

```bash
dotnet test src/NServiceBus.Transport.IbmMq.Tests
```

### Transport tests (requires MQ instance)

```bash
dotnet test src/NServiceBus.Transport.IbmMq.TransportTests
```

### Acceptance tests (requires MQ instance)

```bash
dotnet test src/NServiceBus.Transport.IbmMq.AcceptanceTests
```

### Run all tests

```bash
dotnet test src/NServiceBus.Transport.IbmMq.slnx
```

### Useful dotnet test options

- `--filter "FullyQualifiedName~SomeTest"` — run tests matching a name pattern
- `--filter "TestCategory=SomeCategory"` — run tests in a specific category
- `--configuration Release` — build and run in Release mode
- `--no-build` — skip build (use after a separate `dotnet build`)
- `--verbosity normal` — show individual test results (default is `minimal`)
- `--logger "console;verbosity=detailed"` — detailed console output
- `-- NUnit.NumberOfTestWorkers=1` — run tests sequentially (useful for debugging MQ conflicts)

### MQ connection configuration

Tests connect using the `IbmMq_ConnectionDetails` environment variable:

```
IbmMq_ConnectionDetails=host;user;password[;port]
```

Default when unset: `localhost;admin;passw0rd` (port defaults to `1414`).

Example:

```bash
export IbmMq_ConnectionDetails="myhost;admin;passw0rd;1414"
dotnet test src/NServiceBus.Transport.IbmMq.TransportTests
```

### Running IBM MQ locally with Docker

```bash
docker run -d --name ibm-mq \
  -e LICENSE=accept \
  -e MQ_QMGR_NAME=QM1 \
  -e MQ_ADMIN_PASSWORD=passw0rd \
  -p 1414:1414 -p 9443:9443 \
  icr.io/ibm-messaging/mq:latest
```

### Least-privilege test setup

Some tests require a least-privilege MQ user. After starting the container:

```bash
CONTAINER=$(docker ps -q --filter "ancestor=icr.io/ibm-messaging/mq:latest" | head -1)
extra/setup-leastpriv-tests.sh docker "$CONTAINER"
```

## Performance Tests

The performance test project is a console application (not an NUnit project) at `src/NServiceBus.Transport.IbmMq.PerformanceTests`. It requires a running MQ instance.

### Running

```bash
dotnet run --project src/NServiceBus.Transport.IbmMq.PerformanceTests
```

### Scenarios

Six scenarios are available, selectable via `--scenarios`:

| Scenario | What it measures |
|---|---|
| `send` | Send throughput via `IMessageSession.Send()` |
| `receive` | Receive throughput; pre-fills a queue then measures consumption rate |
| `receiveandsend` | Receive + send inside handler; exercises transactional send path |
| `sendlocal` | Sustained throughput via `context.SendLocal()`; self-sustaining message loop |
| `publish` | Publish throughput via `context.Publish()`; self-sustaining event loop |
| `failure` | Error handler throughput; every message throws, measures error pipeline speed |

### Command-line options

| Option | Default | Description |
|---|---|---|
| `--scenarios <names>` | `all` | Scenarios to run: `send`, `receive`, `receiveandsend`, `failure`, `sendlocal`, `publish`, `all` |
| `--transaction-modes <modes>` | `all` | Transaction modes: `None`, `ReceiveOnly`, `SendsAtomicWithReceive`, `all` |
| `--message-count <n>` | `2500` | Number of messages for throughput tests |
| `--duration-seconds <n>` | `30` | Timeout for time-based tests (receive, sendlocal, publish, failure) |
| `--instance-counts <n...>` | `1 2 4 8` | Number of endpoint instances per scenario |
| `--output <path>` | `(none)` | Path for JSON results file; JSON only written when specified |

Multiple values are space-separated. Examples:

```bash
# Run only send and receive with 5000 messages
dotnet run --project src/NServiceBus.Transport.IbmMq.PerformanceTests -- --scenarios send receive --message-count 5000

# Receive test with ReceiveOnly transaction mode
dotnet run --project src/NServiceBus.Transport.IbmMq.PerformanceTests -- --scenarios receive --transaction-modes ReceiveOnly

# All scenarios with JSON output
dotnet run --project src/NServiceBus.Transport.IbmMq.PerformanceTests -- --output perf-results.json

# Specific instance counts with a 60s timeout
dotnet run --project src/NServiceBus.Transport.IbmMq.PerformanceTests -- --instance-counts 1 4 16 --duration-seconds 60
```

### Output

Results are printed as tables to the console. Throughput scenarios show msg/sec, CPU time, handle count, memory allocations, and GC collections. When `--output` is specified, results are also written as a JSON array for CI consumption.

### Connection

Uses the same `IbmMq_ConnectionDetails` environment variable as the other test projects. Queues are auto-created and purged before each run.

### Benchmark data

Performance results are stored on the `benchmark-data` orphan branch:

- `main/latest.json` and `main/history/{timestamp}_{sha}.json` for baseline tracking
- `branches/{name}/latest.json` for feature branch results

On PRs, results are compared against the `main` baseline. A regression threshold of 10% (configurable) triggers a workflow failure.

## Project Structure

- `src/NServiceBus.Transport.IbmMq/` — transport implementation
- `src/NServiceBus.Transport.IbmMq.Tests/` — unit tests (no MQ needed)
- `src/NServiceBus.Transport.IbmMq.TransportTests/` — NServiceBus transport conformance tests (needs MQ)
- `src/NServiceBus.Transport.IbmMq.AcceptanceTests/` — NServiceBus acceptance tests (needs MQ)
- `src/NServiceBus.Transport.IbmMq.PerformanceTests/` — performance benchmarks (console app)
- `extra/` — scripts and utilities
  - `setup-leastpriv-tests.sh` — sets up least-privilege MQ user for tests
  - `publish-benchmark.sh` — publishes perf results to the `benchmark-data` branch
  - `compare-benchmarks.sh` — compares baseline vs current results, generates markdown table
- `.github/workflows/ci.yml` — main CI workflow (build, tests, perf tests with benchmark tracking)
- `.github/workflows/benchmark-cleanup.yml` — removes stale benchmark data when PRs are closed
- `docs/` — design documents
