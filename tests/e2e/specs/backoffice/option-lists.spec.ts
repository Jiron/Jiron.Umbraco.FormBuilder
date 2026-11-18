import { randomUUID } from 'node:crypto';
import type { APIRequestContext, Locator, Page, TestInfo } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, unique } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';
import { goToTab } from '../../support/places.ts';

// Library > Option lists (ROADMAP 12.1, IA 7, ADR-0214): a designer makes a list of every kind through the screens. The
// create dialog asks the kind first; each list is named, its contents set in its kind's editor, its options previewed
// and saved, and the list screen then shows it. The JSON address is the stub's, and Fetch now fetches it on the server;
// the upload reads a CSV file. Axe checks the list editor with its preview, beside it and under it.

const SECTION = '/umbraco/section/jfb-forms';
const LISTS = `${SECTION}/workspace/jfb-option-lists`;
const HOME = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';

interface Row {
  readonly key: string;
  readonly name: string;
  readonly kind: string;
}

interface List {
  readonly kind: string;
  readonly config: Record<string, unknown>;
  readonly items: { value: string }[];
  readonly cacheSeconds: number;
}

/** Umbraco's open picker and dialogs. */
const modal = (page: Page) => page.locator('umb-backoffice-modal-container');

async function rowsOf(api: APIRequestContext): Promise<Row[]> {
  const answer = await api.get(`${API}/option-list/library`, { params: { take: 500 } });
  expect(answer.ok(), await answer.text()).toBe(true);
  return ((await answer.json()) as { items: Row[] }).items;
}

async function listOf(api: APIRequestContext, key: string): Promise<List> {
  const answer = await api.get(`${API}/option-list/${key}`);
  expect(answer.ok(), await answer.text()).toBe(true);
  return (await answer.json()) as List;
}

/** Create option list, the kind chosen in the dialog: the new list's workspace opens. */
async function create(page: Page, kind: string, slug: string): Promise<Locator> {
  await page.getByRole('button', { name: 'Create option list' }).click();
  const dialog = modal(page).locator('jfb-new-option-list-modal');
  await expect(dialog.getByText('Where do its options come from?')).toBeVisible();
  await dialog.locator('umb-ref-item', { hasText: kind }).first().click();
  await expect(page).toHaveURL(new RegExp(`/workspace/jfb-option-list/create/${slug}$`));
  return page.locator('jfb-option-list-view');
}

/** Names the list in the header's box, where its alias is made from the name. */
async function name(page: Page, text: string): Promise<void> {
  const box = page.getByRole('textbox', { name: 'Name of the option list' });
  await box.fill(text);
  await box.press('Tab');
}

/** Saves the list: the workspace moves to the list's own address. */
async function save(page: Page): Promise<string> {
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(/\/workspace\/jfb-option-list\/edit\/[0-9a-f-]{36}/);
  return /\/edit\/([0-9a-f-]{36})/.exec(page.url())?.[1] ?? '';
}

/** The preview's radio buttons, once the preview has asked. */
const radios = (view: Locator) => view.getByRole('group', { name: 'As radio buttons' }).getByRole('radio');

/** A screenshot kept with the run's results, for the screens' review. */
async function look(page: Page, info: TestInfo, what: string): Promise<void> {
  await page.mouse.move(0, 0);
  const path = info.outputPath(`${what.replaceAll(' ', '-')}.png`);
  await page.screenshot({ path });
  await info.attach(what, { path, contentType: 'image/png' });
}

