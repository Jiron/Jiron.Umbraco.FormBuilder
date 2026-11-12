import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API } from '../../support/forms-api.ts';
import { headerOf } from '../../support/mail.ts';
import { ClickCounter, expectTaskClicks } from '../../support/top-tasks.ts';

// The global settings screens (ROADMAP 12.4a, IA 8 and 9, ADR-0218), one journey per section: an administrator opens
// each from the sidebar, changes what the section holds, saves, and finds it after a reload; a value that can't be kept
// is said under its box before anything is sent. General picks its time zone from the list and lets a site keep a value
// of its own until it uses the value for all sites again; Email sends a test mail of the saved sender; Spam protection
// names the keys a captcha lacks and the key that refuses the implicit consent; Files keeps its largest file under the
// host's cap; Integrations says a reference is not found yet; and Data collection's switch saves. Every section is put
// back as it was, so no other journey's site differs.

const SECTION = '/umbraco/section/jfb-forms';

interface Section {
  readonly version: number;
  readonly values: Record<string, unknown>;
  readonly overrides: readonly string[];
  readonly sites: readonly { readonly key: string; readonly name: string }[];
}

const read = async (api: APIRequestContext, section: string, site?: string) =>
  (await (await api.get(`${API}/settings/${section}${site ? `?site=${site}` : ''}`)).json()) as Section;

/** Puts a section back as it was, all sites' values and, with a site, its own. */
async function restore(
  api: APIRequestContext,
  section: string,
  before: Section,
  site?: { key: string; before: Section },
) {
  const now = await read(api, section);
  const back = await api.put(`${API}/settings/${section}`, { data: { version: now.version, values: before.values } });
  expect(back.ok(), await back.text()).toBe(true);
  if (!site) return;
  const own = await read(api, section, site.key);
  const values = Object.fromEntries(site.before.overrides.map((name) => [name, site.before.values[name]]));
  const kept = await api.put(`${API}/settings/${section}`, {
    data: { version: own.version, site: site.key, values, overrides: site.before.overrides },
  });
  expect(kept.ok(), await kept.text()).toBe(true);
}

/** Opens a settings screen from the sidebar, by its item's label, and waits for its first box; the item's click counts. */
async function open(page: Page, label: string, tag: string, clicks = new ClickCounter()): Promise<Locator> {
  await page.goto(SECTION);
  await clicks.click(page.locator(`umb-menu-item-layout[label="${label}"], uui-menu-item[label="${label}"]`).first());
  const screen = page.locator(tag);
  await expect(screen.locator('uui-box[headline]').first()).toBeVisible({ timeout: 60_000 });
  return screen;
}

const saved = (page: Page) =>
  expect(page.locator('uui-toast-notification').filter({ hasText: 'The settings are saved.' }).last()).toBeVisible();

const saveIn = (screen: Locator) => screen.getByRole('button', { name: 'Save', exact: true }).click();

test.describe.configure({ mode: 'serial' });

