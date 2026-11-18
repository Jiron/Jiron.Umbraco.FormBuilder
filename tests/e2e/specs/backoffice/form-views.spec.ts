import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { chooseLanguage, chooseLanguageIn } from '../../support/designer-row.ts';
import {
  actionSheetOf,
  addAction,
  allSettings,
  backToAllSettings,
  backToKinds,
  chooseMoment,
  expectPicked,
  groupPicked,
  groupRow,
  groupsList,
  kindHeading,
  kindNamed,
  kindsOf,
  kindsShown,
  momentChoice,
  momentLine,
  openSection,
  openSections,
  openSettings,
  otherKinds,
  pickGroup,
  pickGroupByKeyboard,
  pickKind,
  rowLine,
  sectionItems,
  sectionKeys,
  SETTINGS_GROUPS,
  type SettingsGroup,
} from '../../support/form-views.ts';
import { API, formOf, formWithFields, sendAsVisitor, unique } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';

// A form's views, picked first (ROADMAP 22.3, ADR-0258), each check of the step's "Done when" as a test that fails
// without the feature:
// - the Settings view lists its six groups, each row with its line (the row picked without it beside its group),
//   and shows the group picked alone; a group is one pick away by mouse and by keyboard, kept for the next visit;
//   where only one part fits, the list stands alone and "All settings" goes back, the focus following each way; the
//   language is chosen with the Design view's button; and the form check's Fix it picks the group with the focus on
//   its setting, from the Settings view and from another view, a test per group;
// - the inspector shows no "More options" and no section of more than seven settings for any built-in field type the
//   API lists, Messages after Validation and Privacy before Advanced, and what "More options" held is still edited and
//   saved;
// - "Add action" lists the kinds first; an action of every kind the API lists is added by picking its kind, its sheet
//   holding no other kind's settings; an action already added opens on its sheet; "When it runs" is one choice with the
//   chosen moment's line;
// - the form's buttons are on Design, Settings and Actions only, with no footer drawn elsewhere or for who may not
//   design; an open entry's bar holds the one primary button; a change made on Design outlives a visit to Entries and
//   is saved after it, and leaving the form with one asks first.
// The History view as the one way to an earlier version is history-restore.spec.ts's.

const SECTION = '/umbraco/section/jfb-forms';
const ELLIPSIS = String.fromCharCode(0x2026);

const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;

/** A view's tab in the form's workspace (Umbraco's own Settings section has a tab of the same name). */
const viewTab = (page: Page, name: string) =>
  page.locator('jfb-form-workspace-editor').getByRole('tab', { name, exact: true });

interface DraftField {
  readonly key: string;
  readonly type: string;
  readonly alias?: string;
  readonly [other: string]: unknown;
}

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    readonly pages: { readonly groups: { readonly fields: DraftField[] }[] }[];
    settings: Record<string, Record<string, unknown>>;
    actions?: { readonly type: string; readonly name?: string }[];
  };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

