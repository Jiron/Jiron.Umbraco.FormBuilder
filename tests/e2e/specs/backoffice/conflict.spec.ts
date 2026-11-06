import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';
import { E2E_BASE_URL } from '../../support/paths.ts';

// Conflicts and the guard against leaving unsaved (ROADMAP 10.3c, IA 5.7, ADR-0200). Two people open the same form;
// the admin saves a new label for Email, then the designer saves a new label for Name. The designer's save is refused
// and says who saved and when; Cancel keeps the changes, and a second Save is refused again until both versions are
// reviewed. The review starts each difference on the side that made it, and its Save keeps the work of both. A name
// both changed waits for a choice, which Save says in words, on a phone too; Undo gives the own version back; a
// publish saves first, so it stops at a conflict. Leaving with a canvas change asks first, in the backoffice and when
// the tab is closed.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

/** Each field's label, as the stored draft holds it. */
async function labelsOf(api: APIRequestContext, key: string): Promise<unknown[]> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: { label?: Record<string, string> }[] }[] }[] };
  };
  return draft.definition.pages[0]?.groups[0]?.fields.map((field) => field.label?.['']) ?? [];
}

/** Relabels a field in the Design view's inspector. */
async function relabel(page: Page, from: string, to: string) {
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: new RegExp(`^${from}, `) }).click();
  await design.locator('jfb-designer-inspector').getByLabel('Label', { exact: true }).fill(to);
}

const saveDraft = (page: Page) => page.getByRole('button', { name: 'Save draft', exact: true }).click();

test("a save after someone else's is refused, again until both versions are reviewed, and the review keeps both", async ({
  page,
  browser,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Conflict'), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/design`);
  await expect(page.locator('jfb-form-design-view').getByRole('button', { name: /^Name, / })).toBeVisible();

  // The admin, in a browser of their own, saves first.
  const other = await browser.newContext({ baseURL: E2E_BASE_URL, viewport: { width: 1440, height: 900 } });
  try {
    const admin = await other.newPage();
    await loginAs('admin', admin);
    await admin.goto(`${SECTION}/${key}/view/design`);
    await relabel(admin, 'Email', 'Work email');
    await saveDraft(admin);
    await expect.poll(() => labelsOf(api, key)).toEqual(['Name', 'Work email']);
  } finally {
    await other.close();
  }

  // The designer's save is refused: the dialog says who saved, and the changes stay.
  await relabel(page, 'Name', 'Full name');
  await saveDraft(page);
  const dialog = page.locator('umb-confirm-modal');
  await expect(dialog).toContainText("Your draft wasn't saved");
  await expect(dialog).toContainText(/saved this form at .+, after you opened it\. .*Your changes are still here\./);
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('jfb-form-design-view').getByRole('button', { name: /^Full name, / })).toBeVisible();

  // A second Save is refused again, and sends nothing.
  await saveDraft(page);
  await expect(dialog).toContainText("Your draft wasn't saved");
  expect(await labelsOf(api, key)).toEqual(['Name', 'Work email']);

  // The review: Name was changed here, Email there, so each starts on that side.
  await dialog.getByRole('button', { name: 'Review both versions', exact: true }).click();
  const review = page.locator('jfb-review-modal');
  await expect(review.locator('section.difference')).toHaveCount(2);
  await axeInside(page, 'jfb-review-modal');
  const choiceOf = (title: RegExp) =>
    review
      .locator('section.difference')
      .filter({ has: page.locator('h4', { hasText: title }) })
      .locator('uui-radio-group');
  await expect(choiceOf(/Full name/)).toHaveJSProperty('value', 'yours');
  // Named as the user's own version names it, as on their canvas.
  await expect(choiceOf(/"Email"/)).toHaveJSProperty('value', 'theirs');
  await review.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(review).toHaveCount(0);

  await expect.poll(() => labelsOf(api, key)).toEqual(['Full name', 'Work email']);
  // The form as saved is the newest: a later save goes through.
  await relabel(page, 'Work email', 'Email address');
  await saveDraft(page);
  await expect.poll(() => labelsOf(api, key)).toEqual(['Full name', 'Email address']);
  await expect(dialog).toHaveCount(0);
});

