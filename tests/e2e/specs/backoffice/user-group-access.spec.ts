import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { inLanguage, inTheme } from '../../support/backoffice-look.ts';
import { onTab, openTab, screenAddress, SECTION } from '../../support/places.ts';

// Where a group's access is found (ROADMAP 25.1, ADR-0262): a refusal names the place whole, Form Builder > Settings >
// Access, in English and in German; Privacy > People without its permission says which one it needs and shows no
// search; and Umbraco's user group screen has a Form Builder view, for a user who may manage permissions, that says
// what the group may do and links to Settings > Access. The specs write no user and no user group: writes to Umbraco's
// user tables beside other specs' sign-ins hang its SQLite, so every user here is a persona the host seeds before any
// test (support/users.ts), and the groups are only read.

const UMBRACO_API = '/umbraco/management/api/v1';
const VIEW = 'jfb-user-group-access-view';

/** The place's name as every refusal now says it, in each language. */
const WHOLE = 'Form Builder \u203a Settings \u203a Access';
const WHOLE_DE = 'Form Builder \u203a Einstellungen \u203a Zugriff';

/** The view's tab among the workspace's, not the section of the same name in Umbraco's top bar. */
const viewTab = (page: Page) =>
  page.locator('umb-workspace-editor').getByRole('tab', { name: 'Form Builder', exact: true });

/** A user group's screen in Umbraco's Users section. */
const groupScreen = (id: string) => `/umbraco/section/user-management/workspace/user-group/edit/${id}`;

/** Umbraco's user groups by alias, with their ids. */
async function groupIds(api: APIRequestContext): Promise<Map<string, string>> {
  const response = await api.get(`${UMBRACO_API}/user-group`, { params: { skip: 0, take: 200 } });
  expect(response.ok(), 'the user groups').toBe(true);
  const { items } = (await response.json()) as { items: { id: string; alias: string }[] };
  return new Map(items.map((group) => [group.alias, group.id]));
}

/** Opens a user group's screen and its Form Builder view; the view. */
async function openView(page: Page, id: string) {
  // By the view's own address: on a phone Umbraco's page scrolls sideways and the workspace's tabs are out of sight.
  await page.goto(`${groupScreen(id)}/view/form-builder`);
  const view = page.locator(VIEW);
  await expect(view.locator('uui-box')).toBeVisible({ timeout: 60_000 });
  await expect(view.locator('[aria-busy="true"]')).toHaveCount(0, { timeout: 30_000 });
  await expect(view.getByRole('link')).toBeVisible();
  return view;
}

