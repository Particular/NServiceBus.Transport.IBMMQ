# Transport Comparison: IBM MQ vs Other NServiceBus Transports

Analysis based on source code review of each transport's repository.

## Naming

### Address Format (ToTransportAddress)

Every NServiceBus transport must convert a `QueueAddress(BaseAddress, Discriminator, Qualifier)` into a transport-specific string.

| Transport | Discriminator Sep | Qualifier Sep | Format | Example |
|-----------|:-:|:-:|--------|---------|
| **IBM MQ** | `.` | `.` | `base.disc.qual` | `billing.instance1.audit` |
| Azure Service Bus | `-` | `.` | `base-disc.qual` | `billing-instance1.audit` |
| RabbitMQ | `-` | `.` | `base-disc.qual` | `billing-instance1.audit` |
| MSMQ | `-` | `.` | `base-disc.qual@machine` | `billing-instance1.audit@SERVER01` |
| SQL Server | `.` | `.` | `base.qual.disc@[schema]@[catalog]` | `billing.audit.instance1@[dbo]@[NServiceBus]` |
| Amazon SQS | `-` | `-` | `base-disc-qual` | `billing-instance1-audit` |

**Pro:** IBM MQ's `.` separator is the only valid option since `-` is not allowed in MQ object names. Using `.` for both discriminator and qualifier is simple and consistent.

**Con:** Non-standard separator. Most transports use `-` for discriminator and `.` for qualifier, so NServiceBus users moving from another transport must account for address format differences.

### Queue Name Constraints

| Transport | Max Length | Allowed Characters | Sanitization |
|-----------|----------:|---------------------|-------------|
| **IBM MQ** | **48** | `A-Za-z0-9._` | User-provided `ResourceNameSanitizer` delegate |
| Amazon SQS | 80 | `A-Za-z0-9-_` | Built-in: non-allowed chars replaced with `-` |
| MSMQ | ~115 effective | Windows path chars | None (validation only, 150-char format name limit) |
| SQL Server | 128 | SQL identifier rules | Bracket-quoting for special chars |
| RabbitMQ | 255 | Any UTF-8 (except `amq.` prefix) | None |
| Azure Service Bus | 260 | `A-Za-z0-9._/-` | None (validation only) |

**Con: Most restrictive naming of any transport.** IBM MQ's 48-character limit is the tightest by a wide margin. Amazon SQS at 80 chars is the next most restrictive, and it's nearly double. Long fully-qualified type names (common in .NET) routinely exceed 48 chars, making hash-truncation the norm rather than the exception for topic admin names.

**Con: No hyphens.** Hyphens are the standard NServiceBus discriminator separator and are commonly used in endpoint names (e.g., `Sales-OrderProcessor`). IBM MQ rejects them, forcing users to provide a custom sanitizer or avoid hyphens entirely.

**Pro: User-controlled sanitization.** The `ResourceNameSanitizer` delegate gives full control over how names are transformed. Amazon SQS has a similar `QueueNameGenerator` pattern. Azure Service Bus and RabbitMQ validate but do not sanitize, leaving users to pre-comply.

### Topic/Subscription Name Constraints

| Transport | Topic Name Limit | Subscription Name Limit |
|-----------|----------------:|------------------------:|
| **IBM MQ** | 48 (admin name) | 256 |
| Azure Service Bus | 260 | 50 |
| Amazon SQS (SNS) | 256 | N/A (SNS manages) |
| RabbitMQ | 255 (exchange) | N/A (bindings, not named) |
| SQL Server | N/A (table-based) | N/A (type FullName in table) |

**Pro:** IBM MQ subscription names at 256 chars are generous — more than Azure Service Bus's 50-char subscription limit.

**Con:** IBM MQ topic admin names at 48 chars are the tightest. Azure Service Bus allows 260-char topic names. IBM MQ compensates via hash-truncation, but truncated names lose human readability.

## Pub/Sub Topology

### Topology Abstraction

| Transport | Topology Model | Pluggable? | Strategies |
|-----------|---------------|:----------:|-----------|
| **IBM MQ** | Topic-based | **Yes** | TopicPerEvent (subscriber-side fan-out) |
| Azure Service Bus | Topic-per-event | **Yes** | TopicPerEvent, Migration (deprecated) |
| RabbitMQ | Exchange-based | **Yes** | Conventional (fanout exchanges), Direct (amq.topic) |
| Amazon SQS | SNS topics | No | Single strategy (topic-per-event) |
| SQL Server | Subscription table | No | Single strategy (polymorphic query) |
| MSMQ | N/A (unicast only) | N/A | Message-driven pub/sub via external persistence |

