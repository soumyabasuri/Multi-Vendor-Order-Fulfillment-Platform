# ADR 003: Transactional Outbox Pattern for Distributed Messaging

## Status
Accepted

## Context
When business state is updated (such as an order being created or payment being completed), corresponding domain events must be published to Kafka topics so downstream modules (Fulfillment, Notifications, Analytics) can react asynchronously.

Directly publishing to Kafka inside the HTTP request or within the database transaction creates the classic **Dual-Write Problem**:
- If the database commits but Kafka is down or network blips, the event is permanently lost, causing data inconsistency.
- If Kafka succeeds but the database transaction fails and rolls back, downstream systems process phantom events that never existed in the database.
- Holding database transactions open while waiting for Kafka ACKs increases database connection pool contention and latency.

## Decision
We implement the **Transactional Outbox Pattern**:
1. When business operations occur, the domain event is serialized and written to the `outbox_messages` table **within the exact same PostgreSQL database transaction** as the business entity changes.
2. If the transaction commits, both business data and the outbox message are guaranteed to be persisted atomically. If it rolls back, neither is persisted.
3. A dedicated background worker (`OutboxProcessorBackgroundService`) polls pending messages from `outbox_messages` in batches (with `Order By CreatedAtUtc`).
4. The background worker publishes messages to Kafka with message Key = `AggregateId` (ensuring per-order partition ordering).
5. Upon receiving Kafka confirmation (`Acks.All`), the message is marked `Status = Published`.
6. On transient failures, `RetryCount` increments. If `RetryCount >= 5`, the message is escalated to `DeadLetter` status and an alert is logged.

## Consequences
### Positive
- **Guaranteed At-Least-Once Delivery**: No event is ever lost due to Kafka unavailability.
- **Atomic Consistency**: Resolves the Dual-Write problem completely.
- **Decoupled Latency**: Client HTTP requests do not block waiting for Kafka network round-trips.

### Negative
- **At-Least-Once Delivery requires Idempotent Consumers**: Because messages can be re-published if a worker crashes before updating the outbox table, all Kafka consumers must be designed to handle duplicate messages idempotently.
