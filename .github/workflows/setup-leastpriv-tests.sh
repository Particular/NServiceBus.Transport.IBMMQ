#!/usr/bin/env bash
#
# Sets up MQ permissions for least-privilege integration tests.
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
# Usage:
#   ./setup-leastpriv-tests.sh [container_runtime] [container_name_or_id]
#
# Examples:
#   ./setup-leastpriv-tests.sh                          # auto-detect runtime and container
#   ./setup-leastpriv-tests.sh podman ibmmq             # explicit podman + container name
#   ./setup-leastpriv-tests.sh docker abc123            # explicit docker + container ID
#
set -euo pipefail

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
    CONTAINER=$($RUNTIME ps -q --filter "ancestor=icr.io/ibm-messaging/mq:latest" | head -1)
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

# --- Create the testapp OS user ---
echo "Creating testapp OS user..."
$RUNTIME exec -u root "$CONTAINER" bash -c 'id testapp 2>/dev/null || useradd testapp'
$RUNTIME exec -u root "$CONTAINER" bash -c 'echo "testapp:testpass1" | chpasswd'

# --- Create test infrastructure as admin and grant app-level permissions ---
echo "Creating test infrastructure and granting permissions..."
$RUNTIME exec -i "$CONTAINER" runmqsc QM1 <<'MQSC'
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
