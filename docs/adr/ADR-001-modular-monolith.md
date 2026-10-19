# ADR 001: Modular Monolith vs Microservices Architecture

## Status
Accepted

## Context
Building a multi-vendor marketplace involves several distinct functional domains: Identity, Vendor Management, Catalog, Inventory, Cart, Orders, Payments, Fulfillment, and Notifications.

While a microservices architecture offers autonomous deployments and independent scalability for individual domains, it introduces substantial operational complexity:
1. Distributed transaction management (Dual writes, two-phase commits).
2. Complex cross-service queries and joins (e.g. assembling multi-vendor cart and order views).
3. Significant operational overhead (managing separate CI/CD pipelines, container registries, service meshes, observability across multiple runtimes).
4. Network latency and network partition risks between internal services.

## Decision
We adopt a **Modular Monolith** architecture initially. 
The codebase is partitioned into distinct internal modules (`Modules/Identity`, `Modules/Catalog`, `Modules/Inventory`, `Modules/Orders`, `Modules/Payments`, `Modules/Fulfillment`, etc.) with strict boundary separation:
- Each module encapsulates its domain entities, business logic, DTOs, and services.
- Modules communicate synchronously via clean service interfaces within the same process or asynchronously via Kafka domain events.
- Database access is consolidated within `ApplicationDbContext`, with clear schema boundaries that allow future extraction of individual tables into independent microservice databases if required.

## Consequences
### Positive
- **High Developer Productivity**: Single solution, single test runner, single migration pipeline.
- **ACID Guarantees**: Multi-entity transactional updates (such as atomic inventory reservation and multi-vendor order creation) execute within PostgreSQL ACID transactions without distributed locks.
- **Zero In-Process Network Latency**: Internal inter-module method calls run in memory.
- **Clear Extraction Path**: Because module boundaries are strictly enforced without circular references, any individual module (e.g. Catalog or Inventory) can be decoupled into an independent microservice when organizational or traffic scaling demands it.

### Negative
- All modules share the same deployment lifecycle and runtime process.
- A memory leak or CPU spike in one module can potentially impact other modules running in the same container.
