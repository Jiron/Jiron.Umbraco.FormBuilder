import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { inLanguage } from '../../support/backoffice-look.ts';
import { API, contactForm, created, formOf, unique } from '../../support/forms-api.ts';
import { settle } from '../../support/ux-shots.ts';
import { splitAxe } from '../../support/ux-walk.ts';

// The section at its narrowest (ROADMAP 9.6a, ADR-0182). Umbraco's backoffice keeps a width of its own below a window
// of that width, and its 300 px tree beside the workspace, so on a phone or a tablet every section scrolls sideways as
// one page. The section adds no width to Umbraco's, and inside the workspace Umbraco leaves it nothing of its own is
// cut short or scrolls sideways: measured in German, whose words are the longest, on the Design view with a field
// selected, whose inspector then lies over the canvas with the element's tools in its header, on a folder's list, and
// in the template gallery, whose footer keeps its two buttons side by side inside the window. The form is live with a
// change waiting, so its header carries the longest state with the name, which keeps its room (ADR-0206, ADR-0213).

/**
 * What the page measures: its width, and inside the section's own elements the cut texts and sideways scrolls. A row of
 * the forms tree is Umbraco's own tree item with the section's label in it, which ends a long name with Umbraco's
 * ellipsis as its own trees do (ADR-0221), so the tree is Umbraco's here.
 */
const measure = (page: Page) =>
  page.evaluate(() => {
    const ours: Element[] = [];
    const collect = (root: Document | ShadowRoot, inside: boolean) => {
      for (const element of root.querySelectorAll('*')) {
        const name = element.tagName.toLowerCase();
        const mine = (inside || name.startsWith('jfb-')) && name !== 'jfb-library-tree-item';
        if (mine) ours.push(element);
        if (element.shadowRoot) collect(element.shadowRoot, mine);
      }
    };
    collect(document, false);
    const cut: string[] = [];
    const scrolls: string[] = [];
    for (const element of ours) {
      if (!(element instanceof HTMLElement) || element.scrollWidth <= element.clientWidth + 1) continue;
      const box = element.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) continue;
      const style = getComputedStyle(element);
      const text = (element.textContent ?? '').trim().replace(/\s+/g, ' ').slice(0, 50);
      if (style.textOverflow === 'ellipsis' && text) cut.push(text);
      else if (style.overflowX === 'auto' || style.overflowX === 'scroll') scrolls.push(element.tagName.toLowerCase());
    }
    return { width: document.documentElement.scrollWidth, cut, scrolls };
  });

/** Publishes the form as it is. */
async function publish(api: APIRequestContext, key: string): Promise<void> {
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
}

/** Publishes the form, then saves a change to its draft, so it waits as "Published (pending changes)". */
async function publishWithChange(api: APIRequestContext, key: string): Promise<void> {
  await publish(api, key);
  const read = await api.get(`${API}/form/${key}/draft`);
  expect(read.ok(), await read.text()).toBe(true);
  const draft = (await read.json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: { label?: Record<string, string> }[] }[] }[] };
  };
  const field = draft.definition.pages[0]?.groups[0]?.fields[0];
  if (field) field.label = { '': 'Ihr Vor- und Nachname' };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

