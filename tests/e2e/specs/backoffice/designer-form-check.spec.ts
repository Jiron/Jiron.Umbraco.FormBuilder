import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, blankForm, formOf, unique } from '../../support/forms-api.ts';
import { pickerClosed, pickerItem, pickerSearch } from '../../support/picker.ts';

// The form check in the designer (ROADMAP 9.5f, IA 5.1 and 5.7, ADR-0181). The toolbar's badge counts the errors and
// warnings the server's check finds in the draft, and follows each change once the changes pause; its sidebar lists
// errors first, which block publishing, then warnings, then suggestions, each said in words with Fix it, which selects
// the element and puts the focus on its setting, opens the rules builder on a rule, or opens the field picker for a
// form without fields. The publish dialog shows the result: an error blocks Publish with Fix it, a warning doesn't.

const SECTION = '/umbraco/section/jfb-forms';

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    pages: { key: string; groups: { key: string; fields: unknown[] }[] }[];
    settings?: Record<string, unknown>;
  };
}

/**
 * A form whose check finds one of each: a field without a label (an error), a card number asked for and a field a rule
 * hides while another requires it (warnings), and an email address without a consent field (a suggestion).
 */
async function checkedForm(api: APIRequestContext) {
  const keys = { topic: crypto.randomUUID(), untitled: crypto.randomUUID(), notes: crypto.randomUUID() };
  const key = await blankForm(api, unique('Checked'));
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;
  const topicIs = (value: string) => ({ field: keys.topic, op: 'equals', value });
  (draft.definition.pages[0]?.groups[0] as { fields: unknown[] }).fields = [
    {
      key: keys.topic,
      type: 'dropdown',
      alias: 'topic',
      label: { '': 'Topic' },
      options: { items: ['press', 'shop'].map((value) => ({ value, label: { '': value } })) },
    },
    { key: keys.untitled, type: 'text', alias: 'untitled' },
    { key: crypto.randomUUID(), type: 'text', alias: 'card', label: { '': 'Card number' } },
    {
      key: keys.notes,
      type: 'textarea',
      alias: 'notes',
      label: { '': 'Notes' },
      rules: [
        { key: crypto.randomUUID(), effect: 'hide', conditions: [topicIs('press')] },
        { key: crypto.randomUUID(), effect: 'require', conditions: [topicIs('press')] },
      ],
    },
    { key: crypto.randomUUID(), type: 'email', alias: 'email', label: { '': 'Email address' } },
  ];
  // A theme the site lacks: a warning of the form's settings, whose Fix it leads to Settings.
  const settings = draft.definition.settings ?? {};
  draft.definition.settings = {
    ...settings,
    appearance: { ...(settings['appearance'] as object | undefined), theme: 'retired' },
  };
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
  await expect(design.locator('jfb-form-canvas')).toBeVisible();
  return design;
}

const badgeOf = (design: Locator) => design.locator('jfb-form-canvas .bo-check');

async function openCheck(page: Page, design: Locator) {
  await badgeOf(design).click();
  const sidebar = page.locator('jfb-form-check-modal');
  await expect(sidebar.getByRole('heading', { name: 'Form check', exact: true })).toBeVisible();
  return sidebar;
}

/** A row of the sidebar by the code of its problem. */
const rowOf = (sidebar: Locator, code: string) => sidebar.locator(`li[data-code="${code}"]`);

