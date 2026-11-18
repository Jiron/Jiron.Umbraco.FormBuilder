import { readFileSync } from 'node:fs';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, created, unique } from '../../support/forms-api.ts';
import { goToTab } from '../../support/places.ts';

// Activity > Audit log (ROADMAP 12.4b, IA 9, ADR-0041, ADR-0219): a folder an administrator creates and renames is found
// by its name, newest first, with who did it and where; its details narrow the log to that person, shown as a chip until
// cleared; and Export downloads the filtered log as CSV, which is itself recorded.

const SECTION = '/umbraco/section/jfb-forms';

test('the audit log finds what was done by name, narrows to one person, and exports what it shows', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('admin');
  const folderName = unique('Audited folder');
  const folder = await created(await api.post(`${API}/folder`, { data: { name: folderName } }), folderName);
  const renamed = `${folderName} renamed`;
  expect((await api.put(`${API}/folder/${folder}`, { data: { name: renamed } })).ok()).toBe(true);

  try {
    await page.setViewportSize({ width: 1440, height: 900 });
    await loginAs('admin');
    await page.goto(SECTION);
    await goToTab(page, 'Activity', 'Audit log');
    await expect(page).toHaveURL(/workspace\/jfb-audit-log$/);
    const screen = page.locator('jfb-audit-screen');
    await expect(screen.locator('tbody tr').first()).toBeVisible({ timeout: 60_000 });

    await screen.getByLabel('Search people, forms and items').fill(renamed);
    const rows = screen.locator('tbody tr');
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0).locator('td').nth(2)).toHaveText('Renamed the folder');
    await expect(rows.nth(1).locator('td').nth(2)).toHaveText('Created the folder');
    await expect(rows.nth(0).locator('td').nth(3)).toHaveText(renamed);
    const who = (await rows.nth(0).locator('td').nth(1).textContent())?.trim() ?? '';
    expect(who.length).toBeGreaterThan(0);
    await axeInside(page, 'jfb-audit-screen');

    // The details: who, when with its zone, and where; then only this person's actions.
    await rows.nth(0).getByRole('button').click();
    const sidebar = page.locator('jfb-audit-entry-modal');
    await expect(sidebar.locator('dd').nth(0)).toHaveText(who);
    await expect(sidebar.locator('dd').nth(2)).toHaveText(renamed);
    await axeInside(page, 'jfb-audit-entry-modal');
    await sidebar.getByRole('button', { name: `Show only ${who}'s actions` }).click();
    const chip = screen.getByRole('button', { name: `Show everyone, not only ${who}` });
    await expect(chip).toHaveText(`Who: ${who}`);
    await expect(rows).toHaveCount(2);

    // Export writes what the screen shows.
    const downloading = page.waitForEvent('download');
    await screen.getByRole('button', { name: 'Export', exact: true }).click();
    const download = await downloading;
    expect(download.suggestedFilename()).toMatch(/^audit-log-\d{4}-\d{2}-\d{2}\.csv$/);
    const csv = readFileSync(await download.path(), 'utf8');
    expect(csv.startsWith('\ufeff'), 'a byte-order mark').toBe(true);
    const lines = csv
      .replace(/^\ufeff/, '')
      .trim()
      .split('\r\n');
    expect(lines[0]).toBe('occurredUtc,user,userKey,action,entityType,entityKey,item,details');
    expect(lines.slice(1).map((line) => line.split(',')[3])).toEqual(['folder.renamed', 'folder.created']);

    await chip.click();
    await expect(chip).toHaveCount(0);
  } finally {
    await api.post(`${API}/folder/${folder}/trash`);
  }
});
