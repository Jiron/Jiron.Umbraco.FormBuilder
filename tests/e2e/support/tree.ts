import type { Locator, Page } from '@playwright/test';

/** A row of the forms tree, as shown: the Recycle Bin's item holds its rows while it is closed too. */
export const treeRow = (page: Page, name: string): Locator =>
  page.locator(`umb-section-sidebar uui-menu-item[label="${name.replace(/"/g, '\\"')}"]`).filter({ visible: true });

/**
 * Opens the "..." of a row of the forms tree. The row goes to the middle of the sidebar first: in a long tree it can
 * sit at the sidebar's edge, where it moves as the pointer reaches it and the click lands on its neighbour.
 */
export async function openRowActions(page: Page, name: string): Promise<void> {
  const row = treeRow(page, name).first();
  await row.evaluate((element) => element.scrollIntoView({ block: 'center' }));
  await row.hover();
  await page.getByRole('button', { name: `View actions for '${name}'`, exact: true }).click();
}
