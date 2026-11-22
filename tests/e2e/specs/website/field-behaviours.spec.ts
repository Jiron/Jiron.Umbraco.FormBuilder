import type { APIRequestContext, Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { settled } from '../../support/axe.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { logicCome, pressOnThePage } from '../../support/form.ts';

// The field behaviours on the page (ROADMAP 6.4o, ADR-0154), on the demo's forms: the repeater's rows at their limits, a counter
// past its limit, a card chosen, "Other" with its text, a text area growing with `autoGrow` on and keeping its rows with it off,
// a ranking moved by its buttons with the announcement, the grid answered on a phone, a cascading field's options following its
// parent; each sent and stored as the server reads it, and axe finds nothing.

const pathOfForm = (form: string): string => {
  const found = formPages().find((each) => each.form === form);
  if (found === undefined) throw new Error(`The demo has no page for ${form}.`);
  return pathOf(found.texts.en, 'en');
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** A run's own mark: a host may keep its database from the run before, and other tests send these forms too. */
const RUN = Date.now().toString(36);

async function wake(page: Page, form: string): Promise<Locator> {
  await page.goto(pathOfForm(form));
  const found = formOn(page);
  await found.locator('input:not([type="hidden"]), select, textarea').first().focus();
  // The behaviours come with the form's first interaction, and the page's checks with them.
  await logicCome(found);
  return found;
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

interface StoredValue {
  readonly field: string;
  readonly row: number | null;
  readonly text: string | null;
  readonly textKey: string | null;
}

interface Stored {
  readonly values: readonly StoredValue[];
}

/** What an entry stores for a field of the demo, by the end of its key in the seed. */
const inField = (entry: Stored | undefined, key: string): readonly StoredValue[] =>
  (entry?.values ?? []).filter((value) => value.field.endsWith(key));

/** The entries a run's mark stands in, as the server stored them. */
async function storedOf(request: APIRequestContext, form: string, mark: string): Promise<Stored[]> {
  const response = await request.get('/jfb-testing/entries', { params: { form } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: Stored[] }).entries.filter((entry) =>
    JSON.stringify(entry.values).includes(mark),
  );
}

async function send(page: Page, form: Locator): Promise<void> {
  const answered = page.waitForResponse((response) => response.url().endsWith('/entries'));
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  const answer = await answered;
  expect(answer.status(), await answer.text()).toBe(200);
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
}

// The form's second page holds the rows, each page posted as the server pages it until 6.5d; the workshop's are for members,
// whose entries the server takes from 14.3 on.
test('the application: a card chosen, rows added and removed in place to their limits, and all of it stored', async ({
  page,
  request,
}) => {
  const form = await wake(page, 'application');
  await form.locator('[name="fullName.first"]').fill('Ada');
  await form.locator('[name="fullName.last"]').fill(`Applicant ${RUN}`);
  await form.locator('[name="email"]').fill(`${RUN}@visitor.jfb.test`);
  const card = form.locator('[data-jfb-alias="post"] .jfb-choice').nth(1);
  const plain = await card.evaluate((element) => getComputedStyle(element).borderColor);
  await card.locator('label').click();
  await expect(card.locator('input')).toBeChecked();
  await expect.poll(() => card.evaluate((element) => getComputedStyle(element).borderColor)).not.toBe(plain);
  await axe(page, 'a card chosen');
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));

  const repeater = form.locator('.jfb-repeater');
  await expect(repeater).toBeVisible();
  await logicCome(form);
  const add = repeater.locator('> button[name="jfb-nav"]');
  const rows = repeater.locator('> fieldset[data-jfb-row]');
  const remove = (row: number): Locator => rows.nth(row).locator('> button[name="jfb-nav"]');
  const loads: string[] = [];
  page.on('framenavigated', (frame) => loads.push(frame.url()));
  await expect(rows).toHaveCount(2);
  await expect(remove(0), 'at its least nothing goes').toBeHidden();

  await add.click();
  await expect(rows).toHaveCount(3);
  await expect(rows.last().locator('input').first()).toBeFocused();
  await expect(rows.last().locator('> legend')).toContainText('3');
  await expect(rows.last().locator('input').first(), 'required as the rows before it').toHaveAttribute('required');
  await expect(rows.last().locator('.jfb-mark:visible'), 'nothing says otherwise').toHaveCount(0);
  await expect(add, 'at its most nothing comes').toBeHidden();
  await expect(remove(0)).toBeVisible();
  await axe(page, 'a row added');

  for (const [at, row] of (await rows.all()).entries()) {
    await row.locator('input[name$=".refName"]').fill(`Reference ${RUN} ${String(at + 1)}`);
    await row.locator('input[name$=".refContact"]').fill(`${String(at + 1)}@visitor.jfb.test`);
  }
  await remove(1).click();
  await expect(rows).toHaveCount(2);
  await expect(add, 'the focus, as the rows left have no Remove').toBeFocused();
  await expect(form.locator('> [data-jfb-live]')).toContainText('removed');
  await expect(rows.nth(1).locator('input[name="referees[1].refName"]')).toHaveValue(`Reference ${RUN} 3`);
  expect(loads, 'no post').toEqual([]);
  await axe(page, 'a row removed');

  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(form.locator('[name="letter"]')).toBeVisible();
  await form.locator('[name="letter"]').fill(`Dear library, ${RUN}`);
  await form.locator('[data-jfb-alias="privacy"] label').first().click();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeVisible();

  const stored = await storedOf(request, 'application', RUN);
  expect(stored, 'one entry').toHaveLength(1);
  expect(inField(stored[0], '603').map((value) => value.textKey ?? value.text)).toEqual(['coordinator']);
  expect(
    stored[0]?.values
      .filter((value) => value.text?.startsWith('Reference') === true)
      .map((value) => [value.row, value.text]),
    'the row removed went, and the one after it took its place',
  ).toEqual([
    [0, `Reference ${RUN} 1`],
    [1, `Reference ${RUN} 3`],
  ]);
});

test.describe('the feedback form', () => {
  async function fillRequired(form: Locator, comments: string): Promise<void> {
    for (const [name, value] of [
      ['visits', 'monthly'],
      ['lastVisit', '4'],
    ] as const) {
      const input = form.locator(`input[name="${name}"][value="${value}"]`);
      await form.locator(`label[for="${(await input.getAttribute('id')) ?? ''}"]`).click();
    }
    await form.locator('[name="comments"]').fill(comments);
  }

  test('a ranking moves by its buttons, says so, and is stored in that order', async ({ page, request }) => {
    const form = await wake(page, 'feedback');
    const ranking = form.locator('.jfb-ranking');
    const labels = (): Promise<string[]> => ranking.locator('li > span:nth-child(2)').allTextContents();
    const before = await labels();
    const second = before[1] ?? '';
    const loads: string[] = [];
    page.on('framenavigated', (frame) => loads.push(frame.url()));

    const up = ranking.locator('li').nth(1).locator('button').first();
    const moved = await ranking.locator('li').nth(1).locator('input[name^="jfb-order"]').inputValue();
    await up.click();
    await expect.poll(labels).toEqual([second, before[0], ...before.slice(2)]);
    await expect(form.locator('> [data-jfb-live]')).toHaveText(`${second} moved to place 1.`);
    await expect(ranking.locator('li').first().locator('button').first()).toHaveAttribute('aria-disabled', 'true');
    await expect(ranking.locator('li').first().locator('button').first()).toBeFocused();
    await expect(ranking.locator('li > span:first-child').first()).toHaveText('1');
    expect(loads, 'no post').toEqual([]);
    await axe(page, 'a ranking moved');

    await fillRequired(form, `Ranked ${RUN}`);
    await send(page, form);
    const stored = await storedOf(request, 'feedback', `Ranked ${RUN}`);
    expect(stored).toHaveLength(1);
    expect(inField(stored[0], '306')[0]?.textKey ?? inField(stored[0], '306')[0]?.text, 'first as it was moved').toBe(
      moved,
    );
  });

  test("a counter says what is left, in the page's numbers, and how far past its limit, which goes no further", async ({
    page,
    request,
  }) => {
    const form = await wake(page, 'feedback');
    const comments = form.locator('[name="comments"]');
    const id = (await comments.getAttribute('id')) ?? '';
    const counter = form.locator(`#${id}-count`);
    const limit = Number(await counter.getAttribute('data-jfb-limit'));
    await expect(counter).toHaveText(`Up to ${limit.toLocaleString('en-US')} characters`);
    await comments.fill('Twelve chars');
    await expect(counter).toHaveText(`${(limit - 12).toLocaleString('en-US')} characters left`);
    await expect(counter).not.toHaveAttribute('data-jfb-over');

    await comments.fill('a'.repeat(limit + 3));
    await expect(counter).toHaveText('3 characters too many');
    await expect(counter).toHaveAttribute('data-jfb-over', '');
    await axe(page, 'a counter past its limit');

    await fillRequired(form, 'a'.repeat(limit + 3));
    await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
    await expect(form.locator('[data-jfb-summary]'), 'past its limit it does not go').toBeFocused();
    await fillRequired(form, `Counted ${RUN}`);
    await send(page, form);
    expect(await storedOf(request, 'feedback', `Counted ${RUN}`)).toHaveLength(1);
  });

  test('"Other" shows its text once it is chosen, and a text area keeps its rows with autoGrow off', async ({
    page,
    request,
  }) => {
    const form = await wake(page, 'feedback');
    const box = form.locator('.jfb-other').first();
    await expect(box).toBeHidden();
    const choice = box.locator('xpath=preceding-sibling::*[1]');
    await choice.locator('label').click();
    await expect(box).toBeVisible();
    const text = box.locator('input');
    await text.fill(`Other ${RUN}`);
    await axe(page, 'Other chosen');

    const comments = form.locator('[name="comments"]');
    const height = (await comments.boundingBox())?.height ?? 0;
    await comments.fill('line\n'.repeat(30));
    expect((await comments.boundingBox())?.height, 'its rows stay').toBe(height);

    await fillRequired(form, 'With other');
    await send(page, form);
    const stored = await storedOf(request, 'feedback', `Other ${RUN}`);
    expect(stored).toHaveLength(1);
  });

  test("a cascading field's options follow its parent, and an answer no longer offered goes", async ({
    page,
    request,
  }) => {
    const form = await wake(page, 'feedback');
    const branch = form.locator('select[name="branch"]');
    const desk = form.locator('select[name="desk"]');
    const corner = form.locator('[data-jfb-alias="corner"]');
    await expect(desk).toBeDisabled();

    await branch.selectOption('market');
    await expect(desk).toBeEnabled();
    await expect(desk.locator('option')).toHaveText(['Choose one', 'Ground floor', 'Reading room']);
    await expect(corner.locator('input[type="radio"]')).toHaveCount(2);
    await desk.selectOption('reading');
    await corner.getByLabel('By the door').check();
    await axe(page, 'a cascade answered');

    await branch.selectOption('riverside');
    await expect(desk.locator('option')).toHaveText(['Choose one', 'Garden room']);
    await expect(desk).toHaveValue('');
    await expect(corner.getByLabel('By the door')).toHaveCount(0);

    await branch.selectOption('market');
    await expect(desk.locator('option')).toHaveCount(3);
    await desk.selectOption('reading');
    await corner.getByLabel('By the window').check();
    await fillRequired(form, `Cascade ${RUN}`);
    await send(page, form);
    const [entry] = await storedOf(request, 'feedback', `Cascade ${RUN}`);
    expect(inField(entry, '342').map((value) => value.textKey ?? value.text)).toEqual(['reading']);
    expect(inField(entry, '343').map((value) => value.textKey ?? value.text)).toEqual(['window']);
  });
});

test('a text area with autoGrow on grows with its text, and all of it is stored', async ({ page, request }) => {
  const form = await wake(page, 'contact');
  const message = form.locator('[name="message"]');
  const before = (await message.boundingBox())?.height ?? 0;
  await message.fill(`Grown ${RUN}\n${'line\n'.repeat(30)}`);

  await expect.poll(async () => (await message.boundingBox())?.height ?? 0).toBeGreaterThan(before + 100);
  const shown = await message.evaluate((element) => element.scrollHeight - element.clientHeight);
  expect(shown, 'none of it scrolls').toBeLessThanOrEqual(1);

  await form.locator('[name="name"]').fill('Mira Okafor');
  await form.locator('[name="email"]').fill('mira@visitor.jfb.test');
  await send(page, form);
  expect(await storedOf(request, 'contact', `Grown ${RUN}`)).toHaveLength(1);
});

test.describe('on a phone', () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 375, height: 800 } });

  test('the grid stacks, each answer labelled, and is answered and stored', async ({ page, request }) => {
    const form = await wake(page, 'feedback');
    const grid = form.locator('.jfb-matrix');
    await grid.scrollIntoViewIfNeeded();
    const wide = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(wide, 'no sideways scrolling').toBeLessThanOrEqual(375);
    await expect(grid.locator('.jfb-matrix-head')).toBeHidden();
    const label = grid.locator('fieldset').first().locator('label').first();
    await expect(label.locator('span')).toBeVisible();

    await axe(page, 'the grid on a phone');
    for (const row of await grid.locator('fieldset').all()) await row.locator('label').first().click();
    for (const [name, value] of [
      ['visits', 'monthly'],
      ['lastVisit', '4'],
    ] as const) {
      const input = form.locator(`input[name="${name}"][value="${value}"]`);
      await form.locator(`label[for="${(await input.getAttribute('id')) ?? ''}"]`).click();
    }
    await form.locator('[name="comments"]').fill(`Grid ${RUN}`);
    await send(page, form);
    const [entry] = await storedOf(request, 'feedback', `Grid ${RUN}`);
    expect(inField(entry, '305').length, 'an answer for each row').toBe(await grid.locator('fieldset').count());
  });
});
