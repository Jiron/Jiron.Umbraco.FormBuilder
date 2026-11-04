import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { fillForm, submitForm } from '../../support/form.ts';
import { API, blankForm, unique } from '../../support/forms-api.ts';
import { E2E_BASE_URL } from '../../support/paths.ts';
import { headlessPage } from '../../support/static-site.ts';

// Seeing rules, and form-level validation rules (ROADMAP 9.5c, ADR-0178). A rule's chip shows on the element it affects
// and not on the selected one; the rules overview, from the canvas's "...", lists every rule by page, leads from a row
// to its element, warns on its row of a field that one rule hides and another requires for the same answer, adds a
// rule after asking what it affects, and says in one sentence what most rules do where a form has none. A validation
// rule built in the same builder, with its field and its message, shows that message on the published form, at its
// field, when the rule matches.

const SECTION = '/umbraco/section/jfb-forms';

const option = (value: string, label: string) => ({ value, label: { '': label } });

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    pages: { key: string; title?: Record<string, string>; groups: { key: string; fields: unknown[] }[] }[];
    settings?: Record<string, unknown>;
    validationRules?: unknown[];
  };
}

const draftOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;

/** A form of two pages whose fields have rules: one shown, one hidden and required for the same answer. */
async function ruledForm(api: APIRequestContext) {
  const keys = {
    topic: crypto.randomUUID(),
    which: crypto.randomUUID(),
    attachment: crypto.randomUUID(),
    email: crypto.randomUUID(),
    notes: crypto.randomUUID(),
  };
  const key = await blankForm(api, unique('Rules seen'));
  const draft = await draftOf(api, key);
  const first = draft.definition.pages[0];
  expect(first, 'the blank form has a page').toBeTruthy();
  const page = first as Draft['definition']['pages'][number];
  page.title = { '': 'Your details' };
  const topicIs = (value: string) => ({ field: keys.topic, op: 'equals', value });
  (page.groups[0] as { fields: unknown[] }).fields = [
    {
      key: keys.topic,
      type: 'dropdown',
      alias: 'topic',
      label: { '': 'Topic' },
      options: { items: [option('shipping', 'Shipping'), option('press', 'Press'), option('delivery', 'Delivery')] },
    },
    {
      key: keys.which,
      type: 'text',
      alias: 'which',
      label: { '': 'Which country' },
      rules: [{ key: crypto.randomUUID(), effect: 'show', conditions: [topicIs('press')] }],
    },
    {
      key: keys.attachment,
      type: 'text',
      alias: 'attachment',
      label: { '': 'Attachment' },
      rules: [
        { key: crypto.randomUUID(), effect: 'hide', conditions: [topicIs('press')] },
        {
          key: crypto.randomUUID(),
          effect: 'require',
          conditions: [{ field: keys.topic, op: 'isOneOf', value: ['shipping', 'press'] }],
        },
      ],
    },
    { key: keys.email, type: 'email', alias: 'email', label: { '': 'Email' } },
  ];
  draft.definition.pages.push({
    key: crypto.randomUUID(),
    groups: [
      {
        key: crypto.randomUUID(),
        fields: [{ key: keys.notes, type: 'textarea', alias: 'notes', label: { '': 'Notes' } }],
      },
    ],
  });
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  return { key, keys };
}

async function openForm(page: Page, key: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-canvas-field').first()).toBeVisible();
  return design;
}

