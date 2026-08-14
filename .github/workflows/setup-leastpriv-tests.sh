#!/usr/bin/env bash
#
# Sets up MQ permissions for least-privilege integration tests.
#
# Works in two modes:
#   1. Inside the IBM MQ container — executed by the setup-ibmmq-action
#      init-script capability, which copies this script into the container
#      and runs it as root. IBM MQ tooling (runmqsc) is on PATH, so the
#      commands below run directly.
#
#        - name: Setup IBM MQ
#          uses: Particular/setup-ibmmq-action@...
#          with:
#            connection-string-name: IBMMQ_CONNECTIONSTRING
#            init-script: .github/workflows/setup-leastpriv-tests.sh
#
#   2. On a developer machine — auto-detects podman/docker and the running
#      IBM MQ container, then executes the same commands inside it.
#
# Usage:
#   ./setup-leastpriv-tests.sh                          # auto-detect runtime and container
#   ./setup-leastpriv-tests.sh podman ibmmq             # explicit podman + container name
#   ./setup-leastpriv-tests.sh docker abc123            # explicit docker + container ID
#
# Creates two permission profiles:
#   admin (pre-existing) - full access, used during EnableInstallers to create queues/topics
#   testapp             - app-level only, can send/receive/pub/sub but NOT create/delete objects
#
# The tests verify that:
#   - admin can create queues and topics (installer phase)
#   - testapp can send to queues, receive from queues, publish and subscribe to topics
#   - testapp cannot create queues, topics, or delete subscriptions via PCF
#
set -euo pipefail

# --- Mode detection ---
# Inside the container (setup-ibmmq-action init-script) runmqsc is on PATH and
# /.dockerenv exists. On a developer machine neither is true, so wrap the
# commands in docker/podman exec against the running IBM MQ container.
if [ -f /.dockerenv ] || command -v runmqsc >/dev/null 2>&1; then
    echo "Running inside the IBM MQ container"
else
    echo "Running on the host — detecting container runtime..."

    # Optional overrides: ./setup-leastpriv-tests.sh [container_runtime] [container_name_or_id]
    RUNTIME="${1:-}"
    CONTAINER="${2:-}"

    # Auto-detect container runtime
    if [ -z "$RUNTIME" ]; then
        if command -v podman &>/dev/null; then
            RUNTIME="podman"
        elif command -v docker &>/dev/null; then
            RUNTIME="docker"
        else
            echo "ERROR: Neither podman nor docker found" >&2
            exit 1
        fi
    fi

    # Auto-detect container
    if [ -z "$CONTAINER" ]; then
        # Any tag of the image, then well-known names.
        CONTAINER=$($RUNTIME ps --format '{{.ID}} {{.Image}}' | awk '$2 ~ /^icr\.io\/ibm-messaging\/mq/ { print $1; exit }')
        if [ -z "$CONTAINER" ]; then
            for name in ibmmq ibm-mq; do
                if $RUNTIME inspect "$name" &>/dev/null; then
                    CONTAINER="$name"
                    break
                fi
            done
        fi
        if [ -z "$CONTAINER" ]; then
            echo "ERROR: Could not find a running IBM MQ container" >&2
            exit 1
        fi
    fi

    echo "Using $RUNTIME with container: $CONTAINER"

    # Shadow the container commands with exec wrappers so the setup body below
    # is identical in both modes.
    id() { $RUNTIME exec -u root "$CONTAINER" id "$@"; }
    useradd() { $RUNTIME exec -u root "$CONTAINER" useradd "$@"; }
    chpasswd() { $RUNTIME exec -i -u root "$CONTAINER" chpasswd "$@"; }
    runmqsc() { $RUNTIME exec -i "$CONTAINER" runmqsc "$@"; }
fi

# --- Create the testapp OS user ---
echo "Creating testapp OS user..."
id testapp 2>/dev/null || useradd testapp
echo "testapp:testpass1" | chpasswd

# --- Create test infrastructure as admin and grant app-level permissions ---
echo "Creating test infrastructure and granting permissions..."
runmqsc QM1 <<'MQSC'
* =============================================================
* Test infrastructure (created by admin, used by testapp)
* =============================================================
DEFINE QLOCAL('TEST.LEASTPRIV.SEND') REPLACE
DEFINE QLOCAL('TEST.LEASTPRIV.RECEIVE') REPLACE
DEFINE TOPIC('TEST.LEASTPRIV.TOPIC') TOPICSTR('test/leastpriv/topic') REPLACE

* =============================================================
* Allow testapp to connect via the DEV.APP.SVRCONN channel
* =============================================================
SET CHLAUTH('DEV.APP.SVRCONN') TYPE(ADDRESSMAP) ADDRESS('*') USERSRC(CHANNEL) CHCKCLNT(ASQMGR) ACTION(REPLACE)

* =============================================================
* App-level permissions for testapp (minimum for send/receive/pub/sub)
* =============================================================
* Connect and inquire on queue manager
SET AUTHREC OBJTYPE(QMGR) PRINCIPAL('testapp') AUTHADD(CONNECT,INQ)
* Send (put) to queues
SET AUTHREC PROFILE('TEST.LEASTPRIV.SEND') OBJTYPE(QUEUE) PRINCIPAL('testapp') AUTHADD(PUT,INQ)
* Receive (get) from queues
SET AUTHREC PROFILE('TEST.LEASTPRIV.RECEIVE') OBJTYPE(QUEUE) PRINCIPAL('testapp') AUTHADD(GET,BROWSE,INQ,PUT)
* Publish and subscribe to topics
SET AUTHREC PROFILE('TEST.LEASTPRIV.TOPIC') OBJTYPE(TOPIC) PRINCIPAL('testapp') AUTHADD(PUB,SUB)
* Model queue access needed for managed subscriptions
SET AUTHREC PROFILE('SYSTEM.DEFAULT.MODEL.QUEUE') OBJTYPE(QUEUE) PRINCIPAL('testapp') AUTHADD(GET,DSP)
MQSC

echo ""
echo "Setup complete."
echo ""
echo "Permission summary:"
echo "  admin  - full access (create/delete queues, topics, subscriptions)"
echo "  testapp - app-level only (send, receive, publish, subscribe)"
echo ""
echo "  Queues:  TEST.LEASTPRIV.SEND, TEST.LEASTPRIV.RECEIVE"
echo "  Topic:   TEST.LEASTPRIV.TOPIC (test/leastpriv/topic)"
