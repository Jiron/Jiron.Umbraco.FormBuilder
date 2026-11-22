import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { openSections } from '../../support/form-views.ts';
import { addFieldOf, openPicker, pick, pickerClosed, pickerItem } from '../../support/picker.ts';

// Every built-in type and changing a field's type (ROADMAP 9.4d, ADR-0174): a field of every built-in type is added
// through the field picker and configured in its inspector, axe clean with every section open, and saved; the captcha
// placement, which the picker offers only once a provider has its keys, comes from the API and is looked at. A text
// field with a placeholder and a condition using "contains" becomes a dropdown through the inspector's Change type...,
// after a preview
// that names the placeholder, the condition and the options it needs. A lock of the new type needs a host of its own
// (designer-locks.spec.ts).

const SECTION = '/umbraco/section/jfb-forms';

interface DraftField {
  readonly key: string;
  readonly type: string;
  readonly alias?: string;
  readonly settings?: Record<string, unknown>;
  readonly rules?: readonly { readonly conditions: readonly Record<string, unknown>[] }[];
  readonly [other: string]: unknown;
}

/** The fields of a form's saved draft, in their order. */
async function fieldsOf(api: APIRequestContext, key: string): Promise<DraftField[]> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: DraftField[] }[] }[] };
  };
  return draft.definition.pages.flatMap((page) => page.groups.flatMap((group) => group.fields));
}

/** Types into a box and leaves it, as a person does. */
async function typeIn(box: Locator, value: string) {
  await box.fill(value);
  await box.press('Tab');
}

/** Writes a rich text's words in place of what it holds, and leaves it. */
async function write(page: Page, box: Locator, words: string) {
  await box.click();
  await page.keyboard.press('Control+a');
  await page.keyboard.type(words);
  await page.keyboard.press('Tab');
}

async function saveDraft(page: Page) {
  const saved = page.waitForResponse(
    (response) => response.url().includes('/draft') && response.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok()).toBe(true);
}

interface TypeCase {
  /** The type's alias. */
  readonly type: string;
  /** Its name in the picker. */
  readonly name: string;
  /** What the journey sets in its inspector, besides its help text where it has one. */
  readonly configure: (inspector: Locator, page: Page) => Promise<void>;
  /** What the saved field then holds. */
  readonly holds: (field: DraftField) => void;
}

const settingsOf = (field: DraftField) => field.settings ?? {};

