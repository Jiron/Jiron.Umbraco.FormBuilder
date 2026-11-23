import type { APIRequestContext, Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { settled } from '../../support/axe.ts';
import { logicCome, pressOnThePage } from '../../support/form.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';

// Uploads on the page (ROADMAP 6.4m, ADR-0152), on the demo's room hire form, whose photo has `thumbnails` and `camera` on and
// whose document has them off: an oversize file and a renamed program are refused before any upload request is sent, an
// upload cut off by the server is a failed upload with a retry, a file goes up with its progress and is sent with the entry,
// and axe finds nothing in any state.

/** A picture of one pixel, which a browser can show. */
const PNG = [
  ...Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
    'base64',
  ),
];
const PDF = [...'%PDF-1.7\n'].map((letter) => letter.charCodeAt(0));
const PROGRAM = [0x4d, 0x5a, 0x90, 0, 3, 0, 0, 0];

const fileOf = (
  name: string,
  mimeType: string,
  head: number[],
  size = head.length,
): {
  name: string;
  mimeType: string;
  buffer: Buffer;
} => ({
  name,
  mimeType,
  buffer: Buffer.concat([Buffer.from(head), Buffer.alloc(Math.max(0, size - head.length), 0x20)]),
});

const room = formPages().find((each) => each.form === 'roomHire');
if (room === undefined) throw new Error('The demo has no room hire page.');
const PATH = pathOf(room.texts.en, 'en');

/** The answers the form needs besides its files, a name typed for the signature. */
async function fillTheRest(form: Locator): Promise<void> {
  await form.locator('[name="day"]').fill(new Date(Date.now() + 7 * 24 * 3_600_000).toISOString().slice(0, 10));
  await form.locator('[name="from"]').fill('10:00');
  await form.locator('input[name="room"][value="25"]').check();
  await form.locator('[name="contact.first"]').fill('Daniel');
  await form.locator('[name="contact.last"]').fill('Fernandes');
  await form.locator('[name="email"]').fill('daniel@visitor.jfb.test');
  await form.locator('[name="billing.street"]').fill('Main Street 1');
  await form.locator('[name="billing.postcode"]').fill('10115');
  await form.locator('[name="billing.city"]').fill('Berlin');
  await form.locator('[name="billing.country"]').selectOption('DE');
  await form.getByRole('button', { name: 'Type your name instead' }).click();
  await form.locator('[name="signature.typed"]').fill('Daniel Fernandes');
}

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const photo = (form: Locator): Locator => form.locator('[data-jfb-alias="photo"]');
const documents = (form: Locator): Locator => form.locator('[data-jfb-alias="document"]');

/** Wakes the form's logic and waits for its file fields to take files. */
async function ready(page: Page): Promise<Locator> {
  await page.goto(PATH);
  const form = formOn(page);
  await form.locator('input, select, textarea').first().focus();
  await expect(photo(form).locator('.jfb-upload')).toHaveAttribute('data-jfb-enhanced', '');
  return form;
}

/**
 * Listens to the form's live region from now on, and gives what it has said so far, in order. Two things that are told
 * a moment apart come in the order the machine has them, and the later one replaces the earlier one in the region
 * (ROADMAP 26.1: on GitHub's runner the hire's total was told before a failed upload, on the development machine after).
 */
async function told(form: Locator): Promise<() => Promise<string[]>> {
  const live = form.locator('[data-jfb-live]');
  await live.evaluate((region) => {
    const said: string[] = [];
    (region as HTMLElement & { jfbSaid?: string[] }).jfbSaid = said;
    const hear = (): void => {
      const now = region.textContent.trim();
      if (now !== '' && said.at(-1) !== now) said.push(now);
    };
    hear();
    new MutationObserver(hear).observe(region, { childList: true, characterData: true, subtree: true });
  });
  return () => live.evaluate((region) => [...((region as HTMLElement & { jfbSaid?: string[] }).jfbSaid ?? [])]);
}

