import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { FRESH_BASE_URL, FRESH_PORT } from '../../support/paths.ts';
import { ClickCounter, expectTaskClicks } from '../../support/top-tasks.ts';
import { readSecrets } from '../../support/users.ts';
import { onTab, openPlace, openTab } from '../../support/places.ts';

// The setup checklist end to end (ROADMAP 12.6, 9.1, ADR-0165): the demo reset and started as a new site has it, without
// its content (Demo:Seed off), without the test team, and with Umbraco's From under a display name, which forms' mail
// cannot use as its sender, so all five steps are open. The admin follows each step from the Overview as its button
// leads, does what the step asks, and sees it turn green; once all five are done the checklist stays away, and asked
// for again it shows five of five. Putting the form on a page is the administrator's top task 5 (docs/ux/top-tasks.md),
// whose clicks it counts (ROADMAP 16.6).

const SECTION = '/umbraco/section/jfb-forms';

test.use({ baseURL: FRESH_BASE_URL });

let host: Host | undefined;

test.beforeAll(async () => {
  test.setTimeout(600_000);
  const env = hostEnvironment(readSecrets(), { deliveryApi: false });
  // No team but the administrators: the test people's groups, folders and users stay out.
  for (const key of Object.keys(env))
    if (/^JironFormBuilderTesting__(UserGroups|RestrictedFolders|Users)__/.test(key)) delete env[key];
  host = await startHost({
    name: `host-${FRESH_PORT}`,
    port: FRESH_PORT,
    readyPage: '/umbraco',
    readyLines: ['Demo seeding finished'],
    env: {
      ...env,
      Demo__Seed: 'false',
      // The shared host runs the stub already.
      JironFormBuilderTesting__StubPort: '0',
      Umbraco__CMS__Global__Smtp__From: 'Lindenhall Library <library@lindenhall.example>',
    },
  });
  await waitForStartupWork(host.logFile);
});

test.afterAll(async () => {
  await host?.stop();
});

/** The Overview, with its checklist read. */
async function overview(page: Page): Promise<Locator> {
  await page.goto(SECTION);
  const shown = page.locator('jfb-overview');
  await expect(shown.locator('ol li')).toHaveCount(5, { timeout: 60_000 });
  return shown;
}

/** A step of the checklist, by its name. */
const stepOf = (shown: Locator, name: string) => shown.locator('ol li', { hasText: name });

/** Opens the Overview and sees the step done, and how many are. */
async function turnedGreen(page: Page, name: string, done: number): Promise<void> {
  const shown = await overview(page);
  await expect(stepOf(shown, name).locator('.mark--done')).toBeVisible();
  await expect(shown.locator('.count')).toHaveText(`${String(done)} of 5 done`);
}