/** Writes a draft's settings group by group, over what it holds. */
async function withSettings(api: APIRequestContext, key: string, settings: Record<string, Record<string, unknown>>) {
  const draft = await draftOf(api, key);
  for (const [group, values] of Object.entries(settings))
    draft.definition.settings[group] = { ...draft.definition.settings[group], ...values };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

/** A form with a name and an email field; its key and the fields' keys by alias. */
async function personForm(api: APIRequestContext, name: string) {
  const key = await formWithFields(api, unique(name), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);
  const fields = Object.fromEntries(
    (await draftOf(api, key)).definition.pages[0]!.groups[0]!.fields.map((field) => [field.alias, field.key]),
  );
  return { key, fields };
}

/** Saves the draft and waits for the save's own answer, which the button does not wait for. */
async function saveDraft(page: Page, key: string) {
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
}

/** The ids of the boxes around the element with the focus inside a view, the innermost first. */
const focusedWithin = (view: Locator): Promise<string[]> =>
  view.evaluate((element) => {
    let active: Element | null = element.shadowRoot?.activeElement ?? null;
    while (active?.shadowRoot?.activeElement) active = active.shadowRoot.activeElement;
    const ids: string[] = [];
    for (let at: Element | null = active; at && at !== element;) {
      if (at.id) ids.push(at.id);
      at = at.parentElement ?? ((at.getRootNode() as ShadowRoot).host as Element | undefined) ?? null;
    }
    return ids;
  });

// ---- A form's Settings ---------------------------------------------------------------------------------------------

test('the Settings view lists its groups with their lines, shows the one picked alone, and keeps the pick', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const { key } = await personForm(api, 'Settings picked');
  await loginAs('designer');
  const view = await openSettings(page, `${workspaceOf(key)}/view/settings`);

  // Six rows, Submission picked at first: its row its name alone beside its group, every other row with its line.
  await expect(groupsList(view).locator('button')).toHaveCount(SETTINGS_GROUPS.length);
  await expectPicked(view, 'submission');
  await expect(groupRow(view, 'submission')).toHaveAttribute('aria-current', 'true');
  await expect(rowLine(view, 'submission')).toBeHidden();
  for (const group of SETTINGS_GROUPS.filter((one) => one !== 'submission')) {
    await expect(rowLine(view, group), group).toBeVisible();
    await expect(rowLine(view, group), group).not.toBeEmpty();
  }
  // Only the group picked is on the screen: one heading, one group's settings.
  await expect(view.locator('.group-body')).toHaveCount(1);
  await expect(groupPicked(view).locator('h2')).toHaveText('Submission');
  await expect(view.getByLabel('Send button')).toBeVisible();
  await expect(view.getByLabel('Theme')).toHaveCount(0);
  await axeInside(page, 'jfb-form-settings-view');

  // By mouse, each group one pick away, the one before gone and its row with its line again.
  let before: SettingsGroup = 'submission';
  for (const group of SETTINGS_GROUPS.slice(1)) {
    await groupRow(view, group).click();
    await expectPicked(view, group);
    await expect(groupRow(view, group)).toHaveAttribute('aria-current', 'true');
    await expect(groupRow(view, before)).not.toHaveAttribute('aria-current', 'true');
    await expect(rowLine(view, before)).toBeVisible();
    await expect(view.locator('.group-body')).toHaveCount(1);
    before = group;
  }
  await expect(view.getByLabel('Theme')).toHaveCount(0);
  await expect(view.getByLabel('Query values')).toBeVisible();

  // By keyboard: Tab through the rows, Enter picks.
  await pickGroupByKeyboard(page, view, 'appearance');
  await expect(view.getByLabel('Theme')).toBeVisible();

  // Kept for the next visit, by the user.
  await page.reload();
  await expectPicked(view, 'appearance');
  await page.goto(`${workspaceOf(key)}/view/design`);
  await viewTab(page, 'Settings').click();
  await expectPicked(view, 'appearance');
});

test("too narrow for both, a pick takes the list's place and All settings goes back, the focus following", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const { key } = await personForm(api, 'Settings narrow');
  await loginAs('designer');
  const view = await openSettings(page, `${workspaceOf(key)}/view/settings`, 920);

  // The list alone, every row with its line, Submission's too.
  await expect(groupsList(view)).toBeVisible();
  await expect(groupPicked(view)).toBeHidden();
  for (const group of SETTINGS_GROUPS) await expect(rowLine(view, group), group).toBeVisible();

  // A pick by mouse: the group in the list's place, the focus on its first setting.
  await groupRow(view, 'privacy').click();
  await expectPicked(view, 'privacy');
  await expect(groupsList(view)).toBeHidden();
  await expect(allSettings(view)).toBeVisible();
  await expect.poll(() => focusedWithin(view)).toContain('retention');
  await axeInside(page, 'jfb-form-settings-view');

  // All settings: the list again, the focus on the row of the group it left.
  await backToAllSettings(view);
  await expect(groupPicked(view)).toBeHidden();
  await expect(groupRow(view, 'privacy')).toBeFocused();

  // By keyboard the same: Enter on a row, then Enter on All settings.
  await groupRow(view, 'appearance').focus();
  await page.keyboard.press('Enter');
  await expectPicked(view, 'appearance');
  await expect(view.getByLabel('Theme')).toBeFocused();
  await allSettings(view).focus();
  await page.keyboard.press('Enter');
  await expect(groupsList(view)).toBeVisible();
  await expect(groupRow(view, 'appearance')).toBeFocused();
});