/** Every built-in type the picker offers here, in its order, each with one of its own settings set. */
const TYPES: readonly TypeCase[] = [
  {
    type: 'text',
    name: 'Short text',
    configure: (inspector) => typeIn(inspector.getByLabel('Maximum length'), '80'),
    holds: (field) => expect(settingsOf(field)['maxLength']).toBe(80),
  },
  {
    type: 'textarea',
    name: 'Long text',
    configure: (inspector) => typeIn(inspector.getByLabel('Lines shown'), '6'),
    holds: (field) => expect(settingsOf(field)['rows']).toBe(6),
  },
  {
    type: 'email',
    name: 'Email',
    configure: (inspector) => inspector.getByRole('switch', { name: 'Ask for the address twice' }).check(),
    holds: (field) => expect(settingsOf(field)['confirm']).toBe(true),
  },
  {
    type: 'phone',
    name: 'Phone',
    configure: (inspector) => typeIn(inspector.getByLabel('Default country'), 'DE'),
    holds: (field) => expect(settingsOf(field)['defaultCountry']).toBe('DE'),
  },
  {
    type: 'url',
    name: 'Web address',
    configure: (inspector) =>
      inspector
        .getByRole('group', { name: 'Allowed protocols', exact: true })
        .getByRole('checkbox', { name: 'http://' })
        .check(),
    holds: (field) => expect(settingsOf(field)['schemes']).toEqual(['https', 'http']),
  },
  {
    type: 'number',
    name: 'Number',
    configure: (inspector) => typeIn(inspector.getByLabel('Minimum', { exact: true }), '1'),
    holds: (field) => expect(settingsOf(field)['min']).toBe('1'),
  },
  {
    type: 'date',
    name: 'Date',
    configure: (inspector) => inspector.getByRole('switch', { name: 'Start with today' }).check(),
    holds: (field) => expect(settingsOf(field)['defaultToday']).toBe(true),
  },
  {
    type: 'time',
    name: 'Time',
    configure: (inspector) => typeIn(inspector.getByLabel('Step in minutes'), '15'),
    holds: (field) => expect(settingsOf(field)['stepMinutes']).toBe(15),
  },
  {
    type: 'datetime',
    name: 'Date and time',
    configure: (inspector) => inspector.getByRole('switch', { name: 'Start with today' }).check(),
    holds: (field) => expect(settingsOf(field)['defaultToday']).toBe(true),
  },
  {
    type: 'rating',
    name: 'Rating',
    configure: (inspector) => typeIn(inspector.getByLabel('Number of symbols'), '7'),
    holds: (field) => expect(settingsOf(field)['count']).toBe(7),
  },
  {
    type: 'scale',
    name: 'Scale',
    configure: (inspector) => typeIn(inspector.getByLabel('Starts at'), '1'),
    holds: (field) => expect(settingsOf(field)['from']).toBe(1),
  },
  {
    type: 'slider',
    name: 'Slider',
    configure: (inspector) => typeIn(inspector.getByLabel('Minimum', { exact: true }), '5'),
    holds: (field) => expect(settingsOf(field)['min']).toBe('5'),
  },
  {
    type: 'calculation',
    name: 'Calculation',
    configure: (inspector) => typeIn(inspector.getByLabel('Formula', { exact: true }), '2 * 3'),
    holds: (field) => expect(settingsOf(field)['formula']).toBe('2 * 3'),
  },
  {
    type: 'dropdown',
    name: 'Dropdown',
    configure: (inspector) => inspector.getByRole('switch', { name: 'Allow several options' }).check(),
    holds: (field) => expect(settingsOf(field)['multiple']).toBe(true),
  },
  {
    type: 'radio',
    name: 'Radio buttons',
    configure: (inspector) => inspector.getByRole('switch', { name: 'Offer "Other"' }).check(),
    holds: (field) => expect(settingsOf(field)['other']).toBe(true),
  },
  {
    type: 'checkboxes',
    name: 'Checkboxes',
    configure: (inspector) => inspector.getByRole('switch', { name: 'Offer "Other"' }).check(),
    holds: (field) => expect(settingsOf(field)['other']).toBe(true),
  },
  {
    type: 'grid',
    name: 'Grid',
    configure: (inspector) => inspector.getByRole('switch', { name: 'Several options per row' }).check(),
    holds: (field) => expect(settingsOf(field)['multiple']).toBe(true),
  },
  {
    type: 'ranking',
    name: 'Ranking',
    configure: (inspector) => typeIn(inspector.getByLabel('Options to rank'), '2'),
    holds: (field) => expect(settingsOf(field)['top']).toBe(2),
  },
  {
    type: 'checkbox',
    name: 'Checkbox',
    configure: (inspector, page) =>
      write(page, inspector.getByRole('textbox', { name: 'Text beside the box' }), 'Send me the newsletter'),
    holds: (field) => expect(settingsOf(field)['text']).toEqual({ '': 'Send me the newsletter' }),
  },
  {
    type: 'yesno',
    name: 'Yes or no',
    configure: (inspector) => typeIn(inspector.getByLabel('Label of yes'), 'Sure'),
    holds: (field) => expect(settingsOf(field)['yesLabel']).toEqual({ '': 'Sure' }),
  },
  {
    type: 'consent',
    name: 'Consent',
    configure: (inspector, page) =>
      write(page, inspector.getByRole('textbox', { name: 'Consent text' }), 'I agree to the terms'),
    holds: (field) => expect(settingsOf(field)['text']).toEqual({ '': 'I agree to the terms' }),
  },
  {
    type: 'country',
    name: 'Country',
    configure: (inspector) => typeIn(inspector.getByLabel('Preselected country'), 'DE'),
    holds: (field) => expect(settingsOf(field)['default']).toBe('DE'),
  },
  {
    type: 'name',
    name: 'Name',
    configure: (inspector) =>
      inspector
        .getByRole('group', { name: 'Parts', exact: true })
        .getByRole('checkbox', { name: 'Middle name' })
        .check(),
    holds: (field) => expect(settingsOf(field)['parts']).toEqual(['first', 'middle', 'last']),
  },
  {
    type: 'address',
    name: 'Address',
    configure: (inspector) =>
      inspector
        .getByRole('group', { name: 'Extra parts', exact: true })
        .getByRole('checkbox', { name: 'Postcode' })
        .check(),
    holds: (field) => expect(settingsOf(field)['parts']).toEqual(['postcode']),
  },
  {
    type: 'file',
    name: 'File upload',
    // The box takes megabytes; the field stores bytes.
    configure: (inspector) => typeIn(inspector.getByLabel('Largest file'), '10'),
    holds: (field) => expect(settingsOf(field)['maxBytes']).toBe(10 * 1024 * 1024),
  },
  {
    type: 'signature',
    name: 'Signature',
    configure: (inspector) => typeIn(inspector.getByLabel('Pad width'), '400'),
    holds: (field) => expect(settingsOf(field)['width']).toBe(400),
  },
  {
    type: 'repeater',
    name: 'Repeatable group',
    configure: (inspector) => typeIn(inspector.getByLabel('Minimum rows'), '1'),
    holds: (field) => expect(settingsOf(field)['minRows']).toBe(1),
  },
  {
    type: 'hidden',
    name: 'Hidden field',
    configure: (inspector) => typeIn(inspector.getByLabel('Default value', { exact: true }), 'web'),
    holds: (field) => expect(field['defaultValue']).toBe('web'),
  },
  {
    type: 'heading',
    name: 'Heading',
    configure: (inspector) => typeIn(inspector.getByRole('textbox', { name: 'Heading', exact: true }), 'About you'),
    holds: (field) => expect(settingsOf(field)['text']).toEqual({ '': 'About you' }),
  },
  {
    type: 'paragraph',
    name: 'Text block',
    configure: (inspector, page) =>
      write(page, inspector.getByRole('textbox', { name: 'Text', exact: true }), 'Tell us a little about you.'),
    holds: (field) => expect(settingsOf(field)['html']).toEqual({ '': '<p>Tell us a little about you.</p>' }),
  },
  {
    type: 'divider',
    name: 'Divider',
    // A divider has nothing of its own but its rules.
    configure: async (inspector) => {
      await expect(inspector.getByRole('button', { name: /^Rules/ })).toBeVisible();
    },
    holds: (field) => expect(field.settings).toBeUndefined(),
  },
  {
    type: 'image',
    name: 'Image',
    configure: (inspector) => typeIn(inspector.getByLabel('Alternative text'), 'A view of the lake'),
    holds: (field) => expect(settingsOf(field)['alt']).toEqual({ '': 'A view of the lake' }),
  },
];