test('the persona without grants reads where access is given, under Forms and on a place opened by its address, and People names its permission with no search', async ({
  page,
  loginAs,
}) => {
  test.setTimeout(180_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('noAccess');
  await page.goto(SECTION);
  await expect(page.locator('jfb-no-forms')).toContainText(
    `You can't see any forms yet. An administrator can give your user group access in ${WHOLE}.`,
    { timeout: 60_000 },
  );

  await page.goto(screenAddress('jfb-option-lists'));
  await expect(page.locator('jfb-option-lists-screen, jfb-place-tabs').first()).toBeAttached({ timeout: 60_000 });
  await expect(page.getByRole('alert').filter({ hasText: 'Manage the library' })).toContainText(
    `This needs the "Manage the library" permission. An administrator can grant it in ${WHOLE}.`,
  );

  await page.goto(screenAddress('jfb-privacy-people'));
  const people = page.locator('jfb-people-screen');
  await expect(people.getByRole('alert')).toContainText(
    `This needs the "Use the privacy tools" permission. An administrator can grant it in ${WHOLE}.`,
    { timeout: 60_000 },
  );
  await expect(people.getByLabel('Email address', { exact: true })).toHaveCount(0);
  await expect(people.locator('input')).toHaveCount(0);
});

test('a German user with the section and no permission reads the same sentences in German', async ({
  page,
  loginAs,
}) => {
  test.setTimeout(180_000);
  // The persona without grants, its backoffice in German: inLanguage answers Umbraco's current-user request for this
  // page alone with the language de-DE, so the backoffice loads its German words, and nothing on the host changes.
  await inLanguage(page, 'de-DE');
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('noAccess');
  await page.goto(SECTION);
  await expect(page.locator('jfb-no-forms')).toContainText(
    `Sie sehen noch keine Formulare. Wer Administratorrechte hat, kann Ihrer Benutzergruppe unter ${WHOLE_DE} Zugang geben.`,
    { timeout: 60_000 },
  );
  await page.goto(screenAddress('jfb-option-lists'));
  await expect(page.getByRole('alert').filter({ hasText: WHOLE_DE })).toContainText(
    `Daf\u00fcr brauchen Sie die Berechtigung \u201eBibliothek verwalten\u201c. Wer Administratorrechte hat, kann sie unter ${WHOLE_DE} vergeben.`,
    { timeout: 60_000 },
  );
});

test("an administrator reads on Umbraco's user group screen what each group may do, and its link lands on Settings > Access", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(300_000);
  const groups = await groupIds(await apiAs('admin'));
  const id = (alias: string) => {
    const found = groups.get(alias);
    expect(found, alias).toBeDefined();
    return found!;
  };
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');

  // Umbraco's Details first, then the view, named as the section.
  await page.goto(groupScreen(id('jfbNoAccess')));
  await expect(viewTab(page)).toBeVisible({ timeout: 60_000 });
  const details = await page.locator('umb-workspace-editor').getByRole('tab', { name: 'Details' }).boundingBox();
  const ours = await viewTab(page).boundingBox();
  expect(details && ours && ours.x > details.x, 'the view after Details').toBe(true);
  // Opened from its tab, as an administrator would.
  await viewTab(page).click();
  await expect(page.locator(VIEW).getByRole('link')).toBeVisible({ timeout: 30_000 });

  let view = await openView(page, id('jfbNoAccess'));
  await expect(view).toContainText('This group has no permission for forms yet, so its users see an empty section.');
  await expect(view).not.toContainText("can't open Form Builder");

  view = await openView(page, id('jfbEntryHandlers'));
  await expect(view.locator('dl dt')).toHaveText(['Entries']);
  await expect(view.locator('dl dd li')).toHaveText(['View entries', 'Edit entries', 'Export entries']);
  await expect(view).not.toContainText('empty section');

  view = await openView(page, id('jfbHrTeam'));
  const hr = view.locator('.items li').filter({ has: page.locator('.item-name', { hasText: /^HR$/ }) });
  await expect(hr).toContainText('View entries and Edit entries');
  await expect(hr).toContainText('Only this group sees it');
  await expect(view).toContainText('None for every form, only in the folders and forms below.');

  view = await openView(page, id('writer'));
  await expect(view).toContainText(
    "This group can't open Form Builder yet. Add the section to it under Details first.",
  );
  // Its users see no section at all, so nothing speaks of an empty one.
  await expect(view.locator('section')).toHaveText('Permissions for every form No permission for forms yet.', {
    useInnerText: true,
  });
  await expect(view).not.toContainText('empty section');

  // The one link lands on Settings > Access, with its tab and place marked.
  await view.getByRole('link', { name: `Open ${WHOLE}`, exact: true }).click();
  await onTab(page, 'Settings', 'Access');
  await expect.poll(() => openTab(page)).toBe('Access');
});

test('the view passes axe in the light and the dark theme and at a phone width', async ({ page, loginAs, apiAs }) => {
  test.setTimeout(300_000);
  const groups = await groupIds(await apiAs('admin'));
  const hrTeam = groups.get('jfbHrTeam');
  const writers = groups.get('writer');
  expect(hrTeam && writers, 'the groups').toBeTruthy();
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  for (const group of [hrTeam!, writers!]) {
    await openView(page, group);
    await axeInside(page, VIEW);
  }

  await page.setViewportSize({ width: 390, height: 844 });
  const view = await openView(page, hrTeam!);
  await axeInside(page, VIEW);
  // Nothing of the view runs past the window's edge once it is in sight.
  await view.scrollIntoViewIfNeeded();
  const box = await view.locator('uui-box').boundingBox();
  expect(box && box.width > 0 && box.width <= 390, 'the box within a phone width').toBe(true);

  await inTheme(page, 'dark');
  await page.setViewportSize({ width: 1440, height: 900 });
  await openView(page, hrTeam!);
  await expect
    .poll(() => page.evaluate(() => localStorage.getItem('umb-theme-alias')), 'the dark theme')
    .toBe('umb-dark-theme');
  await axeInside(page, VIEW);
  await openView(page, writers!);
  await axeInside(page, VIEW);
});

test('a user with the Users section and without the permission to manage permissions sees no Form Builder view', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const group = (await groupIds(await apiAs('admin'))).get('jfbUserManagers');
  expect(group, 'the user managers').toBeDefined();
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('userManager');
  // Its own group, which Umbraco lets a user of the Users section open.
  await page.goto(groupScreen(group!));
  await expect(page.locator('umb-user-group-details-workspace-view')).toBeVisible({ timeout: 60_000 });
  // The permission condition has its answer once the section's own request for it has come back.
  await page.waitForLoadState('networkidle');
  await expect(viewTab(page)).toHaveCount(0);
  await expect(page.locator(VIEW)).toHaveCount(0);
});
