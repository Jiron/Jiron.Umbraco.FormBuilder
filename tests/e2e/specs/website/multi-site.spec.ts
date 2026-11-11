import type { APIRequestContext, Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { join } from 'node:path';
import { expect, test } from '../../fixtures.ts';
import { axe } from '../../support/axe.ts';
import { fillForm, pressOnThePage } from '../../support/form.ts';
import { API, formWithFields, unique } from '../../support/forms-api.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { bodyOf, headerOf, waitForMailIn } from '../../support/mail.ts';
import { e2eState, SITES_BASE_URL, SITES_PORT } from '../../support/paths.ts';
import { readSecrets } from '../../support/users.ts';

// Two sites on their own names (ROADMAP 14.4, ADR-0008, ADR-0043, ADR-0232), on a host of their own: beside the demo's
// root, an administrator makes Site A on site-a.localhost and Site B on site-b.localhost, each with a page that shows
// the same form, and gives each site a send button of its own and Site B a time zone and a sender. Each site's page shows
// its own button in Edge; a headless call on Site B's name, which sends its Host header itself, gets Site B's time zone
// and a call on Site A's name the shared one; a visitor's send on Site B mails from Site B's sender, with its links on
// Site B's name. Site B deleted for good takes its own settings with it, audited, and Site A keeps its own.

test.describe.configure({ mode: 'serial', timeout: 180_000 });
test.use({ baseURL: SITES_BASE_URL });

const MANAGEMENT_API = '/umbraco/management/api/v1';
/** The demo's home page, whose type and template the sites' roots take, and its events page, whose type the pages take. */
const HOME_KEY = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';
const EVENTS_KEY = '8603878d-af82-4867-92ba-ee28f9f12c86';
const SITE_A = `site-a.localhost:${SITES_PORT}`;
const SITE_B = `site-b.localhost:${SITES_PORT}`;
const ZONE_OF_B = 'Pacific/Auckland';

let host: Host | undefined;

interface Site {
  readonly root: string;
  readonly page: string;
}

/** What the tests share: the form both sites show, and the sites. */
const made: { form?: { key: string; alias: string }; a?: Site; b?: Site } = {};

test.beforeAll(async () => {
  test.setTimeout(600_000);
  host = await startHost({
    name: `host-${SITES_PORT}`,
    port: SITES_PORT,
    env: {
      ...hostEnvironment(readSecrets(), { deliveryApi: false }),
      // The run's host holds the stub's port; this one needs no stub.
      JironFormBuilderTesting__StubPort: '0',
    },
  });
  const wait = await waitForStartupWork(host.logFile);
  if (wait.state !== 'done') throw new Error(`The host's start-up work ended as "${wait.state}".`);
});

test.afterAll(async () => {
  await host?.stop();
});

/** Saves a look at the page for the visual review; the folder is git-ignored. */
async function look(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: join(e2eState, 'look', `${name}.png`), fullPage: true });
}

/** A headless form with a name field and a mail to the team that links to a page of its site; its key and alias. */
async function newForm(api: APIRequestContext) {
  const key = await formWithFields(api, unique('Both sites'), [
    { type: 'text', alias: 'name', label: { '': 'Your name' } },
  ]);
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { settings: { developer?: Record<string, unknown> }; actions?: unknown[] };
  };
  draft.definition.settings.developer = { ...draft.definition.settings.developer, headless: true };
  draft.definition.actions = [
    {
      key: randomUUID(),
      type: 'email',
      name: 'Team',
      settings: {
        to: ['team@jfb.test'],
        subject: { '': 'An entry from {{ site.name }}' },
        body: { '': '<p>A new entry. <a href="/privacy/">Privacy</a></p>' },
      },
    },
  ];
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const form = (await (await api.get(`${API}/form/${key}`)).json()) as { alias: string; concurrencyVersion: number };
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: form.concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  return { key, alias: form.alias };
}

