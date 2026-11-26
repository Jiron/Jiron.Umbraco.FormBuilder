import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';

// The first press of Send (ROADMAP 27.1, the first site's finding 2): the form's logic loads on the visitor's first touch,
// so a press of Send on an untouched form comes while it is on its way. That press is held, not posted: with the logic
// held back by the test, the page stays where it is, and once the logic comes it takes the same press, showing the
// required fields' errors in place, or sending a complete form once. With the logic refused, the press posts the form
// as without JavaScript, once.

const CHUNK = /\/jfb\/assets\/runtime\/chunks\/form-[^/]+\.js$/;
const EMPTY = 'Answer at least one question before you send the form';

const pathOfContact = (): string => {
  const shown = formPages().find((each) => each.form === 'contact');
  if (shown === undefined) throw new Error('The demo has no contact page.');
  return pathOf(shown.texts.en, 'en');
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const send = (form: Locator): Locator => form.locator('button[data-jfb-nav="submit"]');

/** Holds every request for the form's logic until the test lets it go. */
async function holdTheLogic(page: Page): Promise<() => void> {
  let release: () => void = () => undefined;
  const released = new Promise<void>((resolve) => (release = resolve));
  await page.route(
    (url) => CHUNK.test(url.pathname),
    async (route) => {
      await released;
      await route.continue();
    },
  );
  return release;
}

/** Every document request the page makes from now on, by method: a post of the form would be one. */
function documents(page: Page): string[] {
  const seen: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest()) seen.push(`${request.method()} ${new URL(request.url()).pathname}`);
  });
  return seen;
}

/** The page's entry script is there, and has enhanced the form; its logic is not. */
async function frameReady(page: Page): Promise<void> {
  await page.waitForFunction(() => 'JironFormBuilder' in window);
}

/** Each stored entry of the contact form, with its texts. */
async function entriesOfContact(request: APIRequestContext): Promise<string[][]> {
  const response = await request.get('/jfb-testing/entries', { params: { form: 'contact' } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: { values: { text: string | null }[] }[] }).entries.map((entry) =>
    entry.values.map((value) => value.text ?? ''),
  );
}

test('a first press of Send on an untouched form is held while the logic loads, and then shows the errors in place', async ({
  page,
}) => {
  const release = await holdTheLogic(page);
  const asked: string[] = [];
  page.on('request', (request) => {
    if (CHUNK.test(new URL(request.url()).pathname)) asked.push(request.url());
  });
  await page.goto(pathOfContact());
  await frameReady(page);
  const left = documents(page);
  await page.evaluate(() => Object.assign(window, { jfbSamePage: true }));
  const form = formOn(page);

  await send(form).click();
  await expect.poll(() => asked.length, 'the logic was asked for, and is held back').toBeGreaterThan(0);
  await expect(send(form), 'the press shows it is on its way').toHaveAttribute('data-jfb-busy', '');
  await send(form).click();
  expect(left, 'nothing posted while the logic is held back').toEqual([]);

  release();
  const summary = form.locator('[data-jfb-summary]');
  await expect(summary).toBeVisible();
  const links = await summary.locator('a').allTextContents();
  const shown = await form
    .locator('[data-jfb-error]')
    .evaluateAll(
      (errors) =>
        errors.filter((error) => (error.textContent ?? '').trim().length > 0 && !error.closest('template')).length,
    );

  expect(links.length, 'the required fields are named').toBeGreaterThan(1);
  expect(links, 'each its own error, not the general message (ADR-0266)').not.toContain(EMPTY);
  expect(shown, 'and each at its field').toBe(links.length);
  await expect(send(form)).not.toHaveAttribute('data-jfb-busy');
  expect(await page.evaluate(() => (window as unknown as { jfbSamePage?: boolean }).jfbSamePage)).toBe(true);
  expect(left, 'the page never left, the second press queued nothing').toEqual([]);
});

test('a complete form pressed before its logic is there is sent once, without leaving the page', async ({
  page,
  request,
}) => {
  const token = `First press ${Date.now()}`;
  const before = (await entriesOfContact(request)).length;
  const release = await holdTheLogic(page);
  await page.goto(pathOfContact());
  await frameReady(page);
  const left = documents(page);
  const form = formOn(page);
  await form.locator('[name="name"]').fill('Mira Okafor');
  await form.locator('[name="email"]').fill('mira@visitor.jfb.test');
  await form.locator('[name="message"]').fill(token);
  const sent: string[] = [];
  page.on('request', (each) => {
    if (each.method() === 'POST' && each.url().includes('/entries')) sent.push(each.url());
  });

  await send(form).click();
  await send(form).click();
  expect(left).toEqual([]);
  release();

  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  await expect
    .poll(async () => (await entriesOfContact(request)).slice(before).filter((texts) => texts.includes(token)).length)
    .toBe(1);
  expect(sent, 'one send').toHaveLength(1);
  expect(left, 'the page never left').toEqual([]);
});

test('with the logic refused, the first press posts the form as without JavaScript, once', async ({ page }) => {
  await page.route(
    (url) => CHUNK.test(url.pathname),
    (route) => route.abort(),
  );
  await page.goto(pathOfContact());
  await frameReady(page);
  const left = documents(page);
  const posted = page.waitForRequest((request) => request.isNavigationRequest() && request.method() === 'POST');

  await send(formOn(page)).click();
  const post = await posted;
  await page.waitForLoadState('load');

  expect(post.postData() ?? '', 'with the button pressed').toMatch(/jfb-nav=submit/);
  const summary = formOn(page).locator('[data-jfb-summary]');
  await expect(summary, "the server's own page with its errors").toBeVisible();
  expect(await summary.locator('a').allTextContents()).not.toContain(EMPTY);
  expect(
    left.filter((each) => each.startsWith('POST')),
    'one post',
  ).toHaveLength(1);
});
