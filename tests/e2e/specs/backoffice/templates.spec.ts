import type { APIRequestContext, Page, TestInfo } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, blankForm, contactForm, unique } from '../../support/forms-api.ts';
import { openRowActions, treeRow } from '../../support/tree.ts';

// Templates (ROADMAP 12.1, IA 4, 5 and 7, ADR-0217): a group saved from its "..." on one form's canvas, with the
// validation rule on its fields and without the rule and the validation rule that depended on a field outside it,
// inserted from another form's palette as a copy with keys of its own, a free alias, its rule on its own fields and its
// validation rule on the copy's; a form saved as a template from its "..." in the tree, then found, used, renamed and
// deleted in Library > Templates; and the gallery's screens, wide and narrow, for the review of 12.3.

const SECTION = '/umbraco/section/jfb-forms';
const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;

interface Rule {
  readonly key: string;
  readonly effect: string;
  readonly conditions: readonly { readonly field: string; readonly op: string; readonly value?: string }[];
}

interface Field {
  readonly key: string;
  readonly alias: string;
  readonly rules?: readonly Rule[];
}

interface Group {
  readonly key: string;
  readonly title?: Record<string, string>;
  readonly rules?: readonly Rule[];
  readonly fields: readonly Field[];
}

interface Check {
  readonly key: string;
  readonly field: string;
  readonly conditions: readonly { readonly field: string; readonly op: string }[];
}

const rule = (field: string): Rule & Record<string, unknown> => ({
  key: crypto.randomUUID(),
  effect: 'show',
  enabled: true,
  match: 'all',
  conditions: [{ field, op: 'equals', value: 'yes' }],
});

async function draftOf(api: APIRequestContext, key: string) {
  return (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { key: string; groups: Group[] }[]; validationRules?: Check[] };
  };
}

/** A blank form whose first page holds these groups, with these validation rules; its key. */
async function formWithGroups(
  api: APIRequestContext,
  name: string,
  groups: readonly object[],
  validationRules: readonly object[] = [],
): Promise<string> {
  const key = await blankForm(api, name);
  const draft = await draftOf(api, key);
  const page = draft.definition.pages[0] as { groups: object[] };
  page.groups = [...groups];
  (draft.definition as Record<string, unknown>)['validationRules'] = [...validationRules];
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  return key;
}

/** A screenshot kept with the run's results, for the review of 12.3. */
async function look(page: Page, info: TestInfo, what: string): Promise<void> {
  await page.mouse.move(0, 0);
  const path = info.outputPath(`${what.replaceAll(' ', '-')}.png`);
  await page.screenshot({ path });
  await info.attach(what, { path, contentType: 'image/png' });
}

const toast = (page: Page, said: string) => page.locator('uui-toast-notification').filter({ hasText: said });

