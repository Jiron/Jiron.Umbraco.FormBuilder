import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { coverEveryMember, uncoveredAtReady } from '../../support/form-cover.ts';

// The kitchen sink by keyboard alone (ROADMAP 6.4p): the demo's specimen, a form with a field of every built-in type on two pages,
// answered and sent with Tab, Space, Enter, the arrows and typing, from the top of the page to the entry the server stores. Reading
// where the focus stands is the test's; everything that changes the page goes through the keyboard. The journey runs a second
// time with every member of the form covered as fields of those names would cover it (support/form-cover.ts), the browser's proof
// that the runtime and its chunks read none of the form's members plainly; so its locators start at the document.

const RUN = Date.now().toString(36);

const pathOfSpecimen = (): string => {
  const found = formPages().find((each) => each.form === 'specimen');
  if (found === undefined) throw new Error('The demo has no specimen.');
  return pathOf(found.texts.en, 'en');
};

/** What is typed into a text control, by its name, marked as one run's. */
const typedFor = (mark: string): Readonly<Record<string, string>> => ({
  displayName: `Mira ${mark}`,
  email: 'mira.okafor@visitor.jfb.test',
  'email.confirm': 'mira.okafor@visitor.jfb.test',
  copies: '2',
  message: 'Typed without a mouse.',
  'interests.other': 'The courtyard',
  'contact.first': 'Mira',
  'contact.last': 'Okafor',
  'guests[0].guestName': `Guest ${mark}`,
  'signature.typed': 'Mira Okafor',
  anythingElse: `Sent by keyboard ${mark}`,
});

interface Focused {
  readonly inForm: boolean;
  readonly tag: string;
  readonly type: string;
  readonly name: string;
  readonly value: string;
  readonly nav: string;
  readonly toTyped: boolean;
  readonly row: boolean;
}

const focusedOn = (page: Page): Promise<Focused> =>
  page.evaluate(() => {
    const element = document.activeElement as HTMLInputElement | null;
    return {
      inForm: element?.closest('main form.jfb-form') != null,
      tag: element?.localName ?? '',
      type: element?.type ?? '',
      name: element?.getAttribute('name') ?? '',
      value: element?.value ?? '',
      nav: element?.dataset['jfbNav'] ?? '',
      toTyped: element?.hasAttribute('data-jfb-to-typed') === true,
      row: element?.closest('[data-jfb-row]') != null,
    };
  });

/** Tabs through a page, answering each control the focus reaches, up to the button that ends the page, which it presses. */
async function answerByKeyboard(page: Page, typed: Readonly<Record<string, string>>): Promise<string> {
  const pressed = new Set<string>();
  const once = (what: string): boolean => {
    if (pressed.has(what)) return false;
    pressed.add(what);
    return true;
  };
  for (let press = 0; press < 400; press += 1) {
    await page.keyboard.press('Tab');
    const at = await focusedOn(page);
    if (!at.inForm) continue;
    if (at.nav === 'next' || at.nav === 'submit') {
      await page.keyboard.press('Enter');
      return at.nav;
    }
    if (at.toTyped) {
      await page.keyboard.press('Enter');
      continue;
    }
    if (at.tag === 'button') {
      // The ranking's first move, and Add, whose new row takes the focus.
      if (at.value.startsWith('down:') && once('moved')) await page.keyboard.press('Enter');
      if (at.value.startsWith('add:') && once('added')) {
        await page.keyboard.press('Enter');
        await expect.poll(async () => (await focusedOn(page)).row, 'the new row takes the focus').toBe(true);
        const row = await focusedOn(page);
        if (typed[row.name] !== undefined) await page.keyboard.type(typed[row.name] ?? '');
      }
      continue;
    }
    if (at.tag === 'select') {
      if (at.name === 'branch' || at.name === 'topics') await page.keyboard.press('ArrowDown');
      continue;
    }
    if (at.type === 'radio') {
      await page.keyboard.press('Space');
      continue;
    }
    if (at.type === 'checkbox') {
      // Each box of a group is its own stop; "Other" is the one with a text, which follows.
      if (at.name === 'privacy' || at.name === 'reminder' || at.name === 'newsletter' || at.value === '')
        await page.keyboard.press('Space');
      continue;
    }
    if (at.type === 'range') {
      await page.keyboard.press('ArrowRight');
      continue;
    }
    if (typed[at.name] !== undefined && at.value === '') await page.keyboard.type(typed[at.name] ?? '');
  }
  throw new Error('The focus never reached the button that ends the page.');
}

async function entriesWith(request: APIRequestContext, mark: string): Promise<string[]> {
  const response = await request.get('/jfb-testing/entries', { params: { form: 'specimen' } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: { values: unknown }[] }).entries
    .map((entry) => JSON.stringify(entry.values))
    .filter((values) => values.includes(mark));
}

/** The journey, from the top of the page to the stored entry, its texts marked as this run's. */
async function completeByKeyboard(page: Page, request: APIRequestContext, mark: string): Promise<void> {
  const typed = typedFor(mark);
  await page.goto(pathOfSpecimen());

  expect(await answerByKeyboard(page, typed), 'the first page ends with Next').toBe('next');
  await expect(page.locator('[data-jfb-summary]'), 'every answer it needs was given').toHaveCount(0);
  await expect(page.locator('[name="anythingElse"]'), 'the second page').toBeVisible();

  expect(await answerByKeyboard(page, typed), 'the second page ends with the send').toBe('submit');
  await expect(page.locator('main [data-jfb-outcome]')).toBeVisible();

  const [entry, ...others] = await entriesWith(request, `Sent by keyboard ${mark}`);
  expect(others, 'one entry').toEqual([]);
  expect(entry).toContain(`Mira ${mark}`);
  expect(entry, 'its row').toContain(`Guest ${mark}`);
  expect(entry, 'its "Other"').toContain('The courtyard');
}

test('the keyboard alone completes the kitchen sink, from the top of the page to the stored entry', async ({
  page,
  request,
}) => {
  test.setTimeout(240_000);
  await completeByKeyboard(page, request, `${RUN}a`);
});

test('the keyboard alone completes the kitchen sink with every member of the form covered', async ({
  page,
  request,
}) => {
  test.setTimeout(240_000);
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await coverEveryMember(page);
  await completeByKeyboard(page, request, `${RUN}b`);
  const uncovered = await uncoveredAtReady(page);
  expect(uncovered.length, 'a jfb:ready for each form').toBeGreaterThan(0);
  expect(uncovered.flat(), "names a plain read got a form's member for at jfb:ready").toEqual([]);
  expect(errors).toEqual([]);
});
