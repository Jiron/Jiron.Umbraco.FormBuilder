import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { addAction } from '../../support/form-views.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';

// An email action's editor and its preview (ROADMAP 10.2b, IA 5.3, ADR-0189). Its To box holds every kind of
// recipient, added from Add recipient: addresses, an email field's answer and the addresses an answer chooses, which
// Save draft keeps as the server writes them; its body takes merge tags, and its preview shows the mail for the sample
// entry with the sample's answers in their place, in a frame as tall as the mail whose links open nothing, and the mail
// itself meets axe in each scheme; the form check's Fix it of an email setting lands on its editor.

const SECTION = '/umbraco/section/jfb-forms';

interface Action {
  readonly key: string;
  readonly type: string;
  readonly name?: string;
  readonly settings?: Readonly<Record<string, unknown>>;
}

interface Field {
  readonly key: string;
  readonly alias?: string;
}

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    actions?: Action[];
    readonly pages: readonly { readonly groups: readonly { readonly fields: readonly Field[] }[] }[];
    readonly [other: string]: unknown;
  };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

/** A form with a name, an email and a topic of two options; the fields' keys by their aliases. */
async function newForm(api: APIRequestContext, name: string) {
  const key = await formWithFields(api, unique(name), [
    { type: 'name', alias: 'name', label: { '': 'Your name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
    {
      type: 'dropdown',
      alias: 'topic',
      label: { '': 'Topic' },
      options: {
        items: [
          { value: 'sales', label: { '': 'Sales' } },
          { value: 'press', label: { '': 'Press' } },
        ],
      },
    },
  ]);
  const fields = (await draftOf(api, key)).definition.pages.flatMap((page) =>
    page.groups.flatMap((group) => group.fields),
  );
  const keyOf = (alias: string) => fields.find((field) => field.alias === alias)!.key;
  return { key, name: keyOf('name'), email: keyOf('email'), topic: keyOf('topic') };
}

/** A form's draft with one email action, saved as the designer saves it. */
async function withEmail(api: APIRequestContext, key: string, settings: Readonly<Record<string, unknown>>) {
  const draft = await draftOf(api, key);
  const definition = {
    ...draft.definition,
    actions: [
      {
        key: crypto.randomUUID(),
        type: 'email',
        name: 'Email the team',
        enabled: true,
        trigger: 'submitted',
        stopOnFailure: false,
        settings,
      },
    ],
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

const cardOf = (view: Locator, name: string) =>
  view.locator('.open').filter({ has: view.page().locator('.name', { hasText: name }) });

/**
 * Axe's WCAG 2.2 A and AA rules on the mail itself, in a page of its own: the backoffice's checks leave the preview's
 * frame alone, where no script may run (ADR-0189).
 */
async function axeOnTheMail(page: Page, preview: Locator, scheme: string) {
  const html = await preview.locator('iframe').evaluate((frame) => (frame as HTMLIFrameElement).srcdoc);
  const sheet = await page.context().newPage();
  try {
    await sheet.setContent(html);
    const result = await new AxeBuilder({ page: sheet })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(
      result.violations.map(
        (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
      ),
      `axe on the mail, ${scheme}`,
    ).toEqual([]);
  } finally {
    await sheet.close();
  }
}

/** Saves the draft and waits for the save's own answer, which the button does not wait for. */
async function saveDraft(page: Page, key: string) {
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
}

test("the recipient builder adds an address, an email field's answer and the addresses an answer chooses, kept as the server writes them", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const form = await newForm(api, 'Recipients');
  await loginAs('designer');
  const view = await openActions(page, form.key);

  // Add action, Email the team picked from the kinds, in the large sidebar.
  const sidebar = await addAction(page, view, 'Email the team');
  const to = sidebar.getByRole('group', { name: 'To', exact: true });
  const add = to.locator('summary', { hasText: 'Add recipient' });

  // Addresses: several at once, apart by commas; one that is none keeps what was typed and says why.
  await add.click();
  const address = to.getByRole('textbox', { name: 'An address' });
  await address.fill('sales@example.com, sales');
  await address.press('Enter');
  await expect(to.locator('#setting-to-address-why')).toHaveText(
    'Enter addresses such as hello@example.com: "sales" isn\'t one.',
  );
  await expect(address).toHaveValue('sales@example.com, sales');
  await address.fill('sales@example.com, ops@example.com');
  await address.press('Enter');
  await expect(to.locator('.chip-text')).toHaveText(['sales@example.com', 'ops@example.com']);
  await expect(to.locator('#setting-to-address-why')).toHaveCount(0);

  // The answer of an email field: only the form's email field is offered.
  await to.getByRole('button', { name: 'The answer of a field' }).click();
  const field = to.getByRole('combobox', { name: 'The answer of' });
  await expect(field).toBeFocused();
  await expect(field.locator('option')).toHaveText(['Email']);

  // The addresses an answer chooses: the choice field, and each option's addresses.
  await add.click();
  await to.getByRole('button', { name: 'Addresses chosen by an answer' }).click();
  await expect(to.getByRole('combobox', { name: 'Addresses chosen by' })).toHaveValue(form.topic);
  await to.getByRole('textbox', { name: 'Sales' }).fill('sales-team@example.com');
  await to.getByRole('textbox', { name: 'Sales' }).press('Tab');
  // Each kind is there once now: Add recipient offers addresses, the person who created the form and the privacy team.
  await add.click();
  await expect(to.locator('.add-kind')).toHaveText([
    'The person who created the form',
    'Everyone who may use the privacy tools',
  ]);
  await add.click();
  await axeInside(page, 'jfb-action-modal');

  await sidebar.locator('[slot="actions"]').getByRole('button', { name: 'Add', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(cardOf(view, 'Email the team')).toBeFocused();
  await saveDraft(page, form.key);
  const kept = (await draftOf(api, form.key)).definition.actions?.[0]?.settings;
  expect(kept).toEqual({
    to: ['sales@example.com', 'ops@example.com'],
    toField: form.email,
    recipientMap: { field: form.topic, routes: [{ value: 'sales', to: ['sales-team@example.com'] }] },
    subject: { '': 'New entry in {{ form.name }}', de: 'Neuer Eintrag in {{ form.name }}' },
    body: {
      '': '<p>A new entry came in.</p>{{ entry.table }}',
      de: '<p>Ein neuer Eintrag ist eingegangen.</p>{{ entry.table }}',
    },
  });
});

test("an email's preview shows the mail for the sample entry, its answers in place of the body's merge tags", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const form = await newForm(api, 'Email preview');
  await withEmail(api, form.key, {
    to: ['sales@example.com'],
    toField: form.email,
    subject: { '': 'New from {{ field.name }}' },
    body: {
      '': '<p>Hello.</p><p><a href="https://example.com/more">Read more</a></p><p>Line three.</p><p>Line four.</p><p>Line five.</p>',
    },
  });
  await loginAs('designer');
  const view = await openActions(page, form.key);
  await cardOf(view, 'Email the team').click();
  const sidebar = page.locator('jfb-action-modal');
  const preview = sidebar.locator('jfb-email-preview');
  await expect(preview.getByRole('heading', { name: 'Preview with a sample entry' })).toBeVisible();
  const meta = preview.locator('.meta');
  await expect(meta.locator('dt')).toHaveText(['From', 'To', 'Also to', 'Subject']);
  await expect(meta.locator('dd').nth(1)).toHaveText('sales@example.com');
  await expect(meta.locator('dd').nth(2)).toHaveText('alex.berger@example.com, in a mail of its own');
  await expect(meta.locator('dd').nth(3)).toHaveText('New from Alex Berger');
  const mail = page.frameLocator('jfb-email-preview iframe');
  await expect(mail.getByText('Hello.')).toBeVisible();
  // Beside the settings, where the sidebar has room for two columns.
  const [settings, shown] = [await sidebar.locator('.action').boundingBox(), await preview.boundingBox()];
  expect(shown!.x, 'the preview beside the settings').toBeGreaterThanOrEqual(settings!.x + settings!.width);

  // A link in the mail opens nothing: no window, and the frame keeps the mail.
  const opened: Page[] = [];
  page.context().on('page', (other) => opened.push(other));
  await mail.getByRole('link', { name: 'Read more' }).click();
  await page.waitForTimeout(500);
  expect(opened, 'no window opened').toHaveLength(0);
  await expect(mail.getByText('Line five.')).toBeVisible();
  const frame = preview.locator('iframe');
  const tall = (await frame.boundingBox())!.height;

  // The body with the sample's name in place of its tag, asked once the changes pause.
  const body = sidebar.locator('#setting-body [contenteditable="true"]');
  await body.click();
  await body.press('Control+A');
  await body.pressSequentially('Hello. From ');
  await sidebar
    .locator('.item')
    .filter({ has: page.locator('#setting-body') })
    .getByRole('button', { name: 'Insert merge tag' })
    .click();
  await sidebar.getByRole('button', { name: 'Your name {{ field.name }}' }).click();
  await expect(mail.getByText('Hello. From Alex Berger')).toBeVisible();
  // One line now: the frame is as short as its mail.
  await expect
    .poll(async () => (await frame.boundingBox())!.height, { message: 'the frame shrinks with its mail' })
    .toBeLessThan(tall);

  // Dark shows the layout's dark colours, whatever the device's own scheme.
  const pageColour = async () =>
    mail
      .locator('.jfb-page')
      .first()
      .evaluate((element) => getComputedStyle(element).backgroundColor);
  const light = await pageColour();
  await axeOnTheMail(page, preview, 'light');
  await preview.getByRole('button', { name: 'Dark' }).click();
  await expect(preview.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(pageColour, { message: 'the dark page' }).not.toBe(light);
  await axeOnTheMail(page, preview, 'dark');
  await axeInside(page, 'jfb-action-modal');
});

test("the form check's Fix it of an email setting lands on its editor, its problem under it", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const form = await newForm(api, 'Email check');
  // The mail goes to the answer of a field that is no email field.
  await withEmail(api, form.key, {
    toField: form.name,
    subject: { '': 'New entry' },
    body: { '': '<p>A new entry came in.</p>' },
  });
  await loginAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${form.key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-form-canvas .bo-check')).toHaveAccessibleName('Form check: 1 problem');
  await design.locator('jfb-form-canvas .bo-check').click();
  const check = page.locator('jfb-form-check-modal');
  await check.getByRole('button', { name: /^Fix it: .*Email the team/ }).click();
  await expect(check).toHaveCount(0);
  await expect(page).toHaveURL(/\/view\/actions$/);
  const sidebar = page.locator('jfb-action-modal');
  await expect(sidebar.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Email the team');
  const field = sidebar.getByRole('combobox', { name: 'The answer of' });
  await expect(field).toBeFocused();
  await expect(field.locator('option:checked')).toHaveText('Your name (no email field)');
  await expect(sidebar.locator('#setting-toField-found-0')).toHaveText('This must be an email field of this form.');
  await axeInside(page, 'jfb-action-modal');
  // Choosing the email field mends it: the problem goes once the check asks again.
  await field.selectOption({ label: 'Email' });
  await expect(sidebar.locator('#setting-toField-found-0')).toHaveCount(0);
});