test("the Settings view's language is chosen with the Design view's button, and stays with the workspace", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key } = await personForm(api, 'Settings language');
  await loginAs('designer');
  const view = await openSettings(page, `${workspaceOf(key)}/view/settings`);
  await chooseLanguageIn(groupPicked(view), 'de-DE');
  // The group picked says which language its texts are written in.
  await expect(groupPicked(view).locator('.note')).toBeVisible();
  // The Design view shows the same language, with the same button.
  await viewTab(page, 'Design').click();
  const canvas = page.locator('jfb-form-design-view jfb-form-canvas');
  await expect(canvas.locator('.bo-language')).toHaveAttribute('aria-label', /^Language de-DE:/);
  await chooseLanguage(page.locator('jfb-form-design-view'), 'en-US');
  await viewTab(page, 'Settings').click();
  await expect(groupPicked(view).locator('.bo-language')).toHaveAttribute('aria-label', /^Language en-US:/);
});

/** A problem of the form check in each group, as the draft keeps it, and the box its Fix it lands in. */
const PROBLEMS: Readonly<
  Record<SettingsGroup, { settings: (fields: Record<string, string>) => Record<string, unknown>; box: string }>
> = {
  // Verification of a field that is no email field.
  submission: {
    settings: (fields) => ({ verification: { emailField: fields['name'], days: 7 } }),
    box: 'verification-field',
  },
  // A form for some member groups that names none.
  availability: { settings: () => ({ members: 'groups' }), box: 'member-groups' },
  // The captcha level without its provider.
  protection: { settings: () => ({ level: 'captcha' }), box: 'protection' },
  // Full IP addresses, which this host does not allow.
  privacy: { settings: () => ({ ip: 'full' }), box: 'ip' },
  // A theme the site does not have.
  appearance: { settings: () => ({ theme: 'gone-theme' }), box: 'theme' },
  // A query value that is no name.
  developer: { settings: () => ({ queryValues: ['with space'] }), box: 'query-values' },
};

for (const group of SETTINGS_GROUPS) {
  test(`Fix it in ${group} picks the group, the focus on its setting, from another view and from Settings`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    test.setTimeout(180_000);
    const api = await apiAs('designer');
    const { key, fields } = await personForm(api, `Fix it ${group}`);
    const problem = PROBLEMS[group];
    await withSettings(api, key, { [group]: problem.settings(fields) });
    await loginAs('designer');
    await page.setViewportSize({ width: 1440, height: 900 });
    const view = page.locator('jfb-form-settings-view');

    // From another view: the Design view's form check.
    await page.goto(workspaceOf(key));
    const design = page.locator('jfb-form-design-view');
    await design.locator('jfb-form-canvas .bo-check').click();
    const check = page.locator('jfb-form-check-modal');
    await check.getByRole('button', { name: 'Fix it: Form settings' }).first().click();
    await expect(check).toHaveCount(0);
    await expect(page).toHaveURL(/\/view\/settings$/);
    await expectPicked(view, group);
    await expect.poll(() => focusedWithin(view)).toContain(problem.box);

    // From the Settings view, another group picked: the publish dialog's Fix it.
    await pickGroup(view, group === 'submission' ? 'developer' : 'submission');
    await page.getByRole('button', { name: `Publish${ELLIPSIS}` }).click();
    const dialog = page.locator('jfb-publish-modal');
    await dialog.getByRole('button', { name: 'Fix it: Form settings' }).first().click();
    await expect(dialog).toHaveCount(0);
    await expectPicked(view, group);
    await expect.poll(() => focusedWithin(view)).toContain(problem.box);
  });
}