test('the setup checklist, followed from the Overview, turns each step green', async ({ page, loginAs }) => {
  test.setTimeout(600_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');

  let shown = await overview(page);
  await expect(shown.locator('.mark--done')).toHaveCount(0);
  await expect(shown.locator('.count')).toHaveText('0 of 5 done');

  // Check email sending: the email settings, where the sender's address is what is missing.
  await stepOf(shown, 'Check email sending').getByRole('link', { name: 'Open email settings' }).click();
  const email = page.locator('jfb-settings-email');
  await expect(email.locator('uui-box[headline]').first()).toBeVisible({ timeout: 60_000 });
  // The link lands on its tab, marked, with its place marked in the sidebar (ADR-0257).
  await onTab(page, 'Settings', 'Email');
  await expect.poll(() => openTab(page)).toBe('Email');
  await expect.poll(() => openPlace(page)).toBe('Settings');
  await email.getByRole('textbox', { name: 'Address', exact: true }).first().fill('forms@jfb.test');
  await email.getByRole('button', { name: 'Save email settings', exact: true }).click();
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'The settings are saved.' })).toBeVisible();
  await turnedGreen(page, 'Check email sending', 1);

  // Create a form from a template: the gallery, whose form opens.
  shown = await overview(page);
  await stepOf(shown, 'Create a form from a template').getByRole('button', { name: 'Choose a template\u2026' }).click();
  const gallery = page.locator('jfb-template-gallery-modal');
  await gallery.locator('[role="radio"]').first().click();
  await gallery.locator('uui-button[look="primary"]').click();
  await expect(page).toHaveURL(/workspace\/jfb-form\/edit\/[0-9a-f-]{36}$/, { timeout: 60_000 });
  await turnedGreen(page, 'Create a form from a template', 2);

  // Preview and send a test entry: the form opens, its preview fills in sample answers and sends them.
  shown = await overview(page);
  await stepOf(shown, 'Preview and send a test entry').getByRole('link', { name: 'Open the form' }).click();
  await expect(page.locator('jfb-form-design-view jfb-form-canvas')).toBeVisible({ timeout: 60_000 });
  await page.locator('[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.Preview"]').click();
  const preview = page.locator('jfb-preview-modal');
  const form = page.frameLocator('jfb-preview-modal iframe').locator('form[data-jfb-form]');
  await expect(form).toBeVisible({ timeout: 60_000 });
  await preview.locator('uui-button.fill').click();
  await form.locator('button[type="submit"]').last().click();
  await expect(preview.locator('.note[data-sent]')).toBeVisible({ timeout: 60_000 });
  await page.keyboard.press('Escape');
  await turnedGreen(page, 'Preview and send a test entry', 3);

  // Put the form on a page: the dialog creates the form block type.
  shown = await overview(page);
  const block = new ClickCounter();
  await block.click(
    stepOf(shown, 'Put the form on a page').getByRole('button', { name: 'Create a form block type\u2026' }),
  );
  const dialog = page.locator('jfb-form-block-modal');
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  // The dialog's Create is the task's confirming button.
  expectTaskClicks(block, 'Administrator', 5);
  await expect(dialog).toContainText('One step is left');
  await dialog.getByRole('button', { name: 'Close' }).click();
  await expect(stepOf(page.locator('jfb-overview'), 'Put the form on a page').locator('.mark--done')).toBeVisible();
  await turnedGreen(page, 'Put the form on a page', 4);

  // Choose who sees entries: Access gives the editors View entries, and names them as a group that can't open the
  // section yet, with a link to their user group, where the section is added.
  shown = await overview(page);
  await stepOf(shown, 'Choose who sees entries').getByRole('link', { name: 'Open access settings' }).click();
  const access = page.locator('jfb-access-screen');
  const matrix = access.getByRole('region', { name: 'What each user group may do with every form' });
  await expect(matrix).toBeVisible({ timeout: 60_000 });
  await onTab(page, 'Settings', 'Access');
  await expect.poll(() => openTab(page)).toBe('Access');
  await expect.poll(() => openPlace(page)).toBe('Settings');
  await matrix.getByRole('checkbox', { name: 'View entries, Editors' }).check();
  await access.getByRole('button', { name: 'Save access', exact: true }).click();
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'Access is saved.' })).toBeVisible();
  await expect(access.getByText("can't open Form Builder yet")).toBeVisible();
  await access.locator('ul.groups-without a', { hasText: 'Editors' }).click();
  await addTheSection(page);

  // The last step done, setup is complete: the checklist stays away, and asked for again it shows every step done.
  await page.goto(SECTION);
  shown = page.locator('jfb-overview');
  await expect(shown.locator('uui-box').first()).toBeVisible({ timeout: 60_000 });
  await expect(shown.locator('ol li')).toHaveCount(0);
  await shown.locator('umb-dropdown').click();
  await page.locator('uui-menu-item[label="Show the setup checklist"]').filter({ visible: true }).click();
  await expect(shown.locator('ol .mark--done')).toHaveCount(5);
  await expect(shown.locator('.count')).toHaveText('5 of 5 done');
});

/** Adds the Form Builder section to the user group open in Umbraco's own editor, and saves it. */
async function addTheSection(page: Page): Promise<void> {
  const editor = page.locator('umb-user-group-workspace-editor');
  await expect(editor).toBeVisible({ timeout: 60_000 });
  await editor.locator('umb-input-section').getByRole('button', { name: 'Choose' }).click();
  const picker = page.locator('umb-section-picker-modal');
  await picker.getByText('Form Builder', { exact: true }).click();
  await picker.getByRole('button', { name: /^(Choose|Submit)$/ }).click();
  // The save is done when Umbraco has answered it: a toast that says "saved" may still be Access's own from a moment
  // ago, and leaving on that one cuts the group's save off (a busy run did, and the last step stayed open).
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().includes('/management/api/v1/user-group/'),
  );
  await page.locator('umb-workspace-action').getByRole('button', { name: 'Save' }).click();
  expect((await saved).ok(), "the user group's save").toBe(true);
}
