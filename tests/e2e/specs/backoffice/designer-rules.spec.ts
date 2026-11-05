import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, blankForm, unique } from '../../support/forms-api.ts';

// The rules builder (ROADMAP 9.5a, ADR-0175): rules are built from the inspector's Rules section in the builder's
// sidebar. One rule nests condition groups three levels deep with a condition of every operator; a "required when" rule
// two fields and looks at a query value, Try it says what the field then does, and the rule is deleted with Undo. That
// Try it agrees with the shared vectors is held by the backoffice's Vitest run (rule-try.test.ts).

const SECTION = '/umbraco/section/jfb-forms';

interface DraftRule {
  readonly key: string;
  readonly effect: string;
  readonly enabled?: boolean;
  readonly match?: string;
  readonly conditions: readonly unknown[];
}

interface DraftField {
  readonly key: string;
  readonly type: string;
  readonly alias: string;
  readonly rules?: readonly DraftRule[];
  readonly [other: string]: unknown;
}

const KEYS = {
  country: crypto.randomUUID(),
  which: crypto.randomUUID(),
  title: crypto.randomUUID(),
  topics: crypto.randomUUID(),
  agree: crypto.randomUUID(),
  arrival: crypto.randomUUID(),
  departure: crypto.randomUUID(),
  guests: crypto.randomUUID(),
  uploads: crypto.randomUUID(),
};

const option = (value: string, label: string) => ({ value, label: { '': label } });

/** A form whose fields rules look at, which allows the query value "ref". */
async function ruledForm(api: APIRequestContext): Promise<string> {
  const key = await blankForm(api, unique('Rules'));
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: unknown[] }[] }[]; settings?: Record<string, unknown> };
  };
  const field = (name: keyof typeof KEYS, type: string, label: string, extra: Record<string, unknown> = {}) => ({
    key: KEYS[name],
    type,
    alias: name,
    label: { '': label },
    ...extra,
  });
  const group = draft.definition.pages[0]?.groups[0];
  expect(group, 'the blank form has a group').toBeTruthy();
  (group as { fields: unknown[] }).fields = [
    field('country', 'dropdown', 'Country', {
      options: { items: [option('de', 'Germany'), option('other', 'Other')] },
    }),
    field('which', 'text', 'Which country'),
    field('title', 'text', 'Title'),
    field('topics', 'checkboxes', 'Topics', {
      options: { items: [option('shipping', 'Shipping'), option('press', 'Press')] },
    }),
    field('agree', 'checkbox', 'Agree'),
    field('arrival', 'date', 'Arrival'),
    field('departure', 'date', 'Departure'),
    field('guests', 'number', 'Guests'),
    field('uploads', 'file', 'Uploads'),
  ];
  const settings = draft.definition.settings ?? {};
  draft.definition.settings = {
    ...settings,
    developer: { ...(settings['developer'] as object), queryValues: ['ref'] },
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  return key;
}

/** The fields of a form's saved draft, by alias. */
async function fieldsOf(api: APIRequestContext, key: string): Promise<Record<string, DraftField>> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: DraftField[] }[] }[] };
  };
  const fields = draft.definition.pages.flatMap((page) => page.groups.flatMap((group) => group.fields));
  return Object.fromEntries(fields.map((field) => [field.alias, field]));
}

async function saveDraft(page: Page) {
  const saved = page.waitForResponse(
    (response) => response.url().includes('/draft') && response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok()).toBe(true);
}

/** Condition n's question and operator, chosen by their words. */
async function condition(builder: Locator, n: number, subject: string, op: string) {
  await builder.getByRole('combobox', { name: `What condition ${n} looks at` }).selectOption({ label: subject });
  await builder.getByRole('combobox', { name: `How condition ${n} compares` }).selectOption({ label: op });
}

const value = (builder: Locator, n: number, role: 'combobox' | 'textbox' | 'spinbutton' = 'textbox') =>
  builder.getByRole(role, { name: `The value of condition ${n}`, exact: true });

/** The "Add condition" and "Add group" of a group: the rule's own list, or a group by its name. */
const adds = (builder: Locator, group?: string) =>
  (group ? builder.getByRole('group', { name: group, exact: true }) : builder.locator('[data-id="root"]')).locator(
    ':scope > .adds',
  );

async function openRules(page: Page, key: string, field: string) {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: new RegExp(`^${field}, `) }).click();
  const inspector = design.locator('jfb-designer-inspector');
  // A closed Rules section holds Add a rule beside its header.
  await expect(inspector.locator('[data-section="rules"] .section-head button')).toHaveText(/Rules\s+Always shown/);
  await inspector.getByRole('button', { name: 'Add a rule' }).click();
  const builder = page.locator('jfb-rule-builder-modal');
  await expect(builder.getByRole('heading', { name: 'New rule' })).toBeVisible();
  await expect(builder.getByRole('combobox', { name: 'What condition 1 looks at' })).toBeFocused();
  return { design, inspector, builder };
}

