import { expect, type Locator, type Page } from '@playwright/test';

// The designer's field picker (ROADMAP 22.1, ADR-0256), so no spec knows its markup: a sidebar that every Add field and
// insert point opens, with its search focused; a pick lands where it was opened, and Esc or Close lands nothing.

/** The picker's sidebar, wherever it was opened from. */
export const pickerOf = (page: Page): Locator => page.locator('jfb-field-picker-modal');

/** The picker's search box. */
export const pickerSearch = (page: Page): Locator =>
  pickerOf(page).getByRole('searchbox', { name: 'Search field types' });

/** A field type, a group or a saved group in the picker, by its name. */
export const pickerItem = (page: Page, name: string | RegExp): Locator =>
  pickerOf(page)
    .getByRole('button', { name, exact: typeof name === 'string' })
    .first();

/** The match Enter picks, which the picker marks while a search is typed. */
export const pickerFirstMatch = (page: Page): Locator => pickerOf(page).locator('.item[data-first]');

/** The Add field at the end of the page the canvas shows. */
export const addFieldOf = (design: Locator): Locator => design.locator('jfb-form-canvas .bo-add-field').last();

/** The insert point under a field of the canvas, between it and the next one. */
export const insertAfter = (field: Locator): Locator => field.locator('.bo-insert-after').first();

/** Opens the picker by a click on a control, and waits for its search to have the focus (found by its place). */
export async function openPicker(page: Page, opener: Locator): Promise<Locator> {
  await opener.click();
  await expect(pickerOf(page).locator('uui-input input')).toBeFocused();
  return pickerOf(page);
}

/** Opens the picker by the keyboard alone: the focus put on the control, then Enter. */
export async function openPickerByKeyboard(page: Page, opener: Locator): Promise<Locator> {
  await opener.focus();
  await page.keyboard.press('Enter');
  await expect(pickerSearch(page)).toBeFocused();
  return pickerOf(page);
}

/** Waits until the picker's sidebar is gone, and the designer has put the focus where the pick or Esc leaves it. */
export async function pickerClosed(page: Page): Promise<void> {
  await expect(pickerOf(page)).toHaveCount(0);
}

/**
 * Adds a field type, a group ('Group') or a saved group by its name: opens the picker from a control, and clicks it.
 */
export async function pick(page: Page, opener: Locator, name: string | RegExp): Promise<void> {
  await openPicker(page, opener);
  await pickerItem(page, name).click();
  await pickerClosed(page);
}

/** The same by the keyboard alone: Enter on the control, the search typed, Enter on the match marked first. */
export async function pickByKeyboard(page: Page, opener: Locator, search: string): Promise<void> {
  await openPickerByKeyboard(page, opener);
  await page.keyboard.type(search);
  await page.keyboard.press('Enter');
  await pickerClosed(page);
}

/**
 * Opens Paste questions from the picker, its button at the start of its footer, which closes the picker as the dialog
 * opens; returns the dialog. By its place, not its words, so the walk finds it in every language.
 */
export async function openPasteQuestions(page: Page, opener: Locator): Promise<Locator> {
  await openPicker(page, opener);
  await pickerOf(page).locator('uui-button[slot="footer-info"]').click();
  await pickerClosed(page);
  return page.locator('jfb-paste-questions-modal');
}
