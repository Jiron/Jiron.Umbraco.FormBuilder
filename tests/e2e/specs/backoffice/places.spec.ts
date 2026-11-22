import type { Page, Response } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { API, blankForm, headlessPersonForm, sendAsVisitor, unique } from '../../support/forms-api.ts';
import { bodyOf, headerOf } from '../../support/mail.ts';
import {
  ALL_TABS,
  goToPlace,
  onTab,
  openPlace,
  openTab,
  PLACES,
  placeItem,
  placeName,
  placesList,
  placesShown,
  screenAddress,
  SECTION,
  tabItem,
  tabName,
  tabsShown,
} from '../../support/places.ts';
import type { Role } from '../../support/users.ts';

// The section's left side (ROADMAP 22.2, ADR-0257): the five places before the tree, each a link to the first of its
// tabs a user may open, a place's tabs along the top of its screens, and every address that held a screen before still
// opening it, with its tab and place marked. The step's "Done when" checks that have no other home are here; the
// settings' own tests (site-wide/settings.spec.ts, access.spec.ts, retention.spec.ts) save every setting on its tab,
// and ai.spec.ts holds the add-on's tab at the end of Settings.

const OPEN_INBOX = 'Open the Inbox';

/** Each persona of ADR-0013, with the places and their tabs it may use, in the sidebar's order. */
const SEEN: Partial<Record<Role, Record<string, string[]>>> = {
  admin: {
    Inbox: ['Inbox', 'Spam'],
    Library: ['Option lists', 'Themes', 'Email layouts', 'Templates'],
    // The host installs the AI add-on, whose tab ends Settings' (ADR-0018).
    Settings: ['General', 'Email', 'Spam protection', 'Files', 'Access', 'Integrations', 'AI'],
    Privacy: ['People', 'Retention', 'Data collection'],
    Activity: ['Deliveries', 'Audit log'],
  },
  designer: { Library: ['Option lists', 'Themes', 'Email layouts', 'Templates'] },
  entryHandler: { Inbox: ['Inbox', 'Spam'] },
  viewer: { Inbox: ['Inbox', 'Spam'] },
  privacyOfficer: { Inbox: ['Inbox', 'Spam'], Privacy: ['People', 'Retention'] },
  hr: { Inbox: ['Inbox', 'Spam'] },
  noAccess: {},
};

/** The management API's refusals while something happens on the page. */
async function refusalsWhile(page: Page, action: () => Promise<void>): Promise<string[]> {
  const refused: string[] = [];
  const listen = (response: Response) => {
    if (response.url().includes('/umbraco/jfb/management/api/v1/') && response.status() === 403)
      refused.push(new URL(response.url()).pathname);
  };
  page.on('response', listen);
  try {
    await action();
  } finally {
    page.off('response', listen);
  }
  return refused;
}

