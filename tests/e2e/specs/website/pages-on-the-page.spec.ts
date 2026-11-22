import type { Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { settled } from '../../support/axe.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { fillForm, logicCome, pressOnThePage } from '../../support/form.ts';

// Moving through pages in the browser (ROADMAP 6.5d, ADR-0155), on the demo's job application of three pages: each page checked on the
// server before the next, `next()`, `back()` and `jfb:page`, the buttons and the progress following, every answer kept, no page load
// from the first page to the outcome; what the server refuses stays on the page; a file goes up on a later page and a signature is
// drawn on an earlier one; and after the send the copy of the answers shows below the outcome.

const VALIDATE = '**/jfb/api/v1/forms/*/validate';
const RUN = Date.now().toString(36);
const PDF = Buffer.from('%PDF-1.7\n');

const pathOfForm = (form: string): string => {
  const found = formPages().find((each) => each.form === form);
  if (found === undefined) throw new Error(`The demo has no page for ${form}.`);
  return pathOf(found.texts.en, 'en');
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const shownTitle = (form: Locator): Locator => form.locator('[data-jfb-page]:not([hidden]) .jfb-title');

async function axe(page: Page, what: string): Promise<void> {
  await settled(page);
  const result = await new AxeBuilder({ page })
    .include('main')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    result.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    ),
    `${what}: axe`,
  ).toEqual([]);
}

/** Every page load from now on: none is what a move in the browser means. */
function loadsOf(page: Page): string[] {
  const loads: string[] = [];
  page.on('framenavigated', (frame) => {
    if (frame === page.mainFrame()) loads.push(frame.url());
  });
  return loads;
}

async function firstPage(form: Locator): Promise<void> {
  await form.locator('[name="fullName.first"]').fill('Ada');
  await form.locator('[name="fullName.last"]').fill(`Lovelace ${RUN}`);
  await fillForm(form, { email: 'ada@visitor.jfb.test', post: 'assistant' });
  await logicCome(form);
}

async function secondPage(form: Locator): Promise<void> {
  await fillForm(form, {
    'referees[0].refName': 'Grace Hopper',
    'referees[0].refContact': 'grace@visitor.jfb.test',
    'referees[1].refName': 'Mary Somerville',
    'referees[1].refContact': '+44 20 7946 0000',
  });
}

const next = (form: Locator): Promise<boolean | undefined> =>
  form.evaluate((element) => window.JironFormBuilder?.get(element)?.next());
const back = (form: Locator): Promise<boolean | undefined> =>
  form.evaluate((element) => window.JironFormBuilder?.get(element)?.back());

test('next() and back() move through three pages, each checked on the server, with jfb:page on each move', async ({
  page,
}) => {
  await page.goto(pathOfForm('application'));
  const form = formOn(page);
  await firstPage(form);
  await page.evaluate(() => {
    const told = window as Window & { moves?: unknown[] };
    told.moves = [];
    document.addEventListener('jfb:page', (event) => told.moves?.push((event as CustomEvent).detail));
  });
  const moves = (): Promise<{ page: string; number: number; total: number; alias: string }[]> =>
    page.evaluate(
      () => (window as unknown as { moves: { page: string; number: number; total: number; alias: string }[] }).moves,
    );
  const loads = loadsOf(page);
  const checks: unknown[] = [];
  page.on('request', (request) => {
    if (request.url().endsWith('/validate')) checks.push(request.postDataJSON());
  });
  const steps = form.locator('.jfb-progress-steps li');
  await expect(form.locator('.jfb-navigation [data-jfb-nav="back"]'), 'none on the first page').toHaveCount(0);

  expect(await next(form)).toBe(true);
  await expect(shownTitle(form)).toHaveText('Your references');
  await expect(shownTitle(form), 'the focus goes to the title').toBeFocused();
  expect(checks, 'one check, of the page it left').toHaveLength(1);
  await expect(form.locator('.jfb-progress-text')).toHaveText('Page 2 of 3');
  await expect(steps.nth(0)).toHaveAttribute('data-jfb-done', '');
  await expect(steps.nth(1)).toHaveAttribute('aria-current', 'step');
  await expect(form.locator('.jfb-navigation [data-jfb-nav="back"]')).toBeVisible();
  expect(await form.locator('[name="jfb-page"]').inputValue()).toBe((await moves())[0]?.page);
  await axe(page, 'the second page');

  await secondPage(form);
  expect(await next(form)).toBe(true);
  await expect(shownTitle(form)).toHaveText('Your letter');
  await expect(form.locator('.jfb-navigation [data-jfb-nav="next"]'), 'none on the last page').toHaveCount(0);
  await expect(form.locator('.jfb-navigation [data-jfb-nav="submit"]')).toBeVisible();
  expect(await next(form), 'no Next to press').toBe(false);

  expect(await back(form)).toBe(true);
  await expect(shownTitle(form)).toHaveText('Your references');
  await expect(form.locator('[name="referees[1].refName"]'), 'every answer stays').toHaveValue('Mary Somerville');
  expect(checks, 'going back asks nothing').toHaveLength(2);

  expect((await moves()).map((each) => [each.alias, each.number, each.total])).toEqual([
    ['application', 2, 3],
    ['application', 3, 3],
    ['application', 2, 3],
  ]);
  expect(loads, 'no page load').toEqual([]);
});

