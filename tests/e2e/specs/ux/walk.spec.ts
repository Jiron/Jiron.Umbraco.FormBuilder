import AxeBuilder from '@axe-core/playwright';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { inLanguage, inTheme } from '../../support/backoffice-look.ts';
import { API, blankForm, contactForm, created, formOf, KEY, unique } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';
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

// The UX walk of Phase 9 (ROADMAP 9.6a), of Phase 10 (10.4), Phase 11 (11.6) and Phase 12 (12.6): every screen of the Form
// Builder section, its library with its lists, themes, email layouts and templates, its designer, a form's settings,
// actions, history, publishing, conflicts, preview, entries and insights, the settings with Access, and Activity, once in
// each of the twelve ways of support/ux-walk.ts, as the admin, with the forms made for the walk through the API. Each
// shot keeps what axe found inside our own elements, how far the page scrolls sideways, what scrolls inside it and what
// an ellipsis cuts short; the ux-sheets project puts each screen's twelve shots on one sheet. The walk finds its way
// without words, by tags, data marks and icons, so it walks the German backoffice the same way. It runs only when asked:
// JFB_E2E_UX=1 npx playwright test --project=ux-sheets (which walks first).

const SECTION = '/umbraco/section/jfb-forms';
const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;
const entityAction = (id: string) => `[data-mark="entity-action:Jiron.FormBuilder.EntityAction.${id}"]`;
const createOption = (icon: string) => `umb-entity-create-option-action-list-modal umb-ref-item[icon="${icon}"]`;
const PUBLISH = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.Publish"]';
const SAVE_DRAFT = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.SaveDraft"]';
const PREVIEW = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.Preview"]';

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
  // The options first: AxeBuilder's options() replaces what withTags() set, which would run every rule axe has. Frames
  // are left alone, as in axeInside: an email's preview is a sandboxed frame that runs no script (ADR-0189).
  const result = await new AxeBuilder({ page })
    .options({ ancestry: true, iframes: false })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .setLegacyMode(true)
    .analyze();
  return result.violations.flatMap((violation) =>
    violation.nodes.map((node) => ({
      rule: violation.id,
      target: node.target,
      ancestry: (node as { ancestry?: unknown[] }).ancestry ?? [],
    })),
  );
}

/**
 * How the page lies at this width: its sideways scroll, the boxes that scroll inside it, and the texts cut short. The
 * forms tree's names end with Umbraco's ellipsis as its own trees' do (ADR-0221), so its rows are left out.
 */
