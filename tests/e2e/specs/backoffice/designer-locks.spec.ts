import type { APIRequestContext } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { LOCKS_BASE_URL, LOCKS_PORT } from '../../support/paths.ts';
import { readSecrets } from '../../support/users.ts';

// The designer locks in the inspector (ROADMAP 9.4a, ADR-0060), on a host of its own whose configuration fixes the text
// type's maximum length, hides its prefix, and makes every dropdown take several options, three at most: a text field's inspector shows the
// maximum length read-only, with its value and the configuration key behind it, and no prefix at all; and checkboxes
// that become a dropdown (ROADMAP 9.4d, ADR-0174) are told before the change that the site sets the most options, which
// the change neither carries over nor asks for. The run's host stays without locks, since the demo's own forms give
// their text fields a prefix.

const SECTION = '/umbraco/section/jfb-forms';
const LOCKS = 'JironFormBuilder__Designer__SettingLocks';

test.describe.configure({ mode: 'serial', timeout: 120_000 });
test.use({ baseURL: LOCKS_BASE_URL });

let host: Host | undefined;

test.beforeAll(async () => {
  test.setTimeout(600_000);
  host = await startHost({
    name: `host-${LOCKS_PORT}`,
    port: LOCKS_PORT,
    env: {
      ...hostEnvironment(readSecrets(), { deliveryApi: false }),
      // The run's host holds the stub's port; this one needs no stub.
      JironFormBuilderTesting__StubPort: '0',
      [`${LOCKS}__0__Kind`]: 'field',
      [`${LOCKS}__0__Type`]: 'text',
      [`${LOCKS}__0__Setting`]: 'maxLength',
      [`${LOCKS}__0__Value`]: '120',
      [`${LOCKS}__1__Kind`]: 'field',
      [`${LOCKS}__1__Type`]: 'text',
      [`${LOCKS}__1__Setting`]: 'prefix',
      [`${LOCKS}__1__Hidden`]: 'true',
      [`${LOCKS}__2__Kind`]: 'field',
      [`${LOCKS}__2__Type`]: 'dropdown',
      [`${LOCKS}__2__Setting`]: 'maxSelections',
      [`${LOCKS}__2__Value`]: '3',
      // Most options chosen fits only a dropdown of several choices, so the site makes every dropdown one.
      [`${LOCKS}__3__Kind`]: 'field',
      [`${LOCKS}__3__Type`]: 'dropdown',
      [`${LOCKS}__3__Setting`]: 'multiple',
      [`${LOCKS}__3__Value`]: 'true',
    },
  });
  const wait = await waitForStartupWork(host.logFile);
  if (wait.state !== 'done') throw new Error(`The locks host's start-up work ended as "${wait.state}".`);
});

test.afterAll(async () => {
  await host?.stop();
});

test('a locked setting shows read-only with its value and key, and a hidden one not at all', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer', LOCKS_BASE_URL);
  const key = await formWithFields(api, unique('Locks'), [{ type: 'text', alias: 'name', label: { '': 'Name' } }]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: /^Name, / }).click();
  const inspector = design.locator('jfb-designer-inspector');
  const closed = inspector.locator('.section-head button[aria-expanded="false"]');
  while ((await closed.count()) > 0) await closed.first().click();

  const locked = inspector.locator('[data-locked="maxLength"]');
  await expect(locked).toContainText('Maximum length');
  await expect(locked.locator('.locked')).toHaveText('120');
  await expect(locked.locator('code')).toHaveText('JironFormBuilder:Designer:SettingLocks:0');
  await expect(inspector.getByRole('textbox', { name: 'Maximum length' })).toHaveCount(0);
  await expect(inspector.getByText('Text before')).toHaveCount(0);
  await expect(inspector.getByLabel('Text after')).toBeVisible();
  await expect(inspector.getByRole('button', { name: /^Validation/ })).toHaveAttribute('aria-expanded', 'true');
  await axeInside(page, 'jfb-designer-inspector');

  // Closed, the section says the locked value.
  await inspector.getByRole('button', { name: /^Validation/ }).click();
  await expect(inspector.getByRole('button', { name: /^Validation/ })).toContainText('Maximum length: 120');
});

/** The settings of a form's first field in its saved draft. */
async function firstFieldOf(api: APIRequestContext, key: string) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: { type: string; settings?: Record<string, unknown> }[] }[] }[] };
  };
  return draft.definition.pages[0]?.groups[0]?.fields[0];
}

test('a change into a type whose setting the site locks neither carries that setting over nor asks for it', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer', LOCKS_BASE_URL);
  const key = await formWithFields(api, unique('Locked change'), [
    {
      type: 'checkboxes',
      alias: 'topics',
      label: { '': 'Topics' },
      settings: { minSelections: 2, maxSelections: 5, other: true },
      options: { source: 'items', items: [{ value: 'a' }, { value: 'b' }, { value: 'c' }] },
    },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}`);
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: /^Topics, / }).click();
  await design.locator('jfb-designer-inspector').getByRole('button', { name: 'Change type' }).click();
  const dialog = page.locator('jfb-change-type-modal');
  await dialog.getByRole('combobox', { name: 'New type' }).selectOption('dropdown');

  const preview = dialog.locator('.preview');
  await expect(preview.getByRole('heading')).toHaveText(['What goes', 'Set by the site instead']);
  await expect(preview.getByRole('listitem')).toHaveText(['Offer "Other": On', 'Maximum options chosen: 3']);
  await expect(preview).not.toContainText('Needed before it publishes');
  await axeInside(page, 'jfb-change-type-modal');
  await dialog.getByRole('button', { name: 'Change type', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  const inspector = design.locator('jfb-designer-inspector');
  await expect(inspector.getByRole('heading', { name: 'Dropdown field' })).toBeVisible();
  const closed = inspector.locator('.section-head button[aria-expanded="false"]');
  while ((await closed.count()) > 0) await closed.first().click();
  await expect(inspector.locator('[data-locked="maxSelections"] .locked')).toHaveText('3');
  await expect(inspector.getByLabel('Minimum options chosen')).toHaveValue('2');
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect.poll(async () => (await firstFieldOf(api, key))?.type).toBe('dropdown');
  expect((await firstFieldOf(api, key))?.settings).toEqual({ minSelections: 2 });
});
