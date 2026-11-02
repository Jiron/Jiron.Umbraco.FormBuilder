import { expect, type Locator, type Page } from '@playwright/test';
import { setTimeout as delay } from 'node:timers/promises';

// Filling in and sending a form as a visitor does, through the markup contract every theme keeps (ADR-0015): fields are
// found by their alias, controls by their name, and the result by the outcome or the error summary. The same helpers
// drive a form without JavaScript (the post renders the next page) and with the runtime (it moves focus to the summary
// or the outcome, ADR-0004).

/**
 * An answer for one field: text for a text control or a select, the value or label of a choice, several of them for a
 * checkbox list or a multiple select, true or false for a single checkbox, and file paths for an upload.
 */
export type FieldValue = string | number | boolean | readonly string[] | { files: readonly string[] };

/**
 * Waits for the logic of the form around an element, which a first touch loads as its own chunk: until it has come a press
 * is a post, as it should be, which is not what a spec of the page's own behaviour means to press. A busy machine shows the
 * difference. Where no runtime runs this returns at once.
 */
export async function logicCome(inside: Locator): Promise<void> {
  await inside.evaluate(async (element) => {
    const form = element.closest('form');
    const runtime = (window as { JironFormBuilder?: { get(form: Element): { values(): Promise<unknown> } | null } })
      .JironFormBuilder;
    if (form !== null) await runtime?.get(form)?.values();
  });
}

/** Presses a button of a form on the page once the form's logic has come. */
export async function pressOnThePage(button: Locator): Promise<void> {
  await logicCome(button);
  await button.click();
}

/** What sending a form led to. */
export type SubmitResult = { state: 'sent'; outcome: string } | { state: 'errors'; errors: string[] };

const TEXT_TYPES = new Set([
  'text',
  'email',
  'tel',
  'url',
  'number',
  'date',
  'time',
  'datetime-local',
  'month',
  'week',
  'search',
  'password',
  'color',
]);

/** The form on the page: the one with this key, or the only one. */
export function formOn(page: Page, key?: string): Locator {
  return page.locator(key ? `form.jfb-form[data-jfb-form="${key}"]` : 'form.jfb-form');
}

const cssString = (value: string): string => JSON.stringify(value);

async function choiceMatches(control: Locator, wanted: readonly string[]): Promise<boolean> {
  const { value, label } = await control.evaluate((element) => {
    const input = element as HTMLInputElement;
    return { value: input.value, label: input.labels?.[0]?.textContent?.trim() ?? '' };
  });
  return wanted.includes(value) || wanted.includes(label);
}

const asList = (value: FieldValue): string[] =>
  Array.isArray(value) ? value.map(String) : [String(value as string | number | boolean)];

/** Answers one field. Fails with the alias when the form has no such field, or a rule hides it. */
async function fillField(form: Locator, alias: string, value: FieldValue): Promise<void> {
  const field = form.locator(`[data-jfb-field][data-jfb-alias=${cssString(alias)}]`);
  const count = await field.count();
  if (count !== 1) throw new Error(`The form has ${count === 0 ? 'no' : count} field(s) "${alias}".`);
  if (await field.evaluate((element) => element.closest('[hidden], [data-jfb-hidden]') !== null)) {
    throw new Error(`The field "${alias}" is hidden by a rule or on another page.`);
  }

  const controls = field.locator(`[name=${cssString(alias)}]`);
  const first = controls.first();
  if ((await controls.count()) === 0) throw new Error(`The field "${alias}" has no control named "${alias}".`);
  const { tag, type } = await first.evaluate((element) => ({
    tag: element.tagName.toLowerCase(),
    type: (element as HTMLInputElement).type?.toLowerCase() ?? '',
  }));

  if (typeof value === 'object' && !Array.isArray(value) && 'files' in value) {
    if (type !== 'file') throw new Error(`The field "${alias}" takes no files.`);
    await first.setInputFiles([...value.files]);
    return;
  }
  if (tag === 'select') {
    await first.selectOption(await optionValues(first, asList(value)));
    return;
  }
  if (tag === 'textarea' || (tag === 'input' && TEXT_TYPES.has(type))) {
    await first.fill(String(value as string | number));
    return;
  }
  if (type === 'checkbox' && typeof value === 'boolean') {
    if ((await controls.count()) !== 1) throw new Error(`The field "${alias}" is a list of choices; name the choices.`);
    await first.setChecked(value);
    return;
  }
  if (type === 'checkbox' || type === 'radio') {
    const wanted = asList(value);
    let matched = 0;
    for (const control of await controls.all()) {
      const match = await choiceMatches(control, wanted);
      if (match) matched++;
      if (type === 'radio') {
        // A card's whole face is its label, over the input.
        if (match && (await control.evaluate((element) => element.closest('.jfb-choices-cards') !== null)))
          await control
            .locator('xpath=ancestor::*[contains(@class, "jfb-choice")][1]')
            .locator('label')
            .first()
            .click();
        else if (match) await control.check();
      } else {
        await control.setChecked(match);
      }
    }
    if (matched !== wanted.length)
      throw new Error(`The field "${alias}" has no choice for some of ${JSON.stringify(wanted)}.`);
    return;
  }
  throw new Error(`The field "${alias}" has a control fillForm does not know (${tag} ${type}).`);
}

