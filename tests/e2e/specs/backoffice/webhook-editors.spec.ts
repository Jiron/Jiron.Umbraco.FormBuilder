import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { E2E_WEBHOOK_SECRET, STUB_URL } from '../../support/paths.ts';

// The webhook, Slack and Teams editors (ROADMAP 10.2d, IA 5.3, ADR-0191): each is configured in its sidebar and tested
// before it is added, against the stub on 5560, and what the stub got is what the editors set: the names and values
// chosen, a header and the signing secret by their references, never as themselves, and a chat's message as the chat
// reads it, its first line saying it is a test. The form check's Fix it of a webhook's names and values lands on them.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

interface Call {
  readonly path: string;
  readonly body: string;
  readonly headers: readonly { key: string; value: string }[];
}

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: { actions?: { settings?: Record<string, unknown> }[]; readonly [other: string]: unknown };
}

const header = (call: Call, name: string): string | undefined =>
  call.headers.find((each) => each.key.toLowerCase() === name.toLowerCase())?.value;

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

/** The stub's call to a path whose body holds a text, once it came. */
async function callTo(request: APIRequestContext, path: string, holding: string): Promise<Call> {
  let found: Call | undefined;
  await expect
    .poll(
      async () => {
        const calls = (await (await request.get(`${STUB_URL}/_stub/calls`)).json()) as Call[];
        found = calls.find((call) => call.path === path && call.body.includes(holding));
        return found !== undefined;
      },
      { timeout: 30_000 },
    )
    .toBe(true);
  return found!;
}