**Pro: Zero-config polymorphism.** The TopicPerEvent topology provides full polymorphism (classes + interfaces) via subscriber-side fan-out with no duplicate risk and no user configuration. Similar to Azure Service Bus's TopicPerEvent and Amazon SQS's topic-per-event approach.

### Polymorphic Event Handling

| Transport | How It Works | Interface Support | Duplicate Risk |
|-----------|-------------|:-:|:-:|
| **IBM MQ** | Subscriber fan-out: N subscriptions to concrete descendants | Yes | None |
| Azure Service Bus | Explicit `SubscribeTo<TEvent>(topic)` mapping | User-mapped | None |
| RabbitMQ (Conventional) | Exchange-to-exchange bindings for full hierarchy | Yes | None |
| RabbitMQ (Direct) | `#` wildcard routing keys | Limited | None |
| Amazon SQS | Explicit `MapEvent<T>()` mapping | User-mapped | None |
| SQL Server | Query subscription table for all types in hierarchy | Yes | None |
| MSMQ | N/A (unicast, publisher sends to each subscriber) | Via persistence | None |

**Pro: Full polymorphism without publisher overhead.** The publisher sends a single message to the concrete type's topic. Subscribers automatically fan out by subscribing to all concrete descendants of their handled type. This avoids duplicate risk entirely — unlike publisher-side fan-out approaches where a subscriber handling overlapping types receives the same message multiple times.

## Transactionality & Consistency

### Supported Transaction Modes

| Transport | None | ReceiveOnly | SendsAtomicWithReceive | TransactionScope |
|-----------|:----:|:-----------:|:----------------------:|:----------------:|
| **IBM MQ** | Yes | Yes (default) | Yes | No |
| Azure Service Bus | Yes | Yes | Yes (default) | No |
| RabbitMQ | No | Yes (only mode) | No | No |
| Amazon SQS | Yes | Yes (default) | No | No |
| SQL Server | Yes | Yes | Yes | Yes (default) |
| MSMQ | Yes | Yes | Yes | Yes (default) |

**Pro: SendsAtomicWithReceive support.** IBM MQ, Azure Service Bus, SQL Server, and MSMQ support this — RabbitMQ and SQS do not. This mode ensures that outgoing messages are only dispatched if the receive succeeds, without the overhead of distributed transactions. IBM MQ achieves this via `MQPMO_SYNCPOINT` on the receive connection.

**Con: No TransactionScope/MSDTC support.** Only MSMQ and SQL Server support distributed transactions via `TransactionScope`. IBM MQ's managed .NET client does not participate in `System.Transactions`. This means IBM MQ cannot coordinate with database operations in a single distributed transaction — the Outbox pattern is required for exactly-once processing when business data lives in a separate database.

### Consistency Guarantees by Mode

| Mode | IBM MQ Behavior | Comparable To |
|------|----------------|---------------|
| **None** | Fire-and-forget. Message may be lost if process crashes after receive, before processing completes. | Same across all transports |
| **ReceiveOnly** (default) | Receive under native MQ transaction (`MQGMO_SYNCPOINT`). Sends are non-transactional — if process crashes mid-handler, sends may have already gone out but the receive rolls back (ghost messages). | Same as RabbitMQ (manual ACK), Azure Service Bus (PeekLock) |
| **SendsAtomicWithReceive** | Sends enlisted on the receive connection's syncpoint. All sends + receive commit or roll back together. No ghost messages, but duplicates possible on retry. | Azure Service Bus (cross-entity transactions), MSMQ (native MQ transaction) |

**Pro: ReceiveOnly is the safest default for most users.** It matches the default of Amazon SQS and the only mode of RabbitMQ. Combined with the Outbox, it provides exactly-once semantics.

**Con: SendsAtomicWithReceive requires a dedicated send connection.** IBM MQ's `AtomicMessageDispatcher` creates a facade on the receive connection's `MQQueueManager` so that sends and the receive share the same syncpoint. This is an implementation detail, but it means atomic sends are bound to the connection lifetime of the receive pump.

### Outbox Compatibility

