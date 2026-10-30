import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Response } from '@playwright/test';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { signInMember } from '../../support/demo-members.ts';
import { fillForm, submitForm } from '../../support/form.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { CACHED_BASE_URL, CACHED_PORT } from '../../support/paths.ts';
import { readSecrets } from '../../support/users.ts';

// A strict site (ROADMAP 6.4k, ADR-0004), on a host of its own whose pages come from Umbraco's output cache, behind the
// Testing project's Content Security Policy (no inline script, no style attribute, nothing from another host): a form's
// runtime and every chunk it loads on demand run with `script-src 'self'` on a page the cache serves, and with a nonce,
// with and without 'strict-dynamic', on a page that is never stored; and nothing sets a cookie, a post included. The
// violations are counted as the page reports them, and an inline script the page did not bring shows that they are.
// The room hire form loads the calculation language; the merge tags load for the workshop booking, the demo's one form
// with texts that recall answers, which is for members, so its page is never cached and a member signs in first.

test.describe.configure({ mode: 'serial', timeout: 120_000 });
test.use({ baseURL: CACHED_BASE_URL });

let host: Host | undefined;

test.beforeAll(async () => {
  test.setTimeout(600_000);
  host = await startHost({
    name: `host-${CACHED_PORT}`,
    port: CACHED_PORT,
    env: {
      ...hostEnvironment(readSecrets(), { deliveryApi: false }),
      // The run's host holds the stub's port; this one needs no stub.
      JironFormBuilderTesting__StubPort: '0',
      JironFormBuilderTesting__Csp: 'true',
      Umbraco__CMS__Website__OutputCache__Enabled: 'true',
      Umbraco__CMS__Website__OutputCache__ContentDuration: '00:05:00',
    },
  });
  const wait = await waitForStartupWork(host.logFile);
  if (wait.state !== 'done') throw new Error(`The cached host's start-up work ended as "${wait.state}".`);
});

test.afterAll(async () => {
  await host?.stop();
});