test('a rule of groups nested three deep is built with a condition of every operator', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await ruledForm(api);
  await loginAs('designer');
  const { inspector, builder } = await openRules(page, key, 'Which country');
  await expect(builder.getByRole('combobox', { name: 'What the rule does' })).toHaveValue('show');
  await expect(builder.getByRole('combobox', { name: 'What the rule shows, hides or requires' })).toHaveValue(
    KEYS.which,
  );

  // The rule's own list: Country is Other, and Country is not Germany.
  await condition(builder, 1, 'Country', 'is');
  await value(builder, 1, 'combobox').selectOption({ label: 'Other' });
  await adds(builder).getByRole('button', { name: 'Add condition', exact: true }).click();
  await expect(builder.getByRole('combobox', { name: 'What condition 2 looks at' })).toBeFocused();
  await condition(builder, 2, 'Country', 'is not');
  await value(builder, 2, 'combobox').selectOption({ label: 'Germany' });

  // Add group makes them the first group, and any of the groups then counts.
  await adds(builder).getByRole('button', { name: 'Add condition group' }).click();
  await expect(builder.getByRole('combobox', { name: 'How many of these must be true' }).first()).toHaveValue('any');
  const second = builder.getByRole('group', { name: 'Condition group 2', exact: true });
  await condition(builder, 3, 'Title', 'contains');
  await value(builder, 3).fill('Dr');
  const texts: [string, string][] = [
    ["doesn't contain", 'x'],
    ['starts with', 'A'],
    ['ends with', 'z'],
  ];
  for (const [index, [op, text]] of texts.entries()) {
    await adds(builder, 'Condition group 2').getByRole('button', { name: 'Add condition', exact: true }).click();
    await condition(builder, 4 + index, 'Title', op);
    await value(builder, 4 + index).fill(text);
  }
  await adds(builder, 'Condition group 2').getByRole('button', { name: 'Add condition', exact: true }).click();
  await condition(builder, 7, 'Country', 'is one of');
  const ticked = builder.getByRole('group', { name: 'The value of condition 7' });
  await ticked.getByLabel('Germany').check();
  await ticked.getByLabel('Other').check();
  await adds(builder, 'Condition group 2').getByRole('button', { name: 'Add condition', exact: true }).click();
  await condition(builder, 8, 'Title', 'is none of');
  await value(builder, 8).fill('a\nb');
  await adds(builder, 'Condition group 2').getByRole('button', { name: 'Add condition', exact: true }).click();
  await condition(builder, 9, 'Title', 'is empty');
  await second.getByRole('combobox', { name: 'How many of these must be true' }).first().selectOption('any');

  // A group in the second group: the third level.
  await adds(builder, 'Condition group 2').getByRole('button', { name: 'Add condition group' }).click();
  await condition(builder, 10, 'Title', 'is not empty');
  const more = async (n: number, subject: string, op: string) => {
    await adds(builder, 'Condition group 3').getByRole('button', { name: 'Add condition', exact: true }).click();
    await condition(builder, n, subject, op);
  };
  await more(11, 'Guests', 'is more than');
  await value(builder, 11).fill('2');
  await more(12, 'Guests', 'is less than');
  await value(builder, 12).fill('10');
  await more(13, 'Guests', 'is between');
  await value(builder, 13).fill('1');
  await builder.getByRole('textbox', { name: 'The second value of condition 13' }).fill('4');
  await more(14, 'Agree', 'is ticked');
  await more(15, 'Topics', "hasn't ticked");
  await value(builder, 15, 'combobox').selectOption({ label: 'Press' });
  await more(16, 'Uploads', 'has at least');
  await value(builder, 16, 'spinbutton').fill('1');
  await more(17, 'Topics', 'has at most');
  await value(builder, 17, 'spinbutton').fill('2');
  await more(18, 'Title', 'matches a pattern');
  await value(builder, 18).fill('[A-Z]+');
  await axeInside(page, 'jfb-rule-builder-modal');

  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(builder).toHaveCount(0);
  const line = inspector.locator('button.rule');
  await expect(line).toBeFocused();
  await expect(line).toHaveText(/Shown when \(Country is Other and Country is not Germany\) or \(Title contains "Dr"/);
  await saveDraft(page);

  const { which } = await fieldsOf(api, key);
  const rule = which?.rules?.[0];
  expect(rule).toMatchObject({ effect: 'show', match: 'any' });
  expect(rule?.conditions).toEqual([
    {
      match: 'all',
      conditions: [
        { field: KEYS.country, op: 'equals', value: 'other' },
        { field: KEYS.country, op: 'notEquals', value: 'de' },
      ],
    },
    {
      match: 'any',
      conditions: [
        { field: KEYS.title, op: 'contains', value: 'Dr' },
        { field: KEYS.title, op: 'notContains', value: 'x' },
        { field: KEYS.title, op: 'startsWith', value: 'A' },
        { field: KEYS.title, op: 'endsWith', value: 'z' },
        { field: KEYS.country, op: 'isOneOf', value: ['de', 'other'] },
        { field: KEYS.title, op: 'isNoneOf', value: ['a', 'b'] },
        { field: KEYS.title, op: 'isEmpty' },
        {
          match: 'all',
          conditions: [
            { field: KEYS.title, op: 'isNotEmpty' },
            { field: KEYS.guests, op: 'greaterThan', value: '2' },
            { field: KEYS.guests, op: 'lessThan', value: '10' },
            { field: KEYS.guests, op: 'between', value: ['1', '4'] },
            { field: KEYS.agree, op: 'isChecked' },
            { field: KEYS.topics, op: 'isNotChecked', value: 'press' },
            { field: KEYS.uploads, op: 'countAtLeast', value: '1' },
            { field: KEYS.topics, op: 'countAtMost', value: '2' },
            { field: KEYS.title, op: 'matches', value: '[A-Z]+' },
          ],
        },
      ],
    },
  ]);
});

