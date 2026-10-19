# ADR 002: Inventory Concurrency & Zero-Oversell Locking Strategy

## Status
Accepted

## Context
In a multi-vendor marketplace, flash sales and popular product drops cause hundreds of concurrent buyers to attempt purchasing the same product at the same millisecond. If 10 units are available and 100 concurrent requests arrive, naive read-modify-write patterns (`SELECT stock -> check stock >= qty -> stock -= qty -> UPDATE stock`) cause severe race conditions:
- Lost updates: multiple transactions read the same available stock value before previous transactions commit.
- Overselling: inventory reserves more units than physically exist, creating negative stock and operational fulfillment failure.

## Evaluated Alternatives
1. **Distributed Locks (Redis Redlock)**:
   - High complexity: Requires acquiring distributed locks across Redis instances before hitting PostgreSQL.
   - Failure modes: Clock drift, lease expiration before long DB transactions complete, and network partition risks between Redis and app servers.
   - Unnecessary overhead: PostgreSQL already provides ACID transactions and robust row-level locking natively.

2. **Optimistic Concurrency Control (Version / xmin)**:
   - Fails under high contention: 100 requests hitting the same row cause 99 optimistic concurrency exceptions requiring aggressive application-level retries that consume excessive CPU and DB connections.

3. **Pessimistic Row-Level Locking (`SELECT ... FOR UPDATE`) with Sorted Key Ordering**:
   - Acquires row locks exclusively for the target product inventory rows during the transaction.
   - Eliminates lost updates.
   - Deadlock Prevention: When an order contains multiple items across different vendors, locking rows in random order can result in database deadlocks (Transaction A locks Product 1 then requests Product 2; Transaction B locks Product 2 then requests Product 1). To eliminate this risk, all target items are sorted deterministically (`OrderBy(ProductId).ThenBy(ProductVariantId)`) before acquiring row locks.

4. **Atomic Conditional SQL UPDATE (`UPDATE inventories SET ReservedQuantity = ReservedQuantity + @qty WHERE ProductId = @id AND (Quantity - ReservedQuantity >= @qty)`)**:
   - Non-blocking: Relies on PostgreSQL's internal row-level write lock during the update.
   - Immediate feedback: If affected rows == 0, the stock condition failed; transaction rolls back immediately without holding locks.

5. **Database Check Constraint (`CHECK ("Quantity" >= "ReservedQuantity" AND "Quantity" >= 0 AND "ReservedQuantity" >= 0)`)**:
   - The ultimate safety net: Guaranteed at the database engine level regardless of application bugs.

## Decision
We implement a **Hybrid Multi-Layered Defense**:
1. **Primary Strategy**: Pessimistic row-level locking (`SELECT ... FOR UPDATE`) with **deterministic sorted lock ordering** to prevent deadlocks for multi-item multi-vendor checkouts.
2. **Alternative Strategy**: Atomic conditional SQL UPDATE for single-item flash-sale reservations.
3. **Database Invariant**: Explicit check constraints on PostgreSQL table `inventories` guaranteeing `Quantity >= ReservedQuantity` and `ReservedQuantity >= 0` at all times.

## Consequences
### Positive
- **Zero Overselling Guarantee**: Physically impossible for available stock to go negative under any degree of concurrency.
- **Deadlock Free**: Deterministic sorting of lock keys guarantees transactions never deadlock on multi-item orders.
- **Zero Network Drift**: No dependency on external distributed locking layers.

### Negative
- Pessimistic locking holds row locks for the duration of the database transaction (typically 5-15ms), slightly lowering throughput for that single product under extreme load compared to memory-only counters.