/** The English page of a demo form. */
function pathOfForm(alias: string): string {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo shows no ${alias} form.`);
  return pathOf(shown.texts.en, 'en');
}

/** Counts the page's policy violations, from before the page's first script on. */
async function violationsOf(page: Page): Promise<() => Promise<string[]>> {
  await page.addInitScript(() => {
    const seen: string[] = [];
    Object.assign(window, { jfbViolations: seen });
    document.addEventListener('securitypolicyviolation', (event) =>
      seen.push(`${event.effectiveDirective} ${event.blockedURI}`),
    );
  });
  return () => page.evaluate(() => [...((window as unknown as { jfbViolations?: string[] }).jfbViolations ?? [])]);
}

/** The runtime's chunks the page loads, by their names without the content hash. */
function chunksOf(page: Page): Set<string> {
  const loaded = new Set<string>();
  page.on('response', (response) => {
    const found = /\/runtime\/chunks\/([a-z.]+)-[A-Za-z0-9_-]{8}[.]js$/.exec(new URL(response.url()).pathname);
    if (found?.[1] !== undefined && response.ok()) loaded.add(found[1]);
  });
  return loaded;
}

/** Wakes the page's own form and sends it empty: the form chunk, its features' chunks and the words of a refusal. */
async function exercise(page: Page): Promise<void> {
  const form = page.locator('main form.jfb-form');
  await form.locator('input:not([type="hidden"]), textarea').first().focus();
  await form.locator('button[data-jfb-nav="next"], button[data-jfb-nav="submit"]').first().click();
  await expect(form.locator('[data-jfb-summary]')).toBeFocused();
  // The summary fades in (the theme's transition from @starting-style); axe measures what the visitor reads after it.
  await page.waitForFunction(() => document.getAnimations().every((animation) => animation.playState !== 'running'));
}

async function axe(page: Page, what: string): Promise<void> {
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

const policyOf = (response: Response | null): string => response?.headers()['content-security-policy'] ?? '';

test('the policy is in force: an inline script that the page did not bring is refused and counted', async ({
  page,
}) => {
  const violations = await violationsOf(page);
  const response = await page.goto(pathOfForm('contact'));
  expect(policyOf(response)).toContain("script-src 'self';");
  await page.evaluate(() => {
    const script = document.createElement('script');
    script.textContent = 'document.body.dataset.ran = "yes";';
    document.body.append(script);
  });
  await expect.poll(violations).toEqual([expect.stringMatching(/^script-src-elem inline$/)]);
  expect(await page.evaluate(() => document.body.dataset['ran'])).toBeUndefined();
});

test("a page the output cache serves runs the runtime and its chunks with script-src 'self'", async ({
  browser,
  page,
}) => {
  const path = pathOfForm('roomHire');
  // Another visitor's visit renders the page, and the cache keeps it.
  const first = await browser.newPage({ baseURL: CACHED_BASE_URL });
  expect((await first.goto(path))?.ok()).toBe(true);
  await first.close();
  const violations = await violationsOf(page);
  const chunks = chunksOf(page);
  const response = await page.goto(path);
  expect(response?.headers()['age'], 'the page comes from the output cache').toBeDefined();
  expect(policyOf(response)).toContain("script-src 'self';");
  expect(await page.locator('script[nonce]').count(), 'a cached page carries no nonce').toBe(0);
  await exercise(page);
  expect([...chunks].sort()).toEqual(['calc', 'form', 'messages.en']);
  expect(await violations()).toEqual([]);
  await axe(page, 'the room hire from the cache');
});

test("the merge tags' chunk runs with script-src 'self' on a member's page, which is never cached", async ({
  page,
}) => {
  const violations = await violationsOf(page);
  const chunks = chunksOf(page);
  const member = readSecrets().members[0]!;
  await signInMember(page, member.email, member.password);
  const response = await page.goto(pathOfForm('workshop'));
  expect(response?.headers()['age']).toBeUndefined();
  expect(policyOf(response)).toContain("script-src 'self';");
  await exercise(page);
  expect([...chunks].sort()).toEqual(['form', 'messages.en', 'recall']);
  expect(await violations()).toEqual([]);
  await axe(page, 'the workshop booking');
});

for (const mode of ['nonce', 'strict-dynamic'] as const) {
  test(`a page that is never stored runs the runtime and every chunk with its nonce (${mode})`, async ({ page }) => {
    const violations = await violationsOf(page);
    const chunks = chunksOf(page);
    const member = readSecrets().members[0]!;
    await signInMember(page, member.email, member.password);
    for (const [alias, feature] of [
      ['roomHire', 'calc'],
      ['workshop', 'recall'],
    ] as const) {
      const response = await page.goto(`${pathOfForm(alias)}?jfb-test-nonce=${mode}`);
      const policy = policyOf(response);
      expect(policy).toMatch(/script-src 'nonce-[A-Za-z0-9+/=]+'/);
      expect(policy.includes("'strict-dynamic'")).toBe(mode === 'strict-dynamic');
      expect(policy).not.toContain("script-src 'self'");
      expect(response?.headers()['cache-control']).toContain('no-store');
      expect(response?.headers()['age'], 'the page is rendered for this request').toBeUndefined();
      const nonce = await page
        .locator('script[type="module"][src*="jfb-runtime"]')
        .evaluate((script) => (script as HTMLScriptElement).nonce);
      expect(policy).toContain(`'nonce-${nonce}'`);
      await exercise(page);
      expect(chunks.has(feature), `${alias} loads its ${feature} chunk`).toBe(true);
      await axe(page, `${alias} with a nonce (${mode})`);
    }
    expect([...chunks].sort()).toEqual(['calc', 'form', 'messages.en', 'recall']);
    expect(await violations()).toEqual([]);
  });
}

test('nothing sets a cookie: neither the page, its chunks, the checks nor a post', async ({ page, context }) => {
  const setters: string[] = [];
  page.on('response', (response) => {
    if (response.headers()['set-cookie'] !== undefined) setters.push(new URL(response.url()).pathname);
  });
  await page.goto(pathOfForm('contact'));
  const form = page.locator('main form.jfb-form');
  await exercise(page);
  await fillForm(form, {
    name: 'Mira Okafor',
    email: 'mira.okafor@visitor.jfb.test',
    message: 'Is the courtyard step-free?',
  });
  expect(await submitForm(form)).toMatchObject({ state: 'sent' });
  await axe(page, 'the outcome');
  expect(setters).toEqual([]);
  expect(await context.cookies()).toEqual([]);
});