/** Makes a document through Umbraco's Management API and publishes it in English; its key. */
async function documentOf(
  api: APIRequestContext,
  like: string,
  parent: string | null,
  name: string,
  values: { alias: string; value: unknown }[],
): Promise<string> {
  const model = (await (await api.get(`${MANAGEMENT_API}/document/${like}`)).json()) as {
    documentType: { id: string };
    template: { id: string } | null;
  };
  const key = randomUUID();
  const created = await api.post(`${MANAGEMENT_API}/document`, {
    data: {
      id: key,
      parent: parent === null ? null : { id: parent },
      documentType: { id: model.documentType.id },
      template: model.template,
      values: values.map((value) => ({ ...value, culture: null, segment: null })),
      variants: [
        { culture: 'en-US', segment: null, name },
        { culture: 'de-DE', segment: null, name },
      ],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  return key;
}

async function publish(api: APIRequestContext, key: string): Promise<void> {
  const published = await api.put(`${MANAGEMENT_API}/document/${key}/publish`, {
    data: { publishSchedules: [{ culture: 'en-US' }] },
  });
  expect(published.ok(), await published.text()).toBe(true);
}

/** A root reached at its own name, with a page that shows the form. */
async function siteOn(api: APIRequestContext, name: string, domain: string, form: string): Promise<Site> {
  const root = await documentOf(api, HOME_KEY, null, name, []);
  const page = await documentOf(api, EVENTS_KEY, root, 'Form page', [{ alias: 'form', value: form }]);
  const domains = await api.put(`${MANAGEMENT_API}/document/${root}/domains`, {
    data: { defaultIsoCode: 'en-US', domains: [{ domainName: domain, isoCode: 'en-US' }] },
  });
  expect(domains.ok(), await domains.text()).toBe(true);
  await publish(api, root);
  await publish(api, page);
  return { root, page };
}

interface Section {
  readonly version: number;
  readonly values: Record<string, unknown>;
  readonly overrides: readonly string[];
  readonly sites: readonly { readonly key: string; readonly name: string }[];
}

const read = async (api: APIRequestContext, section: string, site?: string) =>
  (await (await api.get(`${API}/settings/${section}${site ? `?site=${site}` : ''}`)).json()) as Section;

/** Gives a site values of its own in a section, as Settings does. */
async function setSite(api: APIRequestContext, section: string, site: string, values: Record<string, unknown>) {
  const own = await read(api, section, site);
  const saved = await api.put(`${API}/settings/${section}`, {
    data: { version: own.version, site, values, overrides: Object.keys(values) },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

/** The time zone a headless call on a site's name gets, the Host header sent by hand. */
async function zoneOn(api: APIRequestContext, name: string, form: string): Promise<string> {
  const response = await api.get(`${SITES_BASE_URL}/jfb/api/v1/forms/${form}`, { headers: { Host: name } });
  expect(response.ok(), await response.text()).toBe(true);
  return ((await response.json()) as { timeZone: string }).timeZone;
}

test('each site shows the form in its own words on its own name, and a headless call by name gets its site', async ({
  apiAs,
  page,
}) => {
  const admin = await apiAs('admin', SITES_BASE_URL);
  const form = await newForm(admin);
  made.form = form;
  made.a = await siteOn(admin, 'Site A', SITE_A, form.alias);
  made.b = await siteOn(admin, 'Site B', SITE_B, form.alias);
  await setSite(admin, 'general', made.a.root, { submitLabel: { '': 'Send to Site A' } });
  await setSite(admin, 'general', made.b.root, { submitLabel: { '': 'Send to Site B' }, timeZone: ZONE_OF_B });
  await setSite(admin, 'email', made.b.root, { senderAddress: 'site-b@jfb.test' });

  for (const [name, label] of [
    [SITE_A, 'Send to Site A'],
    [SITE_B, 'Send to Site B'],
  ] as const) {
    await expect
      .poll(async () => (await page.goto(`http://${name}/form-page/`))?.status(), { timeout: 30_000 })
      .toBe(200);
    await expect(page.locator('main form.jfb-form button[data-jfb-nav="submit"]')).toHaveText(label);
  }

  await axe(page, "Site B's page", true);
  await look(page, 'multi-site-b');
  expect(await zoneOn(admin, SITE_B, form.key), "Site B's time zone").toBe(ZONE_OF_B);
  expect(await zoneOn(admin, SITE_A, form.key), 'the shared one on Site A').not.toBe(ZONE_OF_B);
});

test("a visitor's send on Site B mails from Site B's sender, with its links on Site B's name", async ({ page }) => {
  expect(made.b, 'the first test made the sites').toBeDefined();
  await page.goto(`http://${SITE_B}/form-page/`);
  const form = page.locator('main form.jfb-form');
  await fillForm(form, { name: 'Bea' });
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeVisible();

  const mail = await waitForMailIn(
    host!.mailDir,
    (one) => headerOf(one.raw, 'Subject') === 'An entry from Site B',
    30_000,
  );
  expect(headerOf(mail.raw, 'From')).toContain('site-b@jfb.test');
  expect(bodyOf(mail.raw)).toContain(`https://${SITE_B}/privacy/`);
});

test('Site B deleted for good takes its own settings with it, audited, and Site A keeps its own', async ({ apiAs }) => {
  expect(made.a && made.b, 'the first test made the sites').toBeTruthy();
  const admin = await apiAs('admin', SITES_BASE_URL);
  const trashed = await admin.put(`${MANAGEMENT_API}/document/${made.b!.root}/move-to-recycle-bin`);
  expect(trashed.ok(), await trashed.text()).toBe(true);
  expect((await read(admin, 'general')).sites.map((site) => site.key)).not.toContain(made.b!.root);
  const deleted = await admin.delete(`${MANAGEMENT_API}/recycle-bin/document/${made.b!.root}`);
  expect(deleted.ok(), await deleted.text()).toBe(true);

  const audit = await admin.get(`/jfb-testing/audit?entity=${made.b!.root}`);
  expect(audit.ok(), await audit.text()).toBe(true);
  const actions = ((await audit.json()) as { entries: { action: string }[] }).entries.map((entry) => entry.action);
  expect(
    actions.filter((action) => action === 'settings.removed'),
    'general and email removed with the root',
  ).toHaveLength(2);
  const a = await read(admin, 'general', made.a!.root);
  expect(a.overrides, "Site A's own stay").toContain('submitLabel');
  expect(await zoneOn(admin, SITE_B, made.form!.key), 'its name now names no site').not.toBe(ZONE_OF_B);
});
