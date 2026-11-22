import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { settled } from '../../support/axe.ts';
import { pressOnThePage } from '../../support/form.ts';
import { LANGUAGES, formPages, pathOf, type Language } from '../../support/demo-content.ts';
import { demoProject } from '../../support/paths.ts';

// Calculations on the page (ROADMAP 6.4f, ADR-0114), on the demo's room hire in English and German: the cost follows
// the room and the hours as the visitor answers, in its field's format, a room the visitor describes leaves it
// without a value, and the slider's value follows its thumb beside it and in its `aria-valuetext`; what the page shows
// is what the server stores. The calculation language loads as its own chunk, once, and never on a form without a
// calculation. axe finds no WCAG 2.2 A or AA violation, and nothing writes an error to the console.

const pathOfForm = (alias: string, language: Language): string => {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo has no ${alias} page.`);
  return pathOf(shown.texts[language], language);
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** The key of a demo form's field, from its definition. */
function keyOf(alias: string, field: string): string {
  const definition = JSON.parse(readFileSync(join(demoProject, 'Seeding', 'Forms', `${alias}.json`), 'utf8')) as {
    pages: { groups: { fields: { key: string; alias?: string }[] }[] }[];
  };
  const found = definition.pages
    .flatMap((page) => page.groups.flatMap((group) => group.fields))
    .find((each) => each.alias === field);
  if (found === undefined) throw new Error(`The demo's ${alias} has no field ${field}.`);
  return found.key;
}

/** What each entry of a demo form stores for a field, oldest first. */
async function storedOf(request: APIRequestContext, alias: string, field: string) {
  const response = await request.get('/jfb-testing/entries', { params: { form: alias } });
  expect(response.status()).toBe(200);
  const entries = ((await response.json()) as { entries: { values: { field: string; number: number | null }[] }[] })
    .entries;
  return entries.map((entry) => entry.values.filter((value) => value.field === field).map((value) => value.number));
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

/** What a page does: its console errors and uncaught exceptions, and the runtime's chunks it asks for. */
function watch(page: Page) {
  const errors: string[] = [];
  const chunks: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    const chunk = /\/chunks\/([a-z.]+)-[^/]*\.js(\?|$)/.exec(request.url());
    if (chunk?.[1] !== undefined) chunks.push(chunk[1]);
  });
  return { errors, chunks };
}

const WORDS: Record<Language, { cost: (euros: string) => string; hours: (count: string) => string }> = {
  en: { cost: (euros) => `EUR ${euros}.00`, hours: (count) => `${count} hours` },
  de: { cost: (euros) => `EUR ${euros},00`, hours: (count) => `${count} Stunden` },
};

for (const language of LANGUAGES) {
  test(`${language}: the room hire's cost follows the room and the hours as the visitor answers, none for a room the library must quote, and the slider's value follows its thumb`, async ({
    page,
  }) => {
    const { errors, chunks } = watch(page);
    const words = WORDS[language];
    await page.goto(pathOfForm('roomHire', language));
    const form = formOn(page);
    const cost = form.locator('output#jfb-1-cost');
    const hours = form.locator('input#jfb-1-hours');
    const shown = form.locator('output[for="jfb-1-hours"]');
    await expect(cost, "the server's cost for the reading room and the slider's middle").toHaveText(words.cost('75'));

    await hours.fill('3');
    await expect(cost, 'three hours of the reading room').toHaveText(words.cost('45'));
    await expect(shown).toHaveText(words.hours('3'));
    await expect(hours).toHaveAttribute('aria-valuetext', words.hours('3'));
    await hours.press('ArrowRight');
    await expect(shown, 'moved by the keyboard').toHaveText(words.hours('4'));
    await expect(cost).toHaveText(words.cost('60'));

    await form.locator('input[name="room"][value="25"]').check();
    await expect(cost, 'four hours of the hall').toHaveText(words.cost('100'));

    const after = form.locator('[data-jfb-alias="keyPickup"]');
    const gap = async (): Promise<number> =>
      ((await after.boundingBox())?.y ?? 0) - ((await cost.boundingBox())?.y ?? 0);
    const held = await gap();
    await form.locator('input[name="room"][value=""]').check();
    await form.locator('input[name="room.other"]').fill('A room for forty people');
    await expect(cost, 'a room the library must quote first').toHaveText('');
    expect(await gap(), 'what follows the cost stays where it was').toBe(held);
    await axe(page, `${language}: the room hire with its cost worked out`);

    await form.locator('input[name="room"][value="15"]').check();
    await expect(cost).toHaveText(words.cost('60'));
    expect(
      chunks.filter((chunk) => chunk === 'calc'),
      'the calculation language, once',
    ).toEqual(['calc']);
    expect(errors, 'console errors').toEqual([]);
  });
}

test('the room hire stores the cost the page showed for the answers sent', async ({ page, request }) => {
  const before = (await storedOf(request, 'roomHire', keyOf('roomHire', 'cost'))).length;
  const day = new Date(Date.now() + 7 * 24 * 3_600_000).toISOString().slice(0, 10);
  await page.goto(pathOfForm('roomHire', 'en'));
  const form = formOn(page);
  await form.locator('[name="day"]').fill(day);
  await form.locator('[name="from"]').fill('10:00');
  await form.locator('input[name="room"][value="25"]').check();
  await form.locator('input#jfb-1-hours').fill('3');
  await expect(form.locator('output#jfb-1-cost')).toHaveText('EUR 75.00');
  await form.locator('[name="contact.first"]').fill('Daniel');
  await form.locator('[name="contact.last"]').fill('Fernandes');
  await form.locator('[name="email"]').fill('daniel@visitor.jfb.test');
  await form.locator('[name="billing.street"]').fill('Main Street 1');
  await form.locator('[name="billing.postcode"]').fill('10115');
  await form.locator('[name="billing.city"]').fill('Berlin');
  await form.locator('[name="billing.country"]').selectOption('DE');
  await form.getByRole('button', { name: 'Type your name instead' }).click();
  await form.locator('[name="signature.typed"]').fill('Daniel Fernandes');

  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  const entries = (await storedOf(request, 'roomHire', keyOf('roomHire', 'cost'))).slice(before);
  expect(entries, 'one entry, its cost the 75 the page showed').toEqual([[75]]);
});

test('a form without a calculation never loads the calculation language', async ({ page }) => {
  const { errors, chunks } = watch(page);
  await page.goto(pathOfForm('contact', 'en'));
  const form = formOn(page);
  // The contact form fills its query prefill as it shows, so its chunk may load before the visitor touches it.
  await form.locator('input, textarea').first().fill('Ada');
  await expect.poll(() => chunks, "the form's chunk").toContain('form');
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(form.locator('[data-jfb-summary]'), 'the runtime refused the send').toBeVisible();
  await page.waitForLoadState('networkidle');
  expect(chunks).toContain('form');
  expect(chunks).not.toContain('calc');
  expect(errors, 'console errors').toEqual([]);
});