test('Settings \u203a General keeps draft saves, picks the time zone from the list, and lets a site keep its own', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('admin');
  const before = await read(api, 'general');
  const site = before.sites[0];
  expect(site, 'the demo has a site').toBeDefined();
  const siteBefore = await read(api, 'general', site!.key);
  await loginAs('admin');
  const screen = await open(page, 'General', 'jfb-settings-general');
  await expect(page).toHaveURL(/workspace\/jfb-settings-general$/);

  const days = screen.getByRole('textbox', { name: 'Keep draft saves for' });
  const count = screen.getByRole('textbox', { name: 'Always keep the newest' });
  const zone = screen.getByRole('combobox', { name: 'Time zone' });
  await expect(days).toHaveValue(String((before.values['history'] as { draftDays: number }).draftDays));
  await axeInside(page, 'jfb-settings-general');

  try {
    await days.fill('0');
    await days.blur();
    await expect(screen.locator('#setting-history-draftDays-why')).not.toHaveText('');
    await saveIn(screen);
    await expect(screen.locator('.alert')).toHaveText('Some settings need another value. Each one says why.');
    await expect(days).toBeFocused();
    expect((await read(api, 'general')).version).toBe(before.version);

    // The browser lists each zone as the server spells it; the one picked is the one saved.
    const buenosAires = await zone
      .locator('option')
      .evaluateAll(
        (options) =>
          (options as HTMLOptionElement[])
            .map((option) => option.value)
            .find((value) => value.endsWith('Buenos_Aires')) ?? '',
      );
    expect(buenosAires).not.toBe('');
    await days.fill('30');
    await count.fill('5');
    await zone.selectOption(buenosAires);
    await saveIn(screen);
    await saved(page);
    await page.reload();
    await expect(days).toHaveValue('30', { timeout: 60_000 });
    await expect(zone).toHaveValue(buenosAires);
    const general = await read(api, 'general');
    expect(general.values['history']).toEqual({ draftDays: 30, draftCount: 5 });
    expect(general.values['timeZone']).toBe(buenosAires);

    // A site sets its own zone, says so, and uses the value for all sites again.
    await screen.getByRole('combobox', { name: 'Site' }).selectOption(site!.key);
    await expect(screen.getByText('Settings every site shares are under All sites.')).toBeVisible();
    await expect(days).toHaveCount(0);
    await zone.selectOption('Europe/Lisbon');
    await expect(screen.getByText('This site has its own value.')).toBeVisible();
    await saveIn(screen);
    await saved(page);
    expect((await read(api, 'general', site!.key)).overrides).toContain('timeZone');
    await screen.getByRole('button', { name: 'Use the value for all sites' }).click();
    await expect(zone).toHaveValue(buenosAires);
    await saveIn(screen);
    await saved(page);
    expect((await read(api, 'general', site!.key)).overrides).not.toContain('timeZone');
    await axeInside(page, 'jfb-settings-general');
  } finally {
    await restore(api, 'general', before, { key: site!.key, before: siteBefore });
  }
});

test('Settings \u203a Email saves its sender and sends a test mail of it to the user who asks', async ({
  page,
  loginAs,
  apiAs,
  accountOf,
  waitForMail,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('admin');
  const before = await read(api, 'email');
  await loginAs('admin');
  // The administrator's top tasks 1 and 2 (docs/ux/top-tasks.md, ROADMAP 16.6): the sender is one click away, the
  // name and Save being the task's own, and a test mail one more.
  const clicks = new ClickCounter();
  const screen = await open(page, 'Email', 'jfb-settings-email', clicks);
  expectTaskClicks(clicks, 'Administrator', 2);
  await axeInside(page, 'jfb-settings-email');
  const admin = accountOf('admin');

  try {
    await screen.getByRole('textbox', { name: 'Name' }).fill('Nordwind settings test');
    await clicks.click(screen.getByRole('button', { name: 'Send test email' }));
    expectTaskClicks(clicks, 'Administrator', 1);
    await expect(
      page.locator('uui-toast-notification').filter({ hasText: `Test email sent to you (${admin.email}).` }),
    ).toBeVisible();
    const mail = await waitForMail((candidate) => headerOf(candidate.raw, 'From').includes('Nordwind settings test'));
    expect(headerOf(mail.raw, 'To')).toContain(admin.email);
    expect((await read(api, 'email')).values['senderName']).toBe('Nordwind settings test');
  } finally {
    await restore(api, 'email', before);
  }
});

test('Settings \u203a Spam protection names the keys a captcha lacks and the key that refuses the implicit consent', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('admin');
  const before = await read(api, 'protection');
  await loginAs('admin');
  const screen = await open(page, 'Spam protection', 'jfb-settings-protection');
  await axeInside(page, 'jfb-settings-protection');

  try {
    await expect(screen.getByRole('radio', { name: 'reCAPTCHA v2' })).toBeDisabled();
    await expect(
      screen.getByText('No keys yet. A developer adds them in JironFormBuilder:Captcha:RecaptchaV2.'),
    ).toBeVisible();
    await expect(screen.getByRole('radio', { name: 'Load without asking' })).toBeDisabled();
    await expect(
      screen.getByText('Locked by configuration: JironFormBuilder:Privacy:AllowImplicitCaptchaConsent'),
    ).toBeVisible();

    await expect(screen.getByRole('textbox', { name: 'Puzzle strength' })).toHaveCount(0);
    await screen.getByRole('radio', { name: 'Standard, with a proof-of-work check' }).check();
    await expect(screen.getByRole('textbox', { name: 'Puzzle strength' })).toHaveValue('16');
    await screen.getByRole('radio', { name: 'Follow a Cookiebot category' }).check();
    await screen.getByRole('combobox', { name: 'Cookiebot category' }).selectOption('marketing');
    await saveIn(screen);
    await saved(page);
    const now = await read(api, 'protection');
    expect([now.values['level'], now.values['captchaConsent'], now.values['cookiebotCategory']]).toEqual([
      'proofOfWork',
      'cookiebot',
      'marketing',
    ]);
    await page.reload();
    await expect(screen.getByRole('combobox', { name: 'Cookiebot category' })).toHaveValue('marketing', {
      timeout: 60_000,
    });
  } finally {
    await restore(api, 'protection', before);
  }
});

