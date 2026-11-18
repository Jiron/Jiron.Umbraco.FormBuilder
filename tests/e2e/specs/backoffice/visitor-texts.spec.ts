import { expect, test } from '../../fixtures.ts';
import { logicCome } from '../../support/form.ts';
import { openSettings, openSettingsMore } from '../../support/form-views.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';
import { STATIC_SITE_WAIT_MS, startStaticSite, type StaticSite } from '../../support/static-site.ts';

// A form's own wording of the package's visitor texts (ROADMAP 14.2, ADR-0230): a designer rewords the message of an
// empty answer and the error summary's heading under the Settings view's More options, in English, and publishes; a
// visitor in English then reads the form's words where the package's were, and a visitor in German still reads the
// package's German, as the form reworded nothing there.

let site: StaticSite | undefined;

test.beforeAll(async () => {
  // Another worker's spec may hold the static site's one port for a while (support/static-site.ts).
  test.setTimeout(STATIC_SITE_WAIT_MS + 60_000);
  site = await startStaticSite();
});

test.afterAll(async () => {
  await site?.stop();
});

test("a designer rewords a message in the form's settings, and the visitor reads it in that language only", async ({
  page,
  apiAs,
  loginAs,
}) => {
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Reworded'), [
    { type: 'text', alias: 'name', label: { '': 'Name' }, required: true },
    { type: 'text', alias: 'city', label: { '': 'City' } },
  ]);
  await loginAs('designer');
  const view = await openSettings(page, `/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}/view/settings`);
  await openSettingsMore(view);
  const editor = view.locator('jfb-visitor-texts');
  const choose = editor.getByLabel('Reword a text');
  await expect(choose).toBeVisible();

  // Each text chosen gets a box named by the package's words, the focus in it.
  await choose.selectOption('message.answer');
  const answer = editor.getByLabel('Enter an answer', { exact: true });
  await expect(answer).toBeFocused();
  await answer.fill('Tell us this, please');
  await choose.selectOption('message.summaryHeading.one');
  await editor.getByLabel('This answer needs a change', { exact: true }).fill('One answer to fix');
  await expect(choose.locator('option[value="message.answer"]')).toHaveCount(0);

  // A value its text does not name is said under the box, and taking the brace away clears it.
  await answer.fill('Tell us {name}');
  await expect(editor.getByText("This text can't name a value in braces.")).toBeVisible();
  await answer.fill('Tell us this, please');
  await expect(editor.getByText("This text can't name a value in braces.")).toHaveCount(0);

  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { settings: Record<string, Record<string, unknown>> } & Record<string, unknown>;
  };
  expect(draft.definition.settings['submission']?.['texts']).toEqual({
    'message.answer': { 'en-US': 'Tell us this, please' },
    'message.summaryHeading.one': { 'en-US': 'One answer to fix' },
  });

  // Published for the headless page.
  const settings = draft.definition.settings;
  const headless = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: {
        ...draft.definition,
        settings: { ...settings, developer: { ...settings['developer'], headless: true } },
      },
    },
  });
  expect(headless.ok(), await headless.text()).toBe(true);
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);

  if (site === undefined) throw new Error('The static site did not start.');
  for (const visitor of [
    { culture: 'en-US', error: 'Tell us this, please', heading: 'One answer to fix' },
    { culture: 'de-DE', error: 'Geben Sie eine Antwort ein', heading: 'Diese Antwort muss noch' },
  ]) {
    await page.goto(`${site.url}/?${new URLSearchParams({ form: key, culture: visitor.culture })}`);
    const form = page.locator('jfb-form form.jfb-form');
    await expect(form).toHaveAttribute('lang', visitor.culture);
    // The first press loads the form's logic; until then it would be a post the headless page cannot take.
    await logicCome(form);
    // A city but no name, so the name's own message shows rather than the form's call for any answer at all.
    await form.getByLabel('City').fill('Bern');
    await form.locator('button[data-jfb-nav="submit"]').click();
    await expect(form.locator('[data-jfb-summary] h2')).toContainText(visitor.heading);
    await expect(form.locator('[data-jfb-error]').filter({ hasText: visitor.error })).toHaveCount(1);
  }
});