for (const window of [
  { width: 375, height: 812 },
  { width: 768, height: 1024 },
]) {
  test(`at ${window.width} px the section is as wide as Umbraco's own, and nothing of its own is cut, in German`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const api = await apiAs('designer');
    const folderName = unique('Schmaler Ordner');
    const folder = await created(await api.post(`${API}/folder`, { data: { name: folderName } }), folderName);
    const key = await contactForm(api, unique('Schmal'), folder);
    await publishWithChange(api, key);
    const emptyName = unique('Leerer Ordner');
    const empty = await created(await api.post(`${API}/folder`, { data: { name: emptyName } }), emptyName);
    await inLanguage(page, 'de-DE');
    await page.setViewportSize(window);
    await loginAs('designer');

    // Umbraco's own section sets the width every section has at this window.
    await page.goto('/umbraco/section/content');
    await expect(page.locator('umb-section-sidebar')).toBeVisible();
    const umbraco = await page.evaluate(() => document.documentElement.scrollWidth);

    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}`);
    const design = page.locator('jfb-form-design-view');
    await design.locator('jfb-canvas-field [data-control="select"]').first().click();
    const inspector = design.locator('jfb-designer-inspector');
    await expect(inspector).toHaveAttribute('mode', 'overlay');
    await expect(inspector.locator('h2')).toBeVisible();

    // The longest state goes under the name rather than squeeze it, and the name's box keeps 6rem at least.
    const header = page.locator('jfb-form-workspace-editor');
    await expect(header.locator('uui-tag')).toHaveText('Ver\u00f6ffentlicht, Entwurf ge\u00e4ndert');
    const name = await header.locator('umb-workspace-header-name-editable uui-input').boundingBox();
    expect(name?.width ?? 0, "the name's box").toBeGreaterThanOrEqual(96);

    const found = await measure(page);
    expect(found.width, "the page's width, as Umbraco's own section has it").toBe(umbraco);
    expect(found.cut, 'texts cut short inside the section').toEqual([]);
    expect(found.scrolls, 'boxes that scroll sideways inside the section').toEqual([]);

    // Nothing of the section lies over Umbraco's header, its back button and the form's name: axe finds no target of
    // the section's too small to hit, as it did while the skip link waited above the view (ADR-0182).
    const targets = await new AxeBuilder({ page }).options({ ancestry: true }).withRules(['target-size']).analyze();
    const small = splitAxe(
      targets.violations.flatMap((violation) =>
        violation.nodes.map((node) => ({
          rule: violation.id,
          target: node.target,
          ancestry: (node as { ancestry?: unknown[] }).ancestry ?? [],
        })),
      ),
    ).ours;
    expect(small, 'targets too small to hit inside the section').toEqual([]);

    // Every tool of the element lies inside the inspector, its "..." too; the menu it opens is hidden till then.
    const frame = await inspector.boundingBox();
    const tools = await inspector.locator('.tools > button').evaluateAll((buttons) =>
      buttons.map((button) => {
        const box = button.getBoundingClientRect();
        return {
          left: box.left,
          right: box.right,
          name: button.getAttribute('aria-label') ?? button.textContent?.trim(),
        };
      }),
    );
    expect(tools.length).toBeGreaterThanOrEqual(4);
    for (const tool of tools) {
      expect(tool.left, `${tool.name}: its left edge`).toBeGreaterThanOrEqual((frame?.x ?? 0) - 0.5);
      expect(tool.right, `${tool.name}: its right edge`).toBeLessThanOrEqual(
        (frame?.x ?? 0) + (frame?.width ?? 0) + 0.5,
      );
    }

    // A folder's list: its columns as its own width allows, nothing cut.
    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-folder/edit/${folder}`);
    await expect(page.locator('jfb-folder-list table')).toBeVisible();
    const list = await measure(page);
    expect([list.width, list.cut, list.scrolls], "a folder's list").toEqual([umbraco, [], []]);

    // The template gallery: the footer's two buttons in one row, inside the window.
    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-folder/edit/${empty}`);
    await page.locator('jfb-folder-list uui-button[look="outline"]').click();
    const gallery = page.locator('jfb-template-gallery-modal');
    await expect(gallery.locator('[role="radio"]').first()).toBeVisible();
    const footerOf = () =>
      gallery.locator('[slot="actions"] uui-button').evaluateAll((buttons) =>
        buttons.map((button) => {
          const box = button.getBoundingClientRect();
          return { top: Math.round(box.top), right: box.right };
        }),
      );
    // Umbraco's sidebar slides in from the window's edge, so its footer is measured where it comes to rest.
    await expect
      .poll(async () => Math.max(...(await footerOf()).map((button) => button.right)), { message: 'the right edge' })
      .toBeLessThanOrEqual(window.width);
    const footer = await footerOf();
    expect(footer).toHaveLength(2);
    expect(footer[0]?.top, 'one row').toBe(footer[1]?.top);
    const shown = await measure(page);
    expect([shown.cut, shown.scrolls], 'the gallery').toEqual([[], []]);
  });
}

// At a phone's width Umbraco's page is 920 px wide and the window shows its first part (ADR-0182). The Settings and
// Actions views keep their column inside the window, an action's card offers Test in its "..." and puts its switch on a
// row of its own, and a rule's target takes a line of its own under what the rule does (16.6, ADR-0241). In German, whose
// words are the longest; measured by place, not by words.
test("at 375 px the Settings and Actions views keep to the window, a card's Test is in its menu, and a rule's target has its own line, in German", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const key = await contactForm(api, unique('Telefon'));
  const read = await api.get(`${API}/form/${key}/draft`);
  const draft = (await read.json()) as { form: { concurrencyVersion: number }; definition: Record<string, unknown> };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: {
        ...draft.definition,
        actions: [
          {
            key: crypto.randomUUID(),
            type: 'email',
            name: 'Das Team per E-Mail benachrichtigen',
            enabled: true,
            trigger: 'submitted',
            stopOnFailure: false,
            settings: { to: ['team@example.com'], subject: { '': 'Neuer Eintrag' }, body: { '': '<p>Neu.</p>' } },
          },
        ],
      },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await inLanguage(page, 'de-DE');
  await page.setViewportSize({ width: 375, height: 812 });
  await loginAs('designer');
  const right = async (locator: ReturnType<Page['locator']>) => {
    const box = await locator.boundingBox();
    return (box?.x ?? 0) + (box?.width ?? 0);
  };

  for (const view of ['settings', 'actions'] as const) {
    await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}/view/${view}`);
    const column = page.locator(`jfb-form-${view}-view .page`).first();
    await expect(column).toBeVisible({ timeout: 60_000 });
    // Panned to the workspace's start, where a phone's reader reads it (ADR-0241).
    await settle(page);
    expect(await right(column), `the ${view} view's column`).toBeLessThanOrEqual(375.5);
  }

  const card = page.locator('jfb-form-actions-view jfb-action-list .card').first();
  await expect(card).toBeVisible();
  expect(await right(card), 'the card').toBeLessThanOrEqual(375.5);
  await expect(card.locator('.end .test'), 'Test beside the name').toBeHidden();
  const name = await card.locator('.name').first().boundingBox();
  const end = await card.locator('.end').boundingBox();
  expect(end?.y ?? 0, 'the switch and "..." under the name').toBeGreaterThan((name?.y ?? 0) + (name?.height ?? 0) - 1);
  await card.locator('.more').click();
  await expect(page.locator('jfb-form-actions-view jfb-action-list .menu .test-item').first()).toBeVisible();
  await page.keyboard.press('Escape');

  // A field's rule: the builder's first line puts the target under what the rule does.
  await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await design.locator('jfb-canvas-field [data-control="select"]').first().click();
  const inspector = design.locator('jfb-designer-inspector');
  await expect(inspector).toHaveAttribute('mode', 'overlay');
  await inspector.getByRole('button', { name: 'Regel hinzuf\u00fcgen' }).click();
  const builder = page.locator('jfb-rule-builder-modal');
  const target = builder.locator('.first-line #target');
  await expect(target).toBeVisible();
  await settle(page);
  const line = await builder.locator('.first-line').boundingBox();
  const effect = await builder.locator('.first-line #effect').boundingBox();
  const own = await target.boundingBox();
  expect(own?.y ?? 0, 'the target under what the rule does').toBeGreaterThan(
    (effect?.y ?? 0) + (effect?.height ?? 0) - 1,
  );
  expect(own?.width ?? 0, 'the target takes the line').toBeGreaterThan((line?.width ?? 0) * 0.9);
  expect(await right(target), 'the target inside the window').toBeLessThanOrEqual(375.5);
});

