# IBM MQ Architecture Overview

## Queue Manager (QMGR)

The queue manager is the core component of IBM MQ. It controls queues and channels, and ensures proper delivery of messages to destination queue managers.

### Example of Queue Managers names

```
DEV.QMGR1                — development queue manager
PROD.PAYMENTS.QMGR       — production queue manager
DR.PAYMENTS.QMGR         — disaster recovery counterpart
```
## Queues
IBM MQ uses physical queue names, often with strict naming conventions, to organize and manage messages. Each queue is defined with a unique name and can be configured with specific properties (e.g., max depth, persistence, etc.).

### Types of Queues

| Type | Purpose |
|---|---|
| **Local Queue** | Stores messages within the same queue manager |
| **Remote Queue** | Pointer to a queue in another queue manager; doesn't store messages, forwards them |
| **Alias Queue** | Shortcut/redirect to another queue; enables flexible routing |
| **Model Queue** | Template for creating dynamic queues at runtime; never stores messages directly |
| **Transmission Queue** | A special local queue that holds messages destined for another queue manager |

> **Key point:** Only local queues (including transmission queues) can actually hold messages. Remote, alias, and model queues are pointers, shortcuts, or templates.


### Examples of queue names

```
APP.ORDERS.LOCAL          — local queue for order messages
APP.ORDERS.REMOTE         — remote queue pointing to another QMGR
APP.ORDERS.ALIAS          — alias queue redirecting to the real queue
APP.ORDERS.XMIT           — transmission queue for outbound messages
APP.ORDERS.MODEL          — model/template queue for dynamic creation
DLQ.ORDERS                — dead letter queue
```


## Channels
Channels are the communication pathways between queue managers (or between clients and queue managers). They define how messages are transmitted over the network.


### Types of MQ Channels

| Type | Code | Purpose |
|---|---|---|
| **Sender** | SDR | Sends messages from source queue manager |
| **Receiver** | RCVR | Listens for incoming messages on target queue manager |
| **Server Connection** | SVRCONN | Connects client application to queue manager over TCP/IP |
| **Client Connection** | CLNTCONN | Temporary channel from client side to connect to server queue manager |
| **Cluster Channel** | — | Auto-routes messages between multiple queue managers; eliminates manual sender/receiver setup |

In point-to-point architecture, the sender and receiver channel names must match and communicate over TCP/IP. In clustering, there are no explicit sender/receiver channels.

### Examples of channel names

```
QMGR1.TO.QMGR2           — sender/receiver channel pair (same name on both ends)
DEV.APP.SVRCONN           — server connection for applications
DEV.ADMIN.SVRCONN         — server connection for admin
PROD.PAYMENTS.CLNTCONN    — client connection channel
```

The `DEV.ADMIN.SVRCONN` format breaks down as:

| Segment | Meaning |
|---|---|
| `DEV` | Namespace/prefix — developer edition defaults |
| `ADMIN` | Purpose identifier — admin access |
| `SVRCONN` | Channel type suffix — server connection |


### Topics (pub/sub)

```
APP.ORDERS.EVENTS         — topic for order events
APP.NOTIFICATIONS         — topic for notifications
```

### Reserved Prefixes

| Pattern | Usage |
|---|---|
| `DEV.*` | IBM MQ Developer defaults (From our Docker image) |
| `SYSTEM.*` | Reserved for IBM MQ internal objects — do not use |
| `DLQ.*` | Dead letter queues |

### General Naming Conventions


General pattern: `<ENV>.<APP>.<TYPE>`

There's no restriction on the number of dot segments — as long as the total name stays within 48 characters. For example:

```
  PROD.EU.PAYMENTS.LOCAL          (26 chars — valid)
  PROD.EU.ORDERS.NOTIFY.ALIAS     (31 chars — valid)
```
It's just a naming convention, not a parsed structure. IBM MQ treats the whole string as a flat name — the dots have no special meaning to the queue manager itself.
In practice, more segments = more descriptive but you eat into that 48-char limit fast, so most teams stick to 2–3 segments.


**Constraints**:
- Queue names: max 48 characters
- Typically uppercase
- No special characters except `.` and `_`
- Queues must usually be pre-created by administrators

 The dots have no special meaning to the queue manager — the whole string is treated as a flat name. The number of segments is unrestricted as long as the total stays within 48 characters.


## Naming and Security

The name of an MQ object doesn't grant or deny access by itself — but names are what security policies **match against**. A good naming convention makes it easy to write tight, auditable security rules. A bad one leads to overly broad permissions or gaps.

### Authority Records (OAM)

IBM MQ's Object Authority Manager lets you set permissions using **wildcard profiles** that match on object names:

```
SET AUTHREC PROFILE('APP.ORDERS.**') OBJTYPE(QUEUE) GROUP('orderteam') AUTHADD(PUT, GET)
SET AUTHREC PROFILE('APP.PAYMENTS.**') OBJTYPE(QUEUE) GROUP('paymentteam') AUTHADD(PUT, GET)
```

Consistent naming prefixes per team/application let you apply blanket access rules. Messy naming makes this much harder.