test('a required-when rule compares two fields and a query value, is tried, and is deleted with Undo', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await ruledForm(api);
  await loginAs('designer');
  const { design, inspector, builder } = await openRules(page, key, 'Guests');
  await builder.getByRole('combobox', { name: 'What the rule does' }).selectOption({ label: 'Require' });

  // Add says at the row what it lacks, and the row takes the focus.
  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(builder.getByText('Choose what this condition looks at.')).toBeVisible();
  await expect(builder.getByRole('combobox', { name: 'What condition 1 looks at' })).toBeFocused();

  // Departure is after the answer to Arrival, and the query value ref is "web".
  await condition(builder, 1, 'Departure', 'is after');
  await builder
    .getByRole('combobox', { name: 'What condition 1 compares with' })
    .selectOption({ label: 'the answer to Arrival' });
  await adds(builder).getByRole('button', { name: 'Add condition', exact: true }).click();
  await condition(builder, 2, 'The query value ref', 'is');
  await value(builder, 2).fill('web');

  // Try it says what the field does with the answers given.
  await builder.getByRole('button', { name: 'Try it' }).click();
  const verdict = builder.getByRole('status');
  await builder.getByLabel('Arrival', { exact: true }).fill('2026-12-01');
  await builder.getByLabel('Departure', { exact: true }).fill('2026-12-05');
  await builder.getByRole('textbox', { name: 'The query value ref', exact: true }).fill('web');
  await expect(verdict).toHaveText('Guests is required.');
  await builder.getByLabel('Departure', { exact: true }).fill('2026-11-30');
  await expect(verdict).toHaveText('Guests is not required.');
  await builder.getByLabel('Departure', { exact: true }).fill('2026-12-05');
  await builder.locator('uui-toggle').click();
  // A rule that is off does nothing; the switch's help says why.
  await expect(verdict).toHaveText('Guests is not required.');
  await expect(builder.getByText('The field is then never required.')).toBeVisible();
  await builder.locator('uui-toggle').click();
  await axeInside(page, 'jfb-rule-builder-modal');

  await builder.getByRole('button', { name: 'Add', exact: true }).click();
  await expect(builder).toHaveCount(0);
  const line = inspector.getByRole('button', { name: /^Required when Departure is after the answer to Arrival/ });
  await expect(line).toHaveText(
    'Required when Departure is after the answer to Arrival and the query value ref is "web"',
  );
  await expect(line).toBeFocused();
  // Closed, the section says the rule whole, on as many lines as it takes, never cut before what it depends on.
  const rules = inspector.locator('[data-section="rules"] .section-head button');
  await rules.click();
  const summary = rules.locator('.summary');
  await expect(summary).toHaveText(
    'Required when Departure is after the answer to Arrival and the query value ref is "web"',
  );
  expect(await summary.evaluate((element) => element.scrollWidth <= element.clientWidth), 'nothing cut').toBe(true);
  await rules.click();
  await saveDraft(page);
  expect((await fieldsOf(api, key)).guests?.rules?.[0]).toMatchObject({
    effect: 'require',
    enabled: true,
    conditions: [
      { field: KEYS.departure, op: 'greaterThan', value: { field: KEYS.arrival } },
      { context: 'query.ref', op: 'equals', value: 'web' },
    ],
  });

  // The rule opens from its line; Delete rule takes it at once and offers Undo.
  await line.click();
  await expect(builder.getByRole('heading', { name: 'Rule', exact: true })).toBeVisible();
  await builder.getByRole('button', { name: 'Delete rule' }).click();
  await expect(builder).toHaveCount(0);
  await expect(inspector.getByRole('button', { name: 'Add a rule' })).toBeFocused();
  await page
    .locator('uui-toast-notification')
    .filter({ hasText: 'Rule deleted' })
    .getByRole('button', { name: 'Undo' })
    .click();
  await expect(inspector.locator('button.rule')).toHaveText(/Required when Departure/);
  await expect(design.getByRole('status').filter({ hasText: 'Undone' })).toHaveText('Undone: Deleted a rule of Guests');
});