test('a name both changed waits for a choice, Undo gives the own version back, and Publish stops at a conflict', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const admin = await apiAs('admin');
  const name = unique('Names');
  const key = await formWithFields(api, name, [{ type: 'text', alias: 'name', label: { '': 'Name' } }]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/design`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.getByRole('button', { name: /^Name, / })).toBeVisible();

  // The admin renames the form meanwhile; the designer renames it too, and relabels Name.
  const renamed = await admin.put(`${API}/form/${key}`, {
    data: { concurrencyVersion: (await formOf(admin, key)).concurrencyVersion, name: `${name} (admin)` },
  });
  expect(renamed.ok(), await renamed.text()).toBe(true);
  await page
    .locator('jfb-form-workspace-editor')
    .getByRole('textbox', { name: 'Form name' })
    .fill(`${name} (designer)`);
  await relabel(page, 'Name', 'Full name');
  await saveDraft(page);
  const dialog = page.locator('umb-confirm-modal');
  // No draft was saved meanwhile, so no one is named.
  await expect(dialog).toContainText('Someone changed this form after you opened it.');

  // On a phone, the review stacks; the name both changed waits, the label only the designer changed doesn't.
  await page.setViewportSize({ width: 375, height: 812 });
  await dialog.getByRole('button', { name: 'Review both versions', exact: true }).click();
  const review = page.locator('jfb-review-modal');
  const section = (title: RegExp) =>
    review.locator('section.difference').filter({ has: page.locator('h4', { hasText: title }) });
  await expect(review.locator('section.difference')).toHaveCount(2);
  await expect(section(/^The form$/).locator('uui-tag')).toHaveText('Changed in both');
  await expect(section(/^The form$/).locator('uui-radio-group')).toHaveJSProperty('value', '');
  await expect(section(/Full name/).locator('uui-radio-group')).toHaveJSProperty('value', 'yours');
  await axeInside(page, 'jfb-review-modal');

  // Save says what still waits, in words, and saves nothing.
  await review.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(review.getByRole('alert')).toHaveText('One difference still needs a choice.');
  await expect(section(/^The form$/).locator('.wait')).toHaveText('Choose a version');
  await axeInside(page, 'jfb-review-modal');
  expect((await formOf(api, key)).name).toBe(`${name} (admin)`);
  // A radio is chosen by its label, as UUI hides the native box.
  await section(/^The form$/)
    .locator('uui-radio[label="Your version"]')
    .click();
  await expect(review.getByRole('alert')).toHaveCount(0);
  await section(/Full name/)
    .locator('uui-radio[label="The version saved meanwhile"]')
    .click();
  await review.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(review).toHaveCount(0);
  await expect.poll(async () => (await formOf(api, key)).name).toBe(`${name} (designer)`);
  expect(await labelsOf(api, key)).toEqual(['Name']);

  // Undo takes the review back to the designer's own version, unsaved.
  await page.setViewportSize({ width: 1440, height: 900 });
  await design.getByRole('button', { name: /^Name, / }).focus();
  await page.keyboard.press('Control+z');
  await expect(design.getByRole('button', { name: /^Full name, / })).toBeVisible();
  expect(await labelsOf(api, key)).toEqual(['Name']);

  // The admin saves a draft meanwhile: Publish saves first, so it stops at the conflict, and nothing goes live.
  const draft = (await (await admin.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: { label: Record<string, string> }[] }[] }[] };
  };
  (draft.definition.pages[0]?.groups[0]?.fields[0] as { label: Record<string, string> }).label = { '': 'Your name' };
  const saved = await admin.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  await page.locator('jfb-publish-modal').getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(dialog).toContainText("Your draft wasn't saved");
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect((await formOf(api, key)).isPublished).toBe(false);
  expect(await labelsOf(api, key)).toEqual(['Your name']);
});

test('leaving a canvas change unsaved asks first, in the backoffice and when the tab is closed', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Leave'), [{ type: 'text', alias: 'name', label: { '': 'Name' } }]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/design`);
  await relabel(page, 'Name', 'Full name');

  // Another form in the tree: the demo's contact form.
  await page.locator('umb-section-sidebar uui-menu-item[label="Contact"]').filter({ visible: true }).first().click();
  const discard = page.locator('umb-discard-changes-modal, uui-dialog-layout').filter({ hasText: 'Discard' }).first();
  await expect(discard).toBeVisible();
  await discard.getByRole('button', { name: 'Stay' }).click();
  await expect(page.locator('jfb-form-design-view').getByRole('button', { name: /^Full name, / })).toBeVisible();

  // Closing the tab asks the browser's own question; staying keeps the page and the change.
  const asked = page.waitForEvent('dialog');
  await page.close({ runBeforeUnload: true });
  const question = await asked;
  expect(question.type()).toBe('beforeunload');
  await question.dismiss();
  expect(page.isClosed()).toBe(false);
  await expect(page.locator('jfb-form-design-view').getByRole('button', { name: /^Full name, / })).toBeVisible();
  expect(await labelsOf(api, key)).toEqual(['Name']);
});