test('a group saved on one form is inserted into another with keys of its own and its rules on its own fields', async ({
  page,
  loginAs,
  apiAs,
}, info) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const post = crypto.randomUUID();
  const gift = crypto.randomUUID();
  const note = crypto.randomUUID();
  const block = unique('Delivery block');
  const check = (key: string, when: string) => ({
    key,
    field: note,
    message: { '': 'Add a few words for the gift.' },
    conditions: [
      { field: when, op: 'equals', value: 'yes' },
      { field: note, op: 'isEmpty' },
    ],
  });
  const inside = crypto.randomUUID();
  const source = await formWithGroups(
    api,
    unique('Gift shop'),
    [
      {
        key: crypto.randomUUID(),
        title: { '': 'Contact' },
        fields: [{ key: post, type: 'yesno', alias: 'post', label: { '': 'Send it by post?' } }],
      },
      {
        key: crypto.randomUUID(),
        title: { '': 'Delivery' },
        rules: [rule(post)],
        fields: [
          { key: gift, type: 'yesno', alias: 'gift', label: { '': 'Is it a gift?' } },
          { key: note, type: 'textarea', alias: 'giftNote', label: { '': 'Gift note' }, rules: [rule(gift)] },
        ],
      },
    ],
    [check(inside, gift), check(crypto.randomUUID(), post)],
  );
  const target = await formWithGroups(api, unique('Gift voucher'), [
    {
      key: crypto.randomUUID(),
      title: { '': 'Voucher' },
      fields: [{ key: crypto.randomUUID(), type: 'yesno', alias: 'gift', label: { '': 'For someone else?' } }],
    },
  ]);

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(source));
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: 'Delivery, group' }).click();
  await design.locator('.bo-tools').getByRole('button', { name: 'More actions' }).click();
  await design.getByRole('button', { name: 'Save group as template\u2026' }).click();
  const dialog = page.locator('jfb-template-name-modal');
  await expect(dialog.getByRole('heading', { name: 'Save Delivery as a template' })).toBeVisible();
  await expect(
    dialog.getByText('Rules and validation rules that depend on fields outside the group are left out.'),
  ).toBeVisible();
  const width = (await dialog.locator('uui-dialog-layout').boundingBox())?.width ?? Infinity;
  expect(width, 'the note wraps in a dialog as wide as a sentence reads well').toBeLessThanOrEqual(520);
  await dialog.getByRole('textbox', { name: 'Name of the template' }).fill(block);
  await axeInside(page, 'jfb-template-name-modal');
  await look(page, info, 'save group as template');
  await dialog.getByRole('button', { name: 'Save template' }).click();
  await expect(
    toast(page, `${block} was saved without 1 rule and 1 validation rule that depend on fields outside the group.`),
  ).toBeVisible();

  await page.goto(workspaceOf(target));
  const palette = design.locator('jfb-designer-palette');
  await expect(palette.getByRole('heading', { name: 'Saved groups' })).toBeVisible();
  await palette.getByRole('button', { name: block }).scrollIntoViewIfNeeded();
  await look(page, info, 'the palette with a saved group');
  await palette.getByRole('button', { name: block }).click();
  await expect(design.getByRole('button', { name: 'Delivery, group' })).toBeVisible();
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect.poll(async () => (await draftOf(api, target)).definition.pages[0]?.groups.length).toBe(2);

  const copied = (await draftOf(api, target)).definition;
  const copy = copied.pages[0]?.groups[1] as Group;
  const [copiedGift, copiedNote] = copy.fields;
  expect(copy.title).toEqual({ '': 'Delivery' });
  expect(copy.rules ?? [], 'the group rule that named a field outside it stays out').toEqual([]);
  expect(
    copy.fields.map((field) => field.alias),
    'a free alias where the form has one',
  ).toEqual(['gift2', 'giftNote']);
  expect([copy.key, copiedGift?.key, copiedNote?.key]).not.toContain(gift);
  expect([copiedGift?.key, copiedNote?.key]).not.toContain(note);
  expect(copiedNote?.rules?.[0]?.conditions[0]?.field, "the note's rule names the copy's own field").toBe(
    copiedGift?.key,
  );
  const checks = copied.validationRules ?? [];
  expect(checks, 'the validation rule on the note came with it, on the copy').toHaveLength(1);
  expect(checks[0]?.key).not.toBe(inside);
  expect(checks[0]?.field).toBe(copiedNote?.key);
  expect(checks[0]?.conditions.map((condition) => condition.field)).toEqual([copiedGift?.key, copiedNote?.key]);
  const before = (await draftOf(api, source)).definition;
  expect(
    [(before.pages[0]?.groups[1] as Group).rules?.length, before.validationRules?.length],
    'the form it came from is as it was',
  ).toEqual([1, 2]);
});

