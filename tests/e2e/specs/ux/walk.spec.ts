import AxeBuilder from '@axe-core/playwright';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { inLanguage, inTheme } from '../../support/backoffice-look.ts';
import { API, blankForm, contactForm, created, formOf, KEY, unique } from '../../support/forms-api.ts';
import {
  COMBOS,
  comboName,
  LANGUAGE_CODES,
  splitAxe,
  walkDir,
  wantedScreen,
  type AxeNode,
  type Combo,
  type ShotReport,
} from '../../support/ux-walk.ts';

// The UX walk of Phase 9 (ROADMAP 9.6a): every screen of the Form Builder section, its library and its designer, once in
// each of the twelve ways of support/ux-walk.ts, as the admin, with the form made for the walk through the API. Each
// shot keeps what axe found inside our own elements, how far the page scrolls sideways, what scrolls inside it and what
// an ellipsis cuts short; the ux-sheets project puts each screen's twelve shots on one sheet. The walk finds its way
// without words, by tags, data marks and icons, so it walks the German backoffice the same way. It runs only when asked:
// JFB_E2E_UX=1 npx playwright test --project=ux-sheets (which walks first).

const SECTION = '/umbraco/section/jfb-forms';
const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;
const entityAction = (id: string) => `[data-mark="entity-action:Jiron.FormBuilder.EntityAction.${id}"]`;
const createOption = (icon: string) => `umb-entity-create-option-action-list-modal umb-ref-item[icon="${icon}"]`;
const PUBLISH = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.Publish"]';

/** Dresses a page for a combination: its window, Umbraco's theme, and the backoffice's language, which the walk gives by
 * answering the current user's language as the combination's, so nothing changes on the shared host. */
async function dress(page: Page, combo: Combo): Promise<void> {
  // A step that cannot find its way gives up soon, so the walk notes it and goes on.
  page.setDefaultTimeout(15_000);
  await page.setViewportSize({ width: combo.width, height: combo.height });
  await inTheme(page, combo.theme);
  await inLanguage(page, LANGUAGE_CODES[combo.language]);
}

/** Waits for the motion to end, with the pointer out of the way. */
async function settle(page: Page): Promise<void> {
  await page.mouse.move(1, 1);
  // A loader spins for ever, so the wait has a limit of its own.
  await page
    .waitForFunction(
      () => document.getAnimations().every((animation) => animation.playState !== 'running'),
      undefined,
      {
        timeout: 3000,
      },
    )
    .catch(() => undefined);
  await page.waitForTimeout(250);
}

/** Axe's WCAG 2.2 A and AA findings on the page, each with its ancestry. */
async function axeNodes(page: Page): Promise<AxeNode[]> {
  // The options first: AxeBuilder's options() replaces what withTags() set, which would run every rule axe has.
  const result = await new AxeBuilder({ page })
    .options({ ancestry: true })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  return result.violations.flatMap((violation) =>
    violation.nodes.map((node) => ({
      rule: violation.id,
      target: node.target,
      ancestry: (node as { ancestry?: unknown[] }).ancestry ?? [],
    })),
  );
}

/** How the page lies at this width: its sideways scroll, the boxes that scroll inside it, and the texts cut short. */
const measure = (page: Page) =>
  page.evaluate(() => {
    const all: HTMLElement[] = [];
    const walk = (root: Document | ShadowRoot) => {
      for (const element of root.querySelectorAll('*')) {
        if (element instanceof HTMLElement) all.push(element);
        if (element.shadowRoot) walk(element.shadowRoot);
      }
    };
    walk(document);
    const seen = (element: HTMLElement) => {
      const box = element.getBoundingClientRect();
      return box.width > 0 && box.height > 0 && box.bottom > 0 && box.right > 0 && box.top < innerHeight;
    };
    const scrollers: string[] = [];
    const clipped: string[] = [];
    for (const element of all) {
      if (element.scrollWidth <= element.clientWidth + 1 || !seen(element)) continue;
      const style = getComputedStyle(element);
      const text = (element.textContent ?? '').trim().replace(/\s+/g, ' ');
      if ((style.overflowX === 'auto' || style.overflowX === 'scroll') && element.clientWidth > 0) {
        const name = element.id
          ? `#${element.id}`
          : typeof element.className === 'string' && element.className.trim()
            ? `.${element.className.trim().split(/\s+/).join('.')}`
            : '';
        scrollers.push(`${element.tagName.toLowerCase()}${name} (${element.scrollWidth} > ${element.clientWidth})`);
      } else if (style.textOverflow === 'ellipsis' && text) clipped.push(text.slice(0, 60));
    }
    return {
      overflowX: Math.max(0, document.documentElement.scrollWidth - innerWidth),
      scrollers: [...new Set(scrollers)].slice(0, 8),
      clipped: [...new Set(clipped)].slice(0, 12),
    };
  });

