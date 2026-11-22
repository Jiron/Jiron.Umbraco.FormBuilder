import type { APIRequestContext, Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { settled } from '../../support/axe.ts';
import { pressOnThePage } from '../../support/form.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';

// The signature pad (ROADMAP 6.4n, ADR-0153), on the demo's room hire form, of one page with a required signature: a signature
// is drawn, cleared and drawn again, or typed instead, and each is sent and stored; a required file still on its way is an
// answer; a refused signature's name is marked as the server marks it; axe finds nothing in any state.

const room = formPages().find((each) => each.form === 'roomHire');
if (room === undefined) throw new Error('The demo has no room hire page.');
const PATH = pathOf(room.texts.en, 'en');

interface StoredFile {
  readonly name: string;
  readonly size: number;
  readonly typed?: string | null;
}

/** A run's own mark: a host may keep its database from the run before. */
const RUN = Date.now().toString(36);

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const signatureOf = (form: Locator): Locator => form.locator('[data-jfb-alias="signature"]');

async function ready(page: Page): Promise<Locator> {
  await page.goto(PATH);
  const form = formOn(page);
  await form.locator('input, select, textarea').first().focus();
  await expect(signatureOf(form).locator('.jfb-signature')).toHaveAttribute('data-jfb-enhanced', '');
  return form;
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

/** Draws a stroke on the pad with the mouse, across it. */
async function sign(page: Page, form: Locator): Promise<void> {
  const pad = signatureOf(form).locator('canvas');
  await pad.scrollIntoViewIfNeeded();
  const box = await pad.boundingBox();
  if (box === null) throw new Error('The pad does not show.');
  await page.mouse.move(box.x + box.width * 0.1, box.y + box.height * 0.6);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.3, box.y + box.height * 0.3, { steps: 4 });
  await page.mouse.move(box.x + box.width * 0.6, box.y + box.height * 0.7, { steps: 4 });
  await page.mouse.up();
}

const inkOf = (form: Locator): Promise<number> =>
  signatureOf(form)
    .locator('canvas')
    .evaluate((canvas: HTMLCanvasElement) => {
      const { data } = (canvas.getContext('2d') as CanvasRenderingContext2D).getImageData(
        0,
        0,
        canvas.width,
        canvas.height,
      );
      let inked = 0;
      for (let at = 3; at < data.length; at += 4) if ((data[at] as number) > 0) inked += 1;
      return inked;
    });

/** The answers the room hire form needs besides its signature, under an address that finds the entry again: other tests send this form too. */
async function fillTheRest(form: Locator, visitor: string): Promise<void> {
  await form.locator('[name="day"]').fill(new Date(Date.now() + 7 * 24 * 3_600_000).toISOString().slice(0, 10));
  await form.locator('[name="from"]').fill('10:00');
  await form.locator('input[name="room"][value="25"]').check();
  await form.locator('[name="contact.first"]').fill('Daniel');
  await form.locator('[name="contact.last"]').fill('Fernandes');
  await form.locator('[name="email"]').fill(`${visitor}-${RUN}@visitor.jfb.test`);
  await form.locator('[name="billing.street"]').fill('Main Street 1');
  await form.locator('[name="billing.postcode"]').fill('10115');
  await form.locator('[name="billing.city"]').fill('Berlin');
  await form.locator('[name="billing.country"]').selectOption('DE');
}

/** The files of each entry that visitor sent. */
async function filesOf(request: APIRequestContext, visitor: string): Promise<StoredFile[][]> {
  const response = await request.get('/jfb-testing/entries', { params: { form: 'roomHire' } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: { values: unknown; files: StoredFile[] }[] }).entries
    .filter((entry) => JSON.stringify(entry.values).includes(`${visitor}-${RUN}@`))
    .map((entry) => entry.files);
}

async function send(page: Page, form: Locator): Promise<void> {
  const answered = page.waitForResponse((response) => response.url().endsWith('/entries'));
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  const answer = await answered;
  expect(answer.status(), await answer.text()).toBe(200);
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
}

test('the pad and its way to typing show once the runtime runs, and the typed name until then', async ({ page }) => {
  await page.goto(PATH);
  const form = formOn(page);
  const signature = signatureOf(form);
  await expect(signature.locator('input[name="signature.typed"]')).toBeVisible();
  await expect(signature.locator('canvas')).toBeHidden();
  await expect(signature.getByRole('button', { name: 'Sign by hand instead' })).toBeHidden();

  await form.locator('input, select, textarea').first().focus();
  await expect(signature.locator('canvas')).toBeVisible();
  await expect(signature.locator('input[name="signature.typed"]')).toBeHidden();
  await expect(signature.getByRole('button', { name: 'Type your name instead' })).toBeVisible();
  await expect(signature.getByRole('button', { name: 'Clear', exact: true })).toBeHidden();
  await axe(page, 'the pad');
});

test('a signature is drawn, cleared, brought back and drawn again, and sent with the entry', async ({
  page,
  request,
}) => {
  const form = await ready(page);
  await fillTheRest(form, 'drawn');
  const signature = signatureOf(form);
  const clear = signature.locator('[data-jfb-clear]');

  await sign(page, form);
  const drawn = await inkOf(form);
  expect(drawn, 'the stroke shows').toBeGreaterThan(100);
  await expect(clear).toHaveText('Clear');

  await clear.click();
  expect(await inkOf(form), 'the pad is empty').toBe(0);
  await expect(clear).toHaveText('Undo clear');

  await clear.click();
  // A browser may smooth the same strokes a little otherwise once a page has read the pad.
  expect(await inkOf(form), 'and its strokes back').toBeGreaterThan(drawn * 0.8);
  await expect(clear).toHaveText('Clear');

  await clear.click();
  await sign(page, form);
  await expect(clear).toHaveText('Clear');
  await axe(page, 'a drawn signature');

  const uploads: string[] = [];
  page.on('request', (each) => {
    if (each.url().includes('/uploads')) uploads.push(each.url());
  });
  await send(page, form);

  expect(uploads, 'it goes once, at the send').toHaveLength(1);
  const entries = await filesOf(request, 'drawn');
  expect(entries).toHaveLength(1);
  const stored = entries[0]?.[0];
  expect(stored?.name).toBe('signature.png');
  expect(stored?.typed ?? null, 'a drawing holds no name').toBeNull();
});

test('a name typed instead is drawn by the browser, and the server reads the name back from it', async ({
  page,
  request,
}) => {
  const form = await ready(page);
  await fillTheRest(form, 'typed');
  const signature = signatureOf(form);
  await sign(page, form);

  await signature.getByRole('button', { name: 'Type your name instead' }).click();
  const typed = signature.locator('input[name="signature.typed"]');
  await expect(typed).toBeFocused();
  await expect(signature.locator('canvas')).toBeHidden();
  await signature.getByRole('button', { name: 'Sign by hand instead' }).click();
  await expect(signature.locator('canvas')).toBeVisible();
  expect(await inkOf(form), 'the drawing is kept').toBeGreaterThan(100);
  await signature.getByRole('button', { name: 'Type your name instead' }).click();
  await typed.fill('  Daniel   Fernandes ');
  await axe(page, 'a typed signature');
  await send(page, form);

  const entries = await filesOf(request, 'typed');
  expect(entries).toHaveLength(1);
  expect(entries[0]?.[0]).toMatchObject({ name: 'signature.png', typed: 'Daniel Fernandes' });
});

test('no signature is an error at its field, whose summary link leads the keyboard to typing', async ({ page }) => {
  const form = await ready(page);
  await fillTheRest(form, 'unsigned');
  const signature = signatureOf(form);
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));

  const error = signature.locator('.jfb-error');
  await expect(error).toHaveText('Add your signature, or type your name instead');
  await expect(signature.locator('[data-jfb-typed] input')).toHaveAttribute('aria-invalid', 'true');
  await expect(signature.locator('canvas')).toHaveCSS('border-color', /.+/);
  await axe(page, 'the signature refused');

  await form.locator('[data-jfb-summary] a', { hasText: 'Add your signature' }).click();
  await expect(signature.getByRole('button', { name: 'Type your name instead' })).toBeFocused();

  await sign(page, form);
  await expect(error, 'the error goes with the first stroke').toHaveCount(0);
  await expect(signature.locator('[data-jfb-typed] input')).not.toHaveAttribute('aria-invalid');
});