test('a field of every built-in type is added through the picker and configured in its inspector, axe clean', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(600_000);
  const api = await apiAs('designer');
  // The picker offers the captcha placement only once a captcha provider has its keys, which this host's have not.
  const key = await formWithFields(api, unique('Every type'), [{ type: 'captcha' }]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  const inspector = design.locator('jfb-designer-inspector');

  await design.getByRole('button', { name: /, captcha placement field$/ }).click();
  await expect(inspector.getByRole('heading', { name: 'Captcha placement field' })).toBeVisible();
  await openPicker(page, addFieldOf(design));
  await expect(pickerItem(page, /^Captcha placement/)).toHaveAttribute('aria-disabled', 'true');
  await page.keyboard.press('Escape');
  await pickerClosed(page);
  await openSections(inspector);
  await axeInside(page, 'jfb-designer-inspector');

  for (const each of TYPES) {
    await test.step(each.name, async () => {
      // A pick from Add field goes to the page's end, and selects the new one.
      await pick(page, addFieldOf(design), each.name);
      await expect(inspector.getByRole('heading', { name: `${each.name} field` })).toBeVisible();
      await openSections(inspector);
      const help = inspector.getByLabel('Help text', { exact: true });
      if ((await help.count()) > 0) await typeIn(help, `Help for the ${each.name.toLowerCase()}`);
      await each.configure(inspector, page);
      await openSections(inspector);
      await axeInside(page, 'jfb-designer-inspector');
    });
  }

  await saveDraft(page);
  const fields = await fieldsOf(api, key);
  expect(fields.map((field) => field.type)).toEqual(['captcha', ...TYPES.map((each) => each.type)]);
  for (const each of TYPES) {
    const field = fields.find((one) => one.type === each.type);
    if (!field) throw new Error(`No ${each.type} field was saved.`);
    each.holds(field);
    if (!['hidden', 'heading', 'paragraph', 'divider', 'image'].includes(each.type))
      expect(field['help'], each.type).toEqual({ '': `Help for the ${each.name.toLowerCase()}` });
  }
});

