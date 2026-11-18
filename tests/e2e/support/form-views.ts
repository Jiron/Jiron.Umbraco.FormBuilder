import { expect, type Locator, type Page } from '@playwright/test';

// A form's views as ROADMAP 22.3 draws them (ADR-0258), so no spec knows their markup: the Settings view's groups as a
// list to pick from and the group picked beside it (or in its place, where only one fits), the inspector's sections,
// and "Add action", which lists the kinds before the sheet of the kind picked.

// ---- The Settings view ----------------------------------------------------------------------------------------------

/** The Settings view's groups, in their order. */
export const SETTINGS_GROUPS = [
  'submission',
  'availability',
  'protection',
  'privacy',
  'appearance',
  'developer',
] as const;

export type SettingsGroup = (typeof SETTINGS_GROUPS)[number];

/** The Settings view of the form open in the page. */
export const settingsViewOf = (page: Page): Locator => page.locator('jfb-form-settings-view');

/** The list of groups to pick from. */
export const groupsList = (view: Locator): Locator => view.locator('nav.groups');

/** A group's row in the list. */
export const groupRow = (view: Locator, group: SettingsGroup): Locator => view.locator(`#group-${group}`);

/** A row's line: what its group sets, its problems counted first. */
export const rowLine = (view: Locator, group: SettingsGroup): Locator => groupRow(view, group).locator('.summary');

/** The group picked: its heading, the language button and its settings. */
export const groupPicked = (view: Locator): Locator => view.locator('section.picked');

/** Which group is picked. */
export const pickedGroup = async (view: Locator): Promise<string | null> =>
  groupPicked(view).getAttribute('data-group');

/** "All settings", which goes back to the list where the group stands in its place. */
export const allSettings = (view: Locator): Locator => groupPicked(view).locator('button.back');

/** Submission's "More options" toggle. */
export const settingsMore = (view: Locator): Locator => view.locator('button.more-toggle');

/** Opens a form's Settings view, at a width, and waits for its list (beside the group picked, or alone). */
export async function openSettings(page: Page, address: string, width = 1440): Promise<Locator> {
  await page.setViewportSize({ width, height: 900 });
  await page.goto(address);
  const view = settingsViewOf(page);
  await expect(groupsList(view)).toBeVisible({ timeout: 60_000 });
  return view;
}

/** Waits for a group to be the one picked and shown. */
export async function expectPicked(view: Locator, group: SettingsGroup): Promise<void> {
  await expect(groupPicked(view)).toHaveAttribute('data-group', group);
  await expect(groupPicked(view)).toBeVisible();
}

/**
 * Picks a group with the mouse, from the list or, where the group stands in its place, after "All settings"; nothing
 * is clicked when it is picked and shown already.
 */
export async function pickGroup(view: Locator, group: SettingsGroup): Promise<void> {
  if ((await pickedGroup(view)) === group && (await groupPicked(view).isVisible())) return;
  if (!(await groupsList(view).isVisible())) await allSettings(view).click();
  await groupRow(view, group).click();
  await expectPicked(view, group);
}

/** Picks a group with the keyboard: Tab from the first row to its row, then Enter. */
export async function pickGroupByKeyboard(page: Page, view: Locator, group: SettingsGroup): Promise<void> {
  await groupRow(view, 'submission').focus();
  for (let at = 0; at < SETTINGS_GROUPS.indexOf(group); at++) await page.keyboard.press('Tab');
  await expect(groupRow(view, group)).toBeFocused();
  await page.keyboard.press('Enter');
  await expectPicked(view, group);
}

/** Back to the list with "All settings", where the group stands in its place. */
export async function backToAllSettings(view: Locator): Promise<void> {
  await allSettings(view).click();
  await expect(groupsList(view)).toBeVisible();
}

/** Opens Submission's "More options", unless it is open. */
export async function openSettingsMore(view: Locator): Promise<void> {
  await pickGroup(view, 'submission');
  const toggle = settingsMore(view);
  if ((await toggle.getAttribute('aria-expanded')) !== 'true') await toggle.click();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
}

// ---- The inspector --------------------------------------------------------------------------------------------------

/** The inspector's sections, by their keys (`validation`, `messages`, a site's own type's `essentials-more`...). */
export const sectionKeys = (inspector: Locator): Promise<string[]> =>
  inspector
    .locator('section.section')
    .evaluateAll((sections) => sections.map((section) => section.getAttribute('data-section') ?? ''));

