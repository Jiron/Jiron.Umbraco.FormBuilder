import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { bodyOf, headerOf, visibleText } from '../../support/mail.ts';

// Test of an action (ROADMAP 10.2c, IA 5.3, ADR-0190): from its sidebar before it is added, and from its card, the
// action as the designer holds it runs against the sample entry; an email goes to the tester alone, its first line
// naming whom the real one goes to, and lands in the host's pickup folder.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    actions?: unknown[];
    readonly pages: readonly {
      readonly groups: readonly { readonly fields: readonly { key: string; alias?: string }[] }[];
    }[];
    readonly [other: string]: unknown;
  };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

/** A form with a name and an email field; its key and the email field's. */
async function newForm(api: APIRequestContext, name: string) {
  const key = await formWithFields(api, name, [
    { type: 'name', alias: 'name', label: { '': 'Your name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);
  const fields = (await draftOf(api, key)).definition.pages.flatMap((page) =>
    page.groups.flatMap((group) => group.fields),
  );
  return { key, email: fields.find((field) => field.alias === 'email')!.key };
}

async function openActions(page: Page, key: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/actions`);
  const view = page.locator('jfb-form-actions-view');
  await expect(view.locator('.page')).toBeVisible();
  await expect(view.locator('.skeleton')).toHaveCount(0);
  return view;
}

test('an email from a preset is tested from its sidebar before it is added, and its test mail lands in the pickup folder', async ({
  page,
  loginAs,
  apiAs,
  accountOf,
  waitForMail,
}) => {
  test.setTimeout(240_000);
  const designer = accountOf('designer');
  const api = await apiAs('designer');
  const name = unique('Tested before adding');
  const form = await newForm(api, name);
  await loginAs('designer');
  const view = await openActions(page, form.key);

  // Add action starts ready to email the team; an address goes in the To box.
  await view.getByRole('button', { name: 'Add action' }).click();
  const sidebar = page.locator('jfb-action-modal');
  await expect(sidebar.getByRole('radio', { name: 'Email the team' })).toBeChecked();
  const to = sidebar.getByRole('group', { name: 'To', exact: true });
  await to.locator('summary', { hasText: 'Add recipient' }).click();
  const address = to.getByRole('textbox', { name: 'An address' });
  await address.fill('sales@example.com');
  await address.press('Enter');
  await expect(to.locator('.chip-text')).toHaveText(['sales@example.com']);

  // Test before Add: the mail goes to the tester alone, its first line naming whom the real one goes to.
  await sidebar.getByRole('button', { name: 'Test', exact: true }).click();
  await expect(page.getByText(`Test email sent to you (${designer.email}).`)).toBeVisible();
  const mail = await waitForMail(
    (candidate) =>
      headerOf(candidate.raw, 'To').includes(designer.email) && headerOf(candidate.raw, 'Subject').includes(name),
  );
  expect(headerOf(mail.raw, 'To')).not.toContain('sales@example.com');
  expect(visibleText(bodyOf(mail.raw))).toContain('This is a test. The real email goes to sales@example.com.');

  // The test added nothing: Cancel leaves the form without actions.
  await sidebar.getByRole('button', { name: 'Cancel' }).click();
  await expect(sidebar).toHaveCount(0);
  expect((await draftOf(api, form.key)).definition.actions ?? []).toEqual([]);
});

test('an action is tested from its card, as the draft holds it, and its test mail names who the real one goes to', async ({
  page,
  loginAs,
  apiAs,
  accountOf,
  waitForMail,
}) => {
  test.setTimeout(240_000);
  const designer = accountOf('designer');
  const api = await apiAs('designer');
  const name = unique('Tested from its card');
  const form = await newForm(api, name);
  const draft = await draftOf(api, form.key);
  const saved = await api.put(`${API}/form/${form.key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: {
        ...draft.definition,
        actions: [
          {
            key: crypto.randomUUID(),
            type: 'email',
            name: 'Email the team',
            enabled: true,
            trigger: 'submitted',
            stopOnFailure: false,
            settings: {
              to: ['ops@example.com'],
              toField: form.email,
              subject: { '': 'Card test {{ form.name }}' },
              body: { '': '<p>Hello.</p>' },
            },
          },
        ],
      },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await loginAs('designer');
  const view = await openActions(page, form.key);

  const testButton = view.getByRole('button', { name: 'Test Email the team' });
  // Named for its action, its word Test.
  await expect(view.locator('uui-button.test')).toHaveText('Test');
  await axeInside(page, 'jfb-form-actions-view');
  await testButton.click();
  await expect(page.getByText(`Test email sent to you (${designer.email}).`)).toBeVisible();
  const mail = await waitForMail(
    (candidate) =>
      headerOf(candidate.raw, 'To').includes(designer.email) && headerOf(candidate.raw, 'Subject').includes(name),
  );
  expect(visibleText(bodyOf(mail.raw))).toContain(
    'This is a test. The real email goes to ops@example.com, and to alex.berger@example.com in a mail of its own.',
  );
  await expect(testButton).not.toHaveAttribute('state', 'waiting');
});
