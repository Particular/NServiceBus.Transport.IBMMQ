#!/usr/bin/env bash
set -euo pipefail

# Publishes performance test results to the benchmark-data branch.
#
# Usage: extra/publish-benchmark.sh <json-file> <branch-name> <commit-sha>
#
# Layout on benchmark-data:
#   main/latest.json                              (overwritten each run)
#   main/history/2026-02-20T10-00-00Z_abc1234.json (appended on main)
#   branches/<branch-name>/latest.json             (overwritten each run)

JSON_FILE="${1:?Usage: publish-benchmark.sh <json-file> <branch-name> <commit-sha>}"
BRANCH_NAME="${2:?Usage: publish-benchmark.sh <json-file> <branch-name> <commit-sha>}"
COMMIT_SHA="${3:?Usage: publish-benchmark.sh <json-file> <branch-name> <commit-sha>}"

BENCHMARK_DIR="_benchmark-data"
SHORT_SHA="${COMMIT_SHA:0:7}"
TIMESTAMP=$(date -u +"%Y-%m-%dT%H-%M-%SZ")

if [ ! -f "$JSON_FILE" ]; then
    echo "::warning::No results file found at $JSON_FILE, skipping publish."
    exit 0
fi

if [ ! -d "$BENCHMARK_DIR" ]; then
    echo "::error::Benchmark data directory $BENCHMARK_DIR not found. Check out the benchmark-data branch first."
    exit 1
fi

cd "$BENCHMARK_DIR"

if [ "$BRANCH_NAME" = "main" ] || [ "$BRANCH_NAME" = "master" ]; then
    mkdir -p main/history

    cp "../$JSON_FILE" main/latest.json
    cp "../$JSON_FILE" "main/history/${TIMESTAMP}_${SHORT_SHA}.json"

    git add main/
    COMMIT_MSG="Update main benchmark results (${SHORT_SHA})"
else
    SAFE_BRANCH=$(echo "$BRANCH_NAME" | tr '/' '-')
    mkdir -p "branches/${SAFE_BRANCH}"

    cp "../$JSON_FILE" "branches/${SAFE_BRANCH}/latest.json"

    git add "branches/${SAFE_BRANCH}/"
    COMMIT_MSG="Update benchmark results for ${SAFE_BRANCH} (${SHORT_SHA})"
fi

if git diff --cached --quiet; then
    echo "No changes to commit."
    exit 0
fi

git config user.name "github-actions[bot]"
git config user.email "github-actions[bot]@users.noreply.github.com"
git commit -m "$COMMIT_MSG"
git push
