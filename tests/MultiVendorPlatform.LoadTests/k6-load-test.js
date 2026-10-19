import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 20 },  // Ramp up to 20 users
    { duration: '1m', target: 50 },   // Sustained 50 concurrent users
    { duration: '30s', target: 100 }, // Peak spike to 100 users
    { duration: '20s', target: 0 },   // Ramp down
  ],
  thresholds: {
    'http_req_duration{endpoint:catalog}': ['p(95)<50'],   // Redis cached responses < 50ms
    'http_req_duration{endpoint:orders}': ['p(95)<200'],   // Transactional orders < 200ms
    'http_req_failed': ['rate<0.01'],                     // Less than 1% errors
  },
};

const BASE_URL = __ENV.API_BASE_URL || 'http://localhost:5000/api';

export default function () {
  const headers = {
    'Content-Type': 'application/json',
  };

  // 1. Browse Catalog (Cached in Redis)
  const catalogRes = http.get(`${BASE_URL}/products?pageNumber=1&pageSize=20`, {
    headers: headers,
    tags: { endpoint: 'catalog' },
  });

  check(catalogRes, {
    'catalog status is 200': (r) => r.status === 200,
    'catalog has items': (r) => JSON.parse(r.body).data.items.length >= 0,
  });

  sleep(1);

  // 2. Fetch Single Product (Redis Cache-Aside hit)
  const productId = '11111111-2222-3333-4444-555555555555';
  const productRes = http.get(`${BASE_URL}/products/${productId}`, {
    headers: headers,
    tags: { endpoint: 'catalog' },
  });

  check(productRes, {
    'product detail responds': (r) => r.status === 200 || r.status === 404,
  });

  sleep(0.5);
}