async function openOverview(page: Page, design: ReturnType<Page['locator']>) {
  await design.getByRole('button', { name: 'More actions for the canvas' }).click();
  await design.getByRole('button', { name: 'Rules overview' }).click();
  const overview = page.locator('jfb-rules-overview-modal');
  await expect(overview.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
  return overview;
}

test('rules show as chips, and the overview lists them by page, warns of a conflict, leads to an element and adds a rule', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key, keys } = await ruledForm(api);
  await loginAs('designer');
  const design = await openForm(page, key);

  // A rule's chip on the element it affects, which describes the button that selects it.
  const chipsOf = (field: string) => design.locator(`jfb-canvas-field[key="${field}"] .bo-rule-chip`);
  await expect(chipsOf(keys.which)).toHaveText(['Shown when Topic is Press']);
  await expect(chipsOf(keys.attachment)).toHaveText([
    'Hidden when Topic is Press',
    'Required when Topic is one of Shipping, Press',
  ]);
  const which = design.getByRole('button', { name: /^Which country, / });
  await expect(which).toHaveAccessibleDescription('Shown when Topic is Press');
  // None on the selected element, whose rules the inspector lists.
  await design.getByRole('button', { name: /^Attachment, / }).click();
  await expect(chipsOf(keys.attachment)).toHaveCount(0);
  await expect(chipsOf(keys.which)).toHaveCount(1);
  await expect(design.locator('jfb-designer-inspector [data-section="rules"] .section-head button')).toHaveText(
    /Hidden when Topic is Press\s+\u00b7\s+Required when Topic is one of Shipping, Press/,
  );

  // The overview, by page: the conflict on the row it concerns.
  let overview = await openOverview(page, design);
  await expect(overview.locator('uui-box')).toHaveCount(1);
  await expect(overview.locator('uui-box')).toHaveAttribute('headline', 'Page 1: Your details');
  await expect(overview.locator('button.row')).toHaveText([
    // A row reads with the whitespace of its markup around its lines.
    /^\s*Which country\s+Shown when Topic is Press\s*$/,
    /^\s*Attachment\s/,
  ]);
  await expect(overview.locator(`button.row[data-key="${keys.attachment}"] .conflict`)).toHaveText(
    'Conflict: when Topic is Press, one rule hides it and the other requires it. A hidden field is never required, so visitors can send without it.',
  );
  await axeInside(page, 'jfb-rules-overview-modal');

  // A row leads to its element: selected, with the focus on it.
  await overview.getByRole('button', { name: /^Which country/ }).click();
  await expect(overview).toHaveCount(0);
  await expect(design.locator('jfb-designer-inspector h2')).toHaveText('Short text field');
  await expect(which).toBeFocused();
  await expect(chipsOf(keys.which)).toHaveCount(0);

  // Add rule asks first what the rule affects, then the builder opens on it.
  overview = await openOverview(page, design);
  await overview.getByRole('button', { name: 'Add rule' }).click();
  const affects = overview.getByRole('combobox', { name: 'What should the rule affect?' });
  await expect(affects).toBeFocused();
  await overview.getByRole('button', { name: 'Continue' }).click();
  await expect(overview.getByText('Choose what the rule affects.')).toBeVisible();
  await affects.selectOption({ label: 'Notes' });
  await overview.getByRole('button', { name: 'Continue' }).click();
  await expect(overview).toHaveCount(0);
  const builder = page.locator('jfb-rule-builder-modal');
  await expect(builder.getByRole('heading', { name: 'New rule' })).toBeVisible();
  await expect(builder.getByRole('combobox', { name: 'What the rule shows, hides or requires' })).toHaveValue(
    keys.notes,
  );
  await builder.getByRole('combobox', { name: 'What condition 1 looks at' }).selectOption({ label: 'Topic' });
  await builder
    .getByRole('combobox', { name: 'The value of condition 1', exact: true })
    .selectOption({ label: 'Delivery' });
  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(builder).toHaveCount(0);
  // The rule's element is selected on its page, with the focus on the rule's line.
  await expect(design.locator('jfb-designer-inspector button.rule')).toHaveText('Shown when Topic is Delivery');
  await expect(design.locator('jfb-designer-inspector button.rule')).toBeFocused();
  await expect(design.getByRole('button', { name: /^Notes, / })).toHaveAttribute('aria-current', 'true');

  // The new rule is in the overview, on its page.
  overview = await openOverview(page, design);
  await expect(overview.locator('uui-box')).toHaveCount(2);
  await expect(overview.locator(`button.row[data-key="${keys.notes}"]`)).toHaveText(
    /Notes\s+Shown when Topic is Delivery/,
  );
  await overview.getByRole('button', { name: 'Close' }).click();
  await expect(overview).toHaveCount(0);
});

test('a form without rules says in one sentence what most rules do', async ({ page, loginAs, apiAs }) => {
  const api = await apiAs('designer');
  const key = await blankForm(api, unique('No rules'));
  await loginAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  const overview = await openOverview(page, design);
  await expect(overview.locator('.empty')).toHaveText(
    'No rules yet. Most rules show a question only when another answer calls for it, and Add rule starts by asking which question.',
  );
  await expect(overview.locator('uui-box')).toHaveCount(0);
  await axeInside(page, 'jfb-rules-overview-modal');
});

