import { randomUUID } from 'node:crypto';
import AxeBuilder from '@axe-core/playwright';
import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, created, unique } from '../../support/forms-api.ts';
import { fillForm, logicCome, pressOnThePage } from '../../support/form.ts';
import { bodyOf, headerOf } from '../../support/mail.ts';
import { E2E_BASE_URL } from '../../support/paths.ts';

// Save and continue later (ROADMAP 8.6b, ADR-0163), on a copy of the demo's job application of three pages, shown on a
// page of its own so the demo's form stays as the other specs know it. The editor turns it on in the form's Settings view,
// where days out of range say their message; a visitor saves on the last page and is told until when the link works; the
// link in the mail brings the answers and that page back in a fresh browser, takes its token out of the address, asks
// again for the file and shows the consent unticked; the entry sent from it ends the link.

const SECTION = '/umbraco/section/jfb-forms';
const PDF = Buffer.from('%PDF-1.7\n');
const RUN = Date.now().toString(36);

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const shownTitle = (form: Locator): Locator => form.locator('[data-jfb-page]:not([hidden]) .jfb-title');

async function axe(page: Page, what: string): Promise<void> {
  const result = await new AxeBuilder({ page })
    .include('main')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    result.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    ),
    `${what}: axe`,
  ).toEqual([]);
}

/** The last day a link of so many days from now works, as the runtime writes it in English. */
const lastDay = (days: number): string =>
  new Intl.DateTimeFormat('en-US', { dateStyle: 'long' }).format(new Date(Date.now() + days * 86_400_000));