async function axe(page: Page, what: string): Promise<void> {
  await settled(page);
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

/** Counts the upload requests a page makes. */
function uploadsOf(page: Page): string[] {
  const seen: string[] = [];
  page.on('request', (request) => {
    if (request.url().includes('/uploads')) seen.push(request.url());
  });
  return seen;
}

async function filesOf(request: APIRequestContext): Promise<{ name: string }[][]> {
  const list = await request.get('/jfb-testing/entries', { params: { form: 'roomHire' } });
  expect(list.status()).toBe(200);
  return ((await list.json()) as { entries: { files: { name: string }[] }[] }).entries.map((entry) => entry.files);
}

test('the page shows its own buttons once it runs, and the browser its input until then', async ({ page }) => {
  await page.goto(PATH);
  const form = formOn(page);
  await expect(photo(form).locator('input[type="file"][name="photo"]')).toBeVisible();
  await expect(photo(form).locator('label')).toHaveCount(3);
  await expect(photo(form).getByText('Choose a file')).toBeHidden();

  await form.locator('input, select, textarea').first().focus();
  await expect(photo(form).locator('.jfb-upload')).toHaveAttribute('data-jfb-enhanced', '');
  await expect(photo(form).getByText('Choose a file')).toBeVisible();
  await expect(documents(form).getByText('Choose files')).toBeVisible();
  await expect(photo(form).getByText('Drag a file here, or')).toBeVisible();
  await axe(page, 'the file fields');
});

test('an oversize file and a renamed program are refused before any upload request is sent', async ({ page }) => {
  const form = await ready(page);
  const uploads = uploadsOf(page);

  await photo(form)
    .locator('input[name="photo"]')
    .setInputFiles(fileOf('huge.png', 'image/png', PNG, 3 * 1024 * 1024));
  const big = photo(form).locator('.jfb-file');
  await expect(big).toHaveAttribute('data-jfb-failed', '');
  await expect(big.locator('.jfb-error')).toContainText('2 MB');

  await documents(form)
    .locator('input[name="document"]')
    .setInputFiles(fileOf('invoice.pdf', 'application/pdf', PROGRAM));
  const program = documents(form).locator('.jfb-file');
  await expect(program).toHaveAttribute('data-jfb-failed', '');
  await expect(program.locator('.jfb-error')).not.toBeEmpty();
  await expect(program.locator('[data-jfb-retry]')).toBeHidden();
  await axe(page, 'files refused');

  expect(uploads, 'nothing went up').toEqual([]);
  await program.getByRole('button', { name: 'Remove invoice.pdf' }).click();
  await expect(documents(form).locator('.jfb-files')).toHaveCount(0);
  await expect(documents(form).locator('input[name="document"]')).toBeFocused();
});

test('an upload cut off by the server is a failed upload with a retry', async ({ page }) => {
  const form = await ready(page);
  let cut = true;
  await page.route('**/jfb/api/v1/forms/*/uploads**', async (route) => {
    if (cut) {
      cut = false;
      await route.abort('connectionreset');
    } else await route.continue();
  });
  await documents(form)
    .locator('input[name="document"]')
    .setInputFiles(fileOf('plan.pdf', 'application/pdf', PDF));

  const row = documents(form).locator('.jfb-file');
  await expect(row).toHaveAttribute('data-jfb-failed', '');
  await expect(row.locator('.jfb-error')).toHaveText('The upload stopped.');
  await expect(form.locator('[data-jfb-live]')).toContainText('The upload stopped');
  await axe(page, 'a stopped upload');

  await row.getByRole('button', { name: 'Try again to upload plan.pdf' }).click();
  await expect(row).not.toHaveAttribute('data-jfb-failed');
  await expect(row.locator('input[type="hidden"][name="document"]')).toHaveCount(1);
  await expect(form.locator('[data-jfb-live]')).toHaveText('plan.pdf is uploaded.');
});

test('a file that failed stays in its row as the visitor moves on, and stops the send until it is dealt with', async ({
  page,
}) => {
  const form = await ready(page);
  const said = await told(form);
  let cut = true;
  await page.route('**/jfb/api/v1/forms/*/uploads**', async (route) => {
    // The signature's image goes at the send too, and goes on.
    if (cut && route.request().postDataBuffer()?.includes('plan.pdf') === true) await route.abort('connectionreset');
    else await route.continue();
  });
  await fillTheRest(form);
  await documents(form)
    .locator('input[name="document"]')
    .setInputFiles(fileOf('plan.pdf', 'application/pdf', PDF));
  const row = documents(form).locator('.jfb-file');
  await expect(row).toHaveAttribute('data-jfb-failed', '');

  await form.locator('[name="email"]').focus();
  await form.locator('[name="email"]').blur();
  await expect(row, 'leaving the field leaves it').toHaveAttribute('data-jfb-failed', '');

  const entries: string[] = [];
  page.on('request', (request) => {
    if (request.url().endsWith('/entries')) entries.push(request.url());
  });
  // The hire's total is told a moment after its answers, before or after the failed upload as the machine has it; it
  // is not to speak over what this test listens for, so it has been told by now.
  await expect
    .poll(async () => (await said()).some((each) => each.includes('Room hire')), 'the total has been told')
    .toBe(true);
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(row.locator('[data-jfb-retry]')).toBeFocused();
  await expect(form.locator('[data-jfb-live]')).toContainText('The upload stopped');
  expect(entries, 'nothing goes without it').toEqual([]);

  cut = false;
  await logicCome(form);
  await page.keyboard.press('Enter');
  await expect(row).not.toHaveAttribute('data-jfb-failed');
  await expect(form.locator('[data-jfb-live]')).toHaveText('plan.pdf is uploaded.');
  const answered = page.waitForResponse((response) => response.url().endsWith('/entries'));
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  const answer = await answered;
  expect(answer.status(), await answer.text()).toBe(200);
});

test('the site that cannot take a file now says to try later, with the other answers kept', async ({ page }) => {
  const form = await ready(page);
  await page.route('**/jfb/api/v1/forms/*/uploads**', (route) =>
    route.fulfill({ status: 503, headers: { 'Retry-After': '300' }, json: { code: 'uploads_full' } }),
  );
  await form.locator('[name="contact.first"]').fill('Daniel');
  await documents(form)
    .locator('input[name="document"]')
    .setInputFiles(fileOf('plan.pdf', 'application/pdf', PDF));

  const row = documents(form).locator('.jfb-file');
  await expect(row.locator('.jfb-error')).toContainText('Try again later.');
  await expect(row.locator('[data-jfb-retry]')).toBeHidden();
  await expect(form.locator('[name="contact.first"]')).toHaveValue('Daniel');
});

test('a file goes up with its progress, and is sent with the entry', async ({ page, request }) => {
  const form = await ready(page);
  // The answer waits, so the row can be seen on its way.
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/jfb/api/v1/forms/*/uploads**', async (route) => {
    await held;
    await route.continue();
  });
  await documents(form)
    .locator('input[name="document"]')
    .setInputFiles(fileOf('progress.pdf', 'application/pdf', PDF, 200_000));
  const row = documents(form).locator('.jfb-file');
  await expect(row).toHaveAttribute('data-jfb-busy', '');
  await expect(row.locator('.jfb-help')).toHaveText(/\d+% uploaded/);
  await expect(row.locator('progress')).toBeAttached();
  await expect(row.getByRole('button', { name: 'Stop uploading progress.pdf' })).toBeVisible();
  await axe(page, 'a file on its way');
  release();
  await expect(row).not.toHaveAttribute('data-jfb-busy');
  await expect(row.locator('.jfb-help')).toHaveText(/195\.3 KB/);
  await expect(row.locator('progress')).toBeHidden();
  await expect(row.getByRole('button', { name: 'Remove progress.pdf' })).toBeVisible();

  await fillTheRest(form);
  const left: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest()) left.push(request.url());
  });
  const answered = page.waitForResponse((response) => response.url().includes('/entries'));
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  const answer = await answered;

  expect(answer.status(), await answer.text()).toBe(200);
  const values = (answer.request().postDataJSON() as { values: Record<string, string[]> }).values;
  expect(values['document'], 'the token is the answer').toHaveLength(1);
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  expect(left, 'the page stayed').toEqual([]);
  await expect
    .poll(async () => (await filesOf(request)).flat().some((file) => file.name === 'progress.pdf'), { timeout: 15_000 })
    .toBe(true);
});

test('thumbnails and the camera follow their settings', async ({ page }) => {
  const form = await ready(page);
  await documents(form)
    .locator('input[name="document"]')
    .setInputFiles(fileOf('off.png', 'image/png', PNG));
  await expect(documents(form).locator('.jfb-file .jfb-icon-file'), 'off: an icon').toBeVisible();
  await expect(documents(form).locator('.jfb-file img')).toHaveCount(0);

  await photo(form)
    .locator('input[name="photo"]')
    .setInputFiles(fileOf('on.png', 'image/png', PNG));
  await expect(photo(form).locator('.jfb-file img'), 'on: its picture').toHaveAttribute('src', /^blob:/);
  await expect(photo(form).locator('.jfb-file .jfb-icon-file')).toHaveCount(0);

  await expect(photo(form).locator('input.jfb-upload-camera')).toHaveCount(1);
  await expect(photo(form).locator('input.jfb-upload-camera')).toHaveAttribute('capture', 'environment');
  await expect(documents(form).locator('.jfb-upload-camera'), 'off: no camera').toHaveCount(0);
  await expect(photo(form).locator('label.jfb-upload-camera'), 'and none on a screen without touch').toBeHidden();
});

test.describe('on a touch screen', () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 375, height: 800 } });

  test('"Take a photo" shows only where the setting is on, and the drop frame goes', async ({ page }) => {
    const form = await ready(page);

    await expect(photo(form).getByText('Take a photo')).toBeVisible();
    await expect(documents(form).getByText('Take a photo')).toHaveCount(0);
    await expect(photo(form).getByText('Drag a file here, or')).toBeHidden();
    await axe(page, 'the file fields on a phone');
  });
});

test('a file dropped on the zone goes up, with the zone marked while it is over', async ({ page }) => {
  const form = await ready(page);
  const zone = documents(form).locator('.jfb-upload');
  const transfer = await page.evaluateHandle((bytes) => {
    const data = new DataTransfer();
    data.items.add(new File([new Uint8Array(bytes)], 'dropped.pdf', { type: 'application/pdf' }));
    return data;
  }, PDF);
  await zone.dispatchEvent('dragover', { dataTransfer: transfer });
  await expect(zone).toHaveAttribute('data-jfb-drop', '');
  await zone.dispatchEvent('drop', { dataTransfer: transfer });

  await expect(zone).not.toHaveAttribute('data-jfb-drop');
  await expect(documents(form).locator('.jfb-file strong')).toHaveText('dropped.pdf');
  await expect(documents(form).locator('.jfb-file input[type="hidden"]')).toHaveCount(1);
});