// ---- The inspector -------------------------------------------------------------------------------------------------

interface FieldTypeOfApi {
  readonly alias: string;
  readonly name: string;
  readonly holdsValue: boolean;
  readonly features: readonly string[];
}

test('the inspector folds nothing in a section, and shows at most seven settings, for every built-in type', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(600_000);
  const api = await apiAs('designer');
  const listed = (await (await api.get(`${API}/field-type`)).json()) as { types: FieldTypeOfApi[] };
  // The built-in types are named by the backoffice's own words; a site's own type names itself.
  const types = listed.types.filter((type) => type.name.startsWith('#jfbFieldTypes_'));
  expect(types.length).toBeGreaterThan(30);
  const key = await formWithFields(
    api,
    unique('Every section'),
    types.map((type, at) => ({
      type: type.alias,
      ...(type.holdsValue ? { alias: `f${String(at)}` } : {}),
      ...(type.features.includes('label') ? { label: { '': `Field ${String(at)}` } } : {}),
    })),
  );
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  const inspector = design.locator('jfb-designer-inspector');
  const order = ['essentials', 'validation', 'messages', 'rules', 'appearance', 'privacy', 'advanced'];

  for (const [at, type] of types.entries()) {
    await test.step(type.alias, async () => {
      const select = design.locator('jfb-canvas-field [data-control="select"]').nth(at);
      await select.focus();
      await page.keyboard.press('Enter');
      await expect(inspector.locator('h2')).toBeVisible();
      await openSections(inspector);
      const keys = await sectionKeys(inspector);
      // The sections in their order, none a "more" of another, and no "More options" anywhere in them.
      expect(
        keys.filter((one) => !order.includes(one)),
        type.alias,
      ).toEqual([]);
      expect(keys, type.alias).toEqual(order.filter((one) => keys.includes(one)));
      await expect(inspector.locator('button.more')).toHaveCount(0);
      await expect(inspector.getByRole('button', { name: /^More options/ })).toHaveCount(0);
      for (const section of keys.filter((one) => one !== 'rules'))
        expect(await sectionItems(inspector, section).count(), `${type.alias} ${section}`).toBeLessThanOrEqual(7);
    });
  }
});

test('what More options held is in Messages and Privacy, edited and saved', async ({ page, loginAs, apiAs }) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key } = await personForm(api, 'Messages and Privacy');
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  const inspector = design.locator('jfb-designer-inspector');
  await design.getByRole('button', { name: /^Name, / }).click();
  const keys = await sectionKeys(inspector);
  expect(keys.indexOf('messages')).toBe(keys.indexOf('validation') + 1);
  expect(keys.indexOf('privacy')).toBe(keys.indexOf('advanced') - 1);

  await openSection(inspector, 'messages');
  const required = inspector.getByLabel('Message when no answer is given', { exact: true });
  await required.fill('Tell us your name, please.');
  await required.press('Tab');
  await openSection(inspector, 'privacy');
  await inspector.getByRole('switch', { name: 'Sensitive', exact: true }).check();
  await axeInside(page, 'jfb-designer-inspector');
  await saveDraft(page, key);
  const field = (await draftOf(api, key)).definition.pages[0]!.groups[0]!.fields.find((one) => one.alias === 'name');
  expect(field?.['messages']).toMatchObject({ required: { '': 'Tell us your name, please.' } });
  expect(field?.['sensitive']).toBe(true);
});

// ---- "Add action" --------------------------------------------------------------------------------------------------

interface ActionTypeOfApi {
  readonly alias: string;
  readonly name: string;
  readonly presets: readonly { readonly name: string }[];
  readonly settings: readonly { readonly name: string }[];
}