test('a validation rule built in the builder, with its field and message, shows the message on the published form', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const email = crypto.randomUUID();
  const key = await blankForm(api, unique('Work address'));
  const draft = await draftOf(api, key);
  (draft.definition.pages[0]?.groups[0] as { fields: unknown[] }).fields = [
    { key: email, type: 'email', alias: 'email', label: { '': 'Email' } },
  ];
  // Shown through the public API, so a page shows the published form through the element.
  draft.definition.settings = {
    ...draft.definition.settings,
    developer: { ...(draft.definition.settings?.['developer'] as object), headless: true },
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);

  await loginAs('designer');
  const design = await openForm(page, key);
  await design.getByRole('button', { name: /^Email, / }).click();
  const inspector = design.locator('jfb-designer-inspector');
  await inspector.getByRole('button', { name: 'Add a rule' }).click();
  const builder = page.locator('jfb-rule-builder-modal');
  await expect(builder.getByRole('heading', { name: 'New rule' })).toBeVisible();
  await builder.getByRole('combobox', { name: 'What the rule does' }).selectOption({ label: 'Show a message on' });
  await expect(builder.getByRole('combobox', { name: 'The field that shows the message' })).toHaveValue(email);
  // A validation rule may look at its own field.
  await builder.getByRole('combobox', { name: 'What condition 1 looks at' }).selectOption({ label: 'Email' });
  await builder.getByRole('combobox', { name: 'How condition 1 compares' }).selectOption({ label: 'ends with' });
  await builder.getByRole('textbox', { name: 'The value of condition 1', exact: true }).fill('gmail.com');
  // Add says what the message lacks, at its box.
  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  const message = builder.getByRole('textbox', { name: 'Message', exact: true });
  await expect(message).toBeFocused();
  await expect(builder.getByText('Write the message visitors read.')).toBeVisible();
  await message.fill('Use your work address');
  await builder.getByRole('button', { name: 'Try it' }).click();
  await builder.getByLabel('Email', { exact: true }).fill('ann@gmail.com');
  await expect(builder.getByRole('status')).toHaveText('The message shows on Email.');
  await builder.getByLabel('Email', { exact: true }).fill('ann@example.com');
  await expect(builder.getByRole('status')).toHaveText("The message doesn't show on Email.");
  await axeInside(page, 'jfb-rule-builder-modal');
  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(builder).toHaveCount(0);
  const line = inspector.locator('button.rule');
  await expect(line).toHaveText('Shows "Use your work address" when Email ends with "gmail.com"');
  await expect(line).toBeFocused();

  const saving = page.waitForResponse(
    (response) => response.url().includes('/draft') && response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saving).ok()).toBe(true);
  const kept = await draftOf(api, key);
  expect(kept.definition.validationRules).toEqual([
    {
      key: expect.any(String),
      enabled: true,
      field: email,
      message: { '': 'Use your work address' },
      match: 'all',
      conditions: [{ field: email, op: 'endsWith', value: 'gmail.com' }],
    },
  ]);
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: kept.form.concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);

  // The published form on a page of the site's own origin, shown through the element.
  const address = `${E2E_BASE_URL}/jfb-browser-tests/work-address/`;
  await page.route(address, (route) =>
    route.fulfill({
      contentType: 'text/html; charset=utf-8',
      body: headlessPage(E2E_BASE_URL, { form: key, culture: 'en-US' }),
    }),
  );
  await page.goto(address);
  const form = page.locator('jfb-form form.jfb-form');
  await expect(form).toBeVisible();
  await fillForm(form, { email: 'ann@gmail.com' });
  const refused = await submitForm(form);
  if (refused.state !== 'errors') throw new Error(`The form was sent: ${refused.outcome}`);
  expect(refused.errors).toHaveLength(1);
  expect(refused.errors[0]).toContain('Use your work address');
  const field = form.locator('[data-jfb-field][data-jfb-alias="email"]');
  await expect(field).toContainText('Use your work address');
  await expect(field.getByRole('textbox', { name: 'Email' })).toHaveAttribute('aria-invalid', 'true');
  await fillForm(form, { email: 'ann@example.com' });
  expect((await submitForm(form)).state).toBe('sent');
});
