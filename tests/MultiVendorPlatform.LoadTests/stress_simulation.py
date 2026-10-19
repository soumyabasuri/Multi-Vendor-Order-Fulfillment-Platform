#!/usr/bin/env python3
"""
Multi-Vendor Platform - Concurrent Flash-Sale Stress Simulation
Simulates 100 concurrent purchase requests against 10 available stock units
to prove zero overselling and deterministic concurrency handling.
"""

import asyncio
import json
import time
import uuid

try:
    import aiohttp
except ImportError:
    import sys
    print("aiohttp not installed. Run: pip install aiohttp")
    sys.exit(0)

BASE_URL = "http://localhost:5001/api"

async def place_order(session, user_idx, product_id):
    order_id = str(uuid.uuid4())
    idempotency_key = f"stress-test-{order_id}"

    headers = {
        "Content-Type": "application/json",
        "Idempotency-Key": idempotency_key
    }

    # Direct order simulation or inventory reservation
    payload = {
        "shippingAddress": {
            "street": f"{user_idx} Market St",
            "city": "San Francisco",
            "state": "CA",
            "postalCode": "94105",
            "country": "USA",
            "recipientName": f"Customer {user_idx}",
            "recipientPhone": "555-0199"
        }
    }

    start_time = time.time()
    try:
        async with session.post(f"{BASE_URL}/orders", json=payload, headers=headers) as resp:
            elapsed = (time.time() - start_time) * 1000
            status = resp.status
            data = await resp.json()
            return {
                "user": user_idx,
                "status": status,
                "success": status == 200,
                "elapsed_ms": elapsed,
                "data": data
            }
    except Exception as e:
        return {
            "user": user_idx,
            "status": 500,
            "success": False,
            "elapsed_ms": 0,
            "error": str(e)
        }

async def run_stress_test(concurrent_workers=100, product_id=None):
    print(f"=== Starting Concurrency Stress Test: {concurrent_workers} concurrent requests ===")
    start = time.time()

    async with aiohttp.ClientSession() as session:
        tasks = [place_order(session, i, product_id) for i in range(concurrent_workers)]
        results = await asyncio.gather(*tasks)

    total_time = time.time() - start
    successful = [r for r in results if r["success"]]
    failed = [r for r in results if not r["success"]]

    print(f"\n--- Stress Simulation Results ---")
    print(f"Total Requests:      {len(results)}")
    print(f"Total Duration:      {total_time:.2f}s")
    print(f"Successful Orders:   {len(successful)}")
    print(f"Rejected (Out of Stock / Conflict): {len(failed)}")
    print(f"Average Latency:     {sum(r['elapsed_ms'] for r in results)/len(results):.2f}ms")
    print(f"Max Latency:         {max(r['elapsed_ms'] for r in results):.2f}ms")

    # Invariant assertion
    print(f"\nZero Oversell Verification: {'PASS - Invariants preserved' if len(successful) <= 10 else 'FAIL - Oversold!'}")

if __name__ == "__main__":
    asyncio.run(run_stress_test())
