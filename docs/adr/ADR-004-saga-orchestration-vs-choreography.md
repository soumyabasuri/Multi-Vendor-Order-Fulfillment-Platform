# ADR 004: Saga Orchestration vs Choreography for Order Processing

## Status
Accepted

## Context
The order fulfillment lifecycle in a multi-vendor marketplace spans multiple distinct steps:
1. Cart validation & order record creation
2. Concurrent inventory reservation
3. Payment initiation & payment gateway response
4. Order confirmation
5. Multi-vendor fulfillment task creation
6. Package dispatch & delivery
7. Compensating transactions if payment or validation fails (e.g. releasing reserved inventory back to available stock, expiring orders, notifying customers).

Two architectural patterns exist to coordinate multi-step sagas across distributed domains:
1. **Choreography**: Each module listens to events from other modules and decides independently what action to take next without a central coordinator.
2. **Orchestration**: A dedicated Saga Coordinator explicitly manages the workflow state machine, triggers the forward steps, and executes compensating actions upon failure.

## Decision
We select **Saga Orchestration** with an explicit coordinator (`OrderSagaCoordinator`):
- The `Order` entity maintains an explicit state machine (`Pending` -> `InventoryReserved` -> `PaymentPending` -> `Confirmed` -> `Processing` -> `Shipped` -> `Delivered`, or `Failed` / `Cancelled`).
- The `OrderSagaCoordinator` centralizes the logic for transitioning states and executing compensating actions:
  - On `PaymentCompletedEvent`: Commits inventory reservations, marks order as `Confirmed`, and generates vendor-specific fulfillment tasks for each vendor.
  - On `PaymentFailedEvent`: Triggers the compensating transaction by releasing inventory reservations (`IInventoryService.ReleaseReservationAsync`), marking the order as `Failed`, and sending a customer alert.
  - On Reservation Expiry: The `ReservationExpiryBackgroundJob` detects timed-out unpaid reservations, executes inventory release, and transitions order state to `Failed`.

## Why Orchestration over Choreography?
1. **Discoverability & Maintainability**: All order business workflows and failure compensations are defined in one clear location (`OrderSagaCoordinator.cs`) rather than scattered across dozen event handlers across the codebase.
2. **Avoids "Cyclic Dependency Hell"**: Pure choreography often leads to circular event loops where tracking who initiated what step becomes impossible.
3. **Auditability & Observability**: State transitions are deterministic and centralized, making tracing order progression straightforward.

## Consequences
### Positive
- Clear visualization of the order lifecycle and state transitions.
- Compensating actions (inventory release upon payment decline) are explicitly defined and testable in isolation.
- Easy to add new workflow steps (e.g., fraud inspection, loyalty points) without rewriting downstream consumers.

### Negative
- The orchestrator introduces a central point of coordination logic, which requires careful decoupling from low-level infrastructure details.
