import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';

// The form's Actions view (ROADMAP 10.2a, IA 5.3, ADR-0188). The actions show by trigger as cards: Add action opens a
// new action's sidebar ready to email the team, whose problems the form check finds under their box; a card moves by its
// handle with the pointer or with Alt and the arrows, switches off, gets a rule in the rules builder, and Delete offers
// Undo; Save draft keeps them as the server writes them. Without Manage actions the view and each sidebar only show. The
// form check's Fix it of an action's setting opens its sidebar on that setting.

const SECTION = '/umbraco/section/jfb-forms';

interface Action {
  readonly key: string;
  readonly type: string;
  readonly name?: string;
  readonly enabled?: boolean;
  readonly trigger?: string;
  readonly stopOnFailure?: boolean;
  readonly rules?: readonly { readonly effect: string; readonly conditions: readonly unknown[] }[];
  readonly settings?: Readonly<Record<string, unknown>>;
}

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: { actions?: Action[]; readonly [other: string]: unknown };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

const newForm = (api: APIRequestContext, name: string) =>
  formWithFields(api, unique(name), [
    { type: 'text', alias: 'name', label: { '': 'Name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);

/** A form's draft with these actions, saved as the designer saves it. */
async function withActions(api: APIRequestContext, key: string, actions: readonly Omit<Action, 'key'>[]) {
  const draft = await draftOf(api, key);
  const definition = {
    ...draft.definition,
    actions: actions.map((action) => ({ key: crypto.randomUUID(), ...action })),
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

async function openActions(page: Page, key: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}/view/actions`);
  const view = page.locator('jfb-form-actions-view');
  await expect(view.locator('.page')).toBeVisible();
  await expect(view.locator('.skeleton')).toHaveCount(0);
  return view;
}

/** Types into a box and leaves it, as a person does: what can't be kept, and the check's problems, show once it is left. */
async function enter(box: Locator, value: string) {
  await box.fill(value);
  await box.press('Tab');
}

/** The cards of one trigger, and the button on a card that opens its sidebar, by the action's name. */
const cardsOf = (view: Locator, trigger: string) => view.locator(`jfb-action-list[data-trigger="${trigger}"] .card`);
const cardOf = (view: Locator, name: string) =>
  view.locator('.open').filter({ has: view.page().locator('.name', { hasText: name }) });

/** Saves the draft and waits for the save's own answer, which the button does not wait for. */
async function saveDraft(page: Page, key: string) {
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
}

/** Drags by a handle to a point, in small steps as a hand does, and lets go there. */
async function drag(page: Page, handle: Locator, to: { x: number; y: number }) {
  await handle.hover({ force: true });
  const from = (await handle.boundingBox())!;
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
  await page.mouse.down();
  await page.mouse.move(from.x + from.width / 2 + 8, from.y + from.height / 2 + 8, { steps: 2 });
  await page.mouse.move(to.x, to.y, { steps: 12 });
  await page.mouse.up();
}

test('the Actions view adds an action from a preset, moves it by drag and keyboard, switches it off, gives it a rule and deletes it with Undo', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await newForm(api, 'Actions');
  await loginAs('designer');
  const view = await openActions(page, key);
  const names = cardsOf(view, 'submitted').locator('.name');
  // The view's own live region, after its page; each list has its own.
  const status = view.locator('.page + [role="status"]');

  // A form without actions says what most forms do, which Add action starts with.
  await expect(view.locator('.empty')).toHaveText(
    'No actions yet. Most forms email the team when an entry comes in, and Add action starts with that.',
  );
  await axeInside(page, 'jfb-form-actions-view');

  // Add action: ready to email the team, kept back by the check until the mail has someone to go to.
  await view.getByRole('button', { name: 'Add action' }).click();
  const sidebar = page.locator('jfb-action-modal');
  await expect(sidebar.getByRole('heading', { name: 'New action' })).toBeVisible();
  await expect(sidebar.getByRole('radio', { name: 'Email the team' })).toBeChecked();
  await expect(sidebar.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Email the team');
  await expect(sidebar.getByRole('textbox', { name: 'Subject' })).toHaveValue('New entry in {{ form.name }}');
  await axeInside(page, 'jfb-action-modal');
  await sidebar.locator('[slot="actions"]').getByRole('button', { name: 'Add', exact: true }).click();
  // The To box's Add recipient takes the focus, the problem said under the box (ADR-0189).
  const to = sidebar.locator('summary', { hasText: 'Add recipient' });
  await expect(to).toBeFocused();
  await expect(sidebar.locator('#setting-to-found-0')).toHaveText('A mail needs someone to go to.');
  await to.click();
  await enter(sidebar.getByRole('textbox', { name: 'An address' }), 'sales@example.com');
  await sidebar.locator('[slot="actions"]').getByRole('button', { name: 'Add', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(names).toHaveText(['Email the team']);
  await expect(cardsOf(view, 'submitted').locator('.line')).toHaveText(['To sales@example.com']);
  await expect(status).toHaveText('Added. Email the team runs when an entry comes in.');
  await expect(cardOf(view, 'Email the team')).toBeFocused();

  // Another choice of what happens replaces the settings below it, and the name until one is typed.
  await view.getByRole('button', { name: 'Add action' }).click();
  await sidebar.getByRole('radio', { name: "Change the entry's state" }).check();
  await expect(sidebar.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue("Change the entry's state");
  await expect(sidebar.getByRole('combobox', { name: 'State' })).toHaveValue('approved');
  await expect(sidebar.getByRole('textbox', { name: 'Subject' })).toHaveCount(0);
  await enter(sidebar.getByRole('textbox', { name: 'Name', exact: true }), 'Mark as approved');
  await sidebar.locator('[slot="actions"]').getByRole('button', { name: 'Add', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(names).toHaveText(['Email the team', 'Mark as approved']);
  await expect(cardsOf(view, 'submitted').locator('.line')).toHaveText(['To sales@example.com', 'Approves the entry']);

  // Alt and the arrows move a card within its trigger and say where it went; the focus stays on its handle.
  const handle = view.getByRole('button', { name: 'Move Mark as approved' });
  await handle.focus();
  await handle.press('Alt+ArrowUp');
  await expect(names).toHaveText(['Mark as approved', 'Email the team']);
  await expect(view.locator('jfb-action-list').getByRole('status')).toHaveText(
    'Moved. Mark as approved: position 1 of 2.',
  );
  await expect(handle).toBeFocused();

  // The handle drags it back below the other.
  const below = (await cardsOf(view, 'submitted').nth(1).boundingBox())!;
  await drag(page, handle, { x: below.x + 80, y: below.y + below.height - 4 });
  await expect(names).toHaveText(['Email the team', 'Mark as approved']);

  // Without dragging, a pointer chooses its place in its sidebar (WCAG 2.5.7); Ctrl+Z takes the move back.
  await cardOf(view, 'Mark as approved').click();
  await sidebar.getByRole('combobox', { name: 'Place in the list' }).selectOption({ label: '1 of 2' });
  await sidebar.getByRole('button', { name: 'Update', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(names).toHaveText(['Mark as approved', 'Email the team']);
  await expect(status).toHaveText('Moved. Mark as approved: position 1 of 2.');
  await expect(cardOf(view, 'Mark as approved')).toBeFocused();
  await cardOf(view, 'Mark as approved').press('Control+z');
  await expect(names).toHaveText(['Email the team', 'Mark as approved']);
  await expect(cardOf(view, 'Mark as approved')).toBeFocused();

  // Its switch takes it out of the run without deleting it.
  const use = view.getByRole('switch', { name: 'Use Mark as approved' });
  await use.uncheck();
  await expect(use).not.toBeChecked();

  // A rule in the rules builder, which runs the action only for some entries.
  await cardOf(view, 'Email the team').click();
  await expect(sidebar.getByRole('heading', { name: 'Email the team' })).toBeVisible();
  await expect(sidebar.locator('.kept')).toHaveText('Email');
  await expect(sidebar.locator('#rule-words')).toHaveText('Runs for every entry its trigger brings.');
  await sidebar.getByRole('button', { name: 'Add a rule' }).click();
  const builder = page.locator('jfb-rule-builder-modal');
  await expect(builder.getByRole('heading', { name: 'New rule' })).toBeVisible();
  await expect(builder.locator('.first-line')).toHaveText('Run Email the team when');
  await builder.getByRole('combobox', { name: 'What condition 1 looks at' }).selectOption({ label: 'Name' });
  await builder.getByRole('combobox', { name: 'How condition 1 compares' }).selectOption({ label: 'is not empty' });
  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(builder).toHaveCount(0);
  await expect(sidebar.locator('#rule-words')).toHaveText('Runs when Name is not empty');
  await expect(sidebar.getByRole('button', { name: 'Change rule' })).toBeFocused();
  await sidebar.getByRole('button', { name: 'Update', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(cardsOf(view, 'submitted').first().locator('.rule')).toHaveText('Runs when Name is not empty');
  await expect(cardOf(view, 'Email the team')).toBeFocused();

  // Delete acts at once; Undo in the toast brings it back, the focus on it.
  await view.getByRole('button', { name: 'More actions for Mark as approved' }).click();
  await view.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(names).toHaveText(['Email the team']);
  await page
    .locator('uui-toast-notification')
    .filter({ hasText: 'Mark as approved deleted.' })
    .getByRole('button', { name: 'Undo' })
    .click();
  await expect(names).toHaveText(['Email the team', 'Mark as approved']);
  await expect(cardOf(view, 'Mark as approved')).toBeFocused();
  await expect(view.getByRole('switch', { name: 'Use Mark as approved' })).not.toBeChecked();
  await axeInside(page, 'jfb-form-actions-view');

  // Save draft keeps them as the server writes them, in the order they run.
  await saveDraft(page, key);
  const actions = (await draftOf(api, key)).definition.actions ?? [];
  expect(
    actions.map((one) => [one.type, one.name, one.enabled, one.trigger, one.rules?.map((rule) => rule.effect)]),
  ).toEqual([
    ['email', 'Email the team', true, 'submitted', ['run']],
    ['setState', 'Mark as approved', false, 'submitted', undefined],
  ]);
  expect(actions[0]?.settings?.['to']).toEqual(['sales@example.com']);
  expect(actions[0]?.rules?.[0]?.conditions).toEqual([expect.objectContaining({ op: 'isNotEmpty' })]);
});

test('without Manage actions the view and each sidebar show the actions as they are, with nothing to change', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await newForm(api, 'Read-only actions');
  await withActions(api, key, [
    {
      type: 'email',
      name: 'Email the team',
      enabled: true,
      trigger: 'submitted',
      stopOnFailure: false,
      settings: {
        to: ['sales@example.com'],
        subject: { '': 'New entry' },
        body: { '': '<p>A new entry came in.</p>' },
      },
    },
    {
      type: 'setState',
      name: 'Spam check',
      enabled: false,
      trigger: 'submitted',
      stopOnFailure: false,
      settings: { state: 'spam' },
    },
  ]);
  await loginAs('layoutDesigner');
  const view = await openActions(page, key);

  // One line says what changing them needs; no card has a handle, a switch or "...", and an action that is off says so.
  await expect(view.locator('.read-only')).toHaveText(
    'Changing these actions needs the Manage actions permission for this form. An administrator can grant it.',
  );
  await expect(view.getByRole('button', { name: 'Add action' })).toHaveCount(0);
  await expect(view.getByRole('switch')).toHaveCount(0);
  await expect(view.locator('.handle, .more')).toHaveCount(0);
  await expect(cardsOf(view, 'submitted').locator('.end')).toHaveText(['', 'Off']);
  await axeInside(page, 'jfb-form-actions-view');

  // A card still opens, its settings shown as they are, with Close only.
  await cardOf(view, 'Email the team').click();
  const sidebar = page.locator('jfb-action-modal');
  await expect(sidebar.getByRole('heading', { name: 'Email the team' })).toBeVisible();
  await expect(sidebar.locator('#setting-to')).toHaveText('sales@example.com');
  await expect(sidebar.locator('#setting-subject')).toHaveText('New entry');
  await expect(sidebar.locator('input, textarea, select')).toHaveCount(0);
  await expect(sidebar.getByRole('button', { name: 'Update' })).toHaveCount(0);
  await expect(sidebar.getByRole('button', { name: 'Delete action' })).toHaveCount(0);
  await axeInside(page, 'jfb-action-modal');
  await sidebar.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(cardOf(view, 'Email the team')).toBeFocused();
});

test("the form check's Fix it of an action's setting opens its sidebar on that setting, its problem under it", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await newForm(api, 'Action check');
  // A webhook to a host the site's configuration does not allow.
  await withActions(api, key, [
    {
      type: 'webhook',
      name: 'Send to the CRM',
      enabled: true,
      trigger: 'submitted',
      stopOnFailure: false,
      settings: { url: 'https://crm.example.com/hooks/forms' },
    },
  ]);
  await loginAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-form-canvas .bo-check')).toHaveText('1 problem');
  await design.locator('jfb-form-canvas .bo-check').click();
  const check = page.locator('jfb-form-check-modal');
  await check.getByRole('button', { name: /^Fix it: .*Send to the CRM/ }).click();
  await expect(check).toHaveCount(0);
  await expect(page).toHaveURL(/\/view\/actions$/);
  const sidebar = page.locator('jfb-action-modal');
  await expect(sidebar.getByRole('heading', { name: 'Send to the CRM' })).toBeVisible();
  const address = sidebar.getByRole('textbox', { name: 'Address', exact: true });
  await expect(address).toBeFocused();
  await expect(sidebar.locator('#setting-url-found-0')).toHaveText(
    'The address must be one the host configuration allows: https, on a host in JironFormBuilder:Webhooks:AllowedHosts.',
  );
  await axeInside(page, 'jfb-action-modal');
  // The card counts the problem the sidebar says.
  await sidebar.getByRole('button', { name: 'Cancel' }).click();
  await expect(sidebar).toHaveCount(0);
  const view = page.locator('jfb-form-actions-view');
  await expect(cardsOf(view, 'submitted').locator('.line')).toHaveText([
    /^1 problem\s+\S\s+Webhook to crm\.example\.com$/,
  ]);
  await expect(cardOf(view, 'Send to the CRM')).toBeFocused();
});
