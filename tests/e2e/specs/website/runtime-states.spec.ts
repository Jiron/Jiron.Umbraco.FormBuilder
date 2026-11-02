import type { Locator, Page, Route } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { logicCome, pressOnThePage } from '../../support/form.ts';

// The runtime's states on the demo (ROADMAP 6.4p, docs/ux/review-6.4.md): errors, busy, the notices, the form that changed, the
// redirect, uploads, the signature, rows and moves, at 375 and 1440 px, light, dark and right to left, and in the other themes.
// Axe finds nothing in any of them. With JFB_REVIEW_SHOTS set, each state is also a picture in that folder.

const ENTRIES = '**/jfb/api/v1/forms/*/entries';
const UPLOADS = '**/jfb/api/v1/forms/*/uploads**';
const SHOTS = process.env['JFB_REVIEW_SHOTS'];

const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=',
  'base64',
);
const PDF = Buffer.from('%PDF-1.7\n');

interface Variant {
  readonly name: string;
  readonly theme: string;
  readonly scheme: string;
  readonly rtl: boolean;
}

const VARIANTS: readonly Variant[] = [
  { name: 'default-light', theme: 'default', scheme: '', rtl: false },
  { name: 'default-dark', theme: 'default', scheme: 'dark', rtl: false },
  { name: 'default-rtl', theme: 'default', scheme: '', rtl: true },
  { name: 'bootstrap5-light', theme: 'bootstrap5', scheme: '', rtl: false },
  { name: 'bootstrap5-dark', theme: 'bootstrap5', scheme: 'dark', rtl: false },
  { name: 'unstyled-light', theme: 'unstyled', scheme: '', rtl: false },
];

const pathOfForm = (form: string): string => {
  const found = formPages().find((each) => each.form === form);
  if (found === undefined) throw new Error(`The demo has no page for ${form}.`);
  return pathOf(found.texts.en, 'en');
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const send = (form: Locator): Locator => form.locator('button[data-jfb-nav="submit"]');

/** Axe on a state as the visitor reads it, past its fades; and its picture when a review asks for them. */
async function holds(page: Page, variant: Variant, width: number, state: string): Promise<void> {
  // A busy button's spinner runs for as long as the send: only what ends is waited for.
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every((animation) => animation.playState !== 'running' || animation.effect?.getTiming().iterations === Infinity),
  );
  const result = await new AxeBuilder({ page })
    .include('main')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    result.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    ),
    `${state}: axe`,
  ).toEqual([]);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, `${state}: nothing runs past the page`).toBeLessThanOrEqual(0);
  if (SHOTS !== undefined) {
    await page.locator('main').screenshot({
      path: `${SHOTS}/${variant.name}-${String(width)}-${state}.png`,
      animations: 'disabled',
      caret: 'hide',
    });
  }
}

async function fillContact(form: Locator): Promise<void> {
  await form.locator('[name="name"]').fill('Mira Okafor');
  await form.locator('[name="email"]').fill('mira@visitor.jfb.test');
  await form.locator('[name="message"]').fill('Is the reading room open on Sundays?');
}

/** The form's states around a send, each from what the endpoint answers. */
async function sendingStates(page: Page, url: (form: string) => string, check: (state: string) => Promise<void>) {
  await page.goto(url('contact'));
  const form = formOn(page);
  await form.locator('[name="name"]').focus();
  await pressOnThePage(send(form));
  await expect(form.locator('[data-jfb-summary]')).toBeFocused();
  await check('errors');

  await fillContact(form);
  let release: (answer: (route: Route) => Promise<void>) => void = () => undefined;
  const held = new Promise<(route: Route) => Promise<void>>((resolve) => (release = resolve));
  await page.route(ENTRIES, async (route) => {
    await (
      await held
    )(route);
  });
  await pressOnThePage(send(form));
  await expect(send(form)).toHaveAttribute('data-jfb-busy', '');
  await check('busy');

  release((route) => route.fulfill({ status: 500, json: { code: 'internal' } }));
  await expect(form.locator('[data-jfb-notice]')).toBeFocused();
  await check('notice-failed');
  await page.unroute(ENTRIES);

  await page.route(ENTRIES, (route) =>
    route.fulfill({ status: 429, headers: { 'Retry-After': '90' }, json: { code: 'rate_limited' } }),
  );
  await pressOnThePage(send(form));
  await expect(form.locator('[data-jfb-notice]')).toContainText(/minute|second|moment|wait|later/i);
  await check('notice-wait');
  await page.unroute(ENTRIES);

  await page.route(ENTRIES, (route) => route.fulfill({ status: 409, json: { code: 'form_changed' } }));
  await pressOnThePage(send(form));
  await expect(form.locator('[data-jfb-notice="form_changed"]')).toBeFocused();
  await expect(form.locator('[name="name"]'), 'the answers stay').toHaveValue('Mira Okafor');
  await check('form-changed');
  await page.unroute(ENTRIES);

  await page.route(ENTRIES, (route) =>
    route.fulfill({
      status: 200,
      json: {
        receipt: { reference: 'REQ-2026-00042' },
        outcome: { kind: 'message', html: '<h2>Thank you, Mira</h2>', url: '/en/', delaySeconds: 600 },
      },
    }),
  );
  await pressOnThePage(send(form));
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  await check('redirect');
  await page.unroute(ENTRIES);
}