/** The option values of a select for the wanted values or labels. */
async function optionValues(select: Locator, wanted: readonly string[]): Promise<string[]> {
  const options = await select.evaluate((element) =>
    [...(element as HTMLSelectElement).options].map((option) => ({ value: option.value, label: option.label.trim() })),
  );
  return wanted.map((each) => {
    const option = options.find((candidate) => candidate.value === each || candidate.label === each);
    if (!option) throw new Error(`The select has no option ${JSON.stringify(each)}.`);
    return option.value;
  });
}

/** Answers the fields of the form's current page, by alias, as a visitor would. */
export async function fillForm(form: Locator, values: Record<string, FieldValue>): Promise<void> {
  for (const [alias, value] of Object.entries(values)) await fillField(form, alias, value);
}

/**
 * Presses the form's send button and waits until the page has answered: the outcome, or the error summary after the
 * post rendered the page again or the runtime moved focus to it. Returns what the visitor sees.
 */
export async function submitForm(form: Locator, timeoutMs = 15_000): Promise<SubmitResult> {
  const page = form.page();
  const mainFrame = page.mainFrame();
  let navigated = false;
  const onNavigated = (frame: unknown): void => {
    if (frame === mainFrame) navigated = true;
  };
  page.on('framenavigated', onNavigated);
  try {
    await form.locator('button[data-jfb-nav="submit"]').click();
    const outcome = page.locator('[data-jfb-outcome]');
    const summary = page.locator('[data-jfb-summary]');
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      if (await outcome.isVisible()) return { state: 'sent', outcome: (await outcome.innerText()).trim() };
      const busy = (await page.locator('form.jfb-form[data-jfb-busy]').count()) > 0;
      if (!busy && (await summary.isVisible())) {
        const fresh = navigated || (await summary.evaluate((element) => element.contains(document.activeElement)));
        if (fresh)
          return { state: 'errors', errors: (await summary.locator('a').allInnerTexts()).map((text) => text.trim()) };
      }
      if (Date.now() >= deadline)
        throw new Error(`The form showed neither an outcome nor new errors within ${timeoutMs} ms.`);
      await delay(100);
    }
  } finally {
    page.off('framenavigated', onNavigated);
  }
}

/** Presses a button of the form's navigation and waits for the page it leads to, or for the errors the runtime shows. */
export async function press(form: Locator, nav: 'next' | 'back'): Promise<void> {
  const page = form.page();
  const shown = form.locator('[data-jfb-page]:not([hidden])').first();
  const before = await shown.getAttribute('data-jfb-page');
  const loaded = page.waitForEvent('load', { timeout: 15_000 });
  const refused = expect(form.locator('[data-jfb-summary]')).toBeFocused({ timeout: 15_000 });
  const moved = expect
    .poll(() => shown.getAttribute('data-jfb-page', { timeout: 1_000 }).catch(() => before), { timeout: 15_000 })
    .not.toBe(before);
  loaded.catch(() => undefined);
  refused.catch(() => undefined);
  moved.catch(() => undefined);
  await form.locator(`.jfb-navigation button[data-jfb-nav="${nav}"]`).click();
  // The post loads the page it leads to; the runtime moves on, or keeps a page that needs answers and moves the focus to its errors.
  await Promise.any([loaded, refused, moved]);
}
