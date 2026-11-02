import type { Locator, Page, Route } from '@playwright/test';
import { chromium } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { logicCome, pressOnThePage } from '../../support/form.ts';
import { formPages, pathOf, type Language } from '../../support/demo-content.ts';

// Sending on the page (ROADMAP 6.4i, ADR-0120, ADR-0022), on the demo's contact form: a send with JavaScript reaches
// the entries endpoint without leaving the page and shows its outcome in the form's place; a second press during a slow
// send sends nothing; a refused or failed send shows the notice above the buttons, takes the focus and keeps every
// answer; the server's errors show at their fields with the summary; a redirect after a message counts down with "Go
// now" and "Stay here"; the send works from a plain-HTTP origin, where crypto.randomUUID() does not exist; and axe
// finds nothing in any of these states.

const ENTRIES = '**/jfb/api/v1/forms/*/entries';

const pathOfContact = (language: Language = 'en'): string => {
  const shown = formPages().find((each) => each.form === 'contact');
  if (shown === undefined) throw new Error('The demo has no contact page.');
  return pathOf(shown.texts[language], language);
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const send = (form: Locator): Locator => form.locator('button[data-jfb-nav="submit"]');

const ANSWERS = {
  name: 'Mira Okafor',
  email: 'mira@visitor.jfb.test',
  message: 'Is the reading room open on Sundays?',
};

async function fill(form: Locator): Promise<void> {
  for (const [name, value] of Object.entries(ANSWERS)) await form.locator(`[name="${name}"]`).fill(value);
}

async function answersKept(form: Locator): Promise<void> {
  for (const [name, value] of Object.entries(ANSWERS))
    await expect(form.locator(`[name="${name}"]`)).toHaveValue(value);
}

async function axe(page: Page, what: string): Promise<void> {
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    result.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    ),
    `${what}: axe`,
  ).toEqual([]);
}

/** Records the send events a site hears, each with its detail. */
async function listen(page: Page): Promise<void> {
  await page.evaluate(() => {
    const seen: unknown[] = [];
    Object.assign(window, { jfbSends: seen });
    for (const type of ['jfb:submitting', 'jfb:submitted', 'jfb:error']) {
      document.addEventListener(type, (event) => seen.push({ type, ...(event as CustomEvent<object>).detail }));
    }
  });
}

const heard = (page: Page): Promise<{ type: string; code?: string }[]> =>
  page.evaluate(() => (window as unknown as { jfbSends: { type: string; code?: string }[] }).jfbSends);

/** The page is the same one: no document request left it. */
function documents(page: Page): string[] {
  const seen: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest()) seen.push(request.url());
  });
  return seen;
}

test('a send reaches the entries endpoint and its outcome takes the form place, focused', async ({ page }) => {
  await page.goto(pathOfContact());
  const left = documents(page);
  const form = formOn(page);
  const id = await form.getAttribute('id');
  await fill(form);
  await listen(page);
  const answered = page.waitForResponse((response) => response.url().includes('/entries'));
  await pressOnThePage(send(form));
  const answer = await answered;

  expect(answer.status(), await answer.text()).toBe(200);
  const body = answer.request().postDataJSON() as Record<string, unknown>;
  expect(body['values'], 'the answers').toMatchObject({ name: [ANSWERS.name], email: [ANSWERS.email] });
  expect(body['submissionId'], 'the visitor id').toMatch(/^[0-9a-f-]{36}$/);
  expect(typeof body['challenge'], 'the challenge').toBe('string');
  expect(body['contentKey'], 'the page').toMatch(/^[0-9a-f-]{36}$/);

  const outcome = page.locator(`#${id ?? ''} [data-jfb-outcome]`);
  await expect(outcome).toBeVisible();
  await expect(outcome).toBeFocused();
  await expect(page.locator('main form.jfb-form')).toHaveCount(0);
  await expect(outcome.locator('[data-jfb-countdown]')).toHaveCount(0);
  await expect(outcome.locator('[data-jfb-stay]')).toHaveCount(0);
  expect(left, 'the page stayed').toEqual([]);
  expect((await heard(page)).map((each) => each.type)).toEqual(['jfb:submitting', 'jfb:submitted']);
  expect(JSON.stringify(await heard(page)), 'never an answer').not.toContain('Mira');
  await axe(page, 'the outcome');
});

