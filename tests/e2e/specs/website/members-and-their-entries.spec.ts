import type { APIRequestContext, Browser, Locator, Page } from '@playwright/test';
import { randomInt, randomUUID } from 'node:crypto';
import { join } from 'node:path';
import { expect, test } from '../../fixtures.ts';
import { axe, axeInside } from '../../support/axe.ts';
import { inTheme } from '../../support/backoffice-look.ts';
import { pathOf } from '../../support/demo-content.ts';
import { signInMember } from '../../support/demo-members.ts';
import { fillForm, pressOnThePage } from '../../support/form.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { E2E_BASE_URL, e2eState } from '../../support/paths.ts';
import { MEMBERS, type Account } from '../../support/users.ts';

// Members and their entries (ROADMAP 14.3, ADR-0231), with the seeded members Mila and Moritz. A designer turns on "One
// entry per member" in a form's Availability group and publishes it; Mila sends the form, and the same page still open
// in another tab refuses her second entry with the notice "You've already sent this form", which a new visit shows in
// the form's place too, while Moritz may still send it. A form a designer makes anonymous keeps no member on the entry
// Mila sends while signed in. The member editor's Form entries view lists Mila's entry of the first form and not that
// of the anonymous one, and the entry names Mila, linked back to her editor. Axe finds no WCAG 2.2 A or AA violation in
// the Settings view, the notice or the member's view, nor in the member's view and the entry in Umbraco's dark theme.

test.describe.configure({ mode: 'serial' });

const SECTION = '/umbraco/section/jfb-forms';
const MANAGEMENT_API = '/umbraco/management/api/v1';
/** The home page, under which the specs' pages go, and the events page, whose page type and template they take. */
const HOME_KEY = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';
const EVENTS_KEY = '8603878d-af82-4867-92ba-ee28f9f12c86';
const ELLIPSIS = String.fromCharCode(0x2026);
/** The notice in the place of a form a member has sent, in English, whose texts use the plain apostrophe. */
const ALREADY_SENT = "You've already sent this form";

/** The names of the forms the first two tests make, which the last one looks for on Mila's editor. */
const made = { once: '', anonymous: '' };

/** Saves a look at the page for the visual review (ROADMAP "How to use"); the folder is git-ignored. */
async function look(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: join(e2eState, 'look', `${name}.png`), fullPage: true });
}

async function openSettings(page: Page, key: string): Promise<Locator> {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/workspace/jfb-form/edit/${key}/view/settings`);
  const view = page.locator('jfb-form-settings-view');
  await expect(view.locator('#group-submission')).toBeVisible();
  return view;
}

async function openGroup(view: Locator, group: string): Promise<void> {
  const head = view.locator(`#group-${group}`);
  if ((await head.getAttribute('aria-expanded')) !== 'true') await head.click();
  await expect(head).toHaveAttribute('aria-expanded', 'true');
}

/** Publishes the form from its workspace, the changes not saved yet with it, and waits until visitors get it. */
async function publish(page: Page, api: APIRequestContext, key: string): Promise<void> {
  await page.getByRole('button', { name: `Publish${ELLIPSIS}` }).click();
  const dialog = page.locator('jfb-publish-modal');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await expect
    .poll(async () => ((await (await api.get(`${API}/form/${key}`)).json()) as { isPublished: boolean }).isPublished)
    .toBe(true);
}

/** A form with one text field, its key, alias and name. */
async function newForm(api: APIRequestContext, name: string) {
  const key = await formWithFields(api, unique(name), [{ type: 'text', alias: 'name', label: { '': 'Your name' } }]);
  const form = (await (await api.get(`${API}/form/${key}`)).json()) as { alias: string; name: string };
  return { key, alias: form.alias, name: form.name };
}

