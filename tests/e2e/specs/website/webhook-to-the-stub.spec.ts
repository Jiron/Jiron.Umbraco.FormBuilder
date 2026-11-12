import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { expect, test } from '../../fixtures.ts';
import { E2E_WEBHOOK_SECRET, repoRoot, STUB_URL } from '../../support/paths.ts';

// The webhook action on the test host, against the stub on 5560 (ROADMAP 8.4, ADR-0161): a submission reaches the stub through the
// background worker, signed with the secret its reference names, and the Node sample of docs/actions.md finds the signature genuine.

interface Call {
  path: string;
  body: string;
  headers: { key: string; value: string }[];
}

const header = (call: Call, name: string): string | undefined =>
  call.headers.find((each) => each.key.toLowerCase() === name.toLowerCase())?.value;

/** Whether the Node sample finds the header genuine for the body, at the header's own time. */
function sampleSays(secret: string, signature: string, body: string): boolean {
  const folder = mkdtempSync(join(tmpdir(), 'jfb-webhook-'));
  try {
    const file = join(folder, 'body');
    writeFileSync(file, body);
    const seconds = /^t=(\d+)/.exec(signature)?.[1] ?? '0';
    const sample = join(repoRoot, 'docs', 'samples', 'webhook-signature', 'verify.mjs');
    return spawnSync(process.execPath, [sample, secret, signature, file, seconds]).status === 0;
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
}

test('a submission reaches the stub, signed, and the sample finds it genuine', async ({ request }) => {
  const path = `/webhooks/form-${randomUUID()}`;
  const made = await request.post('/jfb-testing/webhook-form', { data: { url: `${STUB_URL}${path}` } });
  expect(made.status(), await made.text()).toBe(200);
  const { key } = (await made.json()) as { key: string };

  const asked = await request.post(`/jfb/api/v1/forms/${key}/challenge`);
  expect(asked.status(), await asked.text()).toBe(200);
  const { challenge } = (await asked.json()) as { challenge: string };
  const sent = await request.post(`/jfb/api/v1/forms/${key}/entries`, {
    data: { revision: 1, values: { name: 'Ada "the first" & Co' }, challenge },
  });
  expect(sent.status(), await sent.text()).toBe(200);

  let call: Call | undefined;
  await expect
    .poll(
      async () => {
        const calls = (await (await request.get(`${STUB_URL}/_stub/calls`)).json()) as Call[];
        call = calls.find((each) => each.path === path);
        return call !== undefined;
      },
      { timeout: 60_000 },
    )
    .toBe(true);

  const body = JSON.parse(call!.body) as { event: string; fields: Record<string, string>; delivery: string };
  const signature = header(call!, 'X-Jfb-Signature') ?? '';
  expect(body.event).toBe('submitted');
  expect(body.fields.name).toBe('Ada "the first" & Co');
  expect(body.delivery).toBe(header(call!, 'X-Jfb-Delivery'));
  expect(signature).toMatch(/^t=\d+, v1=[0-9a-f]{64}$/);
  expect(sampleSays(E2E_WEBHOOK_SECRET, signature, call!.body)).toBe(true);
  expect(sampleSays(E2E_WEBHOOK_SECRET, signature, `${call!.body} `)).toBe(false);
  expect(sampleSays('JFB_FAKE_another_secret', signature, call!.body)).toBe(false);
});