test('the German page sends and words its states in German', async ({ page }) => {
  await page.goto(pathOfContact('de'));
  const form = formOn(page);
  await fill(form);
  await page.route(ENTRIES, (route) => route.fulfill({ status: 500, json: {} }));
  await pressOnThePage(send(form));

  const notice = form.locator('[data-jfb-notice="server_error"]');
  await expect(notice).toBeVisible();
  await expect(notice).toContainText('Ihre Antworten wurden nicht gesendet');
  await expect(notice).toContainText('Ihre Antworten bleiben erhalten.');
});

test('a second press during a slow send sends nothing, the button keeps its size and focus, and the live region says so', async ({
  page,
}) => {
  await page.goto(pathOfContact());
  const form = formOn(page);
  await fill(form);
  let requests = 0;
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route(ENTRIES, async (route) => {
    requests += 1;
    await held;
    await route.continue();
  });
  const button = send(form);
  const before = await button.boundingBox();

  await button.focus();
  await logicCome(button);
  await button.press('Enter');
  await expect(button).toHaveAttribute('data-jfb-busy', '');
  await expect(button).toHaveAttribute('aria-disabled', 'true');
  await expect(button).toBeFocused();
  await expect(form.locator('[data-jfb-live]')).toHaveText('Sending your answers');
  const during = await button.boundingBox();
  expect([during?.width, during?.height], 'the button keeps its size').toEqual([before?.width, before?.height]);
  await axe(page, 'the busy button');

  await logicCome(button);
  await button.press('Enter');
  // Playwright waits for an aria-disabled button to be enabled; a visitor's mouse does not.
  await button.click({ force: true });
  await page.waitForTimeout(250);
  expect(requests, 'one send').toBe(1);

  release();
  await expect(page.locator('[data-jfb-outcome]')).toBeVisible();
  expect(requests, 'still one send').toBe(1);
});

for (const [name, code, answer, words] of [
  [
    'a 429',
    'rate_limited',
    (route: Route) => route.fulfill({ status: 429, headers: { 'Retry-After': '90' }, json: { code: 'rate_limited' } }),
    'Wait 90 seconds, then send it again.',
  ],
  [
    'a 500',
    'server_error',
    (route: Route) => route.fulfill({ status: 500, json: { code: 'internal' } }),
    'Something went wrong on our side.',
  ],
  ['no answer', 'timeout', (route: Route) => route.abort('connectionreset'), 'Check your connection'],
] as const) {
  test(`${name} shows the notice above the buttons, moves the focus to it and keeps every answer`, async ({ page }) => {
    await page.goto(pathOfContact());
    const form = formOn(page);
    await fill(form);
    await listen(page);
    await page.route(ENTRIES, answer);
    await pressOnThePage(send(form));

    const notice = form.locator(`[data-jfb-notice="${code}"]`);
    await expect(notice).toBeVisible();
    await expect(notice).toBeFocused();
    await expect(notice).toHaveAttribute('role', 'alert');
    await expect(notice).toHaveClass(/jfb-notice-error/);
    await expect(notice).toContainText("Your answers weren't sent");
    await expect(notice).toContainText(words);
    expect(
      await notice.evaluate((element) => element.nextElementSibling?.classList.contains('jfb-navigation')),
      'just above the buttons',
    ).toBe(true);
    await expect(send(form)).not.toHaveAttribute('data-jfb-busy');
    await answersKept(form);
    expect(await heard(page)).toContainEqual(expect.objectContaining({ type: 'jfb:error', code }));
    await axe(page, `the notice of ${name}`);

    // The retry goes through and takes the notice with it, with the same submission id.
    await page.unroute(ENTRIES);
    const retried = page.waitForResponse((response) => response.url().includes('/entries'));
    await pressOnThePage(send(form));
    expect((await retried).status()).toBe(200);
    await expect(page.locator('[data-jfb-outcome]')).toBeFocused();
  });
}

test('a retry after a failed send carries the same submission id', async ({ page }) => {
  await page.goto(pathOfContact());
  const form = formOn(page);
  await fill(form);
  const ids: unknown[] = [];
  await page.route(ENTRIES, async (route) => {
    ids.push((route.request().postDataJSON() as { submissionId: unknown }).submissionId);
    await route.fulfill({ status: 500, json: {} });
  });
  await pressOnThePage(send(form));
  await expect(form.locator('[data-jfb-notice]')).toBeFocused();
  await pressOnThePage(send(form));
  await expect.poll(() => ids.length).toBe(2);

  expect(ids[0]).toBe(ids[1]);
  await expect(form.locator('[data-jfb-notice]'), 'one notice').toHaveCount(1);
});

