import { expect, type Locator, type Page } from '@playwright/test';
import english from '../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/src/lang/section.en.ts';
import {
  PLACES,
  type Place,
  type Tab,
} from '../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/src/section/sidebar.ts';

// The section's left side (ROADMAP 22.2, ADR-0257), so no spec knows its markup: the five places before the tree of
// forms, each a link to the first of its tabs the user may open, and a place's tabs along the top of its screens. The
// places and tabs come from the product's own table, and their names from its English words.

export { PLACES };
export type { Place, Tab };

/** The section's address below the backoffice. */
export const SECTION = '/umbraco/section/jfb-forms';

/** A screen's own address: the workspace of its entity type. */
export const screenAddress = (entityType: string): string => `${SECTION}/workspace/${entityType}`;

const words = english.jfbSection as Record<string, unknown>;
const nameOf = (id: string): string => {
  const word = words[id];
  if (typeof word !== 'string') throw new Error(`No English name for ${id}.`);
  return word;
};

/** A place's name, as the sidebar shows it. */
export const placeName = (place: Place): string => nameOf(place.id);

/** A tab's name, as its screen's tab group shows it. */
export const tabName = (tab: Tab): string => nameOf(tab.id);

/** Every tab with its place, in the table's order. */
export const ALL_TABS: readonly { place: Place; tab: Tab }[] = PLACES.flatMap((place) =>
  place.tabs.map((tab) => ({ place, tab })),
);

/** A place by its name. */
export function placeNamed(name: string): Place {
  const place = PLACES.find((each) => placeName(each) === name);
  if (!place) throw new Error(`No place is named ${name}.`);
  return place;
}

/** A tab of a place by their names. */
export function tabNamed(place: string, tab: string): { place: Place; tab: Tab } {
  const found = placeNamed(place).tabs.find((each) => tabName(each) === tab);
  if (!found) throw new Error(`${place} has no tab named ${tab}.`);
  return { place: placeNamed(place), tab: found };
}

/** The places' list in the sidebar. */
export const placesList = (page: Page): Locator => page.locator('jfb-places nav');

const itemOf = (page: Page, place: Place): Locator =>
  page.locator('jfb-places uui-menu-item').filter({ has: page.locator(`umb-icon[name="${place.icon}"]`) });

/** A place's item in the sidebar, by its name (the Inbox's name for screen readers adds its count). */
export const placeItem = (page: Page, name: string): Locator => itemOf(page, placeNamed(name));

/** What each item shows: the Inbox's label slot shows its name, where its label adds the count. Runs in the page. */
const namesOf = (items: Element[]): string[] =>
  items.map((item) => item.querySelector('[slot="label"]')?.textContent?.trim() || (item.getAttribute('label') ?? ''));

/** The open screen's tabs element: its tab group and its heading for screen readers. */
export const screenTabs = (page: Page): Locator => page.locator('jfb-place-tabs').first();

/** A tab of the open screen, by its name. */
export const tabItem = (page: Page, name: string): Locator => screenTabs(page).locator(`uui-tab[label="${name}"]`);

/** The names of the places the sidebar shows, in order. */
export const placesShown = (page: Page): Promise<string[]> =>
  page.locator('jfb-places uui-menu-item').evaluateAll(namesOf);

/** The name of the place the sidebar marks open, if one is. */
export async function openPlace(page: Page): Promise<string | undefined> {
  return (await page.locator('jfb-places uui-menu-item[active]').evaluateAll(namesOf))[0];
}

/** The names of the open screen's tabs, in order; none where the user may open one tab of the place. */
export const tabsShown = (page: Page): Promise<string[]> =>
  screenTabs(page)
    .locator('uui-tab')
    .evaluateAll((tabs) => tabs.map((tab) => tab.getAttribute('label') ?? ''));

/** The name of the open screen's active tab, if it shows a row of tabs. */
export async function openTab(page: Page): Promise<string | undefined> {
  const active = await screenTabs(page)
    .locator('uui-tab[active]')
    .evaluateAll((tabs) => tabs.map((tab) => tab.getAttribute('label') ?? ''));
  return active[0];
}

/** The open screen's heading for screen readers: its place and tab. */
export const screenHeading = (page: Page): Promise<string> =>
  screenTabs(page)
    .locator('h2')
    .evaluate((heading) => heading.textContent?.trim() ?? '');

/** Waits until the screen of that tab is the open one: its address and its heading. */
export async function onTab(page: Page, place: string, tab: string): Promise<void> {
  const found = tabNamed(place, tab);
  await expect(page).toHaveURL(new RegExp(`/workspace/${found.tab.entityType}(?:[/?#]|$)`));
  await expect.poll(() => screenHeading(page)).toBe(place === tab ? place : `${place}: ${tab}`);
}

/** How a step of the way is clicked: plainly, or through a spec's click counter (support/top-tasks.ts). */
export type Clicker = (target: Locator) => Promise<void>;

const plainly: Clicker = (target) => target.click();

/** Opens a place from the sidebar, as a person would, and waits for its screen. */
export async function goToPlace(page: Page, name: string, click: Clicker = plainly): Promise<void> {
  const item = placeItem(page, name);
  await expect(item).toBeVisible({ timeout: 60_000 });
  await click(item.locator('a').first());
  await expect.poll(() => openPlace(page)).toBe(name);
  // The place's screen, not the one before it: its heading names the place, drawn in the same moment as its tabs.
  await expect.poll(() => screenHeading(page).catch(() => '')).toMatch(new RegExp(`^${name}(?::|$)`));
}

/** Opens a tab of a place from the sidebar and the tab group, as a person would, and waits for its screen. */
export async function goToTab(page: Page, place: string, tab: string, click: Clicker = plainly): Promise<void> {
  await goToPlace(page, place, click);
  if ((await openTab(page)) !== tab && (await tabItem(page, tab).count()) > 0) await click(tabItem(page, tab));
  await onTab(page, place, tab);
}

/**
 * Opens a tab's screen by its entity type, in whatever language the backoffice speaks: from the section's start page,
 * which closes whatever dialog was open, the place's item (by its icon), then the tab whose link is the screen's
 * address, unless the place opened on it.
 */
export async function goToScreen(page: Page, entityType: string, click: Clicker = plainly): Promise<void> {
  const found = ALL_TABS.find(({ tab }) => tab.entityType === entityType);
  if (!found) throw new Error(`No tab has the screen ${entityType}.`);
  const address = new RegExp(`/workspace/${entityType}(?:[/?#]|$)`);
  const item = itemOf(page, found.place);
  await page.goto(SECTION);
  await expect(item).toBeVisible({ timeout: 60_000 });
  await click(item.locator('a').first());
  await expect(item).toHaveAttribute('active', '');
  const tab = screenTabs(page).locator(`uui-tab[href$="/workspace/${entityType}"]`);
  await expect(async () => {
    if (address.test(page.url())) return;
    expect(await tab.count()).toBeGreaterThan(0);
  }).toPass({ timeout: 30_000 });
  if (!address.test(page.url())) await click(tab);
  await expect(page).toHaveURL(address);
}
