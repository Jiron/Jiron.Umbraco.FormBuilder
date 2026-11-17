import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { showAfterSending } from '../../support/designer-row.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';

// After sending on the Design view (ROADMAP 10.1b and 22.1, IA 5.1, ADR-0185, ADR-0256). The outcomes are a list in the
// inspector's column, each opened in a sidebar of its own: a message with merge tags and a copy of the answers, a
// redirect to a page or an address, or a message and then a redirect; its rule in the rules builder; what the server
// can't keep refused at its box and the form check's problems said under their setting. Rows move with Alt and the
// arrows, Delete outcome offers Undo, and Save draft keeps them; the Settings view holds no After sending. The form
// check's Fix it, from the badge and from the publish dialog of another view, leads to an outcome's sidebar there.

const SECTION = '/umbraco/section/jfb-forms';
const DEFAULT = 'Thank you. We have received your message.';

interface Outcome {
  readonly key: string;
  readonly kind?: string;
  readonly message?: Record<string, string>;
  readonly showAnswers?: boolean;
  readonly redirect?: { readonly url?: string; readonly content?: string; readonly delaySeconds?: number };
  readonly rules?: readonly { readonly effect: string; readonly conditions: readonly unknown[] }[];
}

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: { outcomes?: Outcome[]; readonly [other: string]: unknown };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

const newForm = (api: APIRequestContext, name: string) =>
  formWithFields(api, unique(name), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);

/** The Design view on After sending, whose outcomes stand in the inspector's column at 1440 px. */
async function openAfterSending(page: Page, key: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-canvas-field').first()).toBeVisible({ timeout: 60_000 });
  return showAfterSending(design);
}

/** Types into a box and leaves it, as a person does: what can't be kept, and the check's problems, show once it is left. */
async function enter(box: Locator, value: string) {
  await box.fill(value);
  await box.press('Tab');
}

/** The button that opens the outcome a row names: its name is all the row shows, the outcome's name first. */
const rowOf = (list: Locator, name: string) =>
  list.locator('.row-open').filter({ has: list.page().locator('.row-name', { hasText: name }) });

/** Saves the draft and waits for the save's own answer, which the button does not wait for. */
async function saveDraft(page: Page, key: string) {
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
}

