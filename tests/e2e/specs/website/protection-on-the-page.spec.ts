import AxeBuilder from '@axe-core/playwright';
import { expect, test, type APIRequestContext, type Page, type Route } from '@playwright/test';
import { loginThroughApi } from '../../support/api-login.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { fillForm, logicCome, pressOnThePage } from '../../support/form.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { PROTECTION_BASE_URL, PROTECTION_PORT, PROTECTION_STUB_PORT } from '../../support/paths.ts';
import { startStaticSite } from '../../support/static-site.ts';
import { account, readSecrets } from '../../support/users.ts';

// What protects a form on the page (ROADMAP 7.2c, ADR-0157), on a host of its own with fake keys and the stub for the
// providers' verification (D22): every request to a provider's host is routed to a fake script here, so no real third-party
// request ever happens. Zero requests go to it before consent, in the explicit and cmp modes; the script loads after, and a
// check that passes sends the entry. consent.grant('captcha') loads it on a form in the cmp mode, and after withdraw() the next
// page loads none. Proof of work completes in under 2 s at the default level, in a worker, and the server takes the answer; on
// a page of another origin, where no worker can start, it is solved in slices.

test.describe.configure({ mode: 'serial', timeout: 120_000 });
test.use({ baseURL: PROTECTION_BASE_URL });

const MANAGEMENT_API = '/umbraco/jfb/management/api/v1';
const PROVIDER = 'https://js.hcaptcha.com/**';
const PASS = 'JFB_FAKE-captcha-pass';
const FAIL = 'JFB_FAKE-captcha-fail';

/** The provider as a fake: a button in the check's place that hands over the token the page set for it. */
const FAKE_PROVIDER = `window.hcaptcha = {
  render(place, options) {
    const press = document.createElement('button');
    press.type = 'button';
    press.textContent = 'I am a person';
    press.onclick = () => options.callback(window.jfbToken || ${JSON.stringify(PASS)});
    place.append(press);
    return 1;
  },
  reset() { window.jfbResets = (window.jfbResets || 0) + 1; },
};`;

/** What a site's consent manager calls. */
interface Consenting {
  JironFormBuilder?: { consent: { grant(purpose: string): void; withdraw(): void } };
}

const SHOTS = process.env['JFB_REVIEW_SHOTS'];

let host: Host | undefined;

function pathOfForm(alias: string): string {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo shows no ${alias} form.`);
  return pathOf(shown.texts.en, 'en');
}

const formOn = (page: Page) => page.locator('main form.jfb-form');
const managing = (): Promise<APIRequestContext> => loginThroughApi(account('designer'), PROTECTION_BASE_URL);

/** Publishes the contact form at a protection level of its own. */
async function publishAt(
  page: Page,
  api: APIRequestContext,
  protection: Record<string, unknown>,
  headless = false,
): Promise<void> {
  await page.goto(pathOfForm('contact'));
  const contact = (await formOn(page).getAttribute('data-jfb-form')) ?? '';
  const draft = await api.get(`${MANAGEMENT_API}/form/${contact}/draft`);
  const { form, definition } = (await draft.json()) as {
    form: { concurrencyVersion: number };
    definition: { settings?: Record<string, unknown> };
  };
  const saved = await api.put(`${MANAGEMENT_API}/form/${contact}/draft`, {
    data: {
      concurrencyVersion: form.concurrencyVersion,
      definition: {
        ...definition,
        settings: {
          ...definition.settings,
          developer: { ...(definition.settings?.['developer'] as object | undefined), headless },
          protection,
        },
      },
      comment: 'The browser tests change the protection',
    },
  });
  expect(saved.status(), await saved.text()).toBe(200);
  const { form: savedForm } = (await saved.json()) as { form: { concurrencyVersion: number } };
  const published = await api.post(`${MANAGEMENT_API}/form/${contact}/publish`, {
    data: { concurrencyVersion: savedForm.concurrencyVersion },
  });
  expect(published.status(), await published.text()).toBe(200);
}

async function consentMode(page: Page, mode: string): Promise<void> {
  const set = await page.request.post('/jfb-testing/protection', { data: { captchaConsent: mode } });
  expect(set.status(), await set.text()).toBe(204);
}

/** Routes the provider's host to the fake, and counts what asks for it. */
async function routed(page: Page): Promise<string[]> {
  const asked: string[] = [];
  await page.route(PROVIDER, (route: Route) => {
    asked.push(route.request().url());
    return route.fulfill({ contentType: 'text/javascript', body: FAKE_PROVIDER });
  });
  return asked;
}

/** The state of the entry whose answers hold a name: a check that failed would have left it in Spam. */
async function stateOf(page: Page, name: string): Promise<string | undefined> {
  const stored = await page.request.get('/jfb-testing/entries', { params: { form: 'contact' } });
  const { entries } = (await stored.json()) as { entries: { state: string; values: unknown }[] };
  return entries.find((each) => JSON.stringify(each.values).includes(name))?.state;
}

async function axe(page: Page, what: string): Promise<void> {
  // The fake is no provider's real widget; what the page writes around it is what counts here.
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    result.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    ),
    `${what}: axe`,
  ).toEqual([]);
}

const fill = (page: Page, name: string) =>
  fillForm(formOn(page), { name, email: 'visitor@visitor.jfb.test', message: 'A message long enough to send.' });

test.beforeAll(async () => {
  test.setTimeout(600_000);
  host = await startHost({
    name: `host-${PROTECTION_PORT}`,
    port: PROTECTION_PORT,
    env: {
      ...hostEnvironment(readSecrets(), { deliveryApi: false }),
      // The run's host holds the stub's port, so this one has its own to verify through.
      JironFormBuilderTesting__StubPort: String(PROTECTION_STUB_PORT),
      // The stub's answers name this host, and a token must come from one of the site's.
      JironFormBuilder__PublicApi__AllowedOrigins__1: 'http://localhost',
      JironFormBuilder__Captcha__Hcaptcha__SiteKey: 'JFB_FAKE-site-key',
      JironFormBuilder__Captcha__Hcaptcha__SecretKey: 'JFB_FAKE-secret-key',
      JironFormBuilder__Captcha__Hcaptcha__VerifyAddress: `http://127.0.0.1:${String(PROTECTION_STUB_PORT)}/captcha/hcaptcha/siteverify`,
    },
  });
  const wait = await waitForStartupWork(host.logFile);
  if (wait.state !== 'done') throw new Error(`The protection host's start-up work ended as "${wait.state}".`);
});