test('a form saved for later on its last page comes back from the link in the mail: its answers and page, its file asked again, its consent unticked', async ({
  page,
  browser,
  request,
  apiAs,
  loginAs,
  waitForMail,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('designer');
  const found = (await (await api.get(`${API}/form/search`, { params: { text: 'application', take: 50 } })).json()) as {
    items: { key: string; alias?: string | null }[];
  };
  const application = found.items.find((item) => item.alias === 'application')?.key;
  expect(application, "the demo's job application").toBeTruthy();
  const name = unique('Application for later');
  const key = await created(
    await api.post(`${API}/form/${application}/duplicate`, { data: { name, includeActions: false } }),
    name,
  );
  const alias = ((await (await api.get(`${API}/form/${key}`)).json()) as { alias: string }).alias;
  const placed = await request.post('/jfb-testing/form-page', { data: { form: alias } });
  expect(placed.ok(), await placed.text()).toBe(true);
  const { path } = (await placed.json()) as { path: string };

  // The editor turns it on under Submission's More options; days out of range say their message there.
  await loginAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}/view/settings`);
  const view = page.locator('jfb-form-settings-view');
  await expect(view.locator('#group-submission')).toBeVisible();
  await view.getByRole('button', { name: 'More options (6)' }).click();
  const offered = view.getByRole('switch', { name: 'Save and continue later' });
  await expect(offered).not.toBeChecked();
  await offered.check();
  const days = view.getByLabel('Link valid for (days)');
  await expect(days).toHaveValue('30');
  await days.fill('400');
  await days.press('Tab');
  await expect(view.locator('#resume-days-why-0')).toHaveText('Enter a whole number from 1 to 365.');
  await expect(days).toHaveAttribute('aria-invalid', 'true');
  await days.fill('14');
  await days.press('Tab');
  await expect(view.locator('#resume-days-why-0')).toHaveCount(0);
  await axeInside(page, 'jfb-form-settings-view');
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().endsWith(`/form/${key}/draft`),
  );
  await page.getByRole('button', { name: 'Save draft' }).click();
  expect((await saved).ok(), 'the draft is saved').toBe(true);
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { settings: { submission: Record<string, unknown> } };
  };
  expect(draft.definition.settings.submission['resume']).toEqual({ days: 14 });
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);

  // A visitor fills in the three pages, the file and the consent, and saves on the last page.
  const visitor = await browser.newContext({ baseURL: E2E_BASE_URL, viewport: { width: 1280, height: 900 } });
  const tab = await visitor.newPage();
  await tab.goto(path);
  const form = formOn(tab);
  await form.locator('[name="fullName.first"]').fill('Ada');
  await form.locator('[name="fullName.last"]').fill(`Lovelace ${RUN}`);
  await fillForm(form, { email: 'ada@visitor.jfb.test', post: 'assistant' });
  await logicCome(form);
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your references');
  await fillForm(form, {
    'referees[0].refName': 'Grace Hopper',
    'referees[0].refContact': 'grace@visitor.jfb.test',
    'referees[1].refName': 'Mary Somerville',
    'referees[1].refContact': '+44 20 7946 0000',
  });
  await pressOnThePage(form.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(form)).toHaveText('Your letter');
  const uploaded = tab.waitForResponse((response) => response.url().includes('/uploads'));
  await form
    .locator('input[type="file"][name="cv"]')
    .setInputFiles({ name: 'cv.pdf', mimeType: 'application/pdf', buffer: PDF });
  expect((await uploaded).ok()).toBe(true);
  await form.locator('[name="letter"]').fill(`I mend books (${RUN})`);
  await form.locator('[data-jfb-alias="privacy"] label').first().click();

  // The link sits with the form's buttons; its panel takes their place and says, before the address, what is kept.
  const buttons = form.locator(':scope > .jfb-navigation');
  const open = buttons.getByRole('button', { name: 'Save and continue later' });
  await expect(open).toBeVisible();
  await open.click();
  const panel = form.getByRole('group', { name: 'Save and continue later' });
  await expect(panel).toBeVisible();
  await expect(buttons).toBeHidden();
  await expect(panel.locator(':scope > p')).toHaveText(
    'We email you a link back to this page, valid for 14 days. Files, consents and sensitive answers are not kept.',
  );
  const address = panel.getByLabel('Email address');
  await expect(address).toBeFocused();
  await axe(tab, 'the saving, open');
  const mailbox = `later-${randomUUID()}@visitor.jfb.test`;
  await panel.getByRole('button', { name: 'Send the link' }).click();
  await expect(panel.locator('.jfb-error')).toHaveText('Enter an email address');
  await expect(address).toHaveAttribute('aria-invalid', 'true');
  await address.fill(mailbox);
  await panel.getByRole('button', { name: 'Send the link' }).click();
  const savedNotice = form.locator(':scope > [data-jfb-notice="resume_saved"]');
  await expect(savedNotice).toBeFocused();
  await expect(savedNotice.getByRole('heading')).toHaveText('We have emailed you a link');
  await expect(savedNotice.locator('p')).toHaveText(
    `It went to ${mailbox} and works until ${lastDay(14)}. You can close this page now, or carry on.`,
  );
  await expect(panel).toBeHidden();
  await expect(buttons).toBeVisible();
  await axe(tab, 'the saving, saved');
  await visitor.close();

  // The link in the mail, in a fresh browser: the answers and the last page come back, with the notice.
  const mail = await waitForMail((each) => headerOf(each.raw, 'To').includes(mailbox), 60_000);
  const href = /href="([^"]*#jfb-resume=[^"]+)"/.exec(bodyOf(mail.raw, 'text/html'))?.[1]?.replaceAll('&amp;', '&');
  expect(href, 'the mail holds its link').toBeTruthy();
  const link = new URL(href!);
  expect(link.pathname, 'back to the page the form is on').toBe(path);
  const later = await browser.newContext({ baseURL: E2E_BASE_URL, viewport: { width: 1280, height: 900 } });
  const back = await later.newPage();
  await back.goto(`${link.pathname}${link.hash}`);
  const again = formOn(back);
  const notice = again.locator(':scope > [data-jfb-notice="resume_restored"]');
  await expect(notice).toBeFocused();
  await expect(notice.getByRole('heading')).toHaveText('Welcome back');
  expect(await back.evaluate(() => Object.keys(sessionStorage)), 'the token, in this tab only').toEqual([
    `jfb-resume:${key}`,
  ]);
  expect(await back.evaluate(() => location.hash), 'the token leaves the address at once').toBe('');
  await expect(shownTitle(again)).toHaveText('Your letter');
  await expect(again.locator('[name="letter"]')).toHaveValue(`I mend books (${RUN})`);
  await expect(
    again.locator('[data-jfb-alias="privacy"] input[type="checkbox"]'),
    'a consent is asked again',
  ).not.toBeChecked();
  expect(
    await again
      .locator('input[type="file"][name="cv"]')
      .evaluate((input) => (input as HTMLInputElement).files?.length ?? 0),
    'the file is asked for again',
  ).toBe(0);
  await expect(again.getByText('cv.pdf')).toHaveCount(0);
  await axe(back, 'a form taken up from its link');

  // The earlier pages hold their answers too.
  await pressOnThePage(again.locator('button[data-jfb-nav="back"]'));
  await expect(shownTitle(again)).toHaveText('Your references');
  await expect(again.locator('[name="referees[0].refName"]')).toHaveValue('Grace Hopper');
  await pressOnThePage(again.locator('button[data-jfb-nav="back"]'));
  await expect(again.locator('[name="fullName.last"]')).toHaveValue(`Lovelace ${RUN}`);
  await expect(again.locator('[name="email"]')).toHaveValue('ada@visitor.jfb.test');

  // Sent from it, the entry ends the link: opened again, it says so.
  await pressOnThePage(again.locator('button[data-jfb-nav="next"]'));
  await pressOnThePage(again.locator('button[data-jfb-nav="next"]'));
  await expect(shownTitle(again)).toHaveText('Your letter');
  await again.locator('[data-jfb-alias="privacy"] label').first().click();
  await pressOnThePage(again.locator('button[data-jfb-nav="submit"]'));
  await expect(back.locator('main .jfb-form[data-jfb-form] > [data-jfb-outcome]')).toBeFocused();
  expect(await back.evaluate(() => Object.keys(sessionStorage)), 'gone with the entry').toEqual([]);
  await back.goto(`${link.pathname}${link.hash}`);
  const gone = formOn(back).locator(':scope > [data-jfb-notice="resume_expired"]');
  await expect(gone).toBeFocused();
  await expect(gone.getByRole('heading')).toHaveText('This link no longer works');
  await expect(formOn(back).locator('[name="letter"]')).toHaveValue('');
  await later.close();
});
