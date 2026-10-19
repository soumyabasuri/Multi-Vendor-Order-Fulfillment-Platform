# ADR 005: Redis Caching Strategy & Consistency Tradeoffs

## Status
Accepted

## Context
High-traffic multi-vendor marketplaces receive read volumes (browsing product listings, category trees, product detail pages) that are orders of magnitude higher than write volumes (order checkout, stock updates).
Hitting PostgreSQL for every product lookup introduces database connection exhaustion and elevated latency.

However, caching mutable data (such as product prices and available stock) introduces the fundamental cache consistency challenge:
- If cache updates lag behind database commits, customers view stale prices or stale inventory counts.
- If cache invalidation fails, stale data persists indefinitely.

## Decision
We implement a **Cache-Aside Pattern with Dual Invalidation and TTL Safety Fallback**:
1. **Cache Read Flow**:
   - Application queries Redis first by key (e.g. `catalog:product:{id}` or `catalog:categories:all`).
   - If cache HIT: Return deserialized DTO directly (latency ~1-2ms).
   - If cache MISS: Query PostgreSQL, write result to Redis with a 15-minute Time-To-Live (TTL), and return DTO.
2. **Cache Invalidation Flow (Write-Through Eviction)**:
   - When a product is updated or a variant is modified, the service immediately issues `KeyDeleteAsync` for that product's key and evicts product listing prefixes (`catalog:products:*`).
   - When an inventory reservation commits or adjusts, the cached product details are evicted so the next read reflects updated available stock.
3. **Resilience Fallback**:
   - If Redis is unavailable or disconnected during startup or runtime, `RedisCacheService` automatically falls back to an in-memory cache without crashing the application.

## Consistency Tradeoff Analysis
- **Eventual Consistency Window**:
  There exists a micro-window (between database commit and cache key eviction, typically < 5ms) where a concurrent read could theoretically observe the previous cache value.
- **Why this tradeoff is acceptable**:
  - Inventory availability displayed on catalog pages is explicitly treated as **informational**.
  - **Actual reservation decisions NEVER rely on the Redis cache**. They execute directly against PostgreSQL inside a transaction with row-level locks or atomic check constraints.
  - Therefore, even if a user views a cached product page showing "5 units available", attempting to check out when only 0 units remain will be strictly rejected by the PostgreSQL inventory engine with zero possibility of overselling.

## Consequences
### Positive
- Sub-5ms response times for high-volume catalog search and product detail requests.
- Drastic reduction of read pressure on the PostgreSQL primary database.
- Complete safety against overselling because checkout relies exclusively on ACID database invariants.

### Negative
- Prefix-based invalidation in Redis across large key sets requires careful key naming patterns to avoid expensive `KEYS *` operations.