test('a name typed before is no answer once the visitor is back on the empty pad', async ({ page }) => {
  const form = await ready(page);
  const signature = signatureOf(form);
  await signature.getByRole('button', { name: 'Type your name instead' }).click();
  await signature.locator('input[name="signature.typed"]').fill('Daniel Fernandes');
  await signature.getByRole('button', { name: 'Sign by hand instead' }).click();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));

  await expect(signature.locator('.jfb-error')).toHaveText('Add your signature, or type your name instead');
});

test('a signature the site cannot take shows the notice of a failed send, and the next press sends it', async ({
  page,
  request,
}) => {
  const form = await ready(page);
  await fillTheRest(form, 'refused');
  await sign(page, form);
  let refuse = true;
  await page.route('**/jfb/api/v1/forms/*/uploads**', async (route) => {
    if (refuse) await route.fulfill({ status: 503, json: { code: 'uploads_full' } });
    else await route.continue();
  });
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(form.locator('[data-jfb-notice="server_error"]')).toBeFocused();
  await expect(form.locator('[name="contact.first"]'), 'the answers are kept').toHaveValue('Daniel');

  refuse = false;
  await send(page, form);
  expect(await filesOf(request, 'refused')).toHaveLength(1);
});

test.describe('on a touch screen', () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 375, height: 800 } });

  test('the finger signs, without scrolling the page', async ({ page }) => {
    const form = await ready(page);
    const pad = signatureOf(form).locator('canvas');
    await pad.scrollIntoViewIfNeeded();
    await expect(pad).toHaveCSS('touch-action', 'none');
    const box = await pad.boundingBox();
    if (box === null) throw new Error('The pad does not show.');
    expect(box.width).toBeLessThanOrEqual(375);

    const before = await page.evaluate(() => window.scrollY);
    const cdp = await page.context().newCDPSession(page);
    const point = (x: number, y: number): { x: number; y: number } => ({ x: box.x + x, y: box.y + y });
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [point(20, 20)] });
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [point(120, 60)] });
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [point(200, 20)] });
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });

    await expect.poll(() => inkOf(form)).toBeGreaterThan(50);
    expect(await page.evaluate(() => window.scrollY)).toBe(before);
    await axe(page, 'the pad on a phone');
  });
});