// The form's header beside all its views, as the admin has them (the samples' Embed among them on this host): Umbraco's
// bar shares its row with the views' tabs. Umbraco 17 folds tabs into its "..." and leaves the name and the chip about
// 166 px at the narrowest; Umbraco 18.3 gives the tabs their whole width first and the header what is left. With the
// name's 6rem left, the chip goes under the name rather than under Umbraco's "..." beside it, and both stay inside the
// bar (ADR-0213); with less, the name takes what there is and the chip is cut with an ellipsis, its words its title, and
// nothing lies past the header's box or makes Umbraco's page any wider (ADR-0250).
for (const width of [375, 768, 1024, 1440]) {
  test(`at ${width} px the header keeps the name and the state inside Umbraco's bar beside the views, in German`, async ({
    page,
    loginAs,
    apiAs,
  }) => {
    const api = await apiAs('admin');
    const published = await contactForm(api, unique('Kopf'));
    await publish(api, published);
    const pending = await contactForm(api, unique('Kopf'));
    await publishWithChange(api, pending);
    await inLanguage(page, 'de-DE');
    await page.setViewportSize({ width, height: 900 });
    await loginAs('admin');

    const editor = page.locator('jfb-form-workspace-editor');
    // Phase 11's two views, which only a user with all six has.
    for (const [key, state, view] of [
      [published, 'Ver\u00f6ffentlicht', 'entries'],
      [pending, 'Ver\u00f6ffentlicht, Entwurf ge\u00e4ndert', 'insights'],
    ] as const) {
      await page.goto(`/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}/view/${view}`);
      await expect(editor.locator(view === 'entries' ? 'jfb-entries-view' : 'jfb-form-insights-view')).toBeVisible();
      await expect(editor.locator('.header[slot="header"] > uui-tag')).toHaveText(state);
      // The chip settles where its line puts it once the header has its width.
      await page.waitForTimeout(300);
      const [bar, own, name, chip] = await Promise.all([
        editor.locator('umb-body-layout #header').first().boundingBox(),
        editor.locator('.header[slot="header"]').boundingBox(),
        editor.locator('umb-workspace-header-name-editable uui-input').boundingBox(),
        editor.locator('.header[slot="header"] > uui-tag').boundingBox(),
      ]);
      if (!bar || !own || !name || !chip) throw new Error(`${state}: the header's parts`);
      const cramped = /\bcramped\b/.test((await editor.locator('.header[slot="header"]').getAttribute('class')) ?? '');
      expect(cramped, `${state}: cramped exactly where Umbraco leaves the header less than the name's 6rem`).toBe(
        own.width < 96,
      );
      if (cramped) {
        // The header clips its parts to its box, so nothing of it lies over Umbraco's menu or the tabs.
        expect(
          await editor.locator('.header[slot="header"]').evaluate((element) => getComputedStyle(element).overflow),
          `${state}: the header clips its parts`,
        ).toBe('hidden');
        await expect(editor.locator('.header[slot="header"] > uui-tag')).toHaveAttribute('title', state);
      } else {
        expect(name.width, `${state}: the name's box`).toBeGreaterThanOrEqual(96);
        await expect(editor.locator('.header[slot="header"] > uui-tag')).not.toHaveAttribute('title');
        expect(chip.x, `${state}: the chip's left edge`).toBeGreaterThanOrEqual(own.x - 0.5);
        expect(chip.x + chip.width, `${state}: the chip's right edge, short of Umbraco's "..."`).toBeLessThanOrEqual(
          own.x + own.width + 0.5,
        );
      }
      for (const [part, box] of [
        ['the name', name],
        ['the chip', chip],
      ] as const) {
        expect(box.y, `${state}: ${part}'s top, inside the bar`).toBeGreaterThanOrEqual(bar.y);
        expect(box.y + box.height, `${state}: ${part}'s bottom, inside the bar`).toBeLessThanOrEqual(
          bar.y + bar.height,
        );
      }
      // At the narrowest neither state fits beside the name's 6rem, so the chip is under it.
      if (width < 1024 || cramped)
        expect(chip.y, `${state}: the chip under the name`).toBeGreaterThanOrEqual(name.y + name.height);
      const found = await measure(page);
      // Only a cramped header's state may be cut, and only with its words as its title.
      expect(
        found.cut.filter((text) => !(cramped && text === state)),
        `${state}: texts cut short inside the section`,
      ).toEqual([]);
      // Umbraco's page is never narrower than 920 px; the header makes it no wider.
      expect(found.width, `${state}: the page no wider than Umbraco's own`).toBeLessThanOrEqual(Math.max(width, 920));
    }
  });
}