async function openActions(page: Page, key: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/actions`);
  const view = page.locator('jfb-form-actions-view');
  await expect(view.locator('.page')).toBeVisible();
  await expect(view.locator('.skeleton')).toHaveCount(0);
  return view;
}

/** Add action, and the choice of what happens. */
async function addAction(page: Page, view: Locator, choice: string) {
  await view.getByRole('button', { name: 'Add action' }).click();
  const sidebar = page.locator('jfb-action-modal');
  await sidebar.getByRole('radio', { name: choice, exact: true }).check();
  return sidebar;
}

/** Test from the sidebar, after its question, and the toast that says what came of it. */
async function testIt(page: Page, sidebar: Locator, said: string) {
  await sidebar.getByRole('button', { name: 'Test', exact: true }).click();
  const confirm = page.locator('umb-confirm-modal');
  await expect(confirm).toContainText('A sample entry goes there as a real one would, marked as a test.');
  await confirm.getByRole('button', { name: 'Send test' }).click();
  await expect(page.locator('uui-toast-notification').filter({ hasText: said })).toBeVisible();
}

/** Add, and the card's line. */
async function add(page: Page, view: Locator, sidebar: Locator, line: RegExp) {
  await sidebar.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(view.locator('jfb-action-list .card .line').first()).toHaveText(line);
  void page;
}

test('a webhook is configured with its names and values, a header and its secret as references, and tested against the stub', async ({
  page,
  request,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  // A header that sends one of the site's secrets is set up by whoever manages its settings (ADR-0235): the administrator.
  const api = await apiAs('admin');
  const name = unique('Webhook editors');
  const key = await formWithFields(api, name, [
    { type: 'name', alias: 'name', label: { '': 'Your name' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);
  const path = `/webhooks/crm-${crypto.randomUUID()}`;
  await loginAs('admin');
  const view = await openActions(page, key);
  const sidebar = await addAction(page, view, 'Webhook');

  // Where it goes: its own address, the stub's.
  await expect(sidebar.getByRole('combobox', { name: 'Where it goes' })).toHaveValue('url');
  await sidebar.getByRole('textbox', { name: 'Address', exact: true }).fill(`${STUB_URL}${path}`);

  // What it sends: the names and values, which start with every field by its alias.
  const what = sidebar.getByRole('group', { name: 'What it sends' });
  await what.getByRole('radio', { name: /^The names and values you choose/ }).check();
  await expect(what.getByRole('textbox', { name: /^Name \d$/ })).toHaveCount(2);
  await expect(what.getByRole('textbox', { name: 'Name 1' })).toHaveValue('name');
  await expect(what.getByRole('textbox', { name: 'Value of email' })).toHaveValue('{{ field.email }}');
  await what.getByRole('textbox', { name: 'Name 2' }).fill('mail');
  await what.getByRole('button', { name: 'Add a name' }).click();
  await expect(what.getByRole('textbox', { name: 'Name 3' })).toBeFocused();
  await page.keyboard.type('source');
  await what.getByRole('textbox', { name: 'Value of source' }).fill('website');
  await what.getByRole('button', { name: 'Remove name' }).click();
  await expect(what.getByRole('textbox', { name: /^Name \d$/ })).toHaveCount(2);

  // More options: the signing secret and a header that carries a credential take references only.
  await sidebar.getByRole('button', { name: /More options/ }).click();
  const secret = sidebar.getByRole('textbox', { name: 'Signing secret' });
  await secret.fill('plain-signing-secret');
  await secret.blur();
  await expect(sidebar.locator('#setting-secret-why')).toHaveText(
    'Give the secret as a reference, such as $JironFormBuilder:Secrets:CrmSigning.',
  );
  await secret.fill('$JironFormBuilder:Secrets:E2EWebhook');
  await secret.blur();
  await expect(sidebar.locator('#setting-secret-why')).toHaveCount(0);
  await sidebar.getByRole('button', { name: 'Add header' }).click();
  await page.keyboard.type('Authorization');
  const credential = sidebar.getByRole('textbox', { name: 'Value of Authorization' });
  await credential.fill('Bearer abc');
  await credential.blur();
  await expect(sidebar.locator('#setting-headers-value-0-why')).toContainText('Authorization carries a credential');
  await credential.fill('$JironFormBuilder:Secrets:E2EWebhook');
  await credential.blur();
  await expect(sidebar.locator('#setting-headers-value-0-why')).toHaveCount(0);
  await axeInside(page, 'jfb-action-modal');

  // Test before Add: the stub gets the names and values, the header from the host's secret, signed.
  await testIt(page, sidebar, 'Webhook: Test sent. The server answered 200.');
  const call = await callTo(request, path, '"source":"website"');
  const body = JSON.parse(call.body) as { fields: Record<string, string>; form: { name: string } };
  expect(Object.keys(body.fields)).toEqual(['mail', 'source']);
  expect([body.fields['source'], body.form.name]).toEqual(['website', name]);
  expect([header(call, 'X-Jfb-Test'), header(call, 'Authorization')]).toEqual(['true', E2E_WEBHOOK_SECRET]);
  expect(header(call, 'X-Jfb-Signature')).toMatch(/^t=\d+, v1=[0-9a-f]{64}$/);

  // Added as the editors set it, every secret a reference.
  await add(page, view, sidebar, /^Webhook to 127\.0\.0\.1:5560$/);
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
  expect((await draftOf(api, key)).definition.actions?.[0]?.settings).toEqual({
    url: `${STUB_URL}${path}`,
    fields: { mail: '{{ field.email }}', source: 'website' },
    secret: '$JironFormBuilder:Secrets:E2EWebhook',
    headers: { Authorization: '$JironFormBuilder:Secrets:E2EWebhook' },
  });
});

test('a Slack message is configured with its address as a reference and its own words, and tested against the stub', async ({
  page,
  request,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const name = unique('Slack editors');
  const key = await formWithFields(api, name, [{ type: 'name', alias: 'name', label: { '': 'Your name' } }]);
  await loginAs('designer');
  const view = await openActions(page, key);
  const sidebar = await addAction(page, view, 'Slack');

  await expect(sidebar.getByRole('combobox', { name: 'Where it goes' })).toHaveValue('slack');
  // A Slack address holds its key: typed as itself, it is said under the box and not kept.
  const address = sidebar.getByRole('textbox', { name: 'Address', exact: true });
  await address.fill('https://hooks.slack.com/services/T0/B0/abc');
  await address.blur();
  await expect(sidebar.locator('#setting-url-why')).toContainText('A Slack address holds its key');
  await address.fill('$JironFormBuilder:Secrets:E2ESlack');
  await address.blur();
  await expect(sidebar.locator('#setting-url-why')).toHaveCount(0);

  // The message, in Slack's formatting, with merge tags.
  const what = sidebar.getByRole('group', { name: 'What it sends' });
  await expect(what.getByRole('radio', { name: /^A message/ })).toBeChecked();
  const message = what.getByRole('textbox', { name: 'A message' });
  await expect(message).toHaveValue('*New entry in {{ form.name }}*\n{{ entry.answers }}');
  await message.fill('*Lead in {{ form.name }}*\nFrom {{ field.name }}');
  await message.blur();
  await axeInside(page, 'jfb-action-modal');

  await testIt(page, sidebar, 'Slack: Test sent. The server answered 200.');
  const call = await callTo(request, '/webhooks/slack', name);
  const text = (JSON.parse(call.body) as { text: string }).text;
  expect(text.split('\n')).toEqual(['This is a test.', '', `*Lead in ${name}*`, expect.stringMatching(/^From \S+/)]);

  await add(page, view, sidebar, /^Slack message$/);
});

test('a Teams message is configured with its address as a reference, and tested against the stub as a card', async ({
  page,
  request,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const name = unique('Teams editors');
  const key = await formWithFields(api, name, [{ type: 'name', alias: 'name', label: { '': 'Your name' } }]);
  await loginAs('designer');
  const view = await openActions(page, key);
  const sidebar = await addAction(page, view, 'Microsoft Teams');

  await expect(sidebar.getByRole('combobox', { name: 'Where it goes' })).toHaveValue('teams');
  await sidebar.getByRole('textbox', { name: 'Address', exact: true }).fill('$JironFormBuilder:Secrets:E2ETeams');
  const what = sidebar.getByRole('group', { name: 'What it sends' });
  await expect(what.getByRole('textbox', { name: 'A message' })).toHaveValue(
    '**New entry in {{ form.name }}**\n{{ entry.answers }}',
  );

  await testIt(page, sidebar, 'Microsoft Teams: Test sent. The server answered 200.');
  const call = await callTo(request, '/webhooks/teams', name);
  const card = JSON.parse(call.body) as {
    attachments: { content: { type: string; body: { text: string; spacing: string }[] } }[];
  };
  const content = card.attachments[0]!.content;
  expect(content.type).toBe('AdaptiveCard');
  // A text block a line, the blank line after the test's the space before the message.
  expect(content.body.slice(0, 2).map((block) => [block.text, block.spacing])).toEqual([
    ['This is a test.', 'None'],
    [`**New entry in ${name}**`, 'Medium'],
  ]);

  await add(page, view, sidebar, /^Microsoft Teams message$/);
});

test("the form check's Fix it of a webhook's names and values lands on its table, its problem under it", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Webhook mapping check'), [
    { type: 'name', alias: 'name', label: { '': 'Your name' } },
  ]);
  const draft = await draftOf(api, key);
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: {
        ...draft.definition,
        actions: [
          {
            key: crypto.randomUUID(),
            type: 'webhook',
            name: 'Send to the CRM',
            enabled: true,
            trigger: 'submitted',
            stopOnFailure: false,
            settings: { url: `${STUB_URL}/webhooks/crm`, fields: { who: '{{ field.nobody }}' } },
          },
        ],
      },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await loginAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-form-canvas .bo-check')).toHaveText('1 problem');
  await design.locator('jfb-form-canvas .bo-check').click();
  const check = page.locator('jfb-form-check-modal');
  await check.getByRole('button', { name: /^Fix it: .*Send to the CRM/ }).click();
  const sidebar = page.locator('jfb-action-modal');
  await expect(sidebar.getByRole('textbox', { name: 'Name 1' })).toBeFocused();
  await expect(sidebar.locator('#setting-fields-found-0')).toContainText('cannot be used here');
});
