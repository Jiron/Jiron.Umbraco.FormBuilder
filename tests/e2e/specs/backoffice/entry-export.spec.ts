import { readFileSync } from 'node:fs';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';

// The exports of a form's entries (ROADMAP 11.4, IA 6.4, ADR-0210), as an entry handler, who may export but not see
// sensitive fields: Export selected... on three ticked entries saves a CSV file that holds those three and no other, and
// no sensitive field; Export... takes the list's search, saves JSON, and opens with that format the next time; the entry
// panel's Print asks for the printable view of that one entry.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

/** The rows of a CSV file: a byte-order mark first, fields apart by commas, quoted where they hold one. */
function csvRows(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  const body = text.replace(/^\ufeff/, '');
  for (let index = 0; index < body.length; index++) {
    const letter = body[index];
    if (quoted) {
      if (letter === '"' && body[index + 1] === '"') {
        field += '"';
        index++;
      } else if (letter === '"') quoted = false;
      else field += letter;
    } else if (letter === '"' && field.length === 0) quoted = true;
    else if (letter === ',') {
      row.push(field);
      field = '';
    } else if (letter === '\r' && body[index + 1] === '\n') {
      row.push(field);
      rows.push(row);
      row = [];
      field = '';
      index++;
    } else field += letter;
  }

  if (field || row.length > 0) rows.push([...row, field]);
  return rows;
}

test('Export selected... exports only the ticked entries, Export... the search, and Print one entry', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('admin');
  const seeded = await api.post('/jfb-testing/entries-form', {
    data: { count: 12, name: `Exports ${String(Date.now())}` },
    timeout: 120_000,
  });
  expect(seeded.ok(), await seeded.text()).toBe(true);
  const { key } = (await seeded.json()) as { key: string };

  // The print frame's document, as the page hands it over: the frame goes once the print dialog closes.
  await page.addInitScript(() => {
    const srcdoc = Object.getOwnPropertyDescriptor(HTMLIFrameElement.prototype, 'srcdoc');
    Object.defineProperty(HTMLIFrameElement.prototype, 'srcdoc', {
      configurable: true,
      get(this: HTMLIFrameElement) {
        return srcdoc?.get?.call(this) as string;
      },
      set(this: HTMLIFrameElement, value: string) {
        (window as unknown as { jfbPrinted?: string }).jfbPrinted = value;
        srcdoc?.set?.call(this, value);
      },
    });
  });
  await loginAs('entryHandler');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/entries`);
  const view = page.locator('jfb-entries-view');
  const rows = view.locator('tbody tr');
  await expect(rows).toHaveCount(12, { timeout: 60_000 });

  // Three ticked entries, from the bulk bar's "...".
  const names: string[] = [];
  for (const index of [0, 3, 7]) {
    await rows.nth(index).locator('uui-checkbox').click();
    names.push((await rows.nth(index).locator('th .name').innerText()).replace(/,\s*unseen$/, '').trim());
  }

  await expect(view.locator('.bulk .selected')).toHaveText('3 selected');
  await view
    .locator('.bulk')
    .getByRole('button', { name: 'More actions for the selected entries', exact: true })
    .click();
  await view.locator('#bulk-more').getByRole('button', { name: 'Export selected\u2026', exact: true }).click();
  const sidebar = page.locator('jfb-entry-export-modal');
  await expect(sidebar.getByText('3 entries selected', { exact: true })).toBeVisible();
  await expect(sidebar.getByText('Sensitive fields are left out.', { exact: true })).toBeVisible();
  await axeInside(page, 'jfb-entry-export-modal');

  const downloading = page.waitForEvent('download');
  await sidebar.getByRole('button', { name: 'Export 3 entries', exact: true }).click();
  const download = await downloading;
  expect(download.suggestedFilename()).toMatch(/-entries\.csv$/);
  const csv = readFileSync(await download.path(), 'utf8');
  expect(csv.startsWith('\ufeff'), 'a byte-order mark').toBe(true);
  const table = csvRows(csv);
  const header = table[0] ?? [];
  const nameColumn = header.indexOf('Name');
  expect(header.slice(0, 4)).toEqual(['Entry', 'Reference', 'State', 'Received']);
  expect(header, 'no sensitive field').not.toContain('Salary');
  expect(table.slice(1).map((row) => row[nameColumn])).toEqual(names);
  await expect(sidebar).toHaveCount(0);

  // Export... takes the list's search; JSON is the format chosen, and the sidebar opens with it the next time.
  await view.locator('.bulk').getByRole('button', { name: 'Clear selection', exact: true }).click();
  const search = view.locator('uui-input.search input');
  await search.fill('Person 0005');
  await search.press('Enter');
  await expect(rows).toHaveCount(1);
  await view.getByRole('button', { name: 'Export\u2026', exact: true }).click();
  await expect(sidebar.getByText('1 entry that matches the filters', { exact: true })).toBeVisible();
  const format = sidebar.locator('uui-select');
  await format.locator('select').selectOption('json');
  await expect(sidebar.getByRole('button', { name: 'More options', exact: true })).toHaveCount(0);
  const json = page.waitForEvent('download');
  await sidebar.getByRole('button', { name: 'Export 1 entry', exact: true }).click();
  const exported = JSON.parse(readFileSync(await (await json).path(), 'utf8')) as { answers: { name: string } }[];
  expect(exported.map((entry) => entry.answers.name)).toEqual(['Person 0005']);
  await view.getByRole('button', { name: 'Export\u2026', exact: true }).click();
  await expect(format.locator('select')).toHaveValue('json');
  await sidebar.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(sidebar).toHaveCount(0);

  // The panel's Print asks for the printable view of its one entry.
  await rows.first().locator('.name').click();
  const panel = view.locator('jfb-entry-panel');
  await expect(panel.locator('h2')).toHaveText(/Entry #\d+/);
  const printing = page.waitForRequest(
    (request) => request.method() === 'POST' && request.url().endsWith(`/form/${key}/entries/export/file`),
  );
  const printed = page.waitForResponse((response) => response.url().endsWith(`/form/${key}/entries/export/file`));
  await panel.getByRole('button', { name: 'More actions for this entry', exact: true }).click();
  await panel.locator('#panel-more').getByRole('button', { name: 'Print', exact: true }).click();
  const body = (await printing).postDataJSON() as { format: string; entries: string[]; sensitive: boolean };
  expect([body.format, body.entries.length, body.sensitive]).toEqual(['print', 1, false]);
  expect((await printed).status()).toBe(200);
  await expect
    .poll(() => page.evaluate(() => (window as unknown as { jfbPrinted?: string }).jfbPrinted ?? ''))
    .toContain('Person 0005');
});
