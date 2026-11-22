import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { APIRequestContext } from '@playwright/test';
import { keptSignedIn } from './api-login.ts';

// An API client of a backoffice user renews its access token once half its lifetime has gone, before the request that
// finds it so, as the backoffice does: Umbraco 17 gives the token 300 seconds, and a long test outlived it.

const TOKEN = '/umbraco/management/api/v1/security/back-office/token';

/** A context that records what it was asked, and answers the renewal with a lifetime, or refuses it. */
function fake(answer: { status: number; lifetime?: number } = { status: 200, lifetime: 300 }) {
  const calls: string[] = [];
  const response = (status: number, body: unknown = {}) => ({
    ok: () => status >= 200 && status < 300,
    status: () => status,
    json: async () => body,
  });
  const context = {
    async get(url: string) {
      calls.push(`get ${url}`);
      return response(200);
    },
    async post(url: string, options?: { form?: Record<string, string> }) {
      calls.push(`post ${url}${options?.form ? ` ${options.form['grant_type']}` : ''}`);
      return url === TOKEN ? response(answer.status, { expires_in: answer.lifetime }) : response(200);
    },
    async dispose() {
      calls.push('dispose');
    },
  };
  return { calls, context: context as unknown as APIRequestContext };
}

test('sends straight on while the token is younger than half its lifetime', async () => {
  const { calls, context } = fake();
  let time = 0;
  const api = keptSignedIn(context, 'designer', 300, () => time);
  time = 149_000;
  await api.get('/me');
  assert.deepEqual(calls, ['get /me']);
});

test('renews the token first once half its lifetime has gone, then again after half the new one', async () => {
  const { calls, context } = fake({ status: 200, lifetime: 300 });
  let time = 0;
  const api = keptSignedIn(context, 'designer', 300, () => time);
  time = 150_000;
  await api.post('/form', {});
  time = 299_000;
  await api.get('/me');
  time = 300_000;
  await api.get('/me');
  assert.deepEqual(calls, [
    `post ${TOKEN} refresh_token`,
    'post /form',
    'get /me',
    `post ${TOKEN} refresh_token`,
    'get /me',
  ]);
});

test('renews once for the requests that find the token old at the same time', async () => {
  const { calls, context } = fake();
  let time = 0;
  const api = keptSignedIn(context, 'designer', 300, () => time);
  time = 200_000;
  await Promise.all([api.get('/a'), api.get('/b'), api.post('/c')]);
  assert.equal(calls.filter((call) => call.startsWith(`post ${TOKEN}`)).length, 1);
  assert.equal(calls[0], `post ${TOKEN} refresh_token`);
});

test('fails the request whose renewal is refused, naming the user, and leaves the other methods as they are', async () => {
  const { calls, context } = fake({ status: 400 });
  let time = 0;
  const api = keptSignedIn(context, 'designer@jfb.test', 300, () => time);
  time = 160_000;
  await assert.rejects(api.get('/me'), /Renewing the API token of designer@jfb\.test failed with HTTP 400/);
  await api.dispose();
  assert.deepEqual(calls, [`post ${TOKEN} refresh_token`, 'dispose']);
});
