import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { fillForm, press, submitForm } from '../../support/form.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { demoProject, SECURE_BASE_URL, SECURE_HOST_PORT, SECURE_KEY_NAME, SECURE_PORT } from '../../support/paths.ts';
import { readSecrets } from '../../support/users.ts';

// A refused upload over HTTPS with HTTP/2 (ROADMAP 7.3d, ADR-0006), on a host of its own: there the server can end one
// request's stream while the browser is still sending, without closing the connection, and the browser must still read
// the answer. The page sends what its own runtime would refuse first, as a client that skips those checks does, and a
// visitor without JavaScript posts a file too large, and one far too large.

test.describe.configure({ mode: 'serial', timeout: 120_000 });
test.use({ baseURL: SECURE_BASE_URL, ignoreHTTPSErrors: true });

const CAREERS = '/en/careers/';
const PDF = '%PDF-1.7 ';
const PROGRAM = 'MZ';

interface SeededField {
  key: string;
  alias?: string;
  settings?: { maxBytes?: number };
}

const application = JSON.parse(readFileSync(join(demoProject, 'Seeding', 'Forms', 'application.json'), 'utf8')) as {
  pages: { groups: { fields: SeededField[] }[] }[];
};
const cv = application.pages
  .flatMap((page) => page.groups.flatMap((group) => group.fields))
  .find((field) => field.alias === 'cv');
if (cv?.settings?.maxBytes === undefined) throw new Error('The demo application has no cv field with a limit.');
const CV_KEY = cv.key;
const CV_LIMIT = cv.settings.maxBytes;

/** A body far beyond what the server's windows take while it reads nothing. */
const FAR_PAST_THE_LIMIT = 32 * 1024 * 1024;

/** Far beyond twice the form's limit, the most Kestrel reads and discards of a post (ADR-0006). */
const PAST_THE_DRAIN = 40 * 1024 * 1024;

interface Sent {
  /** The answer's status, or what the browser reported instead of one. */
  outcome: number | string;
  code?: string;
  /** The protocol the browser used for the request. */
  protocol: string;
  /** How much of the body the page had handed over when it read the answer. */
  handedOver: number;
  total: number;
}

/** Posts an upload from the page, with its length declared or as a stream that declares none. */
async function send(page: Page, file: { head: string; length: number; declared: boolean }): Promise<Sent> {
  return page.evaluate(
    async ({ field, head, length, declared }) => {
      const form = document.querySelector<HTMLElement>('main form.jfb-form')?.dataset.jfbForm;
      const challenged = await fetch(`/jfb/api/v1/forms/${form}/challenge`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: '{}',
      });
      const { challenge } = (await challenged.json()) as { challenge: string };
      const line = String.fromCharCode(13, 10);
      const boundary = 'jfb-refused-upload';
      const encoder = new TextEncoder();
      const opening = encoder.encode(
        [
          `--${boundary}`,
          'Content-Disposition: form-data; name="challenge"',
          '',
          challenge,
          `--${boundary}`,
          'Content-Disposition: form-data; name="file"; filename="cv.pdf"',
          'Content-Type: application/pdf',
          '',
          '',
        ].join(line),
      );
      const closing = encoder.encode(`${line}--${boundary}--${line}`);
      const piece = new Uint8Array(64 * 1024).fill(120);
      const first = piece.slice();
      first.set(encoder.encode(head));
      function* parts(): Generator<Uint8Array> {
        yield opening;
        let left = length;
        let next = first;
        while (left > 0) {
          const part = next.subarray(0, Math.min(next.length, left));
          yield part;
          left -= part.length;
          next = piece;
        }
        yield closing;
      }

      const total = opening.length + length + closing.length;
      let handedOver = 0;
      let body: BodyInit;
      if (declared) {
        body = new Blob([...parts()].map((part) => part.slice()));
        handedOver = total;
      } else {
        const each = parts();
        body = new ReadableStream<Uint8Array>({
          pull(controller) {
            const { value, done } = each.next();
            if (done) {
              controller.close();
            } else {
              handedOver += value.length;
              controller.enqueue(value);
            }
          },
        });
      }

      const url = `/jfb/api/v1/forms/${form}/uploads?fieldKey=${field}`;
      const init = {
        method: 'POST',
        headers: { 'content-type': `multipart/form-data; boundary=${boundary}` },
        body,
        duplex: 'half',
      } as RequestInit;
      let outcome: number | string;
      let code: string | undefined;
      try {
        const answer = await fetch(url, init);
        outcome = answer.status;
        code = ((await answer.json()) as { code?: string }).code;
      } catch (error) {
        outcome = String(error);
      }
      const timing = performance.getEntriesByType('resource').filter((entry) => entry.name.endsWith(url.slice(1)));
      const protocol = (timing.at(-1) as PerformanceResourceTiming | undefined)?.nextHopProtocol ?? 'no timing';
      return { outcome, code, protocol, handedOver, total };
    },
    { field: CV_KEY, ...file },
  );
}

/** The protocol the page itself came over. */
const pageProtocol = (page: Page): Promise<string> =>
  page.evaluate(() => (performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming).nextHopProtocol);