const measure = (page: Page) =>
  page.evaluate(() => {
    const all: HTMLElement[] = [];
    const walk = (root: Document | ShadowRoot) => {
      for (const element of root.querySelectorAll('*')) {
        if (element.localName === 'jfb-library-tree-item') continue;
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

/** Gives the draft's first field another label and saves the draft. */
async function relabelFirst(api: APIRequestContext, key: string, label: string): Promise<void> {
  const read = await api.get(`${API}/form/${key}/draft`);
  expect(read.ok(), await read.text()).toBe(true);
  const draft = (await read.json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: { label?: Record<string, string> }[] }[] }[] };
  };
  const field = draft.definition.pages[0]?.groups[0]?.fields[0];
  if (field) field.label = { '': label, 'de-DE': label };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

/** Adds a webhook to the draft's actions whose address the stub answers 503, so a run of them leaves one failed. */
async function addFailingWebhook(api: APIRequestContext, request: APIRequestContext, key: string): Promise<void> {
  const path = `/broken/${crypto.randomUUID()}`;
  const scripted = await request.put(`${STUB_URL}/_stub/answers`, {
    data: { method: 'POST', path, status: 503, body: '{"down":true}' },
  });
  expect(scripted.ok(), await scripted.text()).toBe(true);
  const read = await api.get(`${API}/form/${key}/draft`);
  expect(read.ok(), await read.text()).toBe(true);
  const draft = (await read.json()) as {
    form: { concurrencyVersion: number };
    definition: Record<string, unknown> & { actions?: unknown[] };
  };
  const webhook = {
    key: crypto.randomUUID(),
    type: 'webhook',
    name: 'Tell the archive',
    enabled: true,
    trigger: 'submitted',
    stopOnFailure: false,
    settings: { url: `${STUB_URL}${path}` },
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: { ...draft.definition, actions: [...(draft.definition.actions ?? []), webhook] },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

async function publishForm(api: APIRequestContext, key: string): Promise<void> {
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
}

/** What the walk shows, made for it through the API, its names unique to the walk. */
async function seed(api: APIRequestContext, request: APIRequestContext, label: string, importPath: string) {
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
  // with a redirect for German visitors before its message (10.1b), which greets the sample entry by name, masks its
  // sensitive email and shows a copy of the answers in its preview (10.1c).
  const grouped = await blankForm(api, name('members'));
  const groupedDraft = (await (await api.get(`${API}/form/${grouped}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: {
      settings: Record<string, Record<string, unknown>>;
      pages: { groups: { fields: unknown[] }[] }[];
      outcomes?: Record<string, unknown>[];
    };
  };
  const groupedSettings = groupedDraft.definition.settings;
  groupedSettings['availability'] = { ...groupedSettings['availability'], members: 'groups' };
  const groupedFields = groupedDraft.definition.pages[0]?.groups[0];
  if (groupedFields)
    groupedFields.fields = [
      { key: crypto.randomUUID(), type: 'name', alias: 'name', label: { '': 'Your name', 'de-DE': 'Ihr Name' } },
      {
        key: crypto.randomUUID(),
        type: 'email',
        alias: 'email',
        label: { '': 'Email', 'de-DE': 'E-Mail' },
        sensitive: true,
      },
    ];
  const groupedMessage = groupedDraft.definition.outcomes?.[0];
  if (groupedMessage) {
    groupedMessage['message'] = {
      '': '<p>Thanks, {{ field.name }}. We answer at {{ field.email }} within two working days.</p>',
      'de-DE': '<p>Danke, {{ field.name }}. Wir antworten an {{ field.email }} innerhalb von zwei Werktagen.</p>',
    };
    groupedMessage['showAnswers'] = true;
  }
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
  // Its two emails, one to the team and one to the visitor, which the Actions view shows (10.2a).
  const withActions = await duplicate(api, (await demoForm(api, 'contact')).key, sourceName, true);
  // Its archive is down, so the preview's Run actions leaves a failed run with its Details (10.2f).
  await addFailingWebhook(api, request, withActions);
  const targetName = name('target');
  await blankForm(api, targetName);
  // A form with two published versions, draft saves between them and a change since (10.3a to 10.3d), and one whose
  // draft another save takes over while the walk edits it (10.3c).
  const versioned = await contactForm(api, name('versioned'));
  await publishForm(api, versioned);
  await relabelFirst(api, versioned, 'Full name');
  await relabelFirst(api, versioned, 'Your full name');
  await publishForm(api, versioned);
  await relabelFirst(api, versioned, 'Name and surname');
  const conflicted = await contactForm(api, name('conflicted'));
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
  // Phase 11's screens: entries waiting for approval, a month of visits, and an answer of every kind.
  const seeded = async (path: string, data: Record<string, unknown>) => {
    const answer = await api.post(path, { data, timeout: 300_000 });
    expect(answer.ok(), await answer.text()).toBe(true);
    return ((await answer.json()) as { key: string }).key;
  };
  const entries = await seeded('/jfb-testing/entries-form', { count: 27, spam: 3, name: name('applications') });
  const insights = await seeded('/jfb-testing/insights-form', { name: name('insights') });
  const answers = await seeded('/jfb-testing/answers-form', { count: 60, name: name('answers') });
  // Phase 12's library: a list of fixed options and a theme of the look's own, two email layouts, a saved template, and
  // mail and action runs in every state for Deliveries.
  const alias = () => `ux${crypto.randomUUID().replaceAll('-', '').slice(0, 12)}`;
  const libraryName = name('library');
  await created(
    await api.post(`${API}/option-list`, {
      data: {
        list: {
          alias: alias(),
          name: libraryName,
          kind: 'manual',
          items: [
            { value: 's', label: { '': 'Small' } },
            { value: 'm', label: { '': 'Medium' } },
            { value: 'l', label: { '': 'Large' } },
          ],
          cacheSeconds: 0,
        },
      },
    }),
    libraryName,
  );
  await created(
    await api.post(`${API}/theme`, {
      data: {
        theme: {
          alias: alias(),
          name: libraryName,
          baseTheme: 'default',
          tokens: { light: {}, dark: {} },
        },
      },
    }),
    libraryName,
  );
  const layouts = await api.post('/jfb-testing/mail-layouts', { data: { marker: libraryName, word: 'Thanks' } });
  expect(layouts.ok(), await layouts.text()).toBe(true);
  await created(
    await api.post(`${API}/template/form/${feedback}`, {
      data: { name: libraryName, description: 'A short survey after an event.' },
    }),
    libraryName,
  );
  const deliveries = await api.post('/jfb-testing/deliveries', { data: { name: name('deliveries') } });
  expect(deliveries.ok(), await deliveries.text()).toBe(true);
  return {
    folderName,
    libraryName,
    entries,
    insights,
    answers,
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
    withActions,
    targetName,
    trashedName,
    versioned,
    conflicted,
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

/**
 * Presses a tree's "Load more" until it shows the row: the twelve looks' forms share the root, and a look's last names
 * fall past the tree's first page.
 */
async function loadedIn(tree: Locator, row: Locator): Promise<Locator> {
  const more = tree.locator('umb-tree-load-more-button').filter({ visible: true }).first();
  const items = tree.locator('uui-menu-item');
  for (let round = 0; round < 10 && (await row.count()) === 0 && (await more.count()) > 0; round++) {
    const before = await items.count();
    await more.click();
    await expect.poll(() => items.count()).toBeGreaterThan(before);
  }
  return row;
}

const shownRow = (page: Page, name: string): Promise<Locator> =>
  loadedIn(page.locator('umb-section-sidebar'), treeRow(page, name));

/** Opens a tree row's "..." and chooses one of our actions by its alias. */
async function rowAction(page: Page, name: string, id: string): Promise<void> {
  const row = await shownRow(page, name);
  // The menu's item, not the quick button of the same action that a row may show beside it.
  const item = page
    .locator(`umb-entity-action-list ${entityAction(id)}`)
    .filter({ visible: true })
    .first();
  // The row goes to the sidebar's middle before the hover that shows its "...", so no scroll takes the hover away: at
  // the sidebar's edge a row moves as the pointer reaches it, and the click lands on its neighbour (support/tree.ts).
  // A tree that has just loaded more rows can still move, so the hover and the menu's opening are tried again together
  // until the item shows.
  await expect(async () => {
    await row.evaluate((element) => element.scrollIntoView({ block: 'center' }));
    await row.hover();
    await row.locator('umb-entity-actions-dropdown uui-button').first().click({ timeout: 3_000 });
    await expect(item).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });
  await item.click();
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
  test(`the walk, ${comboName(combo)}`, async ({ page, browser, request, loginAs, apiAs }, testInfo) => {
    test.setTimeout(30 * 60_000);
    const api = await apiAs('admin');
    const label = comboName(combo);
    const made = await seed(api, request, label, testInfo.outputPath('import.json'));
    await dress(page, combo);
    await loginAs('admin');
    const walk = new Walk(page, join(walkDir(), label));

    // The section.
    await walk.shot('overview', async () => {
      await openSection(page);
      await expect(page.locator('jfb-overview [aria-busy="true"]')).toHaveCount(0);
    });
    // Privacy's People, the last screen that showed the stand-in until Phase 13 built it (ADR-0165).
    await walk.shot('people', async () => {
      await page.goto(`${SECTION}/workspace/jfb-privacy-people`);
      await expect(page.locator('jfb-people-screen input[type="email"]')).toBeVisible();
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
      await (await loadedIn(picker, picker.locator(`uui-menu-item[label="${made.targetName}"]`))).click();
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
    // A press on an option's handle opens its place in the list (10.4, ADR-0205).
    await walk.shot('option-place', async () => {
      const design = await openDesign(page, made.feedback);
      await select(page, design, fieldOf(feedback, 'dropdown'));
      const editor = design.locator('jfb-option-editor').first();
      await scrollTo(editor);
      await editor.locator('.option-row .row-handle').first().click();
      await expect(editor.locator('.row-place select')).toBeFocused();
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
    // After sending (10.1b): the list, a message's sidebar, and a redirect's with its rule, each with its preview (10.1c).
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
      await expect(outcomeSidebar.locator('jfb-outcome-preview .jfb-summary')).toBeVisible();
    });
    await walk.shot('settings-outcome-redirect', async () => {
      await openSettings(made.grouped);
      await openSettingsGroup('after');
      await settingsView.locator('jfb-outcome-list .row-open').first().click();
      await expect(outcomeSidebar.locator('#address')).toBeVisible();
      await expect(outcomeSidebar.locator('jfb-outcome-preview .said')).toBeVisible();
    });
    // The Actions view (10.2a): the cards by trigger, an action's sidebar, and a new action's.
    const actionsView = page.locator('jfb-form-actions-view');
    const actionSidebar = page.locator('jfb-action-modal');
    await walk.shot('actions', async () => {
      await page.goto(`${workspaceOf(made.withActions)}/view/actions`);
      await expect(actionsView.locator('jfb-action-list .card').first()).toBeVisible();
    });
    await walk.shot('action-sidebar', async () => {
      await page.goto(`${workspaceOf(made.withActions)}/view/actions`);
      await actionsView.locator('jfb-action-list .open').first().click();
      await expect(actionSidebar.locator('#name')).toBeVisible();
    });
    await walk.shot('action-new', async () => {
      await page.goto(`${workspaceOf(made.withActions)}/view/actions`);
      await actionsView.locator('#add').click();
      await expect(actionSidebar.locator('input[name="what-happens"]:checked')).toBeVisible();
    });
    // A new action of each other kind, with its own editor (10.2d, 10.2e): a webhook, content, the entry's state.
    for (const [kind, choice] of [
      ['action-webhook', 'webhook:0'],
      ['action-content', 'content'],
      ['action-set-state', 'setState'],
    ] as const) {
      await walk.shot(kind, async () => {
        await page.goto(`${workspaceOf(made.withActions)}/view/actions`);
        await actionsView.locator('#add').click();
        await actionSidebar.locator(`input[name="what-happens"][value="${choice}"]`).check();
        await expect(actionSidebar.locator('[id^="setting-"]').first()).toBeVisible();
      });
    }
    // The History view (10.3a, 10.3b): a form's versions, with its draft saves, one beside the current draft, two
    // compared, and an earlier version chosen from the form's "...".
    const historyView = page.locator('jfb-form-history-view');
    const comparison = page.locator('jfb-version-comparison-modal');
    const openHistory = async (key: string) => {
      await page.goto(`${workspaceOf(key)}/view/history`);
      await expect(historyView.locator('uui-toggle')).toBeVisible();
    };
    await walk.shot('history', async () => {
      await openHistory(made.feedback);
    });
    await walk.shot('history-versions', async () => {
      await openHistory(made.versioned);
      await expect(historyView.locator('tbody tr[data-number] button.name').first()).toBeVisible();
    });
    await walk.shot('history-drafts', async () => {
      await openHistory(made.versioned);
      await historyView.locator('uui-toggle').click();
      await expect(historyView.locator('tbody tr.draft').first()).toBeVisible();
    });
    await walk.shot('history-version', async () => {
      await openHistory(made.versioned);
      await historyView.locator('tbody tr[data-number] button.name').first().click();
      await expect(comparison.locator('.part').first()).toBeVisible();
    });
    await walk.shot('history-compare', async () => {
      await openHistory(made.versioned);
      const boxes = historyView.locator('tbody tr[data-number] uui-checkbox');
      await expect(boxes.nth(1)).toBeVisible();
      const count = await boxes.count();
      await boxes.nth(count - 1).click();
      await boxes.nth(count - 2).click();
      await historyView.locator('.toolbar uui-button').first().click();
      await expect(comparison.locator('.part').first()).toBeVisible();
    });
    await walk.shot('history-picker', async () => {
      await openDesign(page, made.versioned);
      await page.locator('umb-workspace-entity-action-menu uui-button').first().click();
      await page.locator(entityAction('RestoreEarlier')).filter({ visible: true }).first().click();
      await expect(page.locator('jfb-version-picker-modal .version').first()).toBeVisible();
    });
    // Publish with what changed since the published version (10.3d).
    await walk.shot('publish-changes', async () => {
      await openDesign(page, made.versioned);
      await page.locator(PUBLISH).click();
      await expect(page.locator('jfb-publish-modal .changes dd li').first()).toBeVisible();
    });
    // An action tested from its card (10.2b).
    await walk.shot('action-test', async () => {
      await page.goto(`${workspaceOf(made.withActions)}/view/actions`);
      await actionsView.locator('jfb-action-list uui-button.test').first().click();
      await expect(page.locator('umb-confirm-modal, uui-toast-notification').first()).toBeVisible();
    });
    // The preview (10.3e, 10.3f): the draft in its frame under the note, the "...", the sample answers, a test entry
    // sent, and Run actions...'s confirmation, which the walk confirms, so the Actions view has a last run to show.
    const previewModal = page.locator('jfb-preview-modal');
    const shownForm = page.frameLocator('jfb-preview-modal iframe').locator('form[data-jfb-form]');
    const openPreview = async () => {
      await openDesign(page, made.withActions);
      await page.locator(PREVIEW).click();
      await expect(shownForm).toBeVisible();
    };
    await walk.shot('preview', openPreview);
    await walk.shot('preview-menu', async () => {
      await openPreview();
      await previewModal.locator('.more').click();
      await expect(previewModal.locator('.menu [role="group"]').first()).toBeVisible();
    });
    await walk.shot('preview-sampled', async () => {
      await openPreview();
      await previewModal.locator('uui-button.fill').click();
      await expect(shownForm.locator('input[type="email"]').first()).not.toHaveValue('');
    });
    await walk.shot('preview-sent', async () => {
      await shownForm.locator('button[type="submit"]').last().click();
      await expect(previewModal.locator('.note[data-sent] uui-button')).toBeVisible({ timeout: 30_000 });
    });
    await walk.shot('preview-run-actions', async () => {
      await previewModal.locator('.note[data-sent] uui-button').click();
      await expect(page.locator('umb-confirm-modal li').first()).toBeVisible();
    });
    await walk.shot('action-runs', async () => {
      await page.locator('umb-confirm-modal uui-button[look="primary"]').click();
      await expect(previewModal.locator('.note[data-sent] uui-button')).toHaveCount(0);
      await page.goto(`${workspaceOf(made.withActions)}/view/actions`);
      await expect(actionsView.locator('jfb-action-list .run').first()).toBeVisible({ timeout: 30_000 });
    });
    await walk.shot('action-run', async () => {
      // The archive's run shows once its first try has failed; the view reads the runs as it opens.
      const details = actionsView.locator('jfb-action-list .run button.details').first();
      await expect(async () => {
        await page.goto(`${workspaceOf(made.withActions)}/view/actions`);
        await expect(details).toBeVisible({ timeout: 5_000 });
      }).toPass({ timeout: 90_000 });
      await details.click();
      await expect(page.locator('jfb-action-run-modal .part').first()).toBeVisible();
    });
    // The general settings (10.3b): how long draft saves are kept.
    await walk.shot('general-settings', async () => {
      await page.goto(`${SECTION}/workspace/jfb-settings-general`);
      await expect(page.locator('jfb-settings-general input').first()).toBeVisible();
    });
    // Phase 11: a form's entries, their menus, dialogs and sidebars, the entry panel and its own screen.
    const entriesView = page.locator('jfb-entries-view');
    const rows = entriesView.locator('tbody tr');
    await walk.shot('entries', async () => {
      await page.goto(`${workspaceOf(made.entries)}/view/entries`);
      await expect(rows.first()).toBeVisible({ timeout: 60_000 });
    });
    await walk.shot('entries-filter-menu', async () => {
      await entriesView.locator('uui-button[popovertarget="filter-menu"]').click();
      await expect(entriesView.locator('#filter-menu button').first()).toBeVisible();
    });
    await walk.shot('entries-filter-dialog', async () => {
      await entriesView.locator('#filter-menu button').first().click();
      await expect(page.locator('jfb-entry-filter-modal uui-checkbox').first()).toBeVisible();
    });
    await walk.shot('entries-chips', async () => {
      await page.locator('jfb-entry-filter-modal uui-checkbox').first().click();
      await page.locator('jfb-entry-filter-modal uui-button[look="primary"]').click();
      await expect(entriesView.locator('.chips')).toBeVisible();
    });
    await walk.shot('entries-export', async () => {
      await entriesView.locator('.chips uui-button').last().click();
      await expect(entriesView.locator('.chips')).toHaveCount(0);
      await entriesView.locator('.toolbar > uui-button:not([popovertarget])').first().click();
      await expect(page.locator('jfb-entry-export-modal uui-button[look="primary"]')).toBeVisible();
    });
    await walk.shot('entries-more', async () => {
      await page.keyboard.press('Escape');
      await expect(page.locator('jfb-entry-export-modal')).toHaveCount(0);
      await entriesView.locator('uui-button[popovertarget="more-menu"]').click();
      await expect(entriesView.locator('#more-menu button').first()).toBeVisible();
    });
    await walk.shot('entries-digest', async () => {
      await entriesView.locator('#more-menu button').first().click();
      await expect(page.locator('jfb-entry-digest-modal')).toBeVisible();
    });
    await walk.shot('entries-keys', async () => {
      await page.keyboard.press('Escape');
      await expect(page.locator('jfb-entry-digest-modal')).toHaveCount(0);
      await entriesView.locator('uui-button[popovertarget="more-menu"]').click();
      await entriesView.locator('#more-menu button').last().click();
      await expect(page.locator('jfb-entry-keys-modal')).toBeVisible();
    });
    await walk.shot('entries-bulk', async () => {
      await page.keyboard.press('Escape');
      await expect(page.locator('jfb-entry-keys-modal')).toHaveCount(0);
      await rows.nth(0).locator('uui-checkbox').click();
      await rows.nth(1).locator('uui-checkbox').click();
      await expect(entriesView.locator('.bulk')).toBeVisible();
    });
    const panel = entriesView.locator('jfb-entry-panel');
    await walk.shot('entry-panel', async () => {
      await rows.nth(0).locator('uui-checkbox').click();
      await rows.nth(1).locator('uui-checkbox').click();
      await expect(entriesView.locator('.bulk')).toHaveCount(0);
      await rows.first().locator('.name').click();
      await expect(panel.locator('h2')).toBeVisible();
    });
    await walk.shot('entry-edit', async () => {
      await panel.locator('.details-top uui-button').click();
      await expect(panel.locator('.edit-note')).toBeVisible();
    });
    await walk.shot('entry-timeline', async () => {
      await panel.locator('footer.bar uui-button').first().click();
      await expect(panel.locator('.edit-note')).toHaveCount(0);
      await panel.getByRole('tab').nth(1).click();
      await expect(panel.locator('.events')).toBeVisible();
    });
    await walk.shot('entry-notes', async () => {
      await panel.getByRole('tab').nth(2).click();
      await expect(panel.locator('uui-textarea, textarea').first()).toBeVisible();
    });
    await walk.shot('entry-more', async () => {
      await panel.locator('uui-button[popovertarget="panel-more"]').click();
      await expect(panel.locator('#panel-more button').first()).toBeVisible();
    });
    await walk.shot('entry-screen', async () => {
      await page.keyboard.press('Escape');
      const key = await rows.first().getAttribute('data-key');
      await page.goto(`${SECTION}/workspace/jfb-entry/edit/${key ?? ''}`);
      await expect(page.locator('jfb-entry-screen jfb-entry-panel h2')).toBeVisible({ timeout: 60_000 });
    });
    // The Inbox, its Form filter and Spam.
    await walk.shot('inbox', async () => {
      await page.goto(`${SECTION}/workspace/jfb-inbox`);
      await expect(rows.first()).toBeVisible({ timeout: 60_000 });
    });
    await walk.shot('inbox-forms', async () => {
      await entriesView.locator('uui-button[popovertarget="filter-menu"]').click();
      await entriesView.locator('#filter-menu button').first().click();
      await expect(page.locator('jfb-entry-forms-modal')).toBeVisible();
    });
    await walk.shot('spam', async () => {
      await page.keyboard.press('Escape');
      await expect(page.locator('jfb-entry-forms-modal')).toHaveCount(0);
      await page.goto(`${SECTION}/workspace/jfb-spam`);
      await expect(rows.first()).toBeVisible({ timeout: 60_000 });
    });
    // Insights: the Overview, its range, the Answers and their tables, Data collection and Forms at a glance.
    const insightsView = page.locator('jfb-form-insights-view');
    await walk.shot('insights', async () => {
      await page.goto(`${workspaceOf(made.insights)}/view/insights`);
      await expect(insightsView.locator('dl.stats')).toBeVisible({ timeout: 60_000 });
    });
    await walk.shot('insights-range', async () => {
      await insightsView.locator('.toolbar uui-button').first().click();
      await expect(insightsView.locator('#range button').first()).toBeVisible();
    });
    // "Show as tables" is the user's own and the looks share one admin, so each shot sets it rather than flips it.
    const tablesSwitch = insightsView.locator('.toolbar uui-toggle');
    const showTables = async (tables: boolean) => {
      if ((await tablesSwitch.evaluate((toggle) => (toggle as HTMLInputElement).checked)) !== tables)
        await tablesSwitch.click();
      const table = insightsView.locator('jfb-insight-answers table');
      if (tables) await expect(table.first()).toBeVisible();
      else await expect(table).toHaveCount(0);
    };
    await walk.shot('answers', async () => {
      await page.keyboard.press('Escape');
      await page.goto(`${workspaceOf(made.answers)}/view/insights`);
      await insightsView.locator('.modes button').nth(1).click();
      await expect(insightsView.locator('jfb-insight-answers .lead')).toBeVisible({ timeout: 60_000 });
      await showTables(false);
    });
    await walk.shot('answers-tables', async () => {
      await showTables(true);
    });
    await walk.shot('answers-filtered', async () => {
      await showTables(false);
      await insightsView.locator('uui-button[popovertarget="filter-menu"]').click();
      await insightsView.locator('#filter-menu button').first().click();
      await page.locator('jfb-entry-filter-modal uui-checkbox').first().click();
      await page.locator('jfb-entry-filter-modal uui-button[look="primary"]').click();
      await expect(insightsView.locator('.chips')).toBeVisible();
    });
    await walk.shot('settings-privacy', async () => {
      await openSettings(made.answers);
      await openSettingsGroup('privacy');
    });
    await walk.shot('data-collection', async () => {
      await page.goto(`${SECTION}/workspace/jfb-privacy-collection`);
      await expect(page.locator('jfb-settings-privacy #setting-insights')).toBeVisible({ timeout: 60_000 });
    });
    await walk.shot('glance', async () => {
      await page.goto(SECTION);
      const glance = page.locator('jfb-insight-glance');
      await expect(glance.locator('table')).toBeVisible({ timeout: 60_000 });
      await glance.scrollIntoViewIfNeeded();
    });
    // Phase 12: the library's lists and editors, the templates and Save as template, the settings with a site chosen,
    // Access and a folder's Permissions, Deliveries and the audit log with their details, and Replace from a file.
    for (const [shot, workspace, kind, view] of [
      ['option-lists', 'jfb-option-lists', 'option-lists', 'jfb-option-list-view'],
      ['themes', 'jfb-themes', 'themes', 'jfb-theme-view'],
      ['email-layouts', 'jfb-email-layouts', 'email-layouts', 'jfb-email-layout-view'],
    ] as const) {
      const screen = page.locator(`jfb-${kind}-screen`);
      await walk.shot(shot, async () => {
        await page.goto(`${SECTION}/workspace/${workspace}`);
        await expect(screen.locator('table')).toBeVisible({ timeout: 60_000 });
      });
      await walk.shot(`${shot}-editor`, async () => {
        await screen.locator('tbody a', { hasText: made.libraryName }).first().click();
        await expect(page.locator(view)).toBeVisible({ timeout: 60_000 });
        await expect(page.locator(`${view} [aria-busy="true"]`)).toHaveCount(0);
      });
    }
    await walk.shot('templates', async () => {
      await page.goto(`${SECTION}/workspace/jfb-templates`);
      await expect(page.locator('jfb-templates-screen .name', { hasText: made.libraryName })).toBeVisible({
        timeout: 60_000,
      });
    });
    await walk.shot('template-name', async () => {
      await openSection(page);
      await rowAction(page, made.blankName, 'SaveAsTemplate');
      await expect(page.locator('jfb-template-name-modal input').first()).toBeVisible();
    });
    for (const [shot, workspace, element] of [
      ['settings-email', 'jfb-settings-email', 'jfb-settings-email'],
      ['settings-spam', 'jfb-settings-spam', 'jfb-settings-protection'],
      ['settings-files', 'jfb-settings-files', 'jfb-settings-files'],
      ['settings-integrations', 'jfb-settings-integrations', 'jfb-settings-integrations'],
    ] as const) {
      await walk.shot(shot, async () => {
        await page.goto(`${SECTION}/workspace/${workspace}`);
        await expect(page.locator(`${element} uui-box[headline]`).first()).toBeVisible({ timeout: 60_000 });
      });
    }
    // A per-site section with a site chosen: each field the site sets says so, and the others take the value for all sites.
    await walk.shot('settings-site', async () => {
      await page.goto(`${SECTION}/workspace/jfb-settings-general`);
      const site = page.locator('jfb-settings-general select#site');
      await expect(site).toBeVisible({ timeout: 60_000 });
      await site.selectOption({ index: 1 });
      await expect(page.locator('jfb-settings-general uui-box[headline]').first()).toBeVisible();
    });
    await walk.shot('access', async () => {
      await page.goto(`${SECTION}/workspace/jfb-settings-access`);
      await expect(page.locator('jfb-access-screen table')).toBeVisible({ timeout: 60_000 });
    });
    await walk.shot('permissions', async () => {
      await openSection(page);
      await rowAction(page, made.folderName, 'Permissions');
      // A group added, so the sidebar shows its row of permissions.
      const sidebar = page.locator('jfb-permissions-modal');
      await sidebar.locator('select').first().selectOption({ index: 1 });
      await expect(sidebar.locator('table')).toBeVisible({ timeout: 30_000 });
    });
    await walk.shot('deliveries', async () => {
      await page.goto(`${SECTION}/workspace/jfb-deliveries`);
      await expect(page.locator('jfb-deliveries-screen tbody tr').first()).toBeVisible({ timeout: 60_000 });
    });
    await walk.shot('delivery', async () => {
      await page.locator('jfb-deliveries-screen tbody tr').first().getByRole('button').first().click();
      await expect(page.locator('jfb-delivery-modal .body')).toBeVisible();
    });
    await walk.shot('audit-log', async () => {
      await page.keyboard.press('Escape');
      await expect(page.locator('jfb-delivery-modal')).toHaveCount(0);
      await page.goto(`${SECTION}/workspace/jfb-audit-log`);
      await expect(page.locator('jfb-audit-screen tbody tr').first()).toBeVisible({ timeout: 60_000 });
    });
    await walk.shot('audit-entry', async () => {
      await page.locator('jfb-audit-screen tbody tr').first().getByRole('button').first().click();
      await expect(page.locator('jfb-audit-entry-modal .body')).toBeVisible();
    });
    await walk.shot('replace', async () => {
      await page.keyboard.press('Escape');
      await expect(page.locator('jfb-audit-entry-modal')).toHaveCount(0);
      await openSection(page);
      await rowAction(page, made.targetName, 'Replace');
      const modal = page.locator('jfb-import-modal');
      await modal.locator('input[type="file"]').setInputFiles(testInfo.outputPath('import.json'));
      await expect(modal.locator('uui-button[look="primary"]')).toBeEnabled();
    });

    // A save another took over (10.3c): the dialog, then Review both versions. The walk leaves the changes unsaved, so
    // it comes last of the admin's shots.
    await walk.shot('conflict', async () => {
      await openDesign(page, made.conflicted);
      // A sign-in of its own saves meanwhile: the one the walk seeded with no longer holds this late in the walk.
      await relabelFirst(await apiAs('admin'), made.conflicted, 'Name, saved meanwhile');
      await page
        .locator('jfb-form-workspace-editor umb-workspace-header-name-editable input')
        .fill(`Conflicted ${label}`);
      await page.locator(SAVE_DRAFT).click();
      await expect(page.locator('umb-confirm-modal')).toBeVisible();
    });
    await walk.shot('conflict-review', async () => {
      await page.locator('umb-confirm-modal uui-button[look="primary"]').click();
      await expect(page.locator('jfb-review-modal section.difference').first()).toBeVisible();
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