async function openActions(page: Page, key: string): Promise<Locator> {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${workspaceOf(key)}/view/actions`);
  const view = page.locator('jfb-form-actions-view');
  await expect(view.locator('.page')).toBeVisible();
  await expect(view.locator('.skeleton')).toHaveCount(0);
  return view;
}

/** The Add button at the sheet's foot. */
const addButton = (sheet: Locator) =>
  sheet.locator('[slot="actions"]').getByRole('button', { name: 'Add', exact: true });

test("Add action lists the kinds, then the kind's sheet; Other kinds goes back; When it runs is one choice", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const { key } = await personForm(api, 'Kinds first');
  await loginAs('designer');
  const view = await openActions(page, key);

  // The kinds, each with its icon, its name and its line, under Umbraco's headline; the focus on the first.
  const sheet = await addAction(page, view);
  await expect(sheet.locator('umb-body-layout').first()).toHaveAttribute('headline', 'New action');
  const kinds = await kindsShown(sheet);
  expect(kinds.slice(0, 3)).toEqual([
    { name: 'Email the team', line: 'Tells your team about each entry, with its answers.' },
    { name: 'Confirm to the visitor', line: 'Sends the visitor a copy of what they sent.' },
    { name: 'Webhook', line: 'Sends each entry to an address of your own.' },
  ]);
  for (const kind of kinds) expect(kind.line, kind.name).not.toBe('');
  await expect(kindsOf(sheet).first()).toBeFocused();
  await expect(kindsOf(sheet).locator('uui-icon')).toHaveCount(kinds.length);
  // No box of a sheet yet.
  await expect(sheet.locator('#name')).toHaveCount(0);
  await axeInside(page, 'jfb-action-modal');

  // A pick: the sheet headed by the kind, with the way back, the focus in its first box.
  await pickKind(sheet, 'Email the team');
  await expect(otherKinds(sheet)).toBeVisible();
  await expect.poll(() => focusedWithin(sheet)).toContain('name');
  await expect(sheet.getByRole('textbox', { name: 'Subject' })).toBeVisible();
  await expect(kindsOf(sheet)).toHaveCount(0);
  await axeInside(page, 'jfb-action-modal');

  // When it runs: one choice, the moment chosen said under it, no other.
  await expect(momentChoice(sheet).locator('option')).toHaveCount(4);
  await expect(sheet.locator('input[name="trigger"]')).toHaveCount(0);
  await expect(momentLine(sheet)).toHaveCount(1);
  await expect(momentLine(sheet)).toHaveText('It runs when an entry comes in.');
  await chooseMoment(sheet, 'approved');
  await expect(momentLine(sheet)).toHaveCount(1);
  await expect(momentLine(sheet)).not.toHaveText('It runs when an entry comes in.');
  await chooseMoment(sheet, 'submitted');

  // Other kinds: the kinds again, the focus on the one picked; another kind replaces the sheet.
  await backToKinds(sheet);
  await expect(kindNamed(sheet, 'Email the team')).toBeFocused();
  await pickKind(sheet, 'Webhook');
  await expect(sheet.getByRole('textbox', { name: 'Subject' })).toHaveCount(0);
  await sheet.getByRole('textbox', { name: 'Address', exact: true }).fill(`${STUB_URL}/kinds-first`);
  await addButton(sheet).click();
  await expect(sheet).toHaveCount(0);
  await expect(view.locator('jfb-action-list .card')).toHaveCount(1);

  // An action already added opens on its sheet, headed by its kind, without the kinds or the way back.
  await view.locator('jfb-action-list .open').first().click();
  await expect(kindHeading(sheet)).toHaveText('Webhook');
  await expect(otherKinds(sheet)).toHaveCount(0);
  await expect(kindsOf(sheet)).toHaveCount(0);
  await expect(momentLine(sheet)).toHaveText('It runs when an entry comes in.');
});

/** What each kind of the test host needs before Add keeps it, by its choice. */
const FILL: Readonly<Record<string, (sheet: Locator) => Promise<void>>> = {
  'email:0': async (sheet) => {
    const to = sheet.getByRole('group', { name: 'To', exact: true });
    await to.locator('summary', { hasText: 'Add recipient' }).click();
    const address = to.getByRole('textbox', { name: 'An address' });
    await address.fill('sales@example.com');
    await address.press('Enter');
  },
  'email:1': async (sheet) => {
    const to = sheet.getByRole('group', { name: 'To', exact: true });
    await to.locator('summary', { hasText: 'Add recipient' }).click();
    await to.getByRole('button', { name: 'The answer of a field' }).click();
    await expect(to.getByRole('combobox', { name: 'The answer of' })).toHaveValue(/.+/);
  },
  'webhook:0': async (sheet) => sheet.getByRole('textbox', { name: 'Address', exact: true }).fill(`${STUB_URL}/kind`),
  'webhook:1': async (sheet) =>
    sheet.getByRole('textbox', { name: 'Address', exact: true }).fill('$JironFormBuilder:Secrets:E2ESlack'),
  'webhook:2': async (sheet) =>
    sheet.getByRole('textbox', { name: 'Address', exact: true }).fill('$JironFormBuilder:Secrets:E2ETeams'),
  setState: async () => undefined,
  content: async (sheet) => {
    await sheet.getByRole('combobox', { name: 'Document type' }).selectOption({ label: 'Demo page' });
    await sheet.getByRole('textbox', { name: 'Name of the item' }).fill('Entry {{ field.name }}');
  },
};

test("an action of every kind the API lists is added by its kind, its sheet holding no other kind's settings", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(480_000);
  const api = await apiAs('designer');
  const { key } = await personForm(api, 'Every kind');
  const { types } = (await (await api.get(`${API}/action-type`)).json()) as { types: ActionTypeOfApi[] };
  // The kinds are each type's presets, or the type itself where it has none.
  const choices = types.flatMap((type) =>
    type.presets.length > 0
      ? type.presets.map((_, at) => ({ id: `${type.alias}:${String(at)}`, type }))
      : [{ id: type.alias, type }],
  );
  const boxOf = (name: string) => `setting-${name.replace(/[^a-zA-Z0-9]/g, '-')}`;
  await loginAs('designer');
  const view = await openActions(page, key);

  for (const [at, { id, type }] of choices.entries()) {
    await test.step(id, async () => {
      const fill = FILL[id];
      expect(fill, `the test knows what ${id} needs before Add keeps it`).toBeDefined();
      const sheet = await addAction(page, view);
      await expect(kindsOf(sheet)).toHaveCount(choices.length);
      const kind = sheet.locator(`button.kind[data-choice="${id}"]`);
      const name = ((await kind.locator('.kind-name').textContent()) ?? '').trim();
      await kind.click();
      await expect(kindHeading(sheet)).toHaveText(name);
      // None of another type's own settings.
      const own = new Set(type.settings.map((setting) => setting.name));
      const others = types
        .filter((other) => other.alias !== type.alias)
        .flatMap((other) => other.settings.map((setting) => setting.name))
        .filter((setting) => !own.has(setting));
      for (const setting of new Set(others)) await expect(sheet.locator(`#${boxOf(setting)}`), setting).toHaveCount(0);
      await fill!(sheet);
      await addButton(sheet).click();
      await expect(sheet).toHaveCount(0);
      await expect(view.locator('jfb-action-list .card')).toHaveCount(at + 1);
    });
  }
  await saveDraft(page, key);
  expect(((await draftOf(api, key)).definition.actions ?? []).map((action) => action.type)).toEqual(
    choices.map((choice) => choice.type.alias),
  );
});

