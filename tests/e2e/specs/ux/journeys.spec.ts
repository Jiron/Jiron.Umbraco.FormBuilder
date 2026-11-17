import { randomUUID } from 'node:crypto';
import { join } from 'node:path';
import type { Browser, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { fillForm, press, submitForm } from '../../support/form.ts';
import { API, created, headlessPersonForm, sendAsVisitor, unique } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';
import { addFieldOf, openPasteQuestions } from '../../support/picker.ts';
import { openRowActions } from '../../support/tree.ts';
import { dress, Walk } from '../../support/ux-shots.ts';
import { COMBOS, comboName, walkDir, wantedCombo, type Combo } from '../../support/ux-walk.ts';
import type { Role } from '../../support/users.ts';

// The journeys of the UX polish pass (ROADMAP 16.6): five people's work from start to end, each step shot in the twelve
// ways of support/ux-walk.ts with what axe and the layout measures found, beside the walk's screens, whose contact
// sheets show them too. An editor builds and publishes a form of two pages with a rule and an action; an entry handler
// works through the Inbox; an administrator sets permissions and retention; a visitor fills in the job application on a
// phone (at 375 px only, in the site's light or dark scheme); and a privacy officer erases a person's data. Each journey
// signs in as its own persona, in German as the admin's German twin where the server writes in the user's language.
// Like the walk, it finds its way by tags, classes and data marks, not words, and keeps what it changes for the whole
// site unsaved. It runs only when asked: JFB_E2E_UX=1 npx playwright test --project=ux (JFB_UX_ONLY, JFB_UX_LOOKS).

const SECTION = '/umbraco/section/jfb-forms';
const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;
const screenOf = (entityType: string) => `${SECTION}/workspace/${entityType}`;
const entityAction = (id: string) => `[data-mark="entity-action:Jiron.FormBuilder.EntityAction.${id}"]`;
const createOption = (icon: string) => `umb-entity-create-option-action-list-modal umb-ref-item[icon="${icon}"]`;
const PUBLISH = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.Publish"]';
const SAVE_DRAFT = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.SaveDraft"]';
const KEY_IN_URL = /workspace\/jfb-form\/edit\/([0-9a-f-]{36})/;

/** A page of its own for a persona, dressed in the combination and signed in. */
async function personaPage(
  browser: Browser,
  combo: Combo,
  role: Role,
  loginAs: (role: Role, page?: Page) => Promise<void>,
): Promise<Page> {
  const page = await (await browser.newContext()).newPage();
  await dress(page, combo);
  await loginAs(role, page);
  return page;
}

/** Adds the lines as questions through the field picker's Paste questions, from the page's Add field. */
async function pasteQuestions(page: Page, design: Locator, lines: readonly string[]): Promise<void> {
  const modal = await openPasteQuestions(page, addFieldOf(design));
  await modal.locator('textarea').fill(lines.join('\n'));
  await modal.locator('uui-button[look="primary"]').click();
  await expect(modal).toHaveCount(0);
}

/** Selects the canvas's field at this place on the page it shows, and waits for its inspector. */
async function selectField(design: Locator, index: number): Promise<void> {
  const field = design.locator('jfb-canvas-field [data-control="select"]').nth(index);
  await field.scrollIntoViewIfNeeded();
  await field.click();
  await expect(design.locator('jfb-designer-inspector h2')).toBeVisible();
}

/** Opens an inspector section by its id, unless it is open. */
async function openInspectorSection(design: Locator, id: string): Promise<void> {
  const head = design.locator(`jfb-designer-inspector [data-section="${id}"] .section-head button`);
  await head.scrollIntoViewIfNeeded();
  if ((await head.getAttribute('aria-expanded')) !== 'true') await head.click();
}

for (const combo of COMBOS.filter((each) => wantedCombo(each))) {
  test(`the journeys, ${comboName(combo)}`, async ({ browser, request, loginAs, apiAs }) => {
    test.setTimeout(30 * 60_000);
    const label = comboName(combo);
    const admin = await apiAs('admin');
    const walk = new Walk(await browser.newPage(), join(walkDir(), label));
    const german = combo.language === 'de';

    // 1. An editor builds and publishes a form of two pages, with a rule and an action.
    const editor = await personaPage(browser, combo, 'designer', loginAs);
    const design = editor.locator('jfb-form-design-view');
    let formKey = '';
    await walk.shot(
      'j1-1-new-form',
      async () => {
        await editor.goto(SECTION);
        await editor
          .locator(`umb-section-sidebar ${entityAction('Create')}`)
          .first()
          .click();
        await editor.locator(createOption('icon-document')).click();
        await expect(editor).toHaveURL(KEY_IN_URL, { timeout: 60_000 });
        formKey = KEY_IN_URL.exec(editor.url())?.[1] ?? '';
        await expect(design.locator('jfb-form-canvas :is(.bo-empty-group, .bo-add-field)').first()).toBeVisible();
      },
      editor,
    );
    await walk.shot(
      'j1-2-questions',
      async () => {
        const name = editor.locator('jfb-form-workspace-editor umb-workspace-header-name-editable input').first();
        await name.fill(german ? `Lesekreis ${label}` : `Reading circle ${label}`);
        await pasteQuestions(
          editor,
          design,
          german
            ? ['Ihr Name', 'E-Mail-Adresse', 'Haben Sie schon einmal mitgelesen?', 'Welches Buch schlagen Sie vor?']
            : ['Your name', 'Email address', 'Have you joined a circle before?', 'Which book would you suggest?'],
        );
        await expect(design.locator('jfb-canvas-field')).toHaveCount(4);
      },
      editor,
    );
    await walk.shot(
      'j1-3-second-page',
      async () => {
        await design.locator('jfb-form-canvas .bo-add-page').first().click();
        await expect(design.locator('jfb-form-canvas .bo-tab[data-page]')).toHaveCount(2);
        await pasteQuestions(editor, design, german ? ['Wann passt es Ihnen?'] : ['When suits you?']);
        await expect(design.locator('jfb-canvas-field')).toHaveCount(1);
        // One question pasted is selected to be edited; over a narrow canvas its inspector lies on top, and is closed.
        const close = design.locator('jfb-designer-inspector uui-button:has(uui-icon[name="icon-wrong"])').first();
        if (await close.isVisible()) await close.click();
      },
      editor,
    );
    await walk.shot(
      'j1-4-rule',
      async () => {
        await design.locator('jfb-form-canvas .bo-tab[data-page] .bo-tab-name').first().click();
        await selectField(design, 3);
        await openInspectorSection(design, 'rules');
        await design.locator('jfb-designer-inspector .add-rule').first().click();
        const builder = editor.locator('jfb-rule-builder-modal');
        await builder.locator('select.subject').first().selectOption({ index: 3 });
        // The question pasted as "Have you joined a circle before?" is a yes or no question, whose answers the builder
        // lists (ADR-0241 decision 10); a question of text takes a typed value.
        const value = builder.locator('.value > :is(select:not(.source), input)').first();
        await expect(value).toBeVisible();
        if ((await value.evaluate((element) => element.localName)) === 'select') {
          await value.selectOption({ value: 'yes' });
          await expect(value).toHaveValue('yes');
        } else await value.fill(german ? 'Ja' : 'Yes');
      },
      editor,
    );
    await walk.shot(
      'j1-5-rule-added',
      async () => {
        const builder = editor.locator('jfb-rule-builder-modal');
        await builder.locator('uui-button[look="primary"]').click();
        await expect(builder).toHaveCount(0);
        const saved = editor.waitForResponse(
          (response) => response.url().endsWith(`/form/${formKey}/draft`) && response.request().method() === 'PUT',
        );
        await editor.locator(SAVE_DRAFT).click();
        expect((await saved).ok()).toBe(true);
      },
      editor,
    );
    await walk.shot(
      'j1-6-action',
      async () => {
        await editor.goto(`${workspaceOf(formKey)}/view/actions`);
        const view = editor.locator('jfb-form-actions-view');
        await view.locator('#add').click();
        const sidebar = editor.locator('jfb-action-modal');
        await expect(sidebar.locator('input[name="what-happens"]:checked')).toBeVisible();
        await sidebar.locator('details.add summary').first().click();
        const address = sidebar.locator('details.add[open] .add-panel input').first();
        await address.fill('circle@lindenhall.example');
        await address.press('Enter');
        // The preview asks again once the changes pause; the shot waits until it names the address.
        await expect(sidebar.locator('jfb-email-preview .meta')).toContainText('circle@lindenhall.example', {
          timeout: 15_000,
        });
      },
      editor,
    );
    await walk.shot(
      'j1-7-actions',
      async () => {
        const sidebar = editor.locator('jfb-action-modal');
        await sidebar.locator('uui-button[look="primary"]').click();
        await expect(sidebar).toHaveCount(0);
        await expect(editor.locator('jfb-form-actions-view jfb-action-list .card').first()).toBeVisible();
      },
      editor,
    );
    await walk.shot(
      'j1-8-publish',
      async () => {
        await editor.locator(PUBLISH).click();
        await expect(editor.locator('jfb-publish-modal .said')).not.toBeEmpty();
      },
      editor,
    );
    await walk.shot(
      'j1-9-published',
      async () => {
        const dialog = editor.locator('jfb-publish-modal');
        // The publish answers after the dialog closes; a reload before that would cut it off.
        const published = editor.waitForResponse(
          (response) => response.url().endsWith(`/form/${formKey}/publish`) && response.request().method() === 'POST',
        );
        await dialog.locator('uui-button[look="primary"]').click();
        expect((await published).ok()).toBe(true);
        await expect(dialog).toHaveCount(0);
        await editor.goto(workspaceOf(formKey));
        await expect(design.locator('jfb-canvas-field').first()).toBeVisible({ timeout: 60_000 });
      },
      editor,
    );
    await editor.context().close();

    // 2. An entry handler works through the Inbox: opens it, reads an entry, approves it, leaves a note, ticks two.
    const inboxForm = `UX journey ${label} inbox ${Date.now().toString(36)}`;
    const seeded = await admin.post('/jfb-testing/entries-form', {
      data: { count: 5, name: inboxForm, url: `${STUB_URL}/approved/${randomUUID()}` },
      timeout: 120_000,
    });
    expect(seeded.ok(), await seeded.text()).toBe(true);
    const handler = await personaPage(browser, combo, 'entryHandler', loginAs);
    const view = handler.locator('jfb-entries-view');
    const rows = view.locator('tbody tr').filter({ has: handler.locator('td.form-column', { hasText: inboxForm }) });
    const panel = view.locator('jfb-entry-panel');
    await walk.shot(
      'j2-1-inbox',
      async () => {
        await handler.goto(SECTION);
        await handler.locator('jfb-inbox-menu-item uui-menu-item').click();
        await expect(rows.first()).toBeVisible({ timeout: 60_000 });
      },
      handler,
    );
    await walk.shot(
      'j2-2-entry',
      async () => {
        await rows.first().locator('.name').click();
        await expect(panel.locator('h2')).toBeVisible();
      },
      handler,
    );
    await walk.shot(
      'j2-3-approved',
      async () => {
        await panel.locator('footer.bar uui-button[look="primary"]').click();
        await expect(handler.locator('jfb-undo-notification').last()).toBeVisible();
      },
      handler,
    );
    await walk.shot(
      'j2-4-note',
      async () => {
        await panel.locator('.tabs uui-tab').nth(2).click();
        await panel
          .locator('textarea')
          .first()
          .fill(german ? 'Bitte morgen zur\u00fcckrufen.' : 'Please call back tomorrow.');
      },
      handler,
    );
    await walk.shot(
      'j2-5-ticked',
      async () => {
        await panel.locator('header uui-button:has(uui-icon[name="icon-wrong"])').click();
        await expect(panel).toHaveCount(0);
        for (const index of [1, 2]) await rows.nth(index).locator('uui-checkbox').click();
        await expect(view.locator('.bulk')).toBeVisible();
      },
      handler,
    );
    await handler.context().close();

    // 3. An administrator sets permissions and retention; what the whole site reads stays unsaved.
    const folderName = `UX journey ${label} folder ${Date.now().toString(36)}`;
    const folder = await created(await admin.post(`${API}/folder`, { data: { name: folderName } }), folderName);
    const keeper = await personaPage(browser, combo, german ? 'adminDe' : 'admin', loginAs);
    await walk.shot(
      'j3-1-access',
      async () => {
        await keeper.goto(screenOf('jfb-settings-access'));
        const matrix = keeper.locator('jfb-access-screen table');
        await expect(matrix).toBeVisible({ timeout: 60_000 });
        await matrix.locator('input[type="checkbox"]:not(:disabled):not(:checked)').first().check();
      },
      keeper,
    );
    await walk.shot(
      'j3-2-folder-permissions',
      async () => {
        await keeper.goto(SECTION);
        await openRowActions(keeper, folderName);
        await keeper
          .locator(`umb-entity-action-list ${entityAction('Permissions')}`)
          .filter({ visible: true })
          .first()
          .click();
        const sidebar = keeper.locator('jfb-permissions-modal');
        await sidebar.locator('select').first().selectOption({ index: 1 });
        await expect(sidebar.locator('table')).toBeVisible({ timeout: 30_000 });
        await sidebar.locator('table input[type="checkbox"]').first().check();
        await sidebar.getByRole('switch').first().check();
      },
      keeper,
    );
    await walk.shot(
      'j3-3-folder-saved',
      async () => {
        const sidebar = keeper.locator('jfb-permissions-modal');
        await sidebar.locator('uui-button[look="primary"]').click();
        await expect(sidebar).toHaveCount(0);
        await expect(keeper.locator('uui-toast-notification').last()).toBeVisible();
      },
      keeper,
    );
    await walk.shot(
      'j3-4-retention',
      async () => {
        await keeper.goto(screenOf('jfb-privacy-retention'));
        const screen = keeper.locator('jfb-retention-screen');
        await expect(screen.locator('uui-box').first()).toBeVisible({ timeout: 60_000 });
        await screen.locator('uui-box').first().locator('input').nth(2).fill('30');
      },
      keeper,
    );
    await keeper.context().close();
    await admin.post(`${API}/folder/${folder}/trash`);

    // 4. A visitor fills in the job application on a phone: an empty page's messages, three pages, the thank-you.
    if (combo.width === 375) {
      const context = await browser.newContext({
        viewport: { width: combo.width, height: combo.height },
        colorScheme: combo.theme,
        isMobile: true,
        hasTouch: true,
      });
      const visitor = await context.newPage();
      const application = formPages().find((shown) => shown.form === 'application');
      expect(application, "the demo's job application").toBeTruthy();
      const form = visitor.locator('main form.jfb-form');
      await walk.shot(
        'j4-1-application',
        async () => {
          await visitor.goto(pathOf(application!.texts[combo.language], combo.language));
          await form.scrollIntoViewIfNeeded();
          await expect(form).toBeVisible();
        },
        visitor,
      );
      await walk.shot(
        'j4-2-messages',
        async () => {
          await press(form, 'next');
          await expect(form.locator('.jfb-summary, [role="alert"]').first()).toBeVisible();
        },
        visitor,
      );
      await walk.shot(
        'j4-3-references',
        async () => {
          await form.locator('[name="fullName.first"]').fill('Ada');
          await form.locator('[name="fullName.last"]').fill('Lovelace');
          await fillForm(form, { email: 'ada@visitor.jfb.test', post: 'assistant' });
          await press(form, 'next');
          await expect(form.locator('[name="referees[0].refName"]')).toBeVisible();
        },
        visitor,
      );
      await walk.shot(
        'j4-4-letter',
        async () => {
          await fillForm(form, {
            'referees[0].refName': 'Grace Hopper',
            'referees[0].refContact': 'grace@visitor.jfb.test',
            'referees[1].refName': 'Mary Somerville',
            'referees[1].refContact': '+44 20 7946 0000',
          });
          await press(form, 'next');
          await expect(form.locator('[name="letter"]')).toBeVisible();
        },
        visitor,
      );
      await walk.shot(
        'j4-5-sent',
        async () => {
          await fillForm(form, { letter: 'I mend books on Saturdays.', privacy: true });
          const sent = await submitForm(form);
          expect(sent.state, 'sent').toBe('sent');
        },
        visitor,
      );
      await context.close();
    }

    // 5. A privacy officer finds a person by address and erases their entries.
    const personForm = await headlessPersonForm(admin, unique(`UX journey ${label} people`));
    const address = `ux-${label}-${Date.now().toString(36)}@example.com`;
    await sendAsVisitor(request, personForm, { name: 'Lena Weber', email: address });
    await sendAsVisitor(request, personForm, { name: 'Lena Weber', email: address });
    const officer = await personaPage(browser, combo, 'privacyOfficer', loginAs);
    const people = officer.locator('jfb-people-screen');
    await walk.shot(
      'j5-1-people',
      async () => {
        await officer.goto(screenOf('jfb-privacy-people'));
        await expect(people.locator('#email')).toBeVisible({ timeout: 60_000 });
      },
      officer,
    );
    await walk.shot(
      'j5-2-found',
      async () => {
        await people.locator('#email').fill(address);
        await people.locator('#email').press('Enter');
        await expect(people.locator('.actions uui-button').first()).toBeVisible({ timeout: 60_000 });
      },
      officer,
    );
    await walk.shot(
      'j5-3-erase',
      async () => {
        await people.locator('.actions uui-button').last().click();
        await expect(officer.locator('jfb-people-erase-modal uui-button[look="primary"]')).toBeVisible();
      },
      officer,
    );
    await walk.shot(
      'j5-4-erased',
      async () => {
        const dialog = officer.locator('jfb-people-erase-modal');
        await dialog.locator('uui-button[look="primary"]').click();
        await expect(dialog).toHaveCount(0);
        await expect(people.locator('.actions')).toHaveCount(0, { timeout: 60_000 });
      },
      officer,
    );
    await officer.context().close();

    walk.write(combo, 'journeys.json');
    expect(walk.reports.filter((report) => report.error).map((report) => `${report.screen}: ${report.error}`)).toEqual(
      [],
    );
  });
}