| Transport | Outbox Needed For | Notes |
|-----------|------------------|-------|
| **IBM MQ** | Exactly-once with external DB | No native deduplication |
| Azure Service Bus | Exactly-once with external DB | ASB has native dedup for FIFO queues only |
| RabbitMQ | All exactly-once scenarios | Only mode is ReceiveOnly; no atomic sends |
| Amazon SQS | All exactly-once scenarios | SQS is at-least-once by design |
| SQL Server | Optional (same-DB scenarios are already atomic) | Single local transaction when DB is colocated |
| MSMQ | Cross-resource exactly-once without MSDTC | MSDTC provides distributed atomicity natively |

**Pro: IBM MQ is on par with Azure Service Bus and better than RabbitMQ/SQS for transactional guarantees.** The SendsAtomicWithReceive mode means fewer scenarios require the Outbox compared to RabbitMQ and SQS.

**Con: Cannot match SQL Server's colocated-DB optimization.** SQL Server transport can share a single local transaction between transport operations and business data when they're in the same database, achieving exactly-once without the Outbox. IBM MQ cannot do this since messaging and business data are inherently separate systems.

## Delayed Delivery

| Transport | Native? | Mechanism | Max Delay |
|-----------|:-------:|-----------|----------:|
| **IBM MQ** | **No** | Not supported | N/A |
| Azure Service Bus | Yes | `ScheduledEnqueueTime` property | Unlimited |
| RabbitMQ | Yes | 28-level binary tree of DLX+TTL queues | ~8.5 years |
| Amazon SQS | Yes | SQS `DelaySeconds` (<=15min) + FIFO recycling (>15min) | Unlimited |
| SQL Server | Yes | `{endpoint}.Delayed` table with `Due` datetime column | Unlimited |
| MSMQ | No | External timeout manager | N/A |

**Con: No delayed delivery.** IBM MQ is one of only two transports (alongside MSMQ) that does not support native delayed delivery. This means saga timeouts and deferred messages require the external timeout manager or a custom solution. MQ itself has no built-in message scheduling capability.

## TimeToBeReceived (TTBR)

| Transport | Mechanism | Proactive Expiry? | Transaction Conflict? |
|-----------|-----------|:-:|:-:|
| **IBM MQ** | MQ native message expiry | Broker-managed | No known conflict |
| Azure Service Bus | `TimeToLive` message property | On-read check only | No |
| RabbitMQ | `Expiration` basic property | Front-of-queue only | Cannot combine with delayed delivery |
| Amazon SQS | Header-based + `MessageRetentionPeriod` | Queue-level TTL | Max 14 days |
| SQL Server | `Expires` datetime column + purge task | Periodic (every ~5min) | No |
| MSMQ | Native `TimeToBeReceived` property | Continuous | **Yes** — cannot use with transactional queues without workaround |

**Pro: No TTBR-transaction conflict.** MSMQ infamously cannot combine TTBR with transactional sends — it applies one message's TTBR to ALL messages in the transaction. IBM MQ does not have this limitation.

**Pro: Broker-managed expiry.** IBM MQ handles message expiry at the broker level, similar to Azure Service Bus and RabbitMQ. SQL Server requires a background purge task.

**Con: Behavior differences from MSMQ for migrating users.** Users migrating from MSMQ should be aware that IBM MQ's expiry semantics differ — MSMQ proactively removes expired messages from anywhere in the queue, while IBM MQ evaluates expiry at get time. See [non-durable-messages.md](non-durable-messages.md) for related behavioral differences.

## Summary: IBM MQ Pros and Cons

### Pros

1. **Zero-config polymorphism** — subscriber-side fan-out handles classes and interfaces without duplicates or user configuration
2. **Single message per publish** — only one MQPUT regardless of the event's type hierarchy
3. **SendsAtomicWithReceive support** — better consistency than RabbitMQ and SQS without needing the Outbox
4. **User-controlled name sanitization** — flexible delegate pattern, similar to SQS's generator approach
5. **No TTBR-transaction conflict** — unlike MSMQ's problematic interaction
6. **Generous subscription name limit** — 256 chars vs Azure Service Bus's 50
7. **Broker-managed message expiry** — no application-level purge tasks needed

### Cons

1. **Most restrictive naming** — 48-char queue/topic names, no hyphens; hash-truncation is the norm for real-world type names
2. **Non-standard address separator** — `.` instead of the NServiceBus-conventional `-` for discriminator
3. **No TransactionScope/MSDTC** — cannot coordinate with external databases in a single distributed transaction; Outbox required for exactly-once with external DB
4. **No delayed delivery** — one of only two transports without it; saga timeouts need external mechanism
5. **Cannot match SQL Server's colocated-DB atomicity** — no equivalent of "same local transaction for transport + business data"