/** A section's toggle, by its key. */
export const sectionToggle = (inspector: Locator, key: string): Locator =>
  inspector.locator(`section.section[data-section="${key}"] .section-head button`);

/** The settings an open section shows, one each. */
export const sectionItems = (inspector: Locator, key: string): Locator =>
  inspector.locator(`section.section[data-section="${key}"] .section-body > *`);

/** Opens an inspector section by its key, unless it is open. */
export async function openSection(inspector: Locator, key: string): Promise<void> {
  const toggle = sectionToggle(inspector, key);
  await toggle.scrollIntoViewIfNeeded();
  if ((await toggle.getAttribute('aria-expanded')) !== 'true') await toggle.click();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
}

/** Opens every section of the inspector. */
export async function openSections(inspector: Locator): Promise<void> {
  // Each click takes a match away, so the first closed one is clicked until none is left.
  const closed = inspector.locator('.section-head button[aria-expanded="false"]');
  while ((await closed.count()) > 0) await closed.first().click();
}

// ---- "Add action" ---------------------------------------------------------------------------------------------------

/** The action's sidebar: the kinds of a new action, then the sheet of the kind picked. */
export const actionSheetOf = (page: Page): Locator => page.locator('jfb-action-modal');

/** The kinds a new action may be. */
export const kindsOf = (sheet: Locator): Locator => sheet.locator('button.kind');

/** A kind, by its name. */
export const kindNamed = (sheet: Locator, name: string): Locator =>
  kindsOf(sheet).filter({ has: sheet.page().locator('.kind-name').getByText(name, { exact: true }) });

/**
 * A kind by its choice, in any language: the type's alias and, for a preset, its place among the type's presets
 * (`email:0` emails the team, `webhook:1` posts to Slack, `content`, `setState`).
 */
export const kindOfChoice = (sheet: Locator, choice: string): Locator =>
  kindsOf(sheet).and(sheet.locator(`[data-choice="${choice}"]`));

/** Picks a kind by its choice, in any language, and waits for its sheet. */
export async function pickKindOfChoice(sheet: Locator, choice: string): Promise<void> {
  const name = (await kindOfChoice(sheet, choice).locator('.kind-name').textContent())?.trim() ?? '';
  await kindOfChoice(sheet, choice).click();
  await expect(kindHeading(sheet)).toHaveText(name);
}

/** The kinds listed: each its name and its line, or an empty line where it has none. */
export const kindsShown = (sheet: Locator): Promise<{ name: string; line: string }[]> =>
  kindsOf(sheet).evaluateAll((kinds) =>
    kinds.map((kind) => ({
      name: kind.querySelector('.kind-name')?.textContent?.trim() ?? '',
      line: kind.querySelector('.kind-line')?.textContent?.trim() ?? '',
    })),
  );

/** The sheet's heading: the kind. */
export const kindHeading = (sheet: Locator): Locator => sheet.locator('.kind-head h3');

/** "Other kinds", the way back to the kinds while the action is not added. */
export const otherKinds = (sheet: Locator): Locator => sheet.locator('.kind-head uui-button');

/** "When it runs", one choice. */
export const momentChoice = (sheet: Locator): Locator => sheet.locator('select#trigger');

/** The line under "When it runs" about the moment chosen. */
export const momentLine = (sheet: Locator): Locator => sheet.locator('#trigger-line');

/** Opens "Add action" from the Actions view, and waits for the kinds; with a kind's name, picks it. */
export async function addAction(page: Page, view: Locator, kind?: string): Promise<Locator> {
  await view.locator('#add').click();
  const sheet = actionSheetOf(page);
  await expect(kindsOf(sheet).first()).toBeVisible();
  if (kind) await pickKind(sheet, kind);
  return sheet;
}

/** Picks a kind from the kinds, and waits for the sheet headed by it. */
export async function pickKind(sheet: Locator, kind: string): Promise<void> {
  await kindNamed(sheet, kind).click();
  await expect(kindHeading(sheet)).toHaveText(kind);
}

/** Back to the kinds with "Other kinds". */
export async function backToKinds(sheet: Locator): Promise<void> {
  await otherKinds(sheet).click();
  await expect(kindsOf(sheet).first()).toBeVisible();
}

/** Chooses when the action runs, by the trigger's value (`submitted`, `approved`, `rejected`, `verified`). */
export async function chooseMoment(sheet: Locator, trigger: string): Promise<void> {
  await momentChoice(sheet).selectOption(trigger);
  await expect(momentChoice(sheet)).toHaveValue(trigger);
}
