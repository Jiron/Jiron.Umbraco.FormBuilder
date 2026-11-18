import { expect, type Locator } from '@playwright/test';

// The designer's row above the sheet (ROADMAP 22.1, ADR-0256), so no spec knows its markup: the pages as tabs with the
// open one's menu, Add page and After sending at its start; the widths, the language and the form check at its end.

/** The site's languages by culture, as the test site names them in the language list. */
const LANGUAGE_NAMES: Readonly<Record<string, RegExp>> = {
  'en-US': /English/,
  'de-DE': /Deutsch|German/,
};

/** Shows the form in a language of the site: the Design view's button, then the language in the list it opens. */
export async function chooseLanguage(design: Locator, culture: string): Promise<void> {
  await chooseLanguageIn(design.locator('jfb-form-canvas'), culture);
}

/**
 * The same with the button where another view holds it, the Settings view's group picked among them (ROADMAP 22.3):
 * its button, then the language in the list it opens.
 */
export async function chooseLanguageIn(holder: Locator, culture: string): Promise<void> {
  const name = LANGUAGE_NAMES[culture];
  if (!name) throw new Error(`No language list name for ${culture}`);
  await holder.locator('.bo-language').click();
  await holder.locator('.bo-menu button').filter({ hasText: name }).first().click();
  await expect(holder.locator('.bo-language')).toHaveAttribute('aria-label', new RegExp(`^Language ${culture}:`));
}

/** The pages' row: its tabs, Add page and After sending. */
export const pagesOf = (design: Locator): Locator => design.getByRole('navigation', { name: 'Pages' });

/** A page's tab, by the name it shows. */
export const tabOf = (design: Locator, name: string): Locator =>
  pagesOf(design).getByRole('button', { name, exact: true });

/** Opens the open page's menu at its tab and picks an item: Rename, Duplicate or Delete. */
export async function pageMenu(design: Locator, page: string, item: 'Rename' | 'Duplicate' | 'Delete'): Promise<void> {
  await pagesOf(design)
    .getByRole('button', { name: `More for ${page}`, exact: true })
    .click();
  await design.locator('jfb-form-canvas .bo-tab .bo-menu button').filter({ hasText: item }).first().click();
}

/** Shows After sending, whose outcomes stand in the inspector's column or under the sheet. */
export async function showAfterSending(design: Locator): Promise<Locator> {
  await pagesOf(design).getByRole('button', { name: 'After sending', exact: true }).click();
  const outcomes = design.locator('jfb-form-outcomes');
  await expect(outcomes.locator('jfb-outcome-list')).toBeVisible();
  return outcomes;
}