/** A published page of the demo's page type under the home page that shows the form; its path in English. */
async function pageShowing(api: APIRequestContext, alias: string): Promise<string> {
  const events = (await (await api.get(`${MANAGEMENT_API}/document/${EVENTS_KEY}`)).json()) as {
    documentType: { id: string };
    template: { id: string } | null;
  };
  // Not named like a page of the menu, whose links other specs find by their names.
  const name = `Spec page ${randomInt(100_000, 999_999)}`;
  const key = randomUUID();
  const created = await api.post(`${MANAGEMENT_API}/document`, {
    data: {
      id: key,
      parent: { id: HOME_KEY },
      documentType: { id: events.documentType.id },
      template: events.template,
      values: [{ alias: 'form', value: alias, culture: null, segment: null }],
      variants: [
        { culture: 'en-US', segment: null, name },
        { culture: 'de-DE', segment: null, name },
      ],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const published = await api.put(`${MANAGEMENT_API}/document/${key}/publish`, {
    data: { publishSchedules: [{ culture: 'en-US' }] },
  });
  expect(published.ok(), await published.text()).toBe(true);
  return pathOf({ name, heading: '' }, 'en');
}

/** A page of the member's own browser, signed in on the demo's sign-in page. */
async function signedIn(browser: Browser, member: Account): Promise<Page> {
  const context = await browser.newContext({ baseURL: E2E_BASE_URL, locale: 'en-GB' });
  const page = await context.newPage();
  await signInMember(page, member.email, member.password);
  return page;
}

/** Opens the page once it is served; a page just published can take a moment to route. */
async function visit(page: Page, path: string): Promise<void> {
  await expect.poll(async () => (await page.goto(path))?.status(), { timeout: 30_000 }).toBe(200);
}

const formOf = (page: Page): Locator => page.locator('main form.jfb-form');

/** Sends the page's form with this name, with JavaScript, and waits for its outcome. */
async function send(page: Page, who: string): Promise<void> {
  const form = formOf(page);
  await fillForm(form, { name: who });
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeVisible();
}

interface StoredEntry {
  readonly state: string;
  readonly member: string | null;
}

/** The form's entries as the test host stores them, oldest first. */
async function entriesOf(page: Page, alias: string): Promise<StoredEntry[]> {
  const response = await page.request.get(`/jfb-testing/entries?form=${encodeURIComponent(alias)}`);
  expect(response.ok(), await response.text()).toBe(true);
  return ((await response.json()) as { entries: StoredEntry[] }).entries;
}

test('a designer turns on One entry per member, and a member who has sent sees so in its place and is refused a second entry', async ({
  page,
  browser,
  apiAs,
  loginAs,
  memberOf,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const form = await newForm(api, 'Reading seat');
  made.once = form.name;

  // The designer makes the form one for members, which shows the switch under the choice, and turns it on.
  await loginAs('designer');
  const view = await openSettings(page, form.key);
  await openGroup(view, 'availability');
  await expect(view.getByLabel('One entry per member')).toHaveCount(0);
  await view.getByLabel('Who can fill it in').selectOption('members');
  const once = view.getByLabel('One entry per member');
  await expect(once).not.toBeChecked();
  await once.check();
  await expect(view.locator('#one-per-member-help')).toHaveText(
    'A member who has sent it sees that in its place, and a second entry is refused.',
  );
  await axeInside(page, 'jfb-form-settings-view');
  await look(page, 'members-availability');
  await publish(page, api, form.key);
  const path = await pageShowing(await apiAs('admin'), form.alias);

  // Mila has the page open in two tabs, and sends the form in the first.
  const mila = await signedIn(browser, memberOf(0));
  const stale = await mila.context().newPage();
  try {
    await visit(mila, path);
    await visit(stale, path);
    await send(mila, 'Mila');

    // The second tab still shows the form: its send is refused, and the page says why in the form's place.
    const tried = formOf(stale);
    await fillForm(tried, { name: 'Mila again' });
    // The runtime's answer, read as it passes, since the post without JavaScript that follows replaces the page.
    const heard: { status: number; code?: string }[] = [];
    await stale.route(`**/jfb/api/v1/forms/${form.key}/entries`, async (route) => {
      const response = await route.fetch();
      heard.push({ status: response.status(), ...((await response.json()) as { code?: string }) });
      await route.fulfill({ response });
    });
    await pressOnThePage(tried.locator('button[data-jfb-nav="submit"]'));
    await expect.poll(() => heard.length).toBe(1);
    expect(heard[0], 'the runtime hears the refusal').toMatchObject({ status: 409, code: 'already_submitted' });
    const notice = stale.locator('main .jfb-notice');
    await expect(notice.locator('h2')).toHaveText(ALREADY_SENT);
    await expect(notice).toContainText('Each member can send it once.');
    await expect(formOf(stale)).toHaveCount(0);
    await axe(stale, 'the notice of a form already sent', true);
    await look(stale, 'members-already-sent');

    // A new visit shows the notice in the form's place too.
    await visit(mila, path);
    await expect(mila.locator('main .jfb-notice h2')).toHaveText(ALREADY_SENT);
    await expect(formOf(mila)).toHaveCount(0);
  } finally {
    await mila.context().close();
  }

  // Moritz may still send it: one entry per member, not one for the form.
  const moritz = await signedIn(browser, memberOf(1));
  try {
    await visit(moritz, path);
    await send(moritz, 'Moritz');
  } finally {
    await moritz.context().close();
  }

  const stored = await entriesOf(page, form.alias);
  expect(stored, 'one entry each').toHaveLength(2);
  expect(new Set(stored.map((entry) => entry.member)).size, 'of two members').toBe(2);
  expect(stored.every((entry) => entry.member !== null)).toBe(true);
});

test('a member signed in sends a form a designer made anonymous, and its entry keeps no member', async ({
  page,
  browser,
  apiAs,
  loginAs,
  memberOf,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const form = await newForm(api, 'Suggestion box');
  made.anonymous = form.name;

  await loginAs('designer');
  const view = await openSettings(page, form.key);
  await openGroup(view, 'privacy');
  const anonymous = view.locator('#anonymous');
  await expect(anonymous).not.toBeChecked();
  await anonymous.check();
  await expect(view.locator('#anonymous-help')).toHaveText(
    'Its entries keep no member, even when a member is signed in.',
  );
  await axeInside(page, 'jfb-form-settings-view');
  await publish(page, api, form.key);
  const path = await pageShowing(await apiAs('admin'), form.alias);

  const mila = await signedIn(browser, memberOf(0));
  try {
    await visit(mila, path);
    await send(mila, 'Mila');
  } finally {
    await mila.context().close();
  }

  const stored = await entriesOf(page, form.alias);
  expect(stored).toHaveLength(1);
  expect(stored[0]?.member, 'the member signed in is not kept (ADR-0231)').toBeNull();
});

test("the member editor's Form entries view lists what the member sent, and the entry names the member, linked back", async ({
  page,
  apiAs,
  loginAs,
}) => {
  test.setTimeout(180_000);
  expect(made.once && made.anonymous, 'the tests before have made their forms').toBeTruthy();
  const api = await apiAs('admin');
  const found = (await (
    await api.get(`${MANAGEMENT_API}/filter/member?filter=${encodeURIComponent(MEMBERS[0].email)}&skip=0&take=5`)
  ).json()) as { items: { id: string; email: string }[] };
  const member = found.items.find((one) => one.email === MEMBERS[0].email)?.id ?? '';
  expect(member, "Mila's key").not.toBe('');

  await loginAs('admin');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`/umbraco/section/member-management/workspace/member/edit/${member}`);
  await page.getByRole('tab', { name: 'Form entries' }).click();
  const view = page.locator('jfb-member-entries-view');
  const row = view.locator('tbody tr', { hasText: made.once });
  await expect(row).toHaveCount(1);
  await expect(row.locator('a')).toHaveText(/^Entry #\d+$/);
  await expect(row.locator('td').nth(2)).toHaveText('Approved');
  await expect(view.locator('tbody tr', { hasText: made.anonymous }), 'the anonymous form kept no member').toHaveCount(
    0,
  );
  await axeInside(page, 'jfb-member-entries-view');
  await look(page, 'members-entries-view');

  // On a phone the form and the time move under the entry, so nothing is squashed.
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(row.locator('.narrow')).toBeVisible();
  await expect(row.locator('.narrow')).toContainText(made.once);
  await look(page, 'members-entries-view-phone');
  await page.setViewportSize({ width: 1440, height: 900 });

  // The entry opens on its own screen, whose More details name Mila, linked to her editor.
  await row.locator('a').click();
  const panel = page.locator('jfb-entry-panel');
  await panel.getByRole('button', { name: 'More details' }).click();
  const link = panel.locator('dl.more a', { hasText: MEMBERS[0].name });
  await expect(link).toHaveAttribute('href', new RegExp(member));
  await look(page, 'members-entry-names-the-member');
  await link.click();
  await expect(page).toHaveURL(new RegExp(`/member/edit/${member}`));

  // In Umbraco's dark theme the view and the member's link keep their contrast.
  await inTheme(page, 'dark');
  await page.goto(`/umbraco/section/member-management/workspace/member/edit/${member}`);
  await page.getByRole('tab', { name: 'Form entries' }).click();
  await expect(view.locator('tbody tr', { hasText: made.once })).toHaveCount(1);
  await axeInside(page, 'jfb-member-entries-view');
  await view.locator('tbody tr', { hasText: made.once }).locator('a').click();
  await panel.getByRole('button', { name: 'More details' }).click();
  await expect(link).toBeVisible();
  await axeInside(page, 'jfb-entry-panel');
  await look(page, 'members-entry-names-the-member-dark');
});
