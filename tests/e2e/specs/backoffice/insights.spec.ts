import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';
import { startStaticSite, type StaticSite } from '../../support/static-site.ts';

// Insights (ROADMAP 11.5, IA 5.5, ADR-0211): a form of two pages on a headless page of its own origin sends its
// beacons as a visitor uses it (a view, a start, the field that stopped them, the page reached), which the host writes
// within its ten seconds; the form's Insights view then shows the numbers and the charts, each box switching to its
// table and keeping that for the user; and once the form's own switch in its Settings view's Privacy group is off and
// published, its page carries no address to count at and sends nothing.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';
const EVENTS = '/events';

let site: StaticSite | undefined;

test.beforeAll(async () => {
  site = await startStaticSite();
});

test.afterAll(async () => {
  await site?.stop();
});

interface Draft {
  readonly form: { readonly concurrencyVersion: number };
  readonly definition: {
    pages: unknown[];
    settings?: Record<string, Record<string, unknown>>;
  };
}

/** A headless form of two pages, Name and Email on the first and Phone on the second, published; its key. */
async function twoPages(api: APIRequestContext): Promise<string> {
  const key = await formWithFields(api, unique('Insights'), [
    { type: 'text', alias: 'name', label: { '': 'Name' }, required: true },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
  ]);
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as Draft;
  draft.definition.pages.push({
    key: crypto.randomUUID(),
    title: { '': 'Phone' },
    groups: [
      {
        key: crypto.randomUUID(),
        fields: [{ key: crypto.randomUUID(), type: 'text', alias: 'phone', label: { '': 'Phone' } }],
      },
    ],
  });
  draft.definition.settings = {
    ...draft.definition.settings,
    developer: { ...draft.definition.settings?.['developer'], headless: true },
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await publish(api, key);
  return key;
}

async function publish(api: APIRequestContext, key: string) {
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
}

/** The beacons a page sends from now on, as their bodies read. */
function beaconsOf(page: Page): unknown[] {
  const sent: unknown[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().endsWith(EVENTS))
      sent.push(JSON.parse(request.postData() ?? 'null'));
  });
  return sent;
}

/** The form's page on the static site, enhanced. */
async function openForm(page: Page, key: string) {
  await page.goto(`${site?.url ?? ''}/?${new URLSearchParams({ form: key, culture: 'en-US' })}`);
  const form = page.locator('jfb-form form.jfb-form');
  await expect(form).toBeVisible({ timeout: 60_000 });
  await expect.poll(() => form.evaluate((element) => window.JironFormBuilder?.get(element) !== undefined)).toBe(true);
  return form;
}

test('the beacons arrive, the Insights view fills, and turning the form off in its Privacy group stops them', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('admin');
  const key = await twoPages(api);

  // A visitor: the form is shown, touched, stopped at the email and then sent on to the second page.
  const sent = beaconsOf(page);
  const form = await openForm(page, key);
  await expect.poll(() => sent).toContainEqual({ type: 'view' });
  await form.locator('[name="name"]').fill('Ada');
  await form.locator('[name="email"]').fill('not an address');
  await form.getByRole('button', { name: 'Next' }).click();
  await expect.poll(() => sent).toContainEqual({ type: 'invalid', field: 'email', kind: 'invalid' });
  await form.locator('[name="email"]').fill('ada@visitor.jfb.test');
  await form.getByRole('button', { name: 'Next' }).click();
  await expect(form.locator('[name="phone"]')).toBeVisible();
  await expect.poll(() => sent.some((beacon) => (beacon as { type?: string }).type === 'page')).toBe(true);
  expect(
    sent.filter((beacon) => (beacon as { type?: string }).type === 'start'),
    "one start in the page's life",
  ).toHaveLength(1);
  expect(JSON.stringify(sent), 'no answer goes along').not.toContain('Ada');

  // The host writes them within its ten seconds.
  const today = new Date().toISOString().slice(0, 10);
  await expect
    .poll(
      async () =>
        (
          (await (await api.get(`${API}/form/${key}/insights`, { params: { from: today, to: today } })).json()) as {
            totals: { views: number };
          }
        ).totals.views,
      { timeout: 45_000, intervals: [2_000] },
    )
    .toBeGreaterThanOrEqual(1);

  // The Insights view: the numbers, the charts, and a box as its table, kept for the user.
  await loginAs('admin');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/insights`);
  const view = page.locator('jfb-form-insights-view');
  const stats = view.locator('dl.stats');
  await expect(stats).toContainText('Views', { timeout: 60_000 });
  await expect(stats.locator('.stat').first().locator('.number')).not.toHaveText('0');
  await expect(view.locator('jfb-insight-columns').first().locator('svg[role="img"]')).toBeVisible();
  const fields = view.locator('uui-box', { hasText: 'Fields that stop visitors most' });
  await expect(fields).toContainText('Email');
  await expect(fields).toContainText('mostly the format');
  const funnel = view.locator('uui-box', { hasText: 'Who reaches each page' });
  await expect(funnel).toContainText('Phone');
  await axeInside(page, 'jfb-form-insights-view');

  const kept = page.waitForResponse(
    (response) => response.url().endsWith('/insights/tables') && response.request().method() === 'PUT',
  );
  await fields.getByRole('button', { name: 'Show as table' }).click();
  expect((await kept).ok(), 'the choice is kept').toBe(true);
  await expect(fields.locator('table')).toBeVisible();
  await page.reload();
  await expect(view.locator('uui-box', { hasText: 'Fields that stop visitors most' }).locator('table')).toBeVisible({
    timeout: 60_000,
  });
  await view
    .locator('uui-box', { hasText: 'Fields that stop visitors most' })
    .getByRole('button', { name: 'Show as chart' })
    .click();

  // The form's own switch, the first of its Privacy group, off, saved and published.
  await page.goto(`${SECTION}/${key}/view/settings`);
  const settings = page.locator('jfb-form-settings-view');
  const head = settings.locator('#group-privacy');
  await expect(head).toBeVisible({ timeout: 60_000 });
  await expect(head).toContainText('Insights on');
  await head.click();
  const toggle = settings.getByRole('switch', { name: 'Count what visitors do with this form' });
  await expect(toggle).toBeChecked();
  await toggle.uncheck();
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
  await publish(api, key);

  // The page now carries no address to count at, and sends nothing.
  const visitor = await page.context().browser()!.newPage();
  const after = beaconsOf(visitor);
  const off = await openForm(visitor, key);
  await expect(off).not.toHaveAttribute('data-jfb-insights');
  await off.locator('[name="name"]').fill('Grace');
  await visitor.waitForTimeout(1_000);
  expect(after, 'no beacon once the form is off').toEqual([]);
  await visitor.close();
});
