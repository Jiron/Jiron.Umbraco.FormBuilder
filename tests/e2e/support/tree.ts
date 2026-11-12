import { expect, type Locator, type Page } from '@playwright/test';

/** A row of the forms tree, as shown: the Recycle Bin's item holds its rows while it is closed too. */
export const treeRow = (page: Page, name: string): Locator =>
  page.locator(`umb-section-sidebar uui-menu-item[label="${name.replace(/"/g, '\\"')}"]`).filter({ visible: true });

/**
 * Opens the "..." of a row of the forms tree. The row goes to the middle of the sidebar first: in a long tree it can
 * sit at the sidebar's edge, where it moves as the pointer reaches it and the click lands on its neighbour. A tree that
 * is still reading more rows can move it again and take the hover away, so the hover and the click are tried again
 * together until the row's menu shows.
 */
export async function openRowActions(page: Page, name: string): Promise<void> {
  const row = treeRow(page, name).first();
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
