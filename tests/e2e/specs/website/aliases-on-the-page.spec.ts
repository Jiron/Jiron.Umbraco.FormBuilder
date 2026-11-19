import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { coverEveryMember, uncoveredAtReady } from '../../support/form-cover.ts';
import { pageWith } from '../../support/form-page.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { STATIC_SITE_WAIT_MS, startStaticSite, type StaticSite } from '../../support/static-site.ts';

// A form whose field aliases are names of the form element's own members (ADR-0015 keeps a control's name its alias). A
// browser answers a read on a form with its control of that name in front of every built-in member, so `form.id` on a
// form with a field called `id` is that field's control; the website code reads a form's members through its
// prototype chain instead. Every other member of the form is covered too (support/form-cover.ts), before the runtime
// meets the form. With every name covered, the form checks, shows its error summary, whose link leads to the control,
// sends its answers by alias, and shows its outcome in its place, with nothing thrown on the page: on a page the server
// renders, where the runtime finds the form in the parsed page, and through <jfb-form> on the static site, where the
// element builds it. Every locator starts at the document and names nothing above the element it finds, and the form
// is answered by keyboard (support/form-cover.ts says why).

test.describe.configure({ mode: 'serial' });

/** The fields' aliases: members of a form that the website code reads, and two a form's markup names. */
const ALIASES = [
  'id',
  'dataset',
  'dispatchEvent',
  'replaceWith',
  'querySelector',
  'querySelectorAll',
  'addEventListener',
  'removeEventListener',
  'ownerDocument',
  'classList',
  'contains',
  'matches',
  'closest',
  'elements',
  'children',
  'hasAttribute',
  'action',
  'submit',
] as const;
const REQUIRED = 'dataset';

let site: StaticSite | undefined;

test.beforeAll(async () => {
  // Another worker's spec may hold the static site's one port for a while (support/static-site.ts).
  test.setTimeout(STATIC_SITE_WAIT_MS + 60_000);
  site = await startStaticSite();
});

test.afterAll(async () => {
  await site?.stop();
});

/** A published headless form with a text field for each alias, one of them required; its key. */
async function aliasForm(api: APIRequestContext): Promise<string> {
  const key = await formWithFields(
    api,
    unique('Aliases'),
    ALIASES.map((alias) => ({
      type: 'text',
      alias,
      label: { '': `Answer for ${alias}` },
      ...(alias === REQUIRED ? { required: true } : {}),
    })),
  );
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { settings: { developer?: Record<string, unknown> } };
  };
  draft.definition.settings.developer = { ...draft.definition.settings.developer, headless: true };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const form = (await (await api.get(`${API}/form/${key}`)).json()) as { concurrencyVersion: number };
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: form.concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  return key;
}

/** The events the runtime dispatches and the page's errors, from before its first script. */
async function watch(page: Page): Promise<{ events: () => Promise<string[]>; errors: string[] }> {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  await page.addInitScript(() => {
    const seen: string[] = [];
    Object.assign(window, { jfbEvents: seen });
    for (const type of ['jfb:ready', 'jfb:invalid', 'jfb:submitting', 'jfb:submitted', 'jfb:error'])
      document.addEventListener(type, () => seen.push(type));
  });
  return {
    events: () => page.evaluate(() => [...((window as unknown as { jfbEvents?: string[] }).jfbEvents ?? [])]),
    errors,
  };
}

/** The journey on a page `address` shows the form `key` on, once that page shows it as published. */
async function journey(page: Page, address: string, key: string): Promise<void> {
  const watched = await watch(page);
  await coverEveryMember(page, ALIASES);
  // The page may serve the version before the publish for a moment.
  await expect
    .poll(
      async () => {
        await page.goto(address);
        return page.locator(`input[name="${REQUIRED}"]`).count();
      },
      { timeout: 30_000 },
    )
    .toBe(1);
  await expect.poll(watched.events).toContain('jfb:ready');
  // The page's other forms, such as one its layout shows, are covered as well.
  const uncovered = await uncoveredAtReady(page);
  expect(uncovered.length, 'a jfb:ready for each form').toBeGreaterThan(0);
  expect(uncovered.flat(), "names a plain read got a form's member for at jfb:ready").toEqual([]);

  // The controls by their ids, `<form id>-<alias>`, as the markup contract names them, and the form's send button by its
  // place among the page's.
  const [formId, sendAt] = await page.evaluate((form) => {
    const element = document.querySelector(`form.jfb-form[data-jfb-form="${form}"]`);
    const buttons = [...document.querySelectorAll('button[data-jfb-nav="submit"]')];
    return [
      element === null ? null : Element.prototype.getAttribute.call(element, 'id'),
      buttons.findIndex((button) => element !== null && Node.prototype.contains.call(element, button)),
    ] as const;
  }, key);
  expect(formId).toMatch(/^jfb-\d+$/);
  expect(sendAt).toBeGreaterThanOrEqual(0);
  const control = (alias: string) => page.locator(`[id="${formId ?? ''}-${alias}"]`);
  const send = page.locator('button[data-jfb-nav="submit"]').nth(sendAt);
  const press = async (target: Locator): Promise<void> => {
    await target.focus();
    await page.keyboard.press('Enter');
  };
  const type = async (target: Locator, text: string): Promise<void> => {
    await target.focus();
    await page.keyboard.insertText(text);
  };

  // Every answer but the required one: the summary, whose link leads to the control.
  for (const alias of ALIASES) if (alias !== REQUIRED) await type(control(alias), `answer for ${alias}`);
  await press(send);
  const summary = page.locator('[data-jfb-summary]');
  await expect(summary).toBeVisible();
  await expect(summary).toBeFocused();
  await expect(control(REQUIRED)).toHaveAttribute('aria-invalid', 'true');
  await expect.poll(watched.events).toContain('jfb:invalid');
  await press(page.locator(`a[href="#${formId ?? ''}-${REQUIRED}"]`));
  await expect(control(REQUIRED)).toBeFocused();

  // Answered: the entry holds every answer by its alias, and the outcome takes the form's place.
  await type(control(REQUIRED), `answer for ${REQUIRED}`);
  const posted = page.waitForResponse(
    (response) => response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/entries'),
  );
  await press(send);
  const response = await posted;
  expect(response.status(), await response.text()).toBe(200);
  const values = (response.request().postDataJSON() as { values: Record<string, unknown> }).values;
  for (const alias of ALIASES) expect(values[alias], alias).toEqual([`answer for ${alias}`]);
  await expect(page.locator('[data-jfb-outcome]')).toBeVisible();
  expect(
    await page.evaluate((form) => document.querySelector(`form.jfb-form[data-jfb-form="${form}"]`), key),
  ).toBeNull();

  expect(await watched.events()).toEqual(
    expect.arrayContaining(['jfb:ready', 'jfb:invalid', 'jfb:submitting', 'jfb:submitted']),
  );
  expect(watched.errors).toEqual([]);
}

test('on a page the server renders, a form whose aliases are its own members checks, sends and shows its outcome', async ({
  page,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const key = await aliasForm(await apiAs('admin'));
  await journey(page, await pageWith(await apiAs('editor'), key, 'Aliases page'), key);
});

test('through the element, a form whose aliases are its own members checks, sends and shows its outcome', async ({
  page,
  apiAs,
}) => {
  if (site === undefined) throw new Error('The static site did not start.');
  const key = await aliasForm(await apiAs('admin'));
  await journey(page, `${site.url}/?${new URLSearchParams({ form: key, culture: 'en-US' })}`, key);
});