test('a form saved as a template is found, used, renamed and deleted in Library > Templates', async ({
  page,
  loginAs,
  apiAs,
}, info) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const name = unique('Enquiry');
  await contactForm(api, name);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(SECTION);

  await openRowActions(page, name);
  await page
    .locator('umb-entity-action-list uui-menu-item[label="Save as template\u2026"]')
    .filter({ visible: true })
    .click();
  const dialog = page.locator('jfb-template-name-modal');
  await expect(dialog.getByRole('textbox', { name: 'Name of the template' })).toHaveValue(name);
  await dialog.getByRole('textbox', { name: 'Description' }).fill('For questions about an order');
  await dialog.getByRole('button', { name: 'Save template' }).click();
  await expect(toast(page, `${name} was saved as a template.`)).toBeVisible();

  await page.locator('umb-menu-item-layout[label="Templates"], uui-menu-item[label="Templates"]').first().click();
  const screen = page.locator('jfb-templates-screen');
  await expect(screen.getByRole('heading', { name: 'Built in' })).toBeVisible();
  await expect(screen.getByRole('radio')).toHaveCount(await screen.locator('.card').count());
  const card = screen.locator('.card').filter({ hasText: name });
  await card.click();
  await expect(screen.getByRole('heading', { name, level: 3 })).toBeVisible();
  await axeInside(page, 'jfb-templates-screen');
  await look(page, info, 'library templates');

  await screen.getByRole('button', { name: `Actions for ${name}` }).click();
  await screen.locator('uui-menu-item[label="Rename\u2026"]').click();
  const renamed = `${name} renamed`;
  await dialog.getByRole('textbox', { name: 'Name of the template' }).fill(renamed);
  await dialog.getByRole('button', { name: 'Rename', exact: true }).click();
  await expect(toast(page, `The template is now ${renamed}.`)).toBeVisible();
  await expect(screen.locator('.card').filter({ hasText: renamed })).toBeVisible();

  await screen.getByRole('button', { name: 'Use template' }).click();
  await expect(page).toHaveURL(/\/workspace\/jfb-form\/edit\/[0-9a-f-]{36}/);
  await expect(page.getByRole('textbox', { name: 'Form name' })).toHaveValue(renamed);
  await expect(treeRow(page, renamed), 'the tree lists the new form without a reload').toHaveCount(1);

  await page.locator('umb-menu-item-layout[label="Templates"], uui-menu-item[label="Templates"]').first().click();
  await screen.locator('.card').filter({ hasText: renamed }).click();
  await screen.getByRole('button', { name: `Actions for ${renamed}` }).click();
  await screen.locator('uui-menu-item[label="Delete\u2026"]').click();
  await page.locator('umb-confirm-modal').getByRole('button', { name: 'Delete' }).click();
  await expect(toast(page, `${renamed} was deleted.`)).toBeVisible();
  await expect(screen.locator('.card').filter({ hasText: renamed })).toHaveCount(0);

  // Umbraco's page is never narrower than 920 px (ADR-0182): the screen at its narrowest, the toasts gone.
  await expect(page.locator('uui-toast-notification')).toHaveCount(0, { timeout: 30_000 });
  await page.setViewportSize({ width: 920, height: 900 });
  await look(page, info, 'library templates narrow');
});

test("the gallery offers the nine built-in templates in the backoffice's words, each with its preview", async ({
  page,
  loginAs,
}, info) => {
  test.setTimeout(180_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(SECTION);
  await page.locator('umb-section-sidebar').getByRole('button', { name: 'Create' }).first().click();
  await page
    .locator('umb-entity-create-option-action-list-modal umb-ref-item[name="Form from a template\u2026"]')
    .click();
  const gallery = page.locator('jfb-template-gallery-modal');
  const names = [
    'Contact',
    'Callback',
    'Newsletter',
    'Event registration',
    'Job application',
    'Feedback',
    'Quote request',
    'Support request',
    'Privacy request',
  ];
  for (const one of names) await expect(gallery.getByRole('radio', { name: new RegExp(`^${one}`) })).toBeVisible();
  await look(page, info, 'gallery contact');
  for (const [one, label] of [
    ['Event registration', 'Which session would you like to join?'],
    ['Feedback', 'How likely are you to recommend us to a friend or colleague?'],
    ['Privacy request', 'What would you like us to do?'],
  ]) {
    await gallery.getByRole('radio', { name: new RegExp(`^${one}`) }).click();
    await expect(
      gallery
        .locator('jfb-form-preview')
        .getByText(label as string)
        .first(),
    ).toBeVisible();
    await look(page, info, `gallery ${(one as string).toLowerCase()}`);
  }
  // The last card is chosen low in the list, and its preview stays in view from the top.
  await expect(gallery.locator('.preview .muted')).toBeInViewport();
  await axeInside(page, 'jfb-template-gallery-modal');
  await page.setViewportSize({ width: 920, height: 900 });
  await look(page, info, 'gallery narrow');
});