test("the server's refusal of a page stays on it: its errors at their fields, a notice for the rest", async ({
  page,
}) => {
  await page.goto(pathOfForm('application'));
  const form = formOn(page);
  await firstPage(form);
  const loads = loadsOf(page);

  await page.route(VALIDATE, (route) =>
    route.fulfill({ status: 422, json: { code: 'validation_failed', errors: [{ code: 'email', path: 'email' }] } }),
  );
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(form.locator('[data-jfb-summary]')).toBeFocused();
  await expect(form.locator('[data-jfb-alias="email"] [data-jfb-error]')).toBeVisible();
  await expect(shownTitle(form)).toHaveText('About you');
  await axe(page, 'a page the server refused');
  await page.unroute(VALIDATE);

  await page.route(VALIDATE, (route) =>
    route.fulfill({ status: 429, headers: { 'Retry-After': '90' }, json: { code: 'rate_limited' } }),
  );
  await form.locator('[name="email"]').fill('lin@visitor.jfb.test');
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(form.locator('[data-jfb-notice="rate_limited"]')).toBeFocused();
  await expect(shownTitle(form)).toHaveText('About you');
  await axe(page, 'a check refused by the limit');
  await page.unroute(VALIDATE);

  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form), 'the same press goes on once it can').toHaveText('Your references');
  await expect(form.locator('[data-jfb-notice]'), 'what spoke of the page before goes').toHaveCount(0);
  expect(loads).toEqual([]);
});

test('a send whose errors lie on an earlier page shows that page, with its errors', async ({ page }) => {
  await page.goto(pathOfForm('application'));
  const form = formOn(page);
  await firstPage(form);
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your references');
  await secondPage(form);
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your letter');
  await form.locator('[name="letter"]').fill('A letter.');
  await form.locator('[data-jfb-alias="privacy"] label').first().click();
  const loads = loadsOf(page);
  await page.route('**/jfb/api/v1/forms/*/entries', (route) =>
    route.fulfill({
      status: 422,
      json: { code: 'validation_failed', errors: [{ code: 'email', path: 'email' }] },
    }),
  );
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));

  await expect(shownTitle(form)).toHaveText('About you');
  await expect(form.locator('[data-jfb-summary]')).toBeFocused();
  await expect(form.locator('[data-jfb-alias="email"] [data-jfb-error]')).toBeVisible();
  await expect(form.locator('.jfb-navigation [data-jfb-nav="next"]')).toBeVisible();
  expect(loads).toEqual([]);
});