test('a text field with a placeholder and a condition using contains becomes a dropdown, after a preview of what goes', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const nameKey = crypto.randomUUID();
  const key = await formWithFields(api, unique('Change type'), [
    {
      key: nameKey,
      type: 'text',
      alias: 'name',
      label: { '': 'Your name' },
      placeholder: { '': 'Your full name' },
      settings: { maxLength: 50 },
    },
    {
      type: 'text',
      alias: 'nickname',
      label: { '': 'Nickname' },
      rules: [
        { key: crypto.randomUUID(), effect: 'show', conditions: [{ field: nameKey, op: 'contains', value: 'a' }] },
      ],
    },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  const inspector = design.locator('jfb-designer-inspector');
  const name = design.getByRole('button', { name: /^Your name, / });
  await name.click();

  // The inspector's Change type, and Cancel, which gives the focus back to the field.
  await inspector.getByRole('button', { name: 'Change type' }).click();
  const dialog = page.locator('jfb-change-type-modal');
  await expect(dialog.getByRole('heading', { name: 'Change the type of Your name' })).toBeVisible();
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(name).toBeFocused();

  // The element's "..." offers no Change type: it is the inspector's alone (ADR-0256).
  await design.locator('.bo-tools').getByRole('button', { name: 'More actions' }).click();
  // The menu is open with the focus on an item of it before it is read and closed: an Escape pressed before the menu
  // has the focus is the Design view's, which clears the selection, and the inspector then has no Change type (a busy
  // run pressed it that early, and a count of none passes on a menu not yet there).
  const menu = design.locator('.bo-tools .bo-menu').filter({ visible: true });
  await expect(menu).toHaveCount(1);
  await expect(menu.locator('button:focus')).toHaveCount(1);
  await expect(menu.locator('button').filter({ hasText: 'Change type' })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(menu).toHaveCount(0);
  await inspector.getByRole('button', { name: 'Change type' }).click();
  await expect(dialog.getByText('Now: Short text')).toBeVisible();
  const change = dialog.getByRole('button', { name: 'Change type', exact: true });
  await expect(change).toBeDisabled();
  await dialog.getByRole('combobox', { name: 'New type' }).selectOption('dropdown');

  // Before the change, the preview names the placeholder, the condition and the options it needs.
  const preview = dialog.locator('.preview');
  await expect(preview.getByRole('heading', { name: 'What goes' })).toBeVisible();
  await expect(preview.getByRole('listitem')).toHaveText([
    'Maximum length: 50',
    'Placeholder: Your full name',
    'At least one option',
    'A rule of Nickname uses "contains"',
  ]);
  await expect(preview.getByRole('heading', { name: 'Needed before it publishes' })).toBeVisible();
  await axeInside(page, 'jfb-change-type-modal');
  await change.click();

  await expect(dialog).toHaveCount(0);
  await expect(inspector.getByRole('heading', { name: 'Dropdown field' })).toBeVisible();
  await expect(design.getByRole('status').filter({ hasText: 'Changed Your name' })).toHaveText(
    'Changed Your name to Dropdown',
  );
  await expect(design.getByRole('button', { name: /^Your name, / })).toBeFocused();
  await saveDraft(page);
  const [dropdown, nickname] = await fieldsOf(api, key);
  expect(dropdown).toMatchObject({ key: nameKey, type: 'dropdown', alias: 'name', label: { '': 'Your name' } });
  expect(dropdown).not.toHaveProperty('placeholder');
  expect(dropdown).not.toHaveProperty('settings');
  // The condition stays, for the form check to name until it is changed.
  expect(nickname?.rules?.[0]?.conditions).toEqual([{ field: nameKey, op: 'contains', value: 'a' }]);
});
