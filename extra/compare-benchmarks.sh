#!/usr/bin/env bash
set -euo pipefail

# Compares two benchmark JSON files and generates a markdown table.
# Exits non-zero if any scenario regresses beyond the threshold.
#
# Usage: extra/compare-benchmarks.sh <baseline.json> <current.json> [threshold-percent]
#
# Output: markdown table written to stdout
# Exit code: 0 if no regressions, 1 if any scenario exceeds the threshold

BASELINE="${1:?Usage: compare-benchmarks.sh <baseline.json> <current.json> [threshold-percent]}"
CURRENT="${2:?Usage: compare-benchmarks.sh <baseline.json> <current.json> [threshold-percent]}"
THRESHOLD="${3:-10}"

if [ ! -f "$BASELINE" ]; then
    echo "::warning::Baseline file not found at $BASELINE, skipping comparison."
    echo "No baseline data available for comparison."
    exit 0
fi

if [ ! -f "$CURRENT" ]; then
    echo "::error::Current results file not found at $CURRENT."
    exit 1
fi

jq -r --argjson threshold "$THRESHOLD" --slurpfile base "$BASELINE" '
  # Index baseline by composite key
  ($base[0] | map({key: "\(.scenario)|\(.transactionMode)|\(.instanceCount)", value: .messagesPerSecond}) | from_entries) as $bl |

  # Build rows from current results
  [.[] | {
    scenario,
    mode: .transactionMode,
    instances: .instanceCount,
    current: .messagesPerSecond,
    baseline: ($bl["\(.scenario)|\(.transactionMode)|\(.instanceCount)"] // null)
  }] |

  # Compute delta for each row
  map(. + (
    if .baseline == null then {delta: null, delta_str: "new"}
    elif .baseline == 0 then {delta: null, delta_str: "n/a"}
    else
      ((.current - .baseline) / .baseline * 100) as $d |
      {delta: $d, delta_str: (
        if $d >= 0 then "+\($d | . * 10 | round / 10)%"
        else "\($d | . * 10 | round / 10)%"
        end
      )}
    end
  )) |

  # Detect regressions
  map(. + {regressed: (if .delta != null and .delta < (-$threshold) then true else false end)}) | . as $rows |

  # Find baseline entries not covered by current run
  ($rows | map("\(.scenario)|\(.mode)|\(.instances)")) as $tested_keys |
  [$base[0][] |
    {scenario, mode: .transactionMode, instances: .instanceCount} |
    select(("\(.scenario)|\(.mode)|\(.instances)" | IN($tested_keys[])) | not)
  ] | unique_by("\(.scenario)|\(.mode)|\(.instances)") | . as $skipped |

  # Format output
  "## Performance comparison\n\nComparing against `main` baseline. Regression threshold: **\($threshold)%**.\n\n| Scenario | Mode | Instances | Baseline (msg/s) | Current (msg/s) | Delta |\n|---|---|---|---:|---:|---:|",
  ($rows[] | "| \(.scenario) | \(.mode) | \(.instances) | \(.baseline // "-") | \(.current) | \(.delta_str) |"),
  "",
  if ($rows | any(.regressed)) then
    "### Regressions detected\n",
    ($rows | map(select(.regressed))[] | "  - \(.scenario) / \(.mode) / \(.instances) instances: \(.delta_str)"),
    "",
    "One or more scenarios regressed beyond the \($threshold)% threshold.\n"
  else empty end,
  if ($skipped | length) > 0 then
    "<details>\n<summary>Scenarios not included in this run (\($skipped | length) baseline entries)</summary>\n",
    "| Scenario | Mode | Instances |",
    "|---|---|---|",
    ($skipped[] | "| \(.scenario) | \(.mode) | \(.instances) |"),
    "\n</details>"
  else empty end
' "$CURRENT"

# Check for regressions to set exit code
has_regression=$(jq --argjson threshold "$THRESHOLD" --slurpfile base "$BASELINE" '
  ($base[0] | map({key: "\(.scenario)|\(.transactionMode)|\(.instanceCount)", value: .messagesPerSecond}) | from_entries) as $bl |
  any(.[];
    ($bl["\(.scenario)|\(.transactionMode)|\(.instanceCount)"] // null) as $bv |
    $bv != null and $bv > 0 and (((.messagesPerSecond - $bv) / $bv * 100) < (-$threshold))
  )
' "$CURRENT")

if [ "$has_regression" = "true" ]; then
    exit 1
fi