test.afterAll(async () => {
  await host?.stop();
});

test('the notice stands in every theme, on a phone and in dark mode, with nothing for axe to find', async ({
  page,
}) => {
  await publishAt(page, await managing(), { level: 'captcha', captcha: { provider: 'hcaptcha' } });
  await consentMode(page, 'explicit');
  await routed(page);
  for (const theme of ['default', 'bootstrap5', 'unstyled']) {
    for (const [width, scheme] of [
      [1440, 'light'],
      [1440, 'dark'],
      [375, 'light'],
    ] as const) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${pathOfForm('contact')}?theme=${theme}&scheme=${scheme === 'dark' ? 'dark' : ''}`);
      const form = formOn(page);
      const place = form.locator('.jfb-captcha');
      await expect(place.getByRole('button', { name: 'Load the check' })).toBeVisible();
      await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
      await page.getByRole('main').locator('[data-jfb-summary]').waitFor();
      await page.waitForFunction(() => document.getAnimations().every((each) => each.playState !== 'running'));
      await axe(page, `${theme} ${scheme} ${String(width)}`);
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth),
        `${theme} at ${String(width)}: no sideways scrolling`,
      ).toBe(true);
      if (SHOTS !== undefined)
        await place.screenshot({ path: `${SHOTS}/captcha-${theme}-${scheme}-${String(width)}.png` });
    }
  }
  await page.setViewportSize({ width: 1280, height: 720 });
});

test('explicit: nothing goes to the provider before the visitor agrees, then its check sends the entry', async ({
  page,
}) => {
  await publishAt(page, await managing(), { level: 'captcha', captcha: { provider: 'hcaptcha' } });
  await consentMode(page, 'explicit');
  const asked = await routed(page);
  await page.goto(pathOfForm('contact'));
  const form = formOn(page);
  const place = form.locator('.jfb-captcha');
  await expect(
    place.getByRole('button', { name: 'Load the check' }),
    'before the visitor touches the form',
  ).toBeVisible();
  await axe(page, 'the notice');
  await page.waitForLoadState('networkidle');
  expect(asked, 'no request before consent').toEqual([]);

  await fill(page, 'Clara Explicit');
  expect(asked, 'nor while the form is filled in').toEqual([]);
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(place.locator('.jfb-error')).toHaveText('Complete the check before you send the form.');
  await expect(place.getByRole('button', { name: 'Load the check' })).toBeFocused();
  expect(asked, 'nor at a send').toEqual([]);
  await axe(page, 'what the send asks for');

  await place.getByRole('button', { name: 'Load the check' }).click();
  await expect(place.getByRole('button', { name: 'I am a person' })).toBeVisible();
  expect(asked).toHaveLength(1);
  expect(asked[0]).toContain('hl=en');

  await place.getByRole('button', { name: 'I am a person' }).click();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  expect(await stateOf(page, 'Clara Explicit'), 'the provider passed it').toBe('Approved');
});

test('a check the provider refuses asks for a new one, keeping the answers, and the next one sends', async ({
  page,
}) => {
  await consentMode(page, 'explicit');
  await routed(page);
  await page.goto(pathOfForm('contact'));
  const form = formOn(page);
  const place = form.locator('.jfb-captcha');
  await fill(page, 'Rafa Refused');
  await place.getByRole('button', { name: 'Load the check' }).click();
  await page.evaluate((token) => {
    (window as unknown as { jfbToken: string }).jfbToken = token;
  }, FAIL);
  await place.getByRole('button', { name: 'I am a person' }).click();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));

  await expect(place.locator('.jfb-error')).toHaveText('Complete the check before you send the form.');
  await expect.poll(() => page.evaluate(() => (window as unknown as { jfbResets?: number }).jfbResets)).toBe(1);
  await expect(form.locator('[name="name"]')).toHaveValue('Rafa Refused');

  await page.evaluate((token) => {
    (window as unknown as { jfbToken: string }).jfbToken = token;
  }, PASS);
  await place.getByRole('button', { name: 'I am a person' }).click();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  expect(await stateOf(page, 'Rafa Refused')).toBe('Approved');
});

test("cmp: consent.grant('captcha') loads the provider's script, and after withdraw() the next page loads none", async ({
  page,
}) => {
  await consentMode(page, 'cmp');
  const asked = await routed(page);
  await page.goto(pathOfForm('contact'));
  const place = formOn(page).locator('.jfb-captcha');
  await expect(place).toContainText('cookie settings');
  await expect(place.getByRole('button')).toHaveCount(0);
  await axe(page, 'the cmp notice');
  await page.waitForLoadState('networkidle');
  expect(asked, 'no request before consent').toEqual([]);

  await page.evaluate(() => (window as unknown as Consenting).JironFormBuilder?.consent.grant('captcha'));
  await expect(place.getByRole('button', { name: 'I am a person' })).toBeVisible();
  expect(asked).toHaveLength(1);

  await page.evaluate(() => (window as unknown as Consenting).JironFormBuilder?.consent.withdraw());
  await expect(place.getByRole('button', { name: 'I am a person' })).toHaveCount(0);
  await expect(place).toContainText('cookie settings');

  await page.goto(pathOfForm('contact'));
  await expect(formOn(page).locator('.jfb-captcha')).toContainText('cookie settings');
  await page.waitForLoadState('networkidle');
  expect(asked, 'the next page loads none').toHaveLength(1);
  expect(await page.evaluate(() => 'hcaptcha' in window)).toBe(false);
});

test('proof of work: solved in a worker in under 2 s at the default level, and the server takes the answer', async ({
  page,
}) => {
  await publishAt(page, await managing(), { level: 'proofOfWork' });
  const workers: string[] = [];
  page.on('worker', (worker) => workers.push(worker.url()));
  let bits = 0;
  await page.route('**/jfb/api/v1/forms/*/challenge', async (route) => {
    const response = await route.fetch();
    bits = ((await response.json()) as { proofOfWork?: { bits: number } }).proofOfWork?.bits ?? 0;
    await route.fulfill({ response });
  });
  const posted: { at: number; proofOfWork?: unknown }[] = [];
  page.on('request', (request) => {
    if (request.url().endsWith('/entries'))
      posted.push({ at: Date.now(), ...(request.postDataJSON() as { proofOfWork?: unknown }) });
  });
  await page.goto(pathOfForm('contact'));
  const form = formOn(page);
  await fill(page, 'Pia Proof');
  await logicCome(form);
  await expect.poll(() => bits).toBeGreaterThan(0);

  const pressed = Date.now();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();
  const entry = posted[0];
  expect(typeof entry?.proofOfWork, 'the send carries the answer').toBe('string');
  const took = (entry?.at ?? 0) - pressed;
  console.log(`proof of work at ${String(bits)} bits: ${String(took)} ms`);
  expect(took, 'under 2 s from the press to the send').toBeLessThan(2_000);
  expect(
    workers.some((url) => url.includes('protect-chunk')),
    'in a worker',
  ).toBe(true);

  expect(await stateOf(page, 'Pia Proof')).toBe('Approved');
});

test('proof of work on a page of another origin is solved in slices, as no worker can start there', async ({
  page,
}) => {
  await publishAt(page, await managing(), { level: 'proofOfWork' }, true);
  const key = (await formOn(page).getAttribute('data-jfb-form')) ?? '';
  const site = await startStaticSite(PROTECTION_BASE_URL);
  try {
    const workers: string[] = [];
    page.on('worker', (worker) => workers.push(worker.url()));
    await page.goto(`${site.url}/?${new URLSearchParams({ form: key, culture: 'en-US' })}`);
    const form = page.locator('jfb-form form.jfb-form');
    await expect(form).toBeVisible();
    await fillForm(form, {
      name: 'Sol Slices',
      email: 'visitor@visitor.jfb.test',
      message: 'A message long enough to send.',
    });
    await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
    await expect(page.locator('jfb-form [data-jfb-outcome]')).toBeFocused();
    expect(workers, 'none of its own').toEqual([]);
    expect(await stateOf(page, 'Sol Slices')).toBe('Approved');
  } finally {
    await site.stop();
  }
});