test('After sending edits each outcome in its sidebar, with its rule and its messages, moves it and deletes it with Undo', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await newForm(api, 'Outcomes');
  await loginAs('designer');
  const view = await openAfterSending(page, key);
  const list = view.locator('jfb-outcome-list');
  const rows = list.locator('.outcome-row');
  const names = rows.locator('.row-name');
  const lines = rows.locator('.row-line');

  // A new form's one outcome gives every visitor the default message.
  await expect(names).toHaveText([`Message: "${DEFAULT}"`]);
  await expect(lines).toHaveText(['Used for every visitor']);
  await axeInside(page, 'jfb-form-design-view');

  // Its message, written with a merge tag, and a copy of the answers.
  await rowOf(list, `Message: "${DEFAULT}"`).click();
  const sidebar = page.locator('jfb-outcome-modal');
  await expect(sidebar.getByRole('heading', { name: 'Outcome 1' })).toBeVisible();
  await expect(sidebar.getByRole('radio', { name: 'A message', exact: true })).toBeChecked();
  const message = sidebar.locator('#message [contenteditable="true"]');
  await message.click();
  await message.pressSequentially('Thanks, ');
  await sidebar.getByRole('button', { name: 'Insert merge tag' }).click();
  await sidebar.getByRole('button', { name: 'Name {{ field.name }}' }).click();
  await sidebar.getByRole('switch', { name: 'Show a copy of the answers' }).check();
  await axeInside(page, 'jfb-outcome-modal');
  await sidebar.getByRole('button', { name: 'Update', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(names).toHaveText(['Message: "Thanks, {{ field.name }}"']);
  await expect(rowOf(list, 'Message: "Thanks, {{ field.name }}"')).toBeFocused();

  // A message and then a redirect waits from 0 to 600 seconds; a number past that is refused once its box is left.
  await list.getByRole('button', { name: 'Add outcome' }).click();
  await expect(sidebar.getByRole('heading', { name: 'New outcome' })).toBeVisible();
  await sidebar.getByRole('radio', { name: 'A message, then a redirect' }).check();
  await enter(sidebar.getByLabel('Seconds before the redirect'), '900');
  await expect(sidebar.locator('#delay-why-0')).toHaveText('Enter a whole number from 0 to 600.');
  await sidebar.getByRole('button', { name: 'Cancel' }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(rows).toHaveCount(1);

  // A redirect: the form check's own words under an address it refuses, which keep Add back; then its rule.
  await list.getByRole('button', { name: 'Add outcome' }).click();
  await sidebar.getByRole('radio', { name: 'A redirect', exact: true }).check();
  await expect(sidebar.getByRole('radio', { name: 'A page of this site' })).toBeChecked();
  await sidebar.getByRole('radio', { name: 'An address' }).check();
  const address = sidebar.getByLabel('Address', { exact: true });
  await enter(address, 'javascript:alert(1)');
  await expect(sidebar.locator('#address-why-0')).toHaveText(
    'A redirect is a path on the site, such as /thanks, or an https address.',
  );
  await sidebar.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(address).toBeFocused();
  await enter(address, '/press/thanks');
  await expect(sidebar.locator('#address-why-0')).toHaveCount(0);
  await sidebar.getByRole('button', { name: 'Add a rule' }).click();
  const builder = page.locator('jfb-rule-builder-modal');
  await expect(builder.getByRole('heading', { name: 'New rule' })).toBeVisible();
  await expect(builder.locator('.first-line')).toHaveText('Use the new outcome when');
  await builder.getByRole('combobox', { name: 'What condition 1 looks at' }).selectOption({ label: 'Name' });
  await builder.getByRole('combobox', { name: 'How condition 1 compares' }).selectOption({ label: 'is not empty' });
  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(builder).toHaveCount(0);
  await expect(sidebar.locator('#rule-words')).toHaveText('Used when Name is not empty');
  await expect(sidebar.getByRole('button', { name: 'Change rule' })).toBeFocused();
  await sidebar.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  // An outcome with a rule goes before the one for every visitor, which stays the one the others fall back to.
  await expect(names).toHaveText(['Redirect to /press/thanks', 'Message: "Thanks, {{ field.name }}"']);
  await expect(lines).toHaveText(['Used when Name is not empty', 'Otherwise']);
  await expect(list.getByRole('status')).toHaveText('Added. Redirect to /press/thanks: position 1 of 2.');

  // Alt and the arrows move a row and say where it went; the outcome after the one for every visitor is never used.
  const handle = list.getByRole('button', { name: 'Move Redirect to /press/thanks' });
  await handle.focus();
  await handle.press('Alt+ArrowDown');
  await expect(names).toHaveText(['Message: "Thanks, {{ field.name }}"', 'Redirect to /press/thanks']);
  await expect(lines).toHaveText(['Used for every visitor', 'Never used: an outcome above is used for every visitor']);
  await expect(list.getByRole('status')).toHaveText('Moved. Redirect to /press/thanks: position 2 of 2.');
  await expect(handle).toBeFocused();
  // Ctrl+Z outside a text box undoes the move.
  await handle.press('Control+z');
  await expect(names).toHaveText(['Redirect to /press/thanks', 'Message: "Thanks, {{ field.name }}"']);

  // The sidebar's place moves it without dragging, and says first where no visitor would get it.
  await rowOf(list, 'Redirect to /press/thanks').click();
  await sidebar.getByLabel('Position in the list').selectOption({ label: '2 of 2' });
  await expect(sidebar.locator('#rule-never')).toHaveText('Never used: an outcome above is used for every visitor');
  await sidebar.getByRole('button', { name: 'Update', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(names).toHaveText(['Message: "Thanks, {{ field.name }}"', 'Redirect to /press/thanks']);
  await expect(list.getByRole('status')).toHaveText('Moved. Redirect to /press/thanks: position 2 of 2.');
  const moved = rowOf(list, 'Redirect to /press/thanks');
  await expect(moved).toBeFocused();
  await moved.press('Control+z');
  await expect(names).toHaveText(['Redirect to /press/thanks', 'Message: "Thanks, {{ field.name }}"']);

  // Delete outcome acts at once; Undo in the toast brings it back.
  await rowOf(list, 'Redirect to /press/thanks').click();
  await expect(sidebar.getByRole('heading', { name: 'Outcome 1' })).toBeVisible();
  await sidebar.getByRole('button', { name: 'Delete outcome' }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(names).toHaveText(['Message: "Thanks, {{ field.name }}"']);
  await page
    .locator('uui-toast-notification')
    .filter({ hasText: 'Outcome deleted.' })
    .getByRole('button', { name: 'Undo' })
    .click();
  await expect(names).toHaveText(['Redirect to /press/thanks', 'Message: "Thanks, {{ field.name }}"']);
  // The toast is gone with its Undo: the focus is on the outcome back in the list.
  await expect(rowOf(list, 'Redirect to /press/thanks')).toBeFocused();

  // Save draft keeps them as the server writes them.
  await saveDraft(page, key);
  const outcomes = (await draftOf(api, key)).definition.outcomes ?? [];
  expect(outcomes.map((one) => [one.kind, one.redirect?.url ?? null, one.showAnswers ?? null])).toEqual([
    ['redirect', '/press/thanks', false],
    ['message', null, true],
  ]);
  expect(outcomes[0]?.rules?.map((rule) => [rule.effect, rule.conditions])).toEqual([
    ['use', [expect.objectContaining({ op: 'isNotEmpty' })]],
  ]);
  expect(outcomes[1]?.message?.['']).toContain('Thanks, {{ field.name }}');

  // The Settings view holds no After sending: its groups are the form's settings alone (ADR-0256).
  await page.getByRole('tab', { name: 'Settings', exact: true }).click();
  const settings = page.locator('jfb-form-settings-view');
  await expect(settings.locator('.group').first()).toBeVisible();
  await expect(settings.locator('.group')).toHaveCount(6);
  await expect(settings.locator('#group-after')).toHaveCount(0);
  await expect(settings.locator('jfb-outcome-list')).toHaveCount(0);
});

test("the form check's Fix it of an outcome opens its sidebar on the setting on After sending, from the badge and from another view", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await newForm(api, 'Outcome check');
  // A redirect to an address the server refuses, before the form's own message.
  const draft = await draftOf(api, key);
  const outcomes = [
    { key: crypto.randomUUID(), kind: 'redirect', redirect: { url: '//elsewhere.example', delaySeconds: 0 } },
    ...(draft.definition.outcomes ?? []),
  ];
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: { ...draft.definition, outcomes } },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await loginAs('designer');

  // From the Design view's check: Fix it opens After sending on the outcome's address, its problem under it.
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-form-canvas .bo-check')).toHaveAccessibleName('Form check: 1 problem');
  await design.locator('jfb-form-canvas .bo-check').click();
  const check = page.locator('jfb-form-check-modal');
  await check.getByRole('button', { name: 'Fix it: After sending: outcome 1' }).click();
  await expect(check).toHaveCount(0);
  // The outcomes are the Design view's: no other view opens (ADR-0256).
  await expect(page).not.toHaveURL(/\/view\/settings$/);
  await expect(design.locator('jfb-form-outcomes')).toHaveCount(1);
  const sidebar = page.locator('jfb-outcome-modal');
  await expect(sidebar.getByRole('heading', { name: 'Outcome 1' })).toBeVisible();
  const address = sidebar.getByLabel('Address', { exact: true });
  await expect(address).toBeFocused();
  await expect(sidebar.locator('#address-why-0')).toHaveText(
    'A path that starts with two slashes leads to another site.',
  );
  await axeInside(page, 'jfb-outcome-modal');
  // The row counts the problem, said in the sidebar.
  await sidebar.getByRole('button', { name: 'Cancel' }).click();
  await expect(sidebar).toHaveCount(0);
  const list = design.locator('jfb-form-outcomes jfb-outcome-list');
  await expect(list.locator('.outcome-row').first().locator('.row-line')).toHaveText(
    /^1 problem\s+\S\s+Used for every visitor$/,
  );

  // From the publish dialog on the Settings view, Fix it leads to the Design view's After sending, the sidebar opening
  // once the router stays still (ADR-0227).
  await page.getByRole('tab', { name: 'Settings', exact: true }).click();
  await expect(page.locator('jfb-form-settings-view .group').first()).toBeVisible();
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  const dialog = page.locator('jfb-publish-modal');
  await dialog.getByRole('button', { name: 'Fix it: After sending: outcome 1' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(sidebar.getByRole('heading', { name: 'Outcome 1' })).toBeVisible();
  await expect(page).not.toHaveURL(/\/view\/settings$/);
  await expect(design.locator('jfb-form-canvas .bo-after-step')).toHaveAttribute('aria-current', 'step');
  await expect(address).toBeFocused();
});

test("an outcome's preview shows it as visitors get it, the sample entry's answers in place of its tags, a sensitive one masked", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Preview'), [
    { type: 'name', alias: 'name', label: { '': 'Your name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' }, sensitive: true },
  ]);
  const before = (await draftOf(api, key)).definition.outcomes;
  await loginAs('designer');
  const view = await openAfterSending(page, key);
  const list = view.locator('jfb-outcome-list');
  await rowOf(list, `Message: "${DEFAULT}"`).click();
  const sidebar = page.locator('jfb-outcome-modal');
  const preview = sidebar.locator('jfb-outcome-preview');
  await expect(preview.getByRole('heading', { name: 'Preview with a sample entry' })).toBeVisible();
  const outcome = preview.locator('.jfb-form [data-jfb-outcome]');

  // What visitors get now: the site's thank-you and the sample entry's reference, beside the settings.
  await expect(outcome.locator('p').first()).toHaveText(DEFAULT);
  await expect(outcome.locator('[data-jfb-reference]')).toHaveText(/^Your reference is \d{4}-00042$/);
  const settings = (await sidebar.locator('.outcome').boundingBox())!;
  expect((await preview.boundingBox())!.x, 'the preview beside the settings').toBeGreaterThanOrEqual(
    settings.x + settings.width,
  );

  // The message with a tag of each field: the sample's name in its place, the sensitive email masked.
  const message = sidebar.locator('#message [contenteditable="true"]');
  await message.click();
  await message.pressSequentially('Thanks, ');
  await sidebar.getByRole('button', { name: 'Insert merge tag' }).click();
  await sidebar.getByRole('button', { name: 'Your name {{ field.name }}' }).click();
  await sidebar.getByRole('button', { name: 'Insert merge tag' }).click();
  await sidebar.getByRole('button', { name: 'Email {{ field.email }}' }).click();
  await expect(outcome.locator('p').first()).toContainText('Thanks, Alex Berger');
  await expect(outcome.locator('p').first()).toContainText('\u2022\u2022\u2022');
  await expect(preview).not.toContainText('alex.berger@example.com');

  // The copy of the answers, as visitors get it: the email masked there too.
  await sidebar.getByRole('switch', { name: 'Show a copy of the answers' }).check();
  const rows = preview.locator('.jfb-summary dl > div');
  await expect(rows.locator('dt')).toHaveText(['Your name', 'Email']);
  await expect(rows.locator('dd').first()).toHaveText('Alex Berger');
  await expect(rows.locator('dd[data-jfb-masked]')).toHaveCount(1);
  await expect(preview).not.toContainText('alex.berger@example.com');
  await expect(preview.getByRole('button', { name: 'Print your answers' })).toBeVisible();
  await axeInside(page, 'jfb-outcome-modal');

  // A redirect after the message counts down from at least ten seconds, as the runtime does.
  await sidebar.getByRole('radio', { name: 'A message, then a redirect' }).check();
  await sidebar.getByRole('radio', { name: 'An address' }).check();
  await enter(sidebar.getByLabel('Address', { exact: true }), '/thanks');
  await enter(sidebar.getByLabel('Seconds before the redirect'), '5');
  await expect(outcome.locator('[data-jfb-countdown]')).toHaveText('Taking you to the next page in 10 seconds.');
  await expect(outcome.getByRole('link', { name: 'Go now' })).toHaveAttribute('href', '/thanks');

  // Beside the settings, a preview that fits the window stays in view while they scroll, where one that scrolled with
  // them would be gone by their end.
  await sidebar.getByRole('switch', { name: 'Show a copy of the answers' }).uncheck();
  await expect(preview.locator('.jfb-summary')).toHaveCount(0);
  await page.setViewportSize({ width: 1440, height: 560 });
  await sidebar.getByRole('button', { name: 'Add a rule' }).scrollIntoViewIfNeeded();
  await expect(preview.getByRole('heading', { name: 'Preview with a sample entry' })).toBeInViewport();

  // Where the sidebar has no room for two columns, the preview follows the settings; nothing is kept by the preview.
  await page.setViewportSize({ width: 900, height: 900 });
  const below = async () => {
    const [settingsBox, previewBox] = [await sidebar.locator('.outcome').boundingBox(), await preview.boundingBox()];
    return previewBox!.y - (settingsBox!.y + settingsBox!.height);
  };
  await expect.poll(below, { message: 'the preview below the settings' }).toBeGreaterThanOrEqual(0);
  await sidebar.getByRole('button', { name: 'Cancel' }).click();
  await expect(sidebar).toHaveCount(0);
  expect((await draftOf(api, key)).definition.outcomes).toEqual(before);
});