test('a designer makes an option list of every kind through the screens, each previewed, saved and listed', async ({
  page,
  loginAs,
  apiAs,
  request,
}, info) => {
  test.setTimeout(300_000);
  const api = await apiAs('designer');
  const prefix = unique('Lists');

  // The JSON address the stub answers.
  const address = `/options/${randomUUID()}.json`;
  const scripted = await request.put(`${STUB_URL}/_stub/answers`, {
    data: {
      method: 'GET',
      path: address,
      status: 200,
      body: JSON.stringify([
        { value: 'n', label: 'North' },
        { value: 's', label: 'South' },
        { value: 'e', label: 'East' },
      ]),
    },
  });
  expect(scripted.ok(), await scripted.text()).toBe(true);

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(SECTION);
  await goToTab(page, 'Library', 'Option lists');
  await expect(page).toHaveURL(new RegExp(`${LISTS}$`));
  await look(page, info, 'the list screen');

  const made = new Map<string, string>();

  // Fixed options, pasted one a line.
  let view = await create(page, 'Fixed options', 'manual');
  await look(page, info, 'a new list of fixed options');
  await name(page, `${prefix} sizes`);
  const editor = view.locator('jfb-option-editor');
  await editor.getByRole('button', { name: 'Paste options\u2026' }).click();
  await editor.getByLabel('One option a line').fill(['Small', 'Medium', 'Large'].join('\n'));
  await editor.getByRole('button', { name: 'Add these options' }).click();
  await expect(radios(view)).toHaveCount(3);
  await expect(view.locator('#as-dropdown option')).toHaveText(['Choose one', 'Small', 'Medium', 'Large']);
  // The box's headline names the rows, and the editor names them no second time (ROADMAP 22.4).
  await expect(editor.locator('p.label')).toHaveCount(0);
  await expect(editor.getByRole('list', { name: 'Fixed options', exact: true })).toBeVisible();
  made.set('manual', await save(page));
  await expect(page.locator('jfb-library-item-editor').getByText('Not used by any form')).toBeVisible();
  await axeInside(page, 'jfb-option-list-view');
  await look(page, info, 'a saved list of fixed options');

  // Pages: the ones below the demo's home page.
  await page.goto(LISTS);
  view = await create(page, 'Pages', 'content');
  await name(page, `${prefix} pages`);
  await expect(view.getByText('Its options are the pages below the page the form is on')).toBeVisible();
  await view.locator('umb-input-document').getByRole('button', { name: 'Choose' }).click();
  await modal(page).getByRole('button', { name: 'Home', exact: true }).click({ force: true });
  await modal(page).getByRole('button', { name: 'Choose', exact: true }).click();
  await expect(radios(view).first()).toBeVisible();
  await expect(view.locator('#as-dropdown option')).toContainText(['Events', 'Careers', 'Contact']);
  made.set('content', await save(page));

  // A data type's values: the first data type the site offers.
  await page.goto(LISTS);
  view = await create(page, "A data type's values", 'dataType');
  await name(page, `${prefix} data type`);
  const dataType = view.getByLabel('Data type');
  const firstType = await dataType.locator('option:not([value=""])').first().getAttribute('value');
  expect(firstType, 'a data type the demo has').toMatch(/^[0-9a-f-]{36}$/);
  await dataType.selectOption(firstType ?? '');
  await expect(view.locator('.preview')).not.toContainText('Loading the preview');
  made.set('dataType', await save(page));

  // A JSON address, the stub's, kept a day; Fetch now fetches it on the server.
  await page.goto(LISTS);
  view = await create(page, 'A JSON address', 'url');
  await name(page, `${prefix} regions`);
  const url = view.getByRole('textbox', { name: 'Address' });
  await url.fill(`${STUB_URL}${address}`);
  await url.press('Tab');
  await expect(radios(view)).toHaveCount(3);
  await expect(radios(view).first()).toHaveAccessibleName('North');
  await expect(view.getByLabel('Keep fetched options for')).toHaveValue('3600');
  await view.getByLabel('Keep fetched options for').selectOption({ label: '1 day' });
  await look(page, info, 'a new JSON address with its preview');
  made.set('url', await save(page));
  await expect(view.getByRole('button', { name: 'Fetch now' })).toBeVisible();
  await view.getByRole('button', { name: 'Fetch now' }).click();
  await expect(view.locator('.fetched p')).toHaveText(/^Fetched today at .+: 3 options\.$/);
  await axeInside(page, 'jfb-option-list-view');
  await look(page, info, 'a saved JSON address, fetched');

  // The same editor in a narrow window, its workspace under 900 px: the preview under the settings, still no problem
  // for axe. (Umbraco keeps its own width below a tablet's, where the section scrolls sideways as one page, 9.6a.)
  await page.setViewportSize({ width: 1024, height: 768 });
  const [settings, preview] = await Promise.all(
    [view.locator('uui-box').first(), view.locator('uui-box').last()].map((box) => box.boundingBox()),
  );
  expect(preview?.y ?? 0).toBeGreaterThan((settings?.y ?? 0) + (settings?.height ?? 0) - 1);
  await axeInside(page, 'jfb-option-list-view');
  await look(page, info, 'a saved JSON address in a narrow window');
  await page.setViewportSize({ width: 1440, height: 900 });

  // An upload: a CSV file's options.
  await page.goto(LISTS);
  view = await create(page, 'An upload', 'file');
  await name(page, `${prefix} branches`);
  await expect(view.getByText('No file yet.')).toBeVisible();
  await view.locator('#option-file').setInputFiles({
    name: 'branches.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from('north,North branch\nsouth,South branch\nharbour,Harbour branch\n', 'utf8'),
  });
  await expect(view.getByText('3 options from the last file')).toBeVisible();
  await expect(radios(view)).toHaveCount(3);
  made.set('file', await save(page));

  // Countries: every country, in the visitor's language; the preview shows the first eight and how many more.
  await page.goto(LISTS);
  view = await create(page, 'Countries', 'countries');
  await name(page, `${prefix} countries`);
  await expect(radios(view)).toHaveCount(8);
  await expect(view.getByText(/^and \d+ more$/)).toBeVisible();
  made.set('countries', await save(page));

  // A standard list: the months.
  await page.goto(LISTS);
  view = await create(page, 'A standard list', 'code');
  await name(page, `${prefix} months`);
  await view.getByLabel('Standard list').selectOption({ label: 'Months' });
  await expect(radios(view).first()).toHaveAccessibleName('January');
  await expect(view.getByLabel('Keep the options for')).toHaveValue('0');
  made.set('code', await save(page));

  // Every list is on the list screen with its kind, and holds what was set.
  await page.goto(LISTS);
  const table = page.locator('jfb-option-lists-screen table');
  const listed: readonly (readonly [string, string])[] = [
    ['sizes', 'Fixed options'],
    ['pages', 'Pages'],
    ['data type', "A data type's values"],
    ['regions', 'A JSON address'],
    ['branches', 'An upload'],
    ['countries', 'Countries'],
    ['months', 'A standard list'],
  ];
  for (const [label, kind] of listed) {
    const row = table.getByRole('row', { name: new RegExp(`^${prefix} ${label} `) });
    await expect(row.getByRole('cell').nth(1)).toHaveText(kind);
  }
  await look(page, info, 'the list screen with every kind');

  const rows = (await rowsOf(api)).filter((row) => row.name.startsWith(prefix));
  expect(rows.map((row) => row.kind).sort()).toEqual(
    ['code', 'content', 'countries', 'dataType', 'file', 'manual', 'url'].sort(),
  );
  const lists = Object.fromEntries(
    await Promise.all([...made].map(async ([kind, key]) => [kind, await listOf(api, key)] as const)),
  );
  expect(lists['manual']?.items.length).toBe(3);
  expect(lists['content']?.config['root']).toBe(HOME);
  expect(lists['dataType']?.config['dataType']).toBe(firstType);
  expect(lists['url']?.config['url']).toBe(`${STUB_URL}${address}`);
  expect(lists['url']?.cacheSeconds).toBe(86_400);
  expect(lists['file']?.items.map((item) => item.value)).toEqual(['north', 'south', 'harbour']);
  expect(lists['code']?.config).toEqual({ source: 'months' });
});