test("Settings \u203a Files keeps its types and its largest file under the host's cap", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('admin');
  const before = await read(api, 'files');
  await loginAs('admin');
  const screen = await open(page, 'Files', 'jfb-settings-files');
  await axeInside(page, 'jfb-settings-files');
  const size = screen.getByRole('textbox', { name: 'Largest file' });

  try {
    await expect(
      screen.getByText('Host configuration allows at most 50 MB: JironFormBuilder:Files:MaxFileBytes'),
    ).toBeVisible();
    await size.fill('60');
    await size.blur();
    await expect(screen.locator('#setting-maxFileBytes-why')).toContainText('up to 50');
    await size.fill('20');
    await screen.getByRole('checkbox', { name: 'Archives' }).uncheck();
    await saveIn(screen);
    await saved(page);
    const now = await read(api, 'files');
    expect(now.values['maxFileBytes']).toBe(20 * 1_048_576);
    expect(now.values['allowedTypes']).toEqual(['images', 'pdf', 'office', 'text']);
    await page.reload();
    await expect(size).toHaveValue('20', { timeout: 60_000 });
    await expect(screen.getByRole('checkbox', { name: 'Archives' })).not.toBeChecked();
  } finally {
    await restore(api, 'files', before);
  }
});

test('Settings \u203a Integrations takes a secret only as a reference, and says one is not found yet', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('admin');
  const before = await read(api, 'integrations');
  await loginAs('admin');
  const screen = await open(page, 'Integrations', 'jfb-settings-integrations');
  await axeInside(page, 'jfb-settings-integrations');
  const secret = screen.getByRole('textbox', { name: 'Signing secret' });

  try {
    await secret.fill('https://hooks.example.test/abc');
    await secret.blur();
    await expect(screen.locator('#setting-signingSecret-why')).toContainText('never the secret itself');
    await secret.fill('$JironFormBuilder:Secrets:SettingsTest');
    await secret.blur();
    await saveIn(screen);
    await saved(page);
    await expect(
      screen.getByText('Not in host configuration yet: a developer adds JironFormBuilder:Secrets:SettingsTest.'),
    ).toBeVisible();
    expect((await read(api, 'integrations')).values['signingSecret']).toBe('$JironFormBuilder:Secrets:SettingsTest');
  } finally {
    await restore(api, 'integrations', before);
  }
});

test('Privacy \u203a Data collection saves the insights switch', async ({ page, loginAs, apiAs }) => {
  test.setTimeout(120_000);
  const api = await apiAs('admin');
  const before = await read(api, 'privacy');
  await loginAs('admin');
  const screen = await open(page, 'Data collection', 'jfb-settings-privacy');
  await axeInside(page, 'jfb-settings-privacy');
  const insights = screen.getByRole('switch', { name: 'Count what visitors do with the forms' });

  try {
    await expect(insights).toBeChecked({ checked: before.values['insights'] === true });
    await insights.click();
    await saveIn(screen);
    await saved(page);
    expect((await read(api, 'privacy')).values['insights']).toBe(before.values['insights'] !== true);
  } finally {
    await restore(api, 'privacy', before);
  }
});
