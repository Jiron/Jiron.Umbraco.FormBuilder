import { expect, type Locator, type Page } from '@playwright/test';

/** A row of the forms tree, as shown: the Recycle Bin's item holds its rows while it is closed too. */
export const treeRow = (page: Page, name: string): Locator =>
  page.locator(`umb-section-sidebar uui-menu-item[label="${name.replace(/"/g, '\\"')}"]`).filter({ visible: true });

/**
 * Presses a tree's "Load more" until it shows the row. The tests share their host's root, so a row made late in a run
 * can fall past the tree's first page.
 */
export async function loadedIn(tree: Locator, row: Locator): Promise<Locator> {
  const more = tree.locator('umb-tree-load-more-button').filter({ visible: true }).first();
  const items = tree.locator('uui-menu-item');
  // The tree's first page before anything else: with no row yet there is no Load more either, and the row never came.
  await expect(items.first()).toBeVisible();
  // Load more can show a moment after the first page, so a round without the row and without the button is tried again
  // rather than taken as the end of the tree.
  await expect(async () => {
    if ((await row.count()) > 0) return;
    if ((await more.count()) > 0) {
      const before = await items.count();
      await more.click();
      await expect.poll(() => items.count()).toBeGreaterThan(before);
    }
    expect(await row.count()).toBeGreaterThan(0);
  }).toPass({ timeout: 90_000 });
  return row;
}

/** A row of the forms tree, the tree read as far as the row first. */
export const shownRow = (page: Page, name: string): Promise<Locator> =>
  loadedIn(page.locator('umb-section-sidebar'), treeRow(page, name).first());

/**
 * Opens the "..." of a row of the forms tree, read as far as the row first. The row goes to the middle of the sidebar
 * first: in a long tree it can sit at the sidebar's edge, where it moves as the pointer reaches it and the click lands
 * on its neighbour. A tree that is still reading more rows can move it again and take the hover away, so the hover and
 * the click are tried again together until the row's menu shows.
 */
export async function openRowActions(page: Page, name: string): Promise<void> {
  const row = await shownRow(page, name);
  // The row's own "...", found without words, so a walk in German opens it too.
  const more = row.locator('umb-entity-actions-dropdown uui-button').first();
  const menu = page.locator('umb-entity-action-list').filter({ visible: true }).first();
  await expect(async () => {
    await row.evaluate((element) => element.scrollIntoView({ block: 'center' }));
    await row.hover();
    await more.click({ timeout: 3_000 });
    await expect(menu).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
}