test('a file goes up on a later page, and after the send the copy of the answers shows below the outcome', async ({
  page,
  request,
}) => {
  await page.goto(pathOfForm('application'));
  const form = formOn(page);
  await firstPage(form);
  const loads = loadsOf(page);
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your references');
  await secondPage(form);
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your letter');

  const uploaded = page.waitForResponse((response) => response.url().includes('/uploads'));
  await form
    .locator('input[type="file"][name="cv"]')
    .setInputFiles({ name: 'cv.pdf', mimeType: 'application/pdf', buffer: PDF });
  expect((await uploaded).ok()).toBe(true);
  await form.locator('[name="letter"]').fill(`I mend books (${RUN})`);
  await form.locator('[data-jfb-alias="privacy"] label').first().click();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));

  const wrapper = page.locator('main .jfb-form[data-jfb-form]');
  await expect(wrapper.locator(':scope > [data-jfb-outcome]')).toBeFocused();
  const copy = wrapper.locator(':scope > .jfb-summary');
  await expect(copy).toBeVisible();
  await expect(copy).toContainText('Grace Hopper');
  await expect(copy).toContainText('cv.pdf');
  await expect(copy.locator('button'), 'no Change: the entry is sent').toHaveCount(0);
  await expect(wrapper.locator(':scope > [data-jfb-copy] button[data-jfb-print]')).toBeVisible();
  await expect(wrapper.locator(':scope > [data-jfb-copy] a')).toHaveAttribute('href', /#jfb-1$/);
  await expect(wrapper.locator(':scope > [data-jfb-outcome]'), 'the focus stays').toBeFocused();
  await axe(page, 'the copy of the answers after a send on the page');
  expect(loads, 'from the first page to the copy, no page load').toEqual([]);

  const response = await request.get('/jfb-testing/entries', { params: { form: 'application' } });
  const entries = ((await response.json()) as { entries: { values: unknown; files: { name: string }[] }[] }).entries;
  const entry = entries.find((each) => JSON.stringify(each.values).includes(`I mend books (${RUN})`));
  expect(entry?.files.map((file) => file.name)).toEqual(['cv.pdf']);

  await wrapper.locator(':scope > [data-jfb-copy] a').click();
  await expect(formOn(page).locator('[name="fullName.first"]'), 'the form again, empty').toHaveValue('');
});

test("a signature drawn stays through a page's check, as a picture that goes with the entry", async ({
  page,
  request,
}) => {
  await page.goto(pathOfForm('application'));
  const form = formOn(page);
  await firstPage(form);
  const loads = loadsOf(page);
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your references');
  await secondPage(form);
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your letter');

  const pad = form.locator('canvas.jfb-signature-pad');
  await pad.scrollIntoViewIfNeeded();
  const box = await pad.boundingBox();
  if (box === null) throw new Error('The pad does not show.');
  await page.mouse.move(box.x + box.width * 0.1, box.y + box.height * 0.6);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.5, box.y + box.height * 0.3, { steps: 4 });
  await page.mouse.move(box.x + box.width * 0.8, box.y + box.height * 0.7, { steps: 4 });
  await page.mouse.up();
  await form.locator('[name="letter"]').fill(`Signed (${RUN})`);
  await form.locator('[data-jfb-alias="privacy"] label').first().click();

  await pressOnThePage(form.locator('button[data-jfb-nav="back"]'));
  await expect(shownTitle(form)).toHaveText('Your references');
  const uploads: string[] = [];
  page.on('request', (each) => {
    if (each.url().includes('/uploads')) uploads.push(each.url());
  });
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your letter');
  expect(uploads, "the page's check made the picture").toHaveLength(1);

  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  expect(uploads, 'no second one for a drawing that did not change').toHaveLength(1);
  expect(loads).toEqual([]);

  const response = await request.get('/jfb-testing/entries', { params: { form: 'application' } });
  const entries = (
    (await response.json()) as { entries: { values: unknown; files: { name: string; typed: string | null }[] }[] }
  ).entries;
  const entry = entries.find((each) => JSON.stringify(each.values).includes(`Signed (${RUN})`));
  expect(entry?.files.length, 'the drawing, made into a picture').toBe(1);
  expect(entry?.files[0]?.typed ?? null, 'drawn, not typed').toBeNull();
});