/** One walk: its shots and what each found, written beside them. */
class Walk {
  readonly reports: ShotReport[] = [];
  readonly page: Page;
  readonly dir: string;

  constructor(page: Page, dir: string) {
    this.page = page;
    this.dir = dir;
    mkdirSync(dir, { recursive: true });
  }

  /** Reaches a screen, shoots it and measures it; a screen the walk cannot reach is noted, and the walk goes on. */
  async shot(screen: string, reach: () => Promise<void>, page = this.page): Promise<void> {
    if (!wantedScreen(screen)) return;
    try {
      await reach();
      await settle(page);
      const file = `${screen}.png`;
      await page.screenshot({ path: join(this.dir, file) });
      const { ours, umbraco } = splitAxe(await axeNodes(page));
      this.reports.push({ screen, file, axe: ours, axeUmbraco: umbraco, ...(await measure(page)) });
    } catch (error) {
      const message = (error instanceof Error ? error.message : String(error)).split('\n')[0]?.slice(0, 300) ?? '';
      await page.screenshot({ path: join(this.dir, `${screen}.failed.png`) }).catch(() => undefined);
      this.reports.push({
        screen,
        file: null,
        error: message,
        axe: [],
        axeUmbraco: 0,
        overflowX: 0,
        scrollers: [],
        clipped: [],
      });
    }
  }

  write(combo: Combo): void {
    writeFileSync(join(this.dir, 'report.json'), JSON.stringify({ combo, screens: this.reports }, null, 2));
  }
}

/** The key and name of the demo's form with this alias. */
async function demoForm(api: APIRequestContext, alias: string): Promise<{ key: string; name: string }> {
  const found = (await (await api.get(`${API}/form/search`, { params: { text: alias, take: 50 } })).json()) as {
    items: { key: string; name: string; alias?: string | null }[];
  };
  const form = found.items.find((item) => item.alias === alias);
  expect(form?.key, `the demo's form ${alias}`).toMatch(KEY);
  return form as { key: string; name: string };
}

async function duplicate(api: APIRequestContext, key: string, name: string, includeActions = false) {
  return created(await api.post(`${API}/form/${key}/duplicate`, { data: { name, includeActions } }), name);
}

/** A draft's field: its key, its type and the index of its page. */
interface WalkField {
  readonly key: string;
  readonly type: string;
  readonly page: number;
}

/** A draft's fields, in order, each with its page. */
async function fieldsOf(api: APIRequestContext, key: string): Promise<WalkField[]> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages?: { groups?: { fields?: { key: string; type: string }[] }[] }[] };
  };
  return (draft.definition.pages ?? []).flatMap((page, index) =>
    (page.groups ?? []).flatMap((group) => (group.fields ?? []).map((field) => ({ ...field, page: index }))),
  );
}