### Channel Authentication Rules (CHLAUTH)

Rules can match on channel names to block or allow connections:

```
SET CHLAUTH('DEV.*') TYPE(ADDRESSMAP) ADDRESS('*') USERSRC(CHANNEL)
SET CHLAUTH('PROD.*') TYPE(ADDRESSMAP) ADDRESS('10.0.0.*') USERSRC(MAP) MCAUSER('prodapp')
```

The `DEV.` vs `PROD.` prefix in a channel name can directly determine who is allowed to connect.

### SYSTEM.* is protected

The `SYSTEM.*` namespace has elevated internal privileges. Creating objects with that prefix could unintentionally bypass security restrictions or break MQ internals.


## Messages

IBM MQ has no built-in concept of commands vs events. All messages are simply "messages":




###   Headers
Every IBM MQ message has two types of metadata:

| Header | Purpose | Extensible? |
|--------|---------|-------------|
| **MQMD** | Fixed message envelope | No - predefined fields only |
| **MQRFH2** | Custom application properties | Yes - any key/value pairs |

```
┌─────────────────────────────────────┐
│              MQMD                   │  ← Always present, fixed structure
│  (Message Descriptor)               │
├─────────────────────────────────────┤
│             MQRFH2                  │  ← Optional, extensible
│  (Rules and Formatting Header 2)    │
├─────────────────────────────────────┤
│          Message Body               │  ← Your actual payload
│     (JSON, XML, binary, etc.)       │
└─────────────────────────────────────┘
```

## MQMD (Message Descriptor)

The **Message Descriptor** is a fixed-structure header that accompanies every IBM MQ message. Think of it as the "envelope" with standard fields that MQ uses for routing, expiry, and tracking.

### MQMD Fields

| Field | Type | Description | Example |
|-------|------|-------------|---------|
| `MessageId` | byte[24] | Unique identifier (binary) | Auto-generated by MQ |
| `CorrelationId` | byte[24] | Links request/response messages | Copy from request |
| `Format` | string(8) | Describes what follows the MQMD | `"MQHRF2  "`, `"MQSTR   "` |
| `Expiry` | int | Time-to-live in tenths of seconds | `6000` = 10 minutes |
| `Persistence` | int | Survives queue manager restart? | `MQPER_PERSISTENT` |
| `Priority` | int | Message priority (0-9, higher = more urgent) | `5` |
| `ReplyToQueueName` | string(48) | Where to send replies | `"MY.REPLY.QUEUE"` |
| `ReplyToQueueManagerName` | string(48) | Queue manager for replies | `"QM1"` |
| `BackoutCount` | int | Number of times message was rolled back | `3` |
| `PutDateTime` | DateTime | When message was put to queue | `2024-01-15 10:30:00` |
| `MessageType` | int | Datagram, request, reply, or report | `MQMT_DATAGRAM` |
| `Feedback` | int | Feedback or reason code | `MQFB_NONE` |
| `Encoding` | int | Numeric encoding (big/little endian) | `MQENC_NATIVE` |
| `CodedCharSetId` | int | Character set ID | `1208` (UTF-8) |


### MQMD Limitations

- **Fixed fields only** - you cannot add custom data
- **Binary MessageId** - 24 bytes, not human-readable strings
- **Limited string lengths** - queue names max 48 characters
- **No nested structures** - flat field list only

---

## MQRFH2 (Rules and Formatting Header 2)

**MQRFH2** is an optional, extensible header for custom application properties. It sits between the MQMD and the message body, allowing applications to pass arbitrary metadata. They will appear under `Named Properties` in IBM console.

According to IBM MQ Knowledge Center and the MQ API specification **MQRFH2** namings have some constraints:
Valid Characters:
•	Letters: A-Z, a-z
•	Digits: 0-9
•	Underscore: _
•	Percent: % (used for wildcards in searches only)

Restrictions:
•	The first character must be a letter (A-Z or a-z)
•	Subsequent characters can be letters, digits (0-9), or underscores (_)
•	Names are case-sensitive
•	Maximum length is 4095 characters
•	Percent: % (used for wildcards in searches only)

For this reason NSB Headers need to be escaped on send/publish and unescaped when read

| Component | What It Is | For NServiceBus |
|-----------|------------|-----------------|
| **MQMD** | Fixed envelope (always present) | TTL, persistence, reply-to |
| **MQRFH2** | Custom properties (optional) | ALL NServiceBus headers |
| **Body** | Message payload | Serialized message content |

**Key Takeaway**: MQMD is for MQ infrastructure; MQRFH2 is for your application. NServiceBus headers should go in MQRFH2.


### MQMT_Datagram
**MQMT_DATAGRAM** sets the message type to indicate it's a one-way message that doesn't expect a reply.

  IBM MQ message types:
| Constant | Value | Meaning |
|----------|-------|---------|
| MQMT_DATAGRAM | 8 | One-way message, no reply expected |
| MQMT_REQUEST | 1 | Request message, expects a reply |
| MQMT_REPLY | 2 | Reply to a previous request |
| MQMT_REPORT | 4 | Report message (delivery confirmation, expiry notification, etc.) |
