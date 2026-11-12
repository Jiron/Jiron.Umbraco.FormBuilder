import { expect, type Locator, type Page } from '@playwright/test';

/**
 * Opens the backoffice header's search on one of its tabs, ours by default, and returns the search dialog. The header's
 * button can take a click before the backoffice has wired it, and then no dialog opens, so the click is tried again
 * until the tab shows.
 */
export async function openSearch(page: Page, tab: string | RegExp = 'Forms'): Promise<Locator> {
  const button = page.locator('umb-backoffice-header-apps [label="Search"], umb-search-header-app uui-button').first();
  const search = page.locator('umb-search-modal');
  const tabButton = search.getByRole('button', { name: tab });
  await expect(async () => {
    if (!(await search.isVisible())) await button.click({ timeout: 5_000 });
    await expect(tabButton).toBeVisible({ timeout: 5_000 });
  }).toPass({ timeout: 60_000 });
  await tabButton.click();
  return search;
}