/** What the walk shows, made for it through the API, its names unique to the walk. */
async function seed(api: APIRequestContext, label: string, importPath: string) {
  const name = (what: string) => unique(`UX ${label} ${what}`);
  const folderName = name('folder');
  const folder = await created(await api.post(`${API}/folder`, { data: { name: folderName } }), folderName);
  const emptyName = name('empty folder');
  await created(await api.post(`${API}/folder`, { data: { name: emptyName } }), emptyName);
  await contactForm(api, name('enquiries'), folder);
  await contactForm(api, name('callback'), folder);
  const usedName = name('used on a page');
  await pageThatPicks(api, await contactForm(api, usedName), usedName);
  const feedback = await duplicate(api, (await demoForm(api, 'feedback')).key, name('feedback'));
  const volunteer = await duplicate(api, (await demoForm(api, 'volunteer')).key, name('volunteer'));
  const blankName = name('blank');
  const blank = await blankForm(api, blankName);
  // A form for some member groups that names none, so its Settings view shows the check's problem under the setting,
  // with a redirect for German visitors before its default message (10.1b).
  const grouped = await blankForm(api, name('members'));
  const groupedDraft = (await (await api.get(`${API}/form/${grouped}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { settings: Record<string, Record<string, unknown>>; outcomes?: unknown[] };
  };
  const groupedSettings = groupedDraft.definition.settings;
  groupedSettings['availability'] = { ...groupedSettings['availability'], members: 'groups' };
  groupedDraft.definition.outcomes = [
    {
      key: crypto.randomUUID(),
      kind: 'redirect',
      redirect: { url: '/de/danke', delaySeconds: 0 },
      rules: [
        {
          key: crypto.randomUUID(),
          effect: 'use',
          enabled: true,
          match: 'all',
          conditions: [{ context: 'culture', op: 'startsWith', value: 'de' }],
        },
      ],
    },
    ...(groupedDraft.definition.outcomes ?? []),
  ];
  const groupedSaved = await api.put(`${API}/form/${grouped}/draft`, {
    data: { concurrencyVersion: groupedDraft.form.concurrencyVersion, definition: groupedDraft.definition },
  });
  expect(groupedSaved.ok(), await groupedSaved.text()).toBe(true);
  const restrictedName = name('salaries');
  const restricted = await contactForm(api, restrictedName);
  const restricting = await api.post('/jfb-testing/form-restriction', { data: { form: restricted, restricted: true } });
  expect(restricting.status()).toBe(204);
  const sourceName = name('with actions');
  await duplicate(api, (await demoForm(api, 'contact')).key, sourceName, true);
  const targetName = name('target');
  await blankForm(api, targetName);
  const trashedName = name('trashed');
  const trashed = await contactForm(api, trashedName);
  const { concurrencyVersion } = await formOf(api, trashed);
  expect((await api.post(`${API}/form/${trashed}/trash`, { data: { concurrencyVersion } })).ok()).toBe(true);
  // An export whose theme this site lacks, so the import shows what it would change.
  const file = (await (await api.get(`${API}/form/${feedback}/export`)).json()) as {
    definition: { settings: { appearance?: Record<string, unknown> } };
  };
  file.definition.settings.appearance = { ...file.definition.settings.appearance, theme: 'midnight' };
  writeFileSync(importPath, JSON.stringify(file));
  return {
    folder,
    emptyName,
    usedName,
    feedback,
    feedbackFields: await fieldsOf(api, feedback),
    volunteer,
    volunteerFields: await fieldsOf(api, volunteer),
    blank,
    blankName,
    grouped,
    restrictedName,
    sourceName,
    targetName,
    trashedName,
  };
}

/** The demo's volunteer page, whose copy picks a walk's form, and the home page the copy goes below. */
const VOLUNTEER_PAGE = 'b7d3e0a1-5c94-4f2e-8a6b-0d1c3f9e2a47';
const HOME_PAGE = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';
const MANAGEMENT_API = '/umbraco/management/api/v1';

/**
 * A page of its own that picks the form, as library.spec.ts makes one: a copy of the demo's volunteer page whose form
 * block then names this form, so the trash's confirmation has a page to name and the demo's own forms stay as they are.
 */
async function pageThatPicks(api: APIRequestContext, formKey: string, name: string): Promise<void> {
  const copied = await api.post(`${MANAGEMENT_API}/document/${VOLUNTEER_PAGE}/copy`, {
    data: { target: { id: HOME_PAGE }, relateToOriginal: false, includeDescendants: false },
  });
  expect(copied.status(), await copied.text()).toBe(201);
  const pageKey = copied.headers()['umb-generated-resource'] ?? copied.headers()['location']?.split('/').pop() ?? '';
  expect(pageKey, 'the copied page').toMatch(KEY);
  const page = (await (await api.get(`${MANAGEMENT_API}/document/${pageKey}`)).json()) as {
    template: { id: string } | null;
    values: unknown[];
    variants: { culture: string | null; segment: string | null }[];
  };
  const values = JSON.stringify(page.values);
  const keys = [...new Set(values.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi) ?? [])];
  const forms: string[] = [];
  for (const key of keys) if ((await api.get(`${API}/form/${key}`)).status() === 200) forms.push(key);
  expect(forms, 'the volunteer form, picked in the copy').toHaveLength(1);
  const updated = await api.put(`${MANAGEMENT_API}/document/${pageKey}`, {
    data: {
      template: page.template,
      values: JSON.parse(values.split(forms[0] as string).join(formKey)) as unknown[],
      variants: page.variants.map((variant) => ({ culture: variant.culture, segment: variant.segment, name })),
    },
  });
  expect(updated.ok(), await updated.text()).toBe(true);
  await expect
    .poll(
      async () => ((await (await api.get(`${API}/form/${formKey}/referenced-by`)).json()) as { total: number }).total,
    )
    .toBe(1);
}

/** A row of the forms tree, by the name its label carries. */
const treeRow = (page: Page, name: string): Locator =>
  page.locator(`umb-section-sidebar uui-menu-item[label="${name}"]`).filter({ visible: true }).first();

/** Opens a tree row's "..." and chooses one of our actions by its alias. */
async function rowAction(page: Page, name: string, id: string): Promise<void> {
  const row = treeRow(page, name);
  // The row comes into sight before the hover that shows its "...", so no scroll takes the hover away.
  await row.scrollIntoViewIfNeeded();
  await row.hover();
  await row.locator('umb-entity-actions-dropdown uui-button').first().click();
  // The menu's item, not the quick button of the same action that a row may show beside it.
  await page
    .locator(`umb-entity-action-list ${entityAction(id)}`)
    .filter({ visible: true })
    .first()
    .click();
}

/** Opens a tree row's create dialog and chooses an option by its icon. */
async function createIn(page: Page, name: string, icon: string): Promise<void> {
  const row = treeRow(page, name);
  await row.hover();
  await row.locator(entityAction('Create')).first().click();
  await page.locator(createOption(icon)).click();
}

async function openSection(page: Page): Promise<void> {
  await page.goto(SECTION);
  await expect(page.locator('jfb-overview uui-box').first()).toBeVisible();
}

/** Opens a form at Design and waits for its canvas. */
async function openDesign(page: Page, key: string): Promise<Locator> {
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-form-canvas jfb-canvas-field').first()).toBeVisible();
  return design;
}

/** Selects a field on the canvas, on its page, and waits for its inspector. */
async function select(page: Page, design: Locator, wanted: WalkField): Promise<void> {
  if (wanted.page > 0) await design.locator('jfb-form-canvas .bo-step:not(.bo-step-add)').nth(wanted.page).click();
  const field = design.locator(`jfb-canvas-field[key="${wanted.key}"] [data-control="select"]`).first();
  await field.scrollIntoViewIfNeeded();
  await field.click();
  await expect(design.locator('jfb-designer-inspector h2')).toBeVisible();
}

const fieldOf = (fields: readonly WalkField[], type: string): WalkField => {
  const found = fields.find((field) => field.type === type);
  if (!found) throw new Error(`The walk's form has no ${type} field.`);
  return found;
};

/** The selected element's tools: in the inspector's header over a narrow canvas, on the canvas's bar beside it. */
async function toolsOf(design: Locator): Promise<Locator> {
  const header = design.locator('jfb-designer-inspector .tools');
  return (await header.isVisible())
    ? header
    : design.locator('jfb-canvas-field .bo-tools').filter({ visible: true }).first();
}

/** The palette, opened: from its rail when it is folded, or over the canvas from Add field in a narrow workspace. */
async function openPalette(design: Locator): Promise<Locator> {
  const palette = design.locator('jfb-designer-palette');
  if (await palette.locator('uui-input input').isVisible()) return palette;
  const rail = palette.locator('uui-button').first();
  if (await rail.isVisible()) await rail.click();
  else await design.locator('jfb-form-canvas .bo-add-field').first().click();
  await expect(palette.locator('uui-input input')).toBeVisible();
  return palette;
}

/** Scrolls an editor into the middle of its scroll box; an editor drawn without a box of its own is never "visible". */
const scrollTo = (editor: Locator) => editor.evaluate((element) => element.scrollIntoView({ block: 'center' }));

/** Opens an inspector section by its id, unless it is open. */
async function openInspectorSection(design: Locator, id: string): Promise<void> {
  const head = design.locator(`jfb-designer-inspector [data-section="${id}"] .section-head button`);
  if ((await head.getAttribute('aria-expanded')) !== 'true') await head.click();
}

for (const combo of COMBOS) {
  test(`the walk, ${comboName(combo)}`, async ({ page, browser, loginAs, apiAs }, testInfo) => {
    test.setTimeout(20 * 60_000);
    const api = await apiAs('admin');
    const label = comboName(combo);
    const made = await seed(api, label, testInfo.outputPath('import.json'));
    await dress(page, combo);
    await loginAs('admin');
    const walk = new Walk(page, join(walkDir(), label));

    // The section.
    await walk.shot('overview', async () => {
      await openSection(page);
      await expect(page.locator('jfb-overview [aria-busy="true"]')).toHaveCount(0);
    });
    await walk.shot('coming', async () => {
      await page.goto(`${SECTION}/workspace/jfb-inbox`);
      await expect(page.locator('jfb-coming-screen')).toBeVisible();
    });
    await walk.shot('search', async () => {
      await openSection(page);
      await page.locator('umb-search-header-app uui-button').first().click();
      const search = page.locator('umb-search-modal');
      // Our provider's tab, the one place the walk names a word, in both languages.
      await search.getByRole('button', { name: /^(Forms|Formulare)$/ }).click();
      await search.locator('input').fill(made.restrictedName);
      await expect(search.locator('a', { hasText: made.restrictedName }).first()).toBeVisible();
    });

    // The library.
    await walk.shot('folder', async () => {
      await page.goto(`${SECTION}/workspace/jfb-folder/edit/${made.folder}`);
      await expect(page.locator('jfb-folder-list table')).toBeVisible();
    });
    await walk.shot('create-options', async () => {
      await openSection(page);
      const row = treeRow(page, made.emptyName);
      await row.hover();
      await row.locator(entityAction('Create')).first().click();
      await expect(page.locator(createOption('icon-document'))).toBeVisible();
    });
    await walk.shot('folder-empty', async () => {
      await openSection(page);
      await treeRow(page, made.emptyName).click();
      await expect(page.locator('jfb-folder-list uui-button').first()).toBeVisible();
    });
    await walk.shot('folder-name', async () => {
      await openSection(page);
      await createIn(page, made.emptyName, 'icon-folder');
      await expect(page.locator('jfb-folder-name-modal input').first()).toBeVisible();
    });
    await walk.shot('gallery', async () => {
      await openSection(page);
      await createIn(page, made.emptyName, 'icon-layers');
      await expect(page.locator('jfb-template-gallery-modal [role="radio"]').first()).toBeVisible();
    });
    await walk.shot('import', async () => {
      await openSection(page);
      await createIn(page, made.emptyName, 'icon-cloud-upload');
      const modal = page.locator('jfb-import-modal');
      await modal.locator('input[type="file"]').setInputFiles(testInfo.outputPath('import.json'));
      await expect(modal.locator('uui-button[look="primary"]')).toBeEnabled();
    });
    await walk.shot('move', async () => {
      await openSection(page);
      await rowAction(page, made.blankName, 'Move');
      await expect(page.locator('umb-tree-picker-modal uui-menu-item').first()).toBeVisible();
    });
    await walk.shot('duplicate', async () => {
      await openSection(page);
      await rowAction(page, made.restrictedName, 'Duplicate');
      await expect(page.locator('jfb-duplicate-modal input').first()).toBeVisible();
    });
    await walk.shot('copy-actions', async () => {
      await openSection(page);
      await rowAction(page, made.sourceName, 'CopyActions');
      const modal = page.locator('jfb-copy-actions-modal');
      await modal.locator('uui-button').first().click();
      const picker = page.locator('umb-tree-picker-modal');
      await picker.locator(`uui-menu-item[label="${made.targetName}"]`).click();
      await picker.locator('uui-button[look="primary"]').click();
      await expect(modal).toContainText(made.targetName);
    });
    await walk.shot('copy-actions-done', async () => {
      const modal = page.locator('jfb-copy-actions-modal');
      await modal.locator('uui-button[look="primary"]').click();
      await expect(modal.locator('a').first()).toBeVisible();
    });
    await walk.shot('trash-confirm', async () => {
      await openSection(page);
      await rowAction(page, made.usedName, 'Trash');
      await expect(page.locator('umb-confirm-modal')).toBeVisible();
    });
    await walk.shot('recycle-bin', async () => {
      await page.goto(`${SECTION}/workspace/jfb-recycle-bin`);
      await expect(page.locator('jfb-recycle-bin tr', { hasText: made.trashedName })).toBeVisible();
    });
    await walk.shot('restore', async () => {
      const row = page.locator('jfb-recycle-bin tr', { hasText: made.trashedName });
      await row.locator('uui-button').last().click();
      await page.locator(entityAction('Restore')).filter({ visible: true }).first().click();
      await expect(page.locator('jfb-restore-modal')).toBeVisible();
    });
    await walk.shot('empty-bin', async () => {
      await page.goto(`${SECTION}/workspace/jfb-recycle-bin`);
      await page.locator('jfb-recycle-bin uui-button[color="danger"]').first().click();
      await expect(page.locator('umb-confirm-modal')).toBeVisible();
    });

    // The form workspace and its Design view.
    const feedback = made.feedbackFields;
    await walk.shot('design', async () => {
      await openDesign(page, made.feedback);
    });
    await walk.shot('palette-search', async () => {
      const palette = await openPalette(await openDesign(page, made.feedback));
      await palette.locator('uui-input input').fill('da');
    });
    await walk.shot('paste-questions', async () => {
      const palette = await openPalette(await openDesign(page, made.feedback));
      await palette.locator('.paste').click();
      await page.locator('jfb-paste-questions-modal textarea').fill('Your name\nEmail address\nWhen can we call?');
    });
    await walk.shot('selected', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'dropdown'));
    });
    await walk.shot('element-menu', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'dropdown'));
      // The canvas bar's "..." carries its class; the inspector's is the button that opens its menu.
      await (await toolsOf(design)).locator('.bo-more-element, button[popovertarget]').last().click();
      await expect(design.locator('uui-popover-container:popover-open button').first()).toBeVisible();
    });
    await walk.shot('move-to', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'dropdown'));
      await (await toolsOf(design)).locator('button').first().click();
      await expect(page.locator('jfb-move-to-modal')).toBeVisible();
    });
    await walk.shot('inspector-sections', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'textarea'));
      for (const id of ['validation', 'appearance', 'advanced']) await openInspectorSection(design, id);
    });
    await walk.shot('options', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'dropdown'));
      await scrollTo(design.locator('jfb-option-editor').first());
    });
    await walk.shot('grid', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'grid'));
      await scrollTo(design.locator('jfb-option-editor').first());
    });
    await walk.shot('rich-text', async () => {
      const design = await openDesign(page, made.volunteer);
      await select(page, design, fieldOf(made.volunteerFields, 'consent'));
      await scrollTo(design.locator('jfb-rich-text').first());
    });
    await walk.shot('change-type', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'dropdown'));
      await design.locator('jfb-designer-inspector .change-type').click();
      await expect(page.locator('jfb-change-type-modal')).toBeVisible();
    });
    await walk.shot('rule-builder', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'textarea'));
      await openInspectorSection(design, 'rules');
      await design.locator('jfb-designer-inspector .add-rule').first().click();
      await expect(page.locator('jfb-rule-builder-modal')).toBeVisible();
    });
    await walk.shot('rules-overview', async () => {
      const design = await openDesign(page, made.feedback);
      await design.locator('jfb-form-canvas .bo-more').click();
      await design.locator('#jfb-canvas-more .bo-menu button').nth(2).click();
      await expect(page.locator('jfb-rules-overview-modal')).toBeVisible();
    });
    await walk.shot('shortcuts', async () => {
      const design = await openDesign(page, made.feedback);
      await design.locator('jfb-form-canvas .bo-more').click();
      await design.locator('#jfb-canvas-more .bo-menu button').nth(3).click();
      await expect(page.locator('jfb-shortcuts-modal')).toBeVisible();
    });
    await walk.shot('undo-toast', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'dropdown'));
      await (await toolsOf(design)).locator('button').nth(2).click();
      await expect(page.locator('uui-toast-notification').first()).toBeVisible();
    });
    await walk.shot('content-language', async () => {
      const design = await openDesign(page, made.volunteer);
      await design.locator('jfb-form-canvas .bo-language select').selectOption('de-DE');
    });
    await walk.shot('form-check', async () => {
      const design = await openDesign(page, made.volunteer);
      await design.locator('jfb-form-canvas .bo-check').click();
      await expect(page.locator('jfb-form-check-modal uui-box').first()).toBeVisible();
    });
    await walk.shot('publish', async () => {
      await openDesign(page, made.feedback);
      await page.locator(PUBLISH).click();
      await expect(page.locator('jfb-publish-modal .said')).not.toBeEmpty();
    });
    await walk.shot('publish-blocked', async () => {
      await page.goto(workspaceOf(made.blank));
      await expect(page.locator('jfb-form-design-view jfb-form-canvas')).toBeVisible();
      await page.locator(PUBLISH).click();
      await expect(page.locator('jfb-publish-modal [role="alert"]')).toBeVisible();
    });
    await walk.shot('blank-form', async () => {
      await page.goto(workspaceOf(made.blank));
      await expect(
        page.locator('jfb-form-design-view jfb-form-canvas :is(.bo-empty-group, .bo-add-field)').first(),
      ).toBeVisible();
    });

    // The Settings view (10.1a): Submission open with the others closed, More options, each other group opened in
    // turn, and the form check's problem under its setting.
    const settingsView = page.locator('jfb-form-settings-view');
    const openSettings = async (key: string) => {
      await page.goto(`${workspaceOf(key)}/view/settings`);
      await expect(settingsView.locator('#group-submission')).toBeVisible();
    };
    const openSettingsGroup = async (group: string) => {
      const head = settingsView.locator(`#group-${group}`);
      if ((await head.getAttribute('aria-expanded')) !== 'true') await head.click();
      await expect(head).toHaveAttribute('aria-expanded', 'true');
      await scrollTo(head);
    };
    await walk.shot('settings', async () => {
      await openSettings(made.feedback);
    });
    await walk.shot('settings-more', async () => {
      await openSettings(made.feedback);
      await settingsView.locator('.more-toggle').click();
      await scrollTo(settingsView.locator('#reference-prefix'));
    });
    for (const group of ['availability', 'protection', 'appearance', 'developer'])
      await walk.shot(`settings-${group}`, async () => {
        await openSettings(made.feedback);
        await openSettingsGroup(group);
      });
    await walk.shot('settings-problem', async () => {
      await openSettings(made.grouped);
      await openSettingsGroup('availability');
      await expect(settingsView.locator('#member-groups-why-0')).toBeVisible();
      await scrollTo(settingsView.locator('#member-groups'));
    });
    // After sending (10.1b): the list, a message's sidebar, and a redirect's with its rule.
    await walk.shot('settings-after', async () => {
      await openSettings(made.grouped);
      await openSettingsGroup('after');
    });
    const outcomeSidebar = page.locator('jfb-outcome-modal');
    await walk.shot('settings-outcome', async () => {
      await openSettings(made.grouped);
      await openSettingsGroup('after');
      await settingsView.locator('jfb-outcome-list .row-open').nth(1).click();
      await expect(outcomeSidebar.locator('#message')).toBeVisible();
    });
    await walk.shot('settings-outcome-redirect', async () => {
      await openSettings(made.grouped);
      await openSettingsGroup('after');
      await settingsView.locator('jfb-outcome-list .row-open').first().click();
      await expect(outcomeSidebar.locator('#address')).toBeVisible();
    });
    await walk.shot('view-coming', async () => {
      await page.goto(`${workspaceOf(made.feedback)}/view/actions`);
      await expect(page.locator('jfb-coming-screen')).toBeVisible();
    });

    // Who may only read entries sees the entry views and the form's name, and no designer.
    const reader = await browser.newContext();
    try {
      const readerPage = await reader.newPage();
      await dress(readerPage, combo);
      await loginAs('entryHandler', readerPage);
      await walk.shot(
        'read-only',
        async () => {
          await readerPage.goto(workspaceOf(made.feedback));
          await expect(readerPage.locator('jfb-form-workspace-editor')).toBeVisible();
        },
        readerPage,
      );
    } finally {
      await reader.close();
    }

    walk.write(combo);
    // A screen the walk could not reach is the walk's to fix, so it fails, after the report is written.
    expect(walk.reports.filter((report) => report.error).map((report) => `${report.screen}: ${report.error}`)).toEqual(
      [],
    );
  });
}
