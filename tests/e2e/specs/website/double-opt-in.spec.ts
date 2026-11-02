import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { bodyOf, headerOf } from '../../support/mail.ts';
import { STUB_URL } from '../../support/paths.ts';

// Double opt-in from the mail in the pickup folder (ROADMAP 8.5, ADR-0162). A link scanner's GET confirms nothing and no action runs
// before the visitor presses the button; after it, the submitted and verified actions run together in the order shown.

interface Call {
  path: string;
}

const callsTo = async (
  request: Parameters<Parameters<typeof test>[2]>[0]['request'],
  path: string,
): Promise<number> => {
  const calls = (await (await request.get(`${STUB_URL}/_stub/calls`)).json()) as Call[];
  return calls.filter((call) => call.path.startsWith(path)).length;
};

test('an address confirmed from the mail runs the actions, and a scanner confirms nothing', async ({
  page,
  request,
  waitForMail,
}) => {
  const path = `/webhooks/opt-in-${randomUUID()}`;
  const address = `opt-in-${randomUUID()}@example.test`;
  const made = await request.post('/jfb-testing/verification-form', { data: { url: `${STUB_URL}${path}` } });
  expect(made.status(), await made.text()).toBe(200);
  const { key } = (await made.json()) as { key: string };

  const asked = await request.post(`/jfb/api/v1/forms/${key}/challenge`);
  const { challenge } = (await asked.json()) as { challenge: string };
  const sent = await request.post(`/jfb/api/v1/forms/${key}/entries`, {
    data: { revision: 1, values: { name: 'Ada', email: address }, challenge },
  });
  expect(sent.status(), await sent.text()).toBe(200);

  const mail = await waitForMail((each) => headerOf(each.raw, 'To').includes(address), 60_000);
  const href = /href="([^"]*\/verify\?t=[^"]+)"/.exec(bodyOf(mail.raw, 'text/html'))?.[1]?.replaceAll('&amp;', '&');
  expect(href, 'the mail holds its link').toBeTruthy();
  const link = new URL(href!);
  const onThisHost = `${link.pathname}${link.search}`;

  // What a mail scanner does: it follows the link, twice, and presses nothing.
  for (let scan = 0; scan < 2; scan += 1) {
    const scanned = await request.get(onThisHost);
    expect(scanned.status()).toBe(200);
    expect(await scanned.text()).toContain('Confirm my address');
  }

  await page.waitForTimeout(3_000);
  expect(await callsTo(request, path), 'no action runs before the confirmation').toBe(0);

  await page.goto(onThisHost);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Confirm your address');
  await page.getByRole('button', { name: 'Confirm my address' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Your address is confirmed');

  await expect.poll(() => callsTo(request, path), { timeout: 60_000 }).toBe(2);
  const calls = (await (await request.get(`${STUB_URL}/_stub/calls`)).json()) as Call[];
  expect(calls.filter((call) => call.path.startsWith(path)).map((call) => call.path)).toEqual([
    `${path}/submitted`,
    `${path}/verified`,
  ]);

  // Pressing again from the same link changes nothing.
  await page.goto(onThisHost);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Your address is confirmed');
  await expect(page.getByRole('button')).toHaveCount(0);
});

test('the page is usable on a phone, and says what it must in the visitor language', async ({ page, request }) => {
  await page.setViewportSize({ width: 360, height: 640 });
  await page.goto('/jfb/api/v1/verify?t=nonsense');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText("This link doesn't work");
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  const answer = await request.get('/jfb/api/v1/verify?t=nonsense');
  expect(answer.status()).toBe(404);
});