test('with fifty forms in the tree, at 1440 by 900, the five places are on the screen and the Inbox shows its count', async ({
  page,
  loginAs,
  apiAs,
  request,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  for (let batch = 0; batch < 5; batch++)
    await Promise.all(
      Array.from({ length: 10 }, (_, index) => blankForm(api, unique(`Places ${String(batch * 10 + index)}`))),
    );
  const form = await headlessPersonForm(api, unique('Places count'));
  await sendAsVisitor(request, form, { name: 'Ada Lovelace', email: 'ada@example.test' });
  const unseen = (await (await api.get(`${API}/entries/unseen`)).json()) as { count: number; more: boolean };
  expect(unseen.count, 'an entry the administrator has not seen').toBeGreaterThan(0);
  const said = unseen.more ? `${String(unseen.count)}+` : String(unseen.count);

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(SECTION);
  await expect(placesList(page)).toBeVisible({ timeout: 60_000 });
  expect(await placesShown(page)).toEqual(PLACES.map(placeName));
  // The tree's fifty rows come after: each place lies inside the window, and none had to be scrolled to.
  for (const place of PLACES) await expect(placeItem(page, placeName(place))).toBeInViewport({ ratio: 1 });
  const sidebarScrolled = await page
    .locator('umb-section-sidebar')
    .evaluate((sidebar) => [sidebar, ...sidebar.querySelectorAll('*')].some((element) => element.scrollTop > 0));
  expect(sidebarScrolled).toBe(false);

  const inbox = placeItem(page, 'Inbox');
  await expect(inbox.locator('uui-badge')).toHaveText(said);
  await expect(inbox).toHaveAttribute('label', `Inbox, ${said} unseen`);
});

for (const [role, seen] of Object.entries(SEEN) as [Role, Record<string, string[]>][]) {
  test(`${role} sees the places and tabs it may use and no other, lands on its first tab, and is refused elsewhere`, async ({
    page,
    loginAs,
  }) => {
    test.setTimeout(300_000);
    await page.setViewportSize({ width: 1440, height: 900 });
    await loginAs(role);
    await page.goto(SECTION);
    await expect(page.locator('jfb-overview')).toBeVisible({ timeout: 60_000 });
    const tree = page.locator('umb-section-sidebar umb-section-sidebar-menu-with-entity-actions');
    if (role === 'noAccess') {
      // No place and no tree, and in the tree's place who can give access (ADR-0021).
      await expect(page.locator('jfb-no-forms')).toContainText(
        "You can't see any forms yet. An administrator can give your user group access in Settings \u203a Access.",
      );
      await expect(page.locator('jfb-places')).toHaveCount(0);
      await expect(tree).toHaveCount(0);
    } else {
      await expect(placesList(page)).toBeVisible({ timeout: 60_000 });
      await expect.poll(() => placesShown(page)).toEqual(Object.keys(seen));
      // The tree of forms follows the places, with no sentence in its place.
      await expect(tree).toBeVisible();
      await expect(page.locator('jfb-no-forms')).toHaveCount(0);
    }

    // Each place leads to the first of its tabs the persona may use, and shows those tabs and no other.
    for (const [place, tabs] of Object.entries(seen)) {
      await page.goto(SECTION);
      await goToPlace(page, place);
      await onTab(page, place, tabs[0]!);
      // A place with one tab left shows no row of tabs. The AI add-on's tab comes with its own bundle, which may arrive
      // after the row has drawn, so the row is read until it holds every tab.
      await expect.poll(() => tabsShown(page), place).toEqual(tabs.length > 1 ? tabs : []);
      if (tabs.length > 1) expect(await openTab(page)).toBe(tabs[0]);
    }

    // Every tab the persona may not use is refused at its address, as before (ADR-0013): the screen's reads are refused
    // by the server, except where the server answers each user with only what they may see: the Inbox and Spam list
    // the entries of the forms the user may view, none here, and any user of the section may list the template gallery,
    // whose saved templates' actions need `manageLibrary`. People asks the server only when a person is searched for.
    const allowed = new Set(Object.entries(seen).flatMap(([place, tabs]) => tabs.map((tab) => `${place}/${tab}`)));
    for (const { place, tab } of ALL_TABS) {
      const where = `${placeName(place)} \u203a ${tabName(tab)}`;
      if (allowed.has(`${placeName(place)}/${tabName(tab)}`)) continue;
      const refused = await refusalsWhile(page, async () => {
        await page.goto(screenAddress(tab.entityType));
        await expect(page.locator('jfb-place-tabs')).toBeAttached({ timeout: 60_000 });
        await page.waitForLoadState('networkidle');
      });
      if (tab.id === 'inbox' || tab.id === 'spam') {
        await expect(page.locator('jfb-entries-view tbody tr'), `${role} at ${where}`).toHaveCount(0);
      } else if (tab.id === 'templates') {
        await expect(page.locator('jfb-templates-screen').getByRole('button', { name: /^Actions for / })).toHaveCount(
          0,
        );
      } else if (tab.id === 'people') {
        // People reads nothing until a person is asked for: the search is refused.
        const screen = page.locator('jfb-people-screen');
        const found = await refusalsWhile(page, async () => {
          await screen.getByLabel('Email address', { exact: true }).fill('ada@example.test');
          const answered = page.waitForResponse((response) => response.url().includes('/people/find'));
          await screen.getByRole('button', { name: 'Find', exact: true }).click();
          await answered;
        });
        expect(found, `${role} at ${where}`).not.toEqual([]);
      } else {
        expect(refused, `${role} at ${where}`).not.toEqual([]);
      }
      expect(await tabsShown(page)).not.toContain(tabName(tab));
    }
  });
}

test("every screen's old address opens its tab, with the tab and its place marked", async ({ page, loginAs }) => {
  test.setTimeout(300_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  for (const { place, tab } of ALL_TABS) {
    await page.goto(screenAddress(tab.entityType));
    await onTab(page, placeName(place), tabName(tab));
    await expect.poll(() => openTab(page), tab.entityType).toBe(tabName(tab));
    await expect.poll(() => openPlace(page), tab.entityType).toBe(placeName(place));
  }
});

test("the Overview's links land on their screens, each with its tab and its place marked", async ({
  page,
  loginAs,
}) => {
  test.setTimeout(240_000);
  // The checklist with every step open, and a finding for each screen a health line links to.
  await page.route(`**${API}/setup/checklist`, async (route) => {
    const response = await route.fetch();
    const body = (await response.json()) as { complete: boolean; items: { done: boolean }[] };
    body.complete = false;
    for (const item of body.items) item.done = false;
    await route.fulfill({ response, json: body });
  });
  /** Each health line's link: its words, and the place and tab it opens. */
  const LINKS = {
    settingsEmail: ['Open email settings', 'Settings', 'Email'],
    settingsGeneral: ['Open general settings', 'Settings', 'General'],
    deliveries: ['Open Deliveries', 'Activity', 'Deliveries'],
    privacyDataCollection: ['Open Data collection', 'Privacy', 'Data collection'],
    privacyRetention: ['Open Retention', 'Privacy', 'Retention'],
    settingsProtection: ['Open spam protection settings', 'Settings', 'Spam protection'],
  } as const;
  await page.route(`**${API}/setup/health`, async (route) => {
    const response = await route.fetch();
    const body = (await response.json()) as Record<string, unknown>;
    body['findings'] = Object.keys(LINKS).map((link) => ({
      code: link,
      message: `A finding for ${link}.`,
      link,
      coversEmail: false,
    }));
    await route.fulfill({ response, json: body });
  });

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  const follow = async (name: string, place: string, tab: string) => {
    await page.goto(SECTION);
    const overview = page.locator('jfb-overview');
    await expect(overview.locator('uui-box').first()).toBeVisible({ timeout: 60_000 });
    await overview.getByRole('link', { name, exact: true }).first().click();
    await onTab(page, place, tab);
    await expect.poll(() => openTab(page), name).toBe(tab);
    await expect.poll(() => openPlace(page), name).toBe(place);
  };
  await follow('Open email settings', 'Settings', 'Email');
  // A later open step links by its name.
  await follow('Choose who sees entries', 'Settings', 'Access');
  // The checklist's step and the health line for Settings > Email share their words; either lands there.
  for (const [name, place, tab] of Object.values(LINKS)) await follow(name, place, tab);
  await follow(OPEN_INBOX, 'Inbox', 'Inbox');
});

test("the Inbox's address in a mail the product sends opens the Inbox with its place marked", async ({
  page,
  loginAs,
  apiAs,
  request,
  accountOf,
  waitForMail,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('admin');
  const form = await headlessPersonForm(api, unique('Places mail'));
  await sendAsVisitor(request, form, { name: 'Grace Hopper', email: 'grace@example.test' });
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('viewer');
  await page.goto(screenAddress('jfb-inbox'));
  const view = page.locator('jfb-entries-view');
  await expect(view.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });

  // The summary's test mail holds the address BackofficeLinks builds for the Inbox.
  const asked = Date.now();
  await view.locator('.toolbar').getByRole('button', { name: 'More', exact: true }).click();
  await view.locator('#more-menu').getByRole('button', { name: 'Email me a summary\u2026' }).click();
  const sidebar = page.locator('jfb-entry-digest-modal');
  await sidebar.getByRole('button', { name: 'Send test email', exact: true }).click();
  await expect(page.locator('uui-toast-notification', { hasText: 'Test email sent to ' })).toBeVisible();
  const viewer = accountOf('viewer');
  const mail = await waitForMail(
    (candidate) =>
      headerOf(candidate.raw, 'To').includes(viewer.email) &&
      bodyOf(candidate.raw).includes('/workspace/jfb-inbox') &&
      new Date(headerOf(candidate.raw, 'Date')).getTime() >= asked - 60_000,
  );
  const links = [...bodyOf(mail.raw).matchAll(/href="([^"]*\/umbraco\/section\/[^"]*)"/g)].map(
    (match) => new URL(match[1]!.replace(/&amp;/g, '&')).pathname,
  );
  expect(links).toContain(screenAddress('jfb-inbox'));
  await page.keyboard.press('Escape');

  for (const link of links) {
    await page.goto(link);
    await onTab(page, 'Inbox', 'Inbox');
    await expect.poll(() => openTab(page)).toBe('Inbox');
    await expect.poll(() => openPlace(page)).toBe('Inbox');
  }
});

test('leaving a tab with changes not saved asks first, and Stay keeps the tab and its changes', async ({
  page,
  loginAs,
}) => {
  test.setTimeout(180_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(screenAddress('jfb-settings-email'));
  const screen = page.locator('jfb-settings-email');
  const name = screen.getByRole('textbox', { name: 'Name' });
  await expect(name).toBeVisible({ timeout: 60_000 });
  await expect(screen.getByRole('button', { name: 'Save email settings', exact: true })).toBeVisible();
  await name.fill('A change not saved');

  await tabItem(page, 'General').click();
  const question = page.locator('umb-discard-changes-modal');
  await expect(question).toContainText('Discard unsaved changes');
  await question.getByRole('button', { name: 'Stay' }).click();
  await expect(question).toHaveCount(0);
  await onTab(page, 'Settings', 'Email');
  await expect.poll(() => openTab(page)).toBe('Email');
  await expect(name).toHaveValue('A change not saved');

  // Discard goes on to the tab asked for.
  await tabItem(page, 'General').click();
  await page.locator('umb-discard-changes-modal').getByRole('button', { name: 'Discard changes' }).click();
  await onTab(page, 'Settings', 'General');
  await expect.poll(() => openTab(page)).toBe('General');
});
