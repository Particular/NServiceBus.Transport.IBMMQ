# Performance Tests

Console application that measures throughput of the IBM MQ transport under various scenarios and concurrency levels.

## Running locally

### Prerequisites

- .NET 10 SDK
- IBM MQ queue manager (see [root README](../../AGENTS.md#running-ibm-mq-locally-with-docker) for Docker setup)

### Connection

Uses the `IBMMQ_CONNECTIONSTRING` environment variable (URI format):

```bash
export IBMMQ_CONNECTIONSTRING="mq://admin:passw0rd@localhost:1414"
```

Falls back to the `IbmMq_ConnectionDetails` semicolon format used by other test projects.

### CLI options

| Option | Default | Description |
|---|---|---|
| `--scenarios <names>` | `all` | `send`, `receive`, `receiveandsend`, `failure`, `sendlocal`, `publish`, `all` |
| `--transaction-modes <modes>` | `all` | `None`, `ReceiveOnly`, `SendsAtomicWithReceive`, `all` |
| `--message-count <n>` | `2500` | Messages for throughput tests |
| `--duration-seconds <n>` | `30` | Timeout for time-based tests |
| `--instance-counts <n...>` | `1 2 4 8` | Endpoint instances per scenario |
| `--output <path>` | none | Path for JSON results file |

Multiple values are space-separated.

### Examples

```bash
# All scenarios with defaults
dotnet run --project src/PerformanceTests --configuration Release

# Receive only, ReceiveOnly transactions, 5000 messages
dotnet run --project src/PerformanceTests --configuration Release -- \
  --scenarios receive --transaction-modes ReceiveOnly --message-count 5000

# All scenarios with JSON output
dotnet run --project src/PerformanceTests --configuration Release -- --output perf-results.json
```

## Triggering in CI

Performance tests run as part of the `perf-tests` job in `.github/workflows/ci.yml`. There are three triggers:

### Via PR label

Add the `perf-test` label to a pull request. The workflow runs all scenarios with defaults unless a perf tag narrows the scope (see below).

### Via perf tag

Push a tag to your branch to control which scenarios run. The tag does not trigger the workflow; it configures an existing trigger (PR label, push to main, or workflow_dispatch).

**Format:**

```
perf[/<scenario>[/<transaction-mode>[/<instance-counts>]]]
```

All segments after `perf` are optional. Omitted segments use defaults (all scenarios, all modes, all instance counts).

| Segment | Required | Values | Default |
|---|---|---|---|
| scenario | no | `send`, `receive`, `receiveandsend`, `failure`, `sendlocal`, `publish`, `all` | `all` |
| transaction-mode | no | `None`, `ReceiveOnly`, `SendsAtomicWithReceive`, `all` | `all` |
| instance-counts | no | dash-separated ints, e.g. `1-2` | `1 2 4 8` |

**Examples:**

```bash
# All scenarios, all modes, all instances
git tag perf && git push origin perf

# Receive only, all modes, all instances
git tag perf/receive && git push origin perf/receive

# Send only, no transactions
git tag perf/send/None && git push origin perf/send/None

# receiveandsend, ReceiveOnly, instances 1 and 2
git tag perf/receiveandsend/ReceiveOnly/1-2
git push origin perf/receiveandsend/ReceiveOnly/1-2

# All scenarios, all modes, instances 1, 4, 8
git tag perf/all/all/1-4-8 && git push origin perf/all/all/1-4-8
```

**Ordering matters:** If the `perf-test` label is already on the PR, pushing the branch triggers a build via the `synchronize` event. Push the tag *before* the branch so the build finds it:

```bash
git tag perf/receive
git push origin perf/receive                      # tag first
git push origin performance-report                # branch second
```

If you push the branch first, the build starts before the tag exists and runs all scenarios.

**Auto-cleanup:** The tag is automatically deleted from the remote after the workflow run completes, so the same tag name can be reused.

### Via workflow_dispatch

Trigger the workflow manually from the Actions tab with `run_perf_tests: true`. Runs all scenarios with defaults (perf tags on the selected branch are still respected).

## Repository setup

The CI pipeline requires a one-time setup before benchmark publishing and regression detection will work.

### 1. Create the benchmark-data branch

Results are stored on an orphan branch called `benchmark-data`. Create it once per repository:

```bash
git switch --orphan benchmark-data
git commit --allow-empty -m "Initialize benchmark-data branch"
git push origin benchmark-data
git switch -
```

### 2. Seed a baseline

PR comparisons need a `main/latest.json` to compare against. Run the performance tests on `main` to create one. The easiest way is to push to `main` (the `perf-tests` job runs on every push to `main`), or trigger the workflow manually via `workflow_dispatch` with `run_perf_tests: true` on the `main` branch.

### 3. Verify GitHub Actions permissions

The `perf-tests` and `benchmark-cleanup` jobs need write access:

| Permission | Used by | Purpose |
|---|---|---|
| `contents: write` | `perf-tests`, `benchmark-cleanup` | Push to `benchmark-data`, delete perf tags |
| `pull-requests: write` | `perf-tests` | Post comparison comments on PRs |

These are declared in the workflow files. If the repository or organization restricts the default `GITHUB_TOKEN` permissions, ensure **Read and write permissions** is selected under Settings > Actions > General > Workflow permissions.

## Benchmark data and regression detection

Results are stored on the `benchmark-data` orphan branch:

- `main/latest.json` and `main/history/{timestamp}_{sha}.json` for baseline tracking
- `branches/{name}/latest.json` for feature branch results

On pull requests, the current results are compared against the `main` baseline. A regression exceeding 10% triggers a workflow failure and posts a comparison table as a PR comment.

When a PR is merged or closed, the `benchmark-cleanup` workflow removes its branch directory from `benchmark-data` to prevent stale data from accumulating.