// ---- The form's buttons --------------------------------------------------------------------------------------------

/** The form's three buttons, and the footer that holds them, drawn. */
const formButtons = (page: Page) =>
  ['Preview', 'Save draft', `Publish${ELLIPSIS}`].map((name) =>
    page.locator('umb-workspace-footer').getByRole('button', { name, exact: true }),
  );
const footer = (page: Page) => page.locator('umb-workspace-footer').filter({ visible: true });

test("the form's buttons show on Design, Settings and Actions, and no footer on Entries, Insights and History", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('admin');
  const { key } = await personForm(api, 'Buttons where they apply');
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(workspaceOf(key));
  // Embed is the test host's own view (the samples): a view of a site's own has no footer either.
  for (const view of ['Design', 'Settings', 'Actions', 'Entries', 'Insights', 'History', 'Embed']) {
    await viewTab(page, view).click();
    if (view !== 'Design') await expect(page).toHaveURL(new RegExp(`/view/${view.toLowerCase()}$`));
    const changes = ['Design', 'Settings', 'Actions'].includes(view);
    for (const button of formButtons(page)) await expect(button, view).toHaveCount(changes ? 1 : 0);
    await expect(footer(page), view).toHaveCount(changes ? 1 : 0);
  }
});

test('with an entry open the screen holds one primary button, its own', async ({ page, request, loginAs, apiAs }) => {
  test.setTimeout(240_000);
  const api = await apiAs('admin');
  const { key } = await personForm(api, 'One primary');
  await withSettings(api, key, { submission: { approval: 'manual' }, developer: { headless: true } });
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  await sendAsVisitor(request, key, { name: 'Ada Lovelace', email: 'ada@example.com' });
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(`${workspaceOf(key)}/view/entries`);
  const entries = page.locator('jfb-entries-view');
  await entries.locator('tbody tr').first().locator('.name').click();
  const panel = entries.locator('jfb-entry-panel');
  await expect(panel.getByRole('button', { name: 'Approve' })).toBeVisible();
  // Umbraco's header draws its own icon buttons in the primary look; the screen's actions are in the section's main.
  const primary = page.locator('umb-section-main uui-button[look="primary"]').filter({ visible: true });
  await expect
    .poll(() => primary.evaluateAll((buttons) => buttons.map((button) => button.getAttribute('label'))))
    .toEqual(['Approve']);
});