/** Files on the way, up, stopped and shown, and the signature both ways. */
async function fileStates(page: Page, url: (form: string) => string, check: (state: string) => Promise<void>) {
  await page.goto(url('roomHire'));
  const form = formOn(page);
  await form.locator('input, select, textarea').first().focus();
  await logicCome(form);
  await page.route(UPLOADS, async (route) => {
    const body = route.request().postDataBuffer();
    if (body?.includes('stopped.pdf') === true) await route.abort('connectionreset');
    else if (body?.includes('slow.pdf') === true) await new Promise(() => undefined);
    else await route.continue();
  });
  const document = form.locator('input[type="file"][name="document"]');
  await document.setInputFiles({ name: 'plan.pdf', mimeType: 'application/pdf', buffer: PDF });
  const rows = form.locator('[data-jfb-alias="document"] .jfb-file');
  await expect(rows.first().locator('progress')).toBeHidden();
  await document.setInputFiles({ name: 'stopped.pdf', mimeType: 'application/pdf', buffer: PDF });
  await expect(rows.nth(1)).toHaveAttribute('data-jfb-failed', '');
  await document.setInputFiles({ name: 'slow.pdf', mimeType: 'application/pdf', buffer: PDF });
  await expect(rows.nth(2).locator('progress')).toBeVisible();
  await form
    .locator('input[type="file"][name="photo"]')
    .setInputFiles({ name: 'room.png', mimeType: 'image/png', buffer: PNG });
  await expect(form.locator('[data-jfb-alias="photo"] .jfb-file')).toHaveCount(1);
  await check('uploads');

  const pad = form.locator('canvas.jfb-signature-pad');
  await pad.scrollIntoViewIfNeeded();
  const box = await pad.boundingBox();
  if (box === null) throw new Error('The pad does not show.');
  await page.mouse.move(box.x + box.width * 0.1, box.y + box.height * 0.6);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.4, box.y + box.height * 0.3, { steps: 4 });
  await page.mouse.move(box.x + box.width * 0.7, box.y + box.height * 0.7, { steps: 4 });
  await page.mouse.up();
  await check('signature-drawn');

  await form.locator('[data-jfb-to-typed]').click();
  await form.locator('[name="signature.typed"]').fill('Daniel Fernandes');
  await check('signature-typed');
}

/** Rows and moves, "Other", a counter past its limit and a cascade, on the forms that hold them. */
async function fieldStates(page: Page, url: (form: string) => string, check: (state: string) => Promise<void>) {
  await page.goto(url('feedback'));
  const form = formOn(page);
  await form.locator('[name="comments"]').focus();
  await logicCome(form);
  await form.locator('select[name="branch"]').selectOption('market');
  await expect(form.locator('select[name="desk"] option')).toHaveCount(3);
  await form.locator('[data-jfb-alias="corner"] label').first().click();
  await form.locator('.jfb-other').first().locator('xpath=preceding-sibling::*[1]').locator('label').click();
  await form.locator('.jfb-other input').first().fill('The courtyard');
  await form.locator('[name="comments"]').fill('a'.repeat(2003));
  const up = form.locator('.jfb-ranking li').nth(1).locator('button').first();
  await up.click();
  await expect(form.locator('.jfb-ranking li').first().locator('button').first()).toBeFocused();
  await check('moves-and-followers');

  await page.goto(url('application'));
  await form.locator('[name="fullName.first"]').fill('Ada');
  await form.locator('[name="fullName.last"]').fill('Lovelace');
  await form.locator('[name="email"]').fill('ada@visitor.jfb.test');
  await form.locator('[data-jfb-alias="post"] label').first().click();
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  const repeater = form.locator('.jfb-repeater');
  await expect(repeater).toBeVisible();
  await logicCome(form);
  const add = repeater.locator('> button[name="jfb-nav"]');
  await add.click();
  await expect(repeater.locator('> fieldset')).toHaveCount(3);
  await check('rows-at-their-most');
  await repeater.locator('> fieldset').nth(1).locator('> button[name="jfb-nav"]').click();
  await expect(repeater.locator('> fieldset')).toHaveCount(2);
  await check('rows-at-their-least');
}

for (const variant of VARIANTS) {
  for (const width of [375, 1440]) {
    test.describe(`${variant.name} at ${String(width)} px`, () => {
      test.beforeEach(async ({ page }) => {
        await page.setViewportSize({ width, height: 900 });
        if (variant.rtl) {
          await page.addInitScript(() => {
            document.addEventListener('DOMContentLoaded', () => {
              document.documentElement.dir = 'rtl';
              for (const form of document.querySelectorAll('form.jfb-form')) form.setAttribute('dir', 'rtl');
            });
          });
        }
      });
      const url = (form: string): string => `${pathOfForm(form)}?theme=${variant.theme}&scheme=${variant.scheme}`;

      test('around a send', async ({ page }) => {
        test.setTimeout(180_000);
        await sendingStates(page, url, (state) => holds(page, variant, width, state));
      });

      test('files and the signature', async ({ page }) => {
        test.setTimeout(180_000);
        await fileStates(page, url, (state) => holds(page, variant, width, state));
      });

      test('rows, moves and what follows an answer', async ({ page }) => {
        test.setTimeout(180_000);
        await fieldStates(page, url, (state) => holds(page, variant, width, state));
      });
    });
  }
}