/** Fills the job application without JavaScript up to its page with the CV, where the file goes. */
async function reachTheCv(page: Page): Promise<Locator> {
  const form = page.locator('main form.jfb-form');
  await page.goto(CAREERS);
  await form.locator('[name="fullName.first"]').fill('Ada');
  await form.locator('[name="fullName.last"]').fill('Lovelace');
  await fillForm(form, { email: 'ada@visitor.jfb.test', post: 'assistant' });
  await press(form, 'next');
  await fillForm(form, {
    'referees[0].refName': 'Grace Hopper',
    'referees[0].refContact': 'grace@visitor.jfb.test',
    'referees[1].refName': 'Mary Somerville',
    'referees[1].refContact': '+44 20 7946 0000',
  });
  await press(form, 'next');
  await fillForm(form, { letter: 'I would love to work in a library.' });
  return form;
}

let host: Host | undefined;

test.beforeAll(async () => {
  test.setTimeout(600_000);
  host = await startHost({
    name: `host-${SECURE_PORT}`,
    port: SECURE_HOST_PORT,
    env: {
      ...hostEnvironment(readSecrets(), { deliveryApi: false }),
      // The run's host holds the stub's port; this one needs no stub.
      JironFormBuilderTesting__StubPort: '0',
      JironFormBuilderTesting__HttpsKey: SECURE_KEY_NAME,
      ASPNETCORE_URLS: `http://127.0.0.1:${SECURE_HOST_PORT};${SECURE_BASE_URL}`,
    },
  });
  const wait = await waitForStartupWork(host.logFile);
  if (wait.state !== 'done') throw new Error(`The secure host's start-up work ended as "${wait.state}".`);
});

test.afterAll(async () => {
  await host?.stop();
  // The key Windows' TLS needs in a store (TestCertificate); elsewhere there is none.
  if (process.platform === 'win32') {
    const store = 'Microsoft Software Key Storage Provider';
    spawnSync('certutil', ['-user', '-csp', store, '-delkey', SECURE_KEY_NAME], { stdio: 'ignore' });
  }
});

test('a length declared over the limit gets its 413 before a byte is read', async ({ page }) => {
  await page.goto(CAREERS);
  expect(await pageProtocol(page), 'the page came over HTTP/2').toBe('h2');

  const sent = await send(page, { head: PDF, length: CV_LIMIT + 1_000_000, declared: true });

  expect(sent).toMatchObject({ outcome: 413, code: 'payload_too_large', protocol: 'h2' });
});

test('a body that declares no length gets its 413 at the limit, with the rest never sent', async ({ page }) => {
  await page.goto(CAREERS);

  const sent = await send(page, { head: PDF, length: FAR_PAST_THE_LIMIT, declared: false });

  expect(sent).toMatchObject({ outcome: 413, code: 'payload_too_large', protocol: 'h2' });
  expect(sent.handedOver, 'the browser stopped sending').toBeLessThan(sent.total / 2);
});

test('a program gets its 415 on its first bytes, while the rest is still on its way', async ({ page }) => {
  await page.goto(CAREERS);

  const sent = await send(page, { head: PROGRAM, length: FAR_PAST_THE_LIMIT, declared: false });

  expect(sent).toMatchObject({ outcome: 415, code: 'unsupported_media_type', protocol: 'h2' });
  expect(sent.handedOver, 'the browser stopped sending').toBeLessThan(sent.total / 2);
});

test('after a refusal, the same page uploads a file that fits', async ({ page }) => {
  await page.goto(CAREERS);

  const refused = await send(page, { head: PROGRAM, length: FAR_PAST_THE_LIMIT, declared: false });
  const taken = await send(page, { head: PDF, length: 100_000, declared: false });

  expect(refused.outcome).toBe(415);
  expect(taken).toMatchObject({ outcome: 200, protocol: 'h2' });
});

test.describe('without JavaScript', () => {
  test.use({ javaScriptEnabled: false });

  test('a post too large for the field shows its page with the error at the field', async ({ page }) => {
    const form = await reachTheCv(page);

    await form
      .locator('input[type="file"][name="cv"]')
      .setInputFiles({ name: 'cv.pdf', mimeType: 'application/pdf', buffer: Buffer.alloc(CV_LIMIT + 100_000, 'x') });
    const result = await submitForm(form);

    expect(result.state, 'the page, not a reset connection').toBe('errors');
    await expect(form.locator('#jfb-1-cv-error')).toBeVisible();
    expect(await pageProtocol(page)).toBe('h2');
  });

  test('a post past what the server drains still shows its page with the notice, where HTTP/1.1 reset the connection', async ({
    page,
  }) => {
    const form = await reachTheCv(page);

    await form
      .locator('input[type="file"][name="cv"]')
      .setInputFiles({ name: 'cv.pdf', mimeType: 'application/pdf', buffer: Buffer.alloc(PAST_THE_DRAIN, 'x') });
    await form.locator('button[data-jfb-nav="submit"]').click();

    await expect(page.locator('main [data-jfb-notice="too_large"]')).toBeVisible();
    expect(await pageProtocol(page)).toBe('h2');
  });
});