test('a required file still on its way is an answer, and a send waits for it', async ({ page }) => {
  // The room hire's plan needs no answer, so what the page reads shows it counts: the unit tests hold what a required field does.
  const form = await ready(page);
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/jfb/api/v1/forms/*/uploads**', async (route) => {
    await held;
    await route.continue();
  });
  const plan = form.locator('[data-jfb-alias="plan"]');
  await plan.locator('input[name="plan"]').setInputFiles({
    name: 'plan.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.7\n'),
  });
  await expect(plan.locator('.jfb-file')).toHaveAttribute('data-jfb-busy', '');
  const before = await page.evaluate(
    (element) =>
      (
        window as unknown as { JironFormBuilder: { get(form: Element): { values(): unknown } | null } }
      ).JironFormBuilder.get(element)?.values(),
    await form.elementHandle(),
  );
  expect((before as Record<string, unknown>)['plan'], 'the page counts it').toBeTruthy();
  release();
  await expect(plan.locator('.jfb-file')).not.toHaveAttribute('data-jfb-busy');
});

test('a form with a file and a signature sends both on the page', async ({ page, request }) => {
  const form = await ready(page);
  await fillTheRest(form, 'both');
  await form.locator('input[name="plan"]').setInputFiles({
    name: 'plan.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.7\n'),
  });
  await sign(page, form);
  await send(page, form);

  const entries = await filesOf(request, 'both');
  expect(entries).toHaveLength(1);
  expect(entries[0]?.map((file) => file.name).sort()).toEqual(['plan.pdf', 'signature.png']);
});