test('the badge counts the problems, the sidebar lists them errors first, and Fix it leads to the setting and the rule', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const { key, keys } = await checkedForm(api);
  await loginAs('designer');
  const design = await openForm(page, key);

  // The badge counts the error and the two warnings; the suggestion is listed, not counted.
  await expect(badgeOf(design)).toHaveAccessibleName('Form check: 4 problems');
  await expect(badgeOf(design)).toHaveAttribute('data-state', 'error');
  let sidebar = await openCheck(page, design);
  await expect(sidebar.locator('uui-box')).toHaveCount(3);
  await expect(sidebar.locator('uui-box').nth(0)).toHaveAttribute('headline', '1 error');
  await expect(sidebar.locator('uui-box').nth(0).locator('[slot="header-actions"]')).toHaveText('Blocks publishing');
  await expect(sidebar.locator('uui-box').nth(1)).toHaveAttribute('headline', '3 warnings');
  await expect(sidebar.locator('uui-box').nth(2)).toHaveAttribute('headline', '1 suggestion');
  await expect(rowOf(sidebar, 'label_missing')).toContainText(
    'This field has no label, which screen readers and the entries name it by.',
  );
  await expect(rowOf(sidebar, 'card_number_asked').locator('.name')).toHaveText('Card number');
  await expect(rowOf(sidebar, 'required_can_hide').locator('.name')).toHaveText('Notes');
  await expect(rowOf(sidebar, 'consent_missing').locator('.name')).toHaveText('Email address');
  await axeInside(page, 'jfb-form-check-modal');

  // Fix it on the error: the field selected, the focus in its label's box, which a label then clears.
  await rowOf(sidebar, 'label_missing').getByRole('button', { name: 'Fix it: untitled' }).click();
  await expect(sidebar).toHaveCount(0);
  const label = design.locator('jfb-designer-inspector #prop-label');
  await expect(label).toBeFocused();
  await expect(design.locator('jfb-designer-inspector h2')).toHaveText('Short text field');
  await expect(design.locator(`jfb-canvas-field[key="${keys.untitled}"] [data-control="select"]`)).toHaveAttribute(
    'aria-current',
    'true',
  );
  await label.fill('Order number');
  await expect(badgeOf(design)).toHaveAccessibleName('Form check: 3 problems');
  await expect(badgeOf(design)).toHaveAttribute('data-state', 'warning');

  // Fix it on a rule's warning opens the rules builder on that rule, its field selected.
  sidebar = await openCheck(page, design);
  await expect(sidebar.locator('uui-box').first()).toHaveAttribute('headline', '3 warnings');
  await rowOf(sidebar, 'required_can_hide').getByRole('button', { name: 'Fix it: Notes' }).click();
  const builder = page.locator('jfb-rule-builder-modal');
  await expect(builder.getByRole('heading', { name: 'Rule', exact: true })).toBeVisible();
  await expect(builder.getByRole('combobox', { name: 'What the rule shows, hides or requires' })).toHaveValue(
    keys.notes,
  );
  await builder.getByRole('button', { name: 'Cancel' }).click();
  await expect(builder).toHaveCount(0);
  await expect(design.locator('jfb-designer-inspector h2')).toHaveText('Long text field');

  // From the publish dialog, the theme's Fix it opens Settings on the theme, with the warning under it (ADR-0184).
  await page.getByRole('button', { name: 'Publish' + String.fromCharCode(0x2026) }).click();
  const dialog = page.locator('jfb-publish-modal');
  await expect(dialog.locator('.note.warning .lead')).toHaveText(
    "The form check found 3 warnings. They don't block publishing.",
  );
  await dialog.getByRole('button', { name: 'Fix it: Form settings' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page).toHaveURL(/\/view\/settings$/);
  const settings = page.locator('jfb-form-settings-view');
  await expect(settings.locator('#theme')).toBeFocused();
  await expect(settings.locator('#theme-why-0')).toHaveText(
    'Warning: The site has no theme "retired", so the form shows with the default theme.',
  );
});

test("a blank form's one error opens the field picker, blocks Publish with Fix it, and a warning alone publishes", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await blankForm(api, unique('Empty'));
  await loginAs('designer');
  const design = await openForm(page, key);
  const search = pickerSearch(page);

  await expect(badgeOf(design)).toHaveAccessibleName('Form check: 1 problem');
  const sidebar = await openCheck(page, design);
  await expect(sidebar.locator('uui-box')).toHaveAttribute('headline', '1 error');
  await expect(rowOf(sidebar, 'fields_missing').locator('.name')).toHaveText('Add at least one field');
  await expect(rowOf(sidebar, 'fields_missing').locator('.said')).toHaveText('Visitors would have nothing to send.');
  await axeInside(page, 'jfb-form-check-modal');
  await rowOf(sidebar, 'fields_missing').getByRole('button', { name: 'Fix it: Add at least one field' }).click();
  await expect(sidebar).toHaveCount(0);
  await expect(search).toBeFocused();
  await page.keyboard.press('Escape');
  await pickerClosed(page);

  // Publish... shows the error, which blocks Publish, with Fix it, which leads to the field picker again.
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  let dialog = page.locator('jfb-publish-modal');
  await expect(dialog.locator('[role="alert"] .lead')).toHaveText('1 error blocks publishing.');
  await expect(dialog.getByRole('button', { name: 'Publish', exact: true })).toBeDisabled();
  // While an error blocks it, the dialog says what to fix, and nothing of what changed (ROADMAP 10.3d).
  await expect(dialog.locator('.changes')).toHaveCount(0);
  await axeInside(page, 'jfb-publish-modal');
  await dialog.getByRole('button', { name: 'Fix it: Add at least one field' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(search).toBeFocused();

  // A field asking for a card number: a warning, which doesn't block.
  await pickerItem(page, 'Short text').click();
  await pickerClosed(page);
  const label = design.locator('jfb-designer-inspector #prop-label');
  await label.fill('Card number');
  await expect(badgeOf(design)).toHaveAccessibleName('Form check: 1 problem');
  await expect(badgeOf(design)).toHaveAttribute('data-state', 'warning');
  await page.getByRole('button', { name: 'Publish\u2026' }).click();
  dialog = page.locator('jfb-publish-modal');
  await expect(dialog.locator('.note.warning .lead')).toHaveText(
    "The form check found 1 warning. It doesn't block publishing.",
  );
  await expect(dialog.locator('.note.warning li')).toContainText('Card number: This field asks for a card number.');
  await axeInside(page, 'jfb-publish-modal');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect(page.locator('jfb-form-workspace-editor uui-tag')).toHaveText('Published');
  await expect.poll(async () => (await formOf(api, key)).isPublished).toBe(true);
});
