import { randomUUID } from 'node:crypto';
import { existsSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { loginThroughApi } from '../../support/api-login.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { e2eState, LB_PORTS } from '../../support/paths.ts';
import { account, readSecrets } from '../../support/users.ts';

// Two servers on one database (ROADMAP 8.1, ADR-0007, ADR-0158): CI starts two hosts on a shared SQL Server, and this suite
// sends submissions to both while each server's worker runs. Every run is delivered exactly once, both servers deliver, a
// form published on one is served by the other, and a challenge from one is good on the other. SQLite cannot be shared
// between hosts, so with JFB_E2E_LB=1 and no SQL Server the suite runs its checks on one host, to prove the suite itself.

test.describe.configure({ mode: 'serial', timeout: 600_000 });

const MANAGEMENT_API = '/umbraco/jfb/management/api/v1';
const SUBMISSIONS = 40;
const SA_PASSWORD = process.env['SA_PASSWORD'];
const ports = SA_PASSWORD ? LB_PORTS : LB_PORTS.slice(0, 1);
const urls = ports.map((port) => `http://127.0.0.1:${String(port)}`);
const deliveriesFile = join(e2eState, 'lb-deliveries.log');
// One Data Protection key ring with one application name for both servers, as docs/deployment.md asks of a load-balanced
// site: without it each refused the other's challenge (ROADMAP 26.1, 401 challenge_required).
const sharedKeys = join(e2eState, 'lb-keys');
const hosts: Host[] = [];

interface Delivery {
  key: string;
  entry: string;
  server: number;
}

function deliveries(): Delivery[] {
  if (!existsSync(deliveriesFile)) return [];
  return readFileSync(deliveriesFile, 'utf8')
    .split('\n')
    .filter((line) => line.trim().length > 0)
    .map((line) => JSON.parse(line) as Delivery);
}

/** Publishes the contact form with an action that writes down each delivery, served to headless sites too. */
async function publishWithTheAction(page: Page, api: APIRequestContext, url: string): Promise<string> {
  const contact = formPages().find((each) => each.form === 'contact');
  if (!contact) throw new Error('The demo has no contact form.');
  await page.goto(`${url}${pathOf(contact.texts.en, 'en')}`);
  const key = (await page.locator('form[data-jfb-form]').first().getAttribute('data-jfb-form')) ?? '';
  const draft = await api.get(`${url}${MANAGEMENT_API}/form/${key}/draft`);
  const { form, definition } = (await draft.json()) as {
    form: { concurrencyVersion: number };
    definition: { settings?: Record<string, unknown> };
  };
  const saved = await api.put(`${url}${MANAGEMENT_API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: form.concurrencyVersion,
      definition: {
        ...definition,
        settings: {
          ...definition.settings,
          developer: { ...(definition.settings?.['developer'] as object | undefined), headless: true },
        },
        actions: [{ key: randomUUID(), type: 'testing.record', name: 'Write it down' }],
      },
      comment: 'The load-balancing suite adds its action',
    },
  });
  expect(saved.status(), await saved.text()).toBe(200);
  const { form: savedForm } = (await saved.json()) as { form: { concurrencyVersion: number } };
  const published = await api.post(`${url}${MANAGEMENT_API}/form/${key}/publish`, {
    data: { concurrencyVersion: savedForm.concurrencyVersion },
  });
  expect(published.status(), await published.text()).toBe(200);
  return key;
}

test.beforeAll(async () => {
  test.setTimeout(900_000);
  rmSync(deliveriesFile, { force: true });
  rmSync(sharedKeys, { recursive: true, force: true });
  const env: Record<string, string> = {
    ...hostEnvironment(readSecrets(), { deliveryApi: false, severalServers: true }),
    JironFormBuilderTesting__StubPort: '0',
    JironFormBuilderTesting__DeliveriesFile: deliveriesFile,
    JironFormBuilderTesting__SharedKeys: sharedKeys,
    JironFormBuilder__Limits__Submit__PerAddressAndFormPerMinute: '0',
    JironFormBuilder__Limits__Challenge__PerAddressAndFormPerMinute: '0',
  };
  if (SA_PASSWORD) {
    env['ConnectionStrings__umbracoDbDSN'] =
      `Server=localhost,1433;Database=JfbLoadBalancing;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True`;
    env['ConnectionStrings__umbracoDbDSN_ProviderName'] = 'Microsoft.Data.SqlClient';
  }

  // One after the other: the first installs and seeds the database the second finds.
  for (const port of ports) {
    const host = await startHost({ name: `lb-${String(port)}`, port, env });
    hosts.push(host);
    const wait = await waitForStartupWork(host.logFile);
    if (wait.state !== 'done')
      throw new Error(`The host on ${String(port)} ended its start-up work as "${wait.state}".`);
  }
});

test.afterAll(async () => {
  for (const host of hosts) await host.stop();
});

test('every run is delivered once, whichever server got the submission', async ({ page, request }) => {
  const first = urls[0];
  if (first === undefined) throw new Error('No host.');
  const key = await publishWithTheAction(page, await loginThroughApi(account('admin'), first), first);

  // A form published on one server reaches the others through Umbraco's cache instructions.
  let revision = 0;
  for (const url of urls) {
    await expect
      .poll(
        async () => {
          const served = await request.get(`${url}/jfb/api/v1/forms/${key}`);
          if (served.status() !== 200) return 0;
          revision = ((await served.json()) as { revision: number }).revision;
          return revision;
        },
        { timeout: 60_000, message: `${url} serves the form` },
      )
      .toBeGreaterThan(0);
  }

  for (let i = 0; i < SUBMISSIONS; i++) {
    // The challenge comes from one server and the answers go to the other, as a load balancer may send them.
    const to = urls[i % urls.length] ?? first;
    const from = urls[(i + 1) % urls.length] ?? first;
    const asked = await request.post(`${from}/jfb/api/v1/forms/${key}/challenge`);
    expect(asked.status(), await asked.text()).toBe(200);
    const { challenge } = (await asked.json()) as { challenge: string };
    const sent = await request.post(`${to}/jfb/api/v1/forms/${key}/entries`, {
      data: {
        revision,
        challenge,
        values: {
          name: `Visitor ${String(i)}`,
          email: `visitor${String(i)}@visitor.jfb.test`,
          message: 'A message long enough to send.',
        },
      },
    });
    expect(sent.status(), await sent.text()).toBe(200);
  }

  await expect
    .poll(() => deliveries().length, { timeout: 180_000, message: 'every run was delivered' })
    .toBeGreaterThanOrEqual(SUBMISSIONS);

  // Long enough for a second delivery of any of them to show.
  await page.waitForTimeout(5_000);
  const delivered = deliveries();
  const keys = delivered.map((each) => each.key);
  expect(
    keys.filter((one, at) => keys.indexOf(one) !== at),
    'no run was delivered twice',
  ).toEqual([]);
  expect(new Set(keys).size).toBe(SUBMISSIONS);
  expect(new Set(delivered.map((each) => each.server)).size, 'each server delivered what it was woken for').toBe(
    ports.length,
  );
});
