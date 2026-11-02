import type { Browser, Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { pressOnThePage } from '../../support/form.ts';
import { LANGUAGES, formPages, pathOf, type Language } from '../../support/demo-content.ts';

// The rules on the page (ROADMAP 6.4c, ADR-0111), on the demo's feedback form in English and German: the evenings
// that suit a visitor show once they would come in the evening and hide again when they would not, and the comments
// are required, without their "(optional)", while they would hardly recommend the library. The page then shows what
// the server renders for the same answers after a post without JavaScript, axe finds no WCAG 2.2 A or AA violation in
// any of these states, and nothing writes an error to the console.

const feedbackPath = (language: Language): string => {
  const shown = formPages().find((each) => each.form === 'feedback');
  if (shown === undefined) throw new Error('The demo has no feedback page.');
  return pathOf(shown.texts[language], language);
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const fieldOf = (form: Locator, alias: string): Locator => form.locator(`[data-jfb-alias="${alias}"]`);

/** Picks a choice by its label, as a visitor does. */
async function choose(form: Locator, name: string, value: string): Promise<void> {
  const input = form.locator(`input[name="${name}"][value="${value}"]`);
  const id = await input.getAttribute('id');
  await form.locator(`label[for="${id ?? ''}"]`).click();
  await expect(input).toBeChecked();
}

/** What the rules decide of each field: shown or hidden, its controls disabled or required, its marks shown. */
const stateOf = (form: Locator) =>
  form.evaluate((element) =>
    [...element.querySelectorAll('[data-jfb-field]')].map((field) => {
      const own = (selector: string) =>
        [...field.querySelectorAll(selector)].filter((each) => each.closest('[data-jfb-field]') === field);
      return {
        field: field.getAttribute('data-jfb-alias') ?? field.getAttribute('data-jfb-field'),
        hidden: field.hasAttribute('hidden'),
        ruled: field.hasAttribute('data-jfb-hidden'),
        controls: own('input, select, textarea, button').map(
          (control) =>
            `${control.getAttribute('name') ?? ''}${control.hasAttribute('disabled') ? ' disabled' : ''}${control.hasAttribute('required') ? ' required' : ''}`,
        ),
        marks: own('.jfb-mark').map((mark) => (mark.hasAttribute('hidden') ? 'hidden' : mark.textContent)),
      };
    }),
  );

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

/** The state the server renders for answers posted without JavaScript: the page again, with the post's errors. */
async function renderedByTheServer(browser: Browser, path: string, answers: readonly (readonly [string, string])[]) {
  const context = await browser.newContext({ javaScriptEnabled: false });
  try {
    const page = await context.newPage();
    await page.goto(path);
    for (const [name, value] of answers) await choose(formOn(page), name, value);
    const loaded = page.waitForEvent('load');
    await pressOnThePage(formOn(page).locator('button[data-jfb-nav="submit"]'));
    await loaded;
    await expect(formOn(page).locator('[data-jfb-summary]'), 'the post was refused for its errors').toBeVisible();
    return await stateOf(formOn(page));
  } finally {
    await context.close();
  }
}

for (const language of LANGUAGES) {
  test(`${language}: the feedback form shows, hides and requires its fields as the answers change, as the server would render them`, async ({
    page,
    browser,
  }) => {
    const errors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') errors.push(message.text());
    });
    page.on('pageerror', (error) => errors.push(error.message));
    const path = feedbackPath(language);
    await page.goto(path);
    const form = formOn(page);
    const days = fieldOf(form, 'eveningDays');
    const comments = fieldOf(form, 'comments');
    const optional = comments.locator('label .jfb-mark');
    const textarea = comments.locator('textarea');

    await expect(days, 'no evening yet, so no evenings to choose').toBeHidden();
    await expect(days).toHaveAttribute('data-jfb-hidden', '');
    await expect(optional, 'the comments are optional').toBeVisible();
    await expect(textarea).not.toHaveAttribute('required');

    await choose(form, 'evenings', 'yes');
    await expect(days, 'an evening, so the evenings to choose').toBeVisible();
    await expect(days).not.toHaveAttribute('data-jfb-hidden');
    for (const box of await days.locator('input[type="checkbox"]').all()) await expect(box).toBeEnabled();
    await axe(page, `${language}, the evenings shown`);

    await choose(form, 'recommend', '3');
    await expect(optional, 'hardly recommended, so the comments are asked for').toBeHidden();
    await expect(textarea).toHaveAttribute('required');
    await axe(page, `${language}, the comments required`);

    const answers = [
      ['evenings', 'yes'],
      ['recommend', '3'],
    ] as const;
    expect(await stateOf(form), 'what the server renders for the same answers').toEqual(
      await renderedByTheServer(browser, path, answers),
    );

    await choose(form, 'evenings', 'no');
    await expect(days, 'no evening after all').toBeHidden();
    for (const box of await days.locator('input[type="checkbox"]').all()) await expect(box).toBeDisabled();
    await choose(form, 'recommend', '9');
    await expect(optional, 'recommended, so the comments are optional again').toBeVisible();
    await expect(textarea).not.toHaveAttribute('required');
    await axe(page, `${language}, back to the start`);
    expect(errors, 'nothing wrote an error to the console').toEqual([]);
  });
}