test("the server's errors of a 422 show in place, with the error summary, which takes the focus", async ({ page }) => {
  await page.goto(pathOfContact());
  const form = formOn(page);
  const id = await form.getAttribute('id');
  await fill(form);
  await page.route(ENTRIES, (route) =>
    route.fulfill({
      status: 422,
      json: { code: 'validation_failed', errors: [{ code: 'email', path: 'email' }] },
    }),
  );
  await pressOnThePage(send(form));

  const summary = form.locator(':scope > [data-jfb-summary]');
  await expect(summary).toBeFocused();
  const email = form.locator('[name="email"]');
  await expect(email).toHaveAttribute('aria-invalid', 'true');
  await expect(form.locator(`#${id ?? ''}-email-error`)).toBeVisible();
  await expect(summary.locator('a')).toHaveAttribute('href', `#${id ?? ''}-email`);
  await answersKept(form);
  await axe(page, 'the error summary from a 422');

  await summary.locator('a').click();
  await expect(email).toBeFocused();
});

test('a redirect after a message counts down, with "Go now" and "Stay here"', async ({ page }) => {
  await page.goto(pathOfContact());
  const form = formOn(page);
  await fill(form);
  const thanks = new URL('/en/', page.url()).pathname;
  const answer = {
    receipt: { reference: 'REQ-2026-00042' },
    outcome: { kind: 'message', html: '<h2>Thank you, Mira</h2>', url: thanks, delaySeconds: 60 },
  };
  await page.route(ENTRIES, (route) => route.fulfill({ status: 200, json: answer }));
  await pressOnThePage(send(form));

  const outcome = page.locator('[data-jfb-outcome]');
  await expect(outcome).toBeFocused();
  await expect(outcome.locator('h2')).toHaveText('Thank you, Mira');
  await expect(outcome.locator('.jfb-reference strong')).toHaveText('REQ-2026-00042');
  const line = outcome.locator('[data-jfb-countdown]');
  await expect(line).toHaveText(/Taking you to the next page in \d+ seconds\./);
  const first = await line.innerText();
  await expect.poll(() => line.innerText(), 'it counts').not.toBe(first);
  const go = outcome.getByRole('link', { name: 'Go now' });
  await expect(go).toHaveAttribute('href', new RegExp(`${thanks}$`));
  await axe(page, 'the redirect notice');

  const stay = outcome.getByRole('button', { name: 'Stay here' });
  await stay.click();
  await expect(line, 'the countdown stops').toHaveCount(0);
  await expect(stay).toHaveCount(0);
  await expect(go, 'the focus is not lost').toBeFocused();

  await go.click();
  await expect(page).toHaveURL(new RegExp(`${thanks}$`));
});

test('a redirect after a message goes when its countdown ends', async ({ page }) => {
  await page.goto(pathOfContact());
  const form = formOn(page);
  await fill(form);
  const thanks = new URL('/en/', page.url()).pathname;
  await page.route(ENTRIES, (route) =>
    route.fulfill({
      status: 200,
      json: { receipt: {}, outcome: { kind: 'message', html: '<p>Hi</p>', url: thanks, delaySeconds: 1 } },
    }),
  );
  await pressOnThePage(send(form));

  // A shorter delay has ten seconds, the time to reach "Stay here".
  await expect(page.locator('[data-jfb-countdown]')).toHaveText(/Taking you to the next page in (10|9) seconds\./);
  await expect(page).toHaveURL(new RegExp(`${thanks}$`), { timeout: 20_000 });
});

test('a send from a plain-HTTP origin, where crypto.randomUUID() does not exist, reaches the entries endpoint', async ({
  baseURL,
}) => {
  const address = new URL(baseURL ?? '');
  // The host's address under a name that is no potentially trustworthy origin: localhost and 127.0.0.1 are.
  const browser = await chromium.launch({
    channel: 'msedge',
    args: [`--host-resolver-rules=MAP plain-http.jfb.test ${address.hostname}`],
  });
  try {
    const page = await browser.newPage({ baseURL: `http://plain-http.jfb.test:${address.port}` });
    await page.goto(pathOfContact());
    expect(await page.evaluate(() => [window.isSecureContext, typeof crypto.randomUUID])).toEqual([false, 'undefined']);
    const form = formOn(page);
    await fill(form);
    const answered = page.waitForResponse((response) => response.url().includes('/entries'));
    await pressOnThePage(send(form));
    const answer = await answered;

    expect(answer.status(), await answer.text()).toBe(200);
    expect((answer.request().postDataJSON() as { submissionId: string }).submissionId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
    );
    await expect(page.locator('[data-jfb-outcome]')).toBeFocused();
  } finally {
    await browser.close();
  }
});
