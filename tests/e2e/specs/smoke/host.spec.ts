import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { deliveryApiOn } from '../../support/host-env.ts';
import { STUB_URL } from '../../support/paths.ts';

// The host is the browser tests' test host (ADR-0029): its test-only endpoints answer, it runs on the test clock, its
// stub records calls, and the Content Delivery API is on or off as the run asked.

test('the test-only status says the host runs on the test clock with the stub', async ({ request }) => {
  const response = await request.get('/jfb-testing/status');

  expect(response.status()).toBe(200);
  const status = (await response.json()) as { testHost: boolean; clock: boolean; utc: string; stub: string };
  expect(status).toMatchObject({ testHost: true, clock: true, stub: `${STUB_URL}/` });
  expect(Math.abs(Date.parse(status.utc) - Date.now())).toBeLessThan(60_000);
});

test('the stub records a webhook call', async ({ request }) => {
  const path = `/webhooks/smoke-${randomUUID()}`;

  const answer = await request.post(`${STUB_URL}${path}`, { data: { hello: 'stub' } });

  expect(answer.status()).toBe(200);
  const calls = (await (await request.get(`${STUB_URL}/_stub/calls`)).json()) as {
    path: string;
    method: string;
    body: string;
  }[];
  expect(calls.filter((call) => call.path === path)).toEqual([
    expect.objectContaining({ method: 'POST', body: '{"hello":"stub"}' }),
  ]);
});

test('the Content Delivery API is on or off as the run asked', async ({ request }) => {
  const response = await request.get('/umbraco/delivery/api/v2/content', { params: { take: 1 } });

  // While it is off, Umbraco 17 refuses every request with 401: the API has no public access then.
  expect(response.status()).toBe(deliveryApiOn() ? 200 : 401);
});
