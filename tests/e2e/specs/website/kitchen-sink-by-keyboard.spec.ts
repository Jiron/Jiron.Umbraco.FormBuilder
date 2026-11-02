import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';

// The kitchen sink by keyboard alone (ROADMAP 6.4p): the demo's specimen, a form with a field of every built-in type on two pages,
// answered and sent with Tab, Space, Enter, the arrows and typing, from the top of the page to the entry the server stores. Reading
// where the focus stands is the test's; everything that changes the page goes through the keyboard.

const RUN = Date.now().toString(36);

const pathOfSpecimen = (): string => {
  const found = formPages().find((each) => each.form === 'specimen');
  if (found === undefined) throw new Error('The demo has no specimen.');
  return pathOf(found.texts.en, 'en');
};

/** What is typed into a text control, by its name. */
const TYPED: Readonly<Record<string, string>> = {
  displayName: `Mira ${RUN}`,
  email: 'mira.okafor@visitor.jfb.test',
  'email.confirm': 'mira.okafor@visitor.jfb.test',
  copies: '2',
  message: 'Typed without a mouse.',
  'interests.other': 'The courtyard',
  'contact.first': 'Mira',
  'contact.last': 'Okafor',
  'guests[0].guestName': `Guest ${RUN}`,
  'signature.typed': 'Mira Okafor',
  anythingElse: `Sent by keyboard ${RUN}`,
};

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
async function answerByKeyboard(page: Page): Promise<string> {
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
        if (TYPED[row.name] !== undefined) await page.keyboard.type(TYPED[row.name] ?? '');
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
    if (TYPED[at.name] !== undefined && at.value === '') await page.keyboard.type(TYPED[at.name] ?? '');
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

test('the keyboard alone completes the kitchen sink, from the top of the page to the stored entry', async ({
  page,
  request,
}) => {
  test.setTimeout(240_000);
  await page.goto(pathOfSpecimen());

  expect(await answerByKeyboard(page), 'the first page ends with Next').toBe('next');
  const form = page.locator('main form.jfb-form');
  await expect(form.locator('[data-jfb-summary]'), 'every answer it needs was given').toHaveCount(0);
  await expect(form.locator('[name="anythingElse"]'), 'the second page').toBeVisible();

  expect(await answerByKeyboard(page), 'the second page ends with the send').toBe('submit');
  await expect(page.locator('main [data-jfb-outcome]')).toBeVisible();

  const [entry, ...others] = await entriesWith(request, `Sent by keyboard ${RUN}`);
  expect(others, 'one entry').toEqual([]);
  expect(entry).toContain(`Mira ${RUN}`);
  expect(entry, 'its row').toContain(`Guest ${RUN}`);
  expect(entry, 'its "Other"').toContain('The courtyard');
});