test('a change on Design outlives a visit to Entries and is saved after it, and leaving the form asks first', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('admin');
  const { key } = await personForm(api, 'Kept across views');
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  const relabel = async (from: RegExp, to: string) => {
    await design.getByRole('button', { name: from }).click();
    await design.locator('jfb-designer-inspector').getByLabel('Label', { exact: true }).fill(to);
    await expect(design.getByRole('button', { name: new RegExp(`^${to}, `) })).toBeVisible();
  };
  await relabel(/^Name, /, 'Full name');

  // Entries, without the form's buttons, then Design again: the change is there, and Save draft keeps it.
  await viewTab(page, 'Entries').click();
  await expect(page.locator('jfb-entries-view')).toBeVisible();
  await expect(footer(page)).toHaveCount(0);
  await viewTab(page, 'Design').click();
  await expect(design.getByRole('button', { name: /^Full name, / })).toBeVisible();
  await saveDraft(page, key);
  expect((await draftOf(api, key)).definition.pages[0]!.groups[0]!.fields[0]!['label']).toEqual({ '': 'Full name' });

  // Another change, then Entries, then away from the form: Umbraco asks first, and Stay keeps it.
  await relabel(/^Full name, /, 'Your name');
  await viewTab(page, 'Entries').click();
  await expect(page.locator('jfb-entries-view')).toBeVisible();
  await page.locator('umb-section-sidebar uui-menu-item[label="Contact"]').filter({ visible: true }).first().click();
  const discard = page.locator('umb-discard-changes-modal, uui-dialog-layout').filter({ hasText: 'Discard' }).first();
  await expect(discard).toBeVisible();
  await discard.getByRole('button', { name: 'Stay' }).click();
  await viewTab(page, 'Design').click();
  await expect(design.getByRole('button', { name: /^Your name, / })).toBeVisible();
});
