import { expect, test, type Page } from '@playwright/test';
import { axe } from '../../support/axe.ts';
import { E2E_BASE_URL } from '../../support/paths.ts';
import { startStaticSite, type StaticSite } from '../../support/static-site.ts';

// The element on a headless page (ROADMAP 6.6f, ADR-0128): a static site on 5521, an origin of its own that the host's
// public API allows, loads <jfb-form> from the host and shows the demo's volunteer sign-up through it, in English and
// in German: the theme's stylesheets from the host, the honeypot out of view and out of the accessibility tree, and
// the runtime enhancing the form as on a Razor page (a rule shows a field, a check shows its error), axe clean. The
// same page under a name the API does not allow shows nothing and says why in the console.

test.describe.configure({ mode: 'serial' });

let site: StaticSite | undefined;
let key = '';

test.beforeAll(async ({ request }) => {
  site = await startStaticSite();
  const answer = await request.get(`${E2E_BASE_URL}/jfb/api/v1/forms/volunteer`);
  expect(answer.status(), 'the volunteer sign-up is a headless form').toBe(200);
  key = ((await answer.json()) as { form: { key: string } }).form.key;
});

test.afterAll(async () => {
  await site?.stop();
});

/** The events the element and the runtime dispatch, and the page's console errors, from before its first script. */
async function watch(page: Page): Promise<{ events: () => Promise<string[]>; errors: string[] }> {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  await page.addInitScript(() => {
    const seen: string[] = [];
    Object.assign(window, { jfbEvents: seen });
    for (const type of ['jfb:rendered', 'jfb:error', 'jfb:ready']) {
      document.addEventListener(type, (event) => seen.push(`${type} ${JSON.stringify((event as CustomEvent).detail)}`));
    }
  });
  return {
    events: () => page.evaluate(() => [...((window as unknown as { jfbEvents?: string[] }).jfbEvents ?? [])]),
    errors,
  };
}

/** The static page for the volunteer sign-up, on an origin and in a culture. */
function pageAt(origin: string, culture: string): string {
  return `${origin}/?${new URLSearchParams({ form: key, culture })}`;
}

const LANGUAGES = [
  {
    culture: 'en-US',
    title: 'About you',
    name: 'Your name',
    other: 'Something else',
    asked: 'What would you like to do?',
    next: 'Next',
  },
  {
    culture: 'de-DE',
    title: '\u00dcber Sie',
    name: 'Ihr Name',
    other: 'Etwas anderes',
    asked: 'Was m\u00f6chten Sie tun?',
    next: 'Weiter',
  },
] as const;

for (const language of LANGUAGES) {
  test(`the element shows the volunteer sign-up on a static page, enhanced, in ${language.culture}`, async ({
    page,
  }) => {
    if (site === undefined) throw new Error('The static site did not start.');
    const watched = await watch(page);
    const stylesheets: string[] = [];
    page.on('response', (response) => {
      if (response.request().resourceType() === 'stylesheet' && response.ok()) stylesheets.push(response.url());
    });

    await page.goto(pageAt(site.url, language.culture));
    const form = page.locator('jfb-form form.jfb-form');
    await expect(form).toBeVisible();
    await expect
      .poll(watched.events)
      .toEqual([
        `jfb:ready ${JSON.stringify({ key, alias: 'volunteer' })}`,
        `jfb:rendered ${JSON.stringify({ form: key, instance: 'jfb-1' })}`,
      ]);

    expect(new URL(page.url()).origin, 'a page of another origin than the host').not.toBe(new URL(E2E_BASE_URL).origin);
    // Which answers first is the network's; the order that counts is the links', which the cascade follows.
    const themes = ['/jfb/assets/themes/base.css', '/jfb/assets/themes/default.css'];
    expect(stylesheets.map((url) => new URL(url).pathname).sort()).toEqual(themes);
    expect(
      await page.evaluate(() =>
        [...document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].map(
          (link) => new URL(link.href).pathname,
        ),
      ),
    ).toEqual(themes);
    await expect(form).toHaveAttribute('lang', language.culture);
    await expect(page.getByRole('heading', { name: language.title })).toBeVisible();
    await expect(page.getByRole('textbox', { name: language.name })).toBeVisible();

    // The honeypot: clipped to nothing by the theme's base stylesheet, and hidden from assistive technology.
    const honeypot = form.locator('.jfb-hp');
    const label = (await honeypot.locator('label').textContent())?.trim() ?? '';
    expect(label.length).toBeGreaterThan(0);
    expect(await honeypot.evaluate((element) => getComputedStyle(element).clipPath)).toBe('inset(50%)');
    const box = await honeypot.boundingBox();
    expect(box !== null && box.width <= 1 && box.height <= 1, 'the honeypot takes no room').toBe(true);
    await expect(page.getByRole('textbox', { name: label })).toHaveCount(0);
    expect(await page.locator('jfb-form').ariaSnapshot()).not.toContain(label);

    // A rule shows a field.
    const asked = page.getByRole('textbox', { name: language.asked });
    await expect(asked).toBeHidden();
    await page.getByRole('radio', { name: language.other }).check();
    await expect(asked).toBeVisible();

    // A check shows its error, with the summary, and the page stays where it is.
    const address = page.url();
    await page.getByRole('button', { name: language.next }).click();
    const summary = form.locator('.jfb-error-summary');
    await expect(summary).toBeVisible();
    await expect(summary).toBeFocused();
    await expect(page.getByRole('textbox', { name: language.name })).toHaveAttribute('aria-invalid', 'true');
    await expect(form.locator('[data-jfb-alias="name"] .jfb-error')).toBeVisible();
    expect(page.url()).toBe(address);

    await axe(page, `the static page in ${language.culture}`, true);
    expect(watched.errors).toEqual([]);
  });
}

test('the element moves through its pages and sends, on a page with no post to go to', async ({ page }) => {
  if (site === undefined) throw new Error('The static site did not start.');
  const watched = await watch(page);
  await page.goto(pageAt(site.url, 'en-US'));
  const form = page.locator('jfb-form form.jfb-form');
  await expect(form).toBeVisible();
  const address = page.url();
  await form.locator('[name="name"]').fill('Mira Okafor');
  await form.locator('[name="email"]').fill('mira@visitor.jfb.test');
  await form.locator('[name="role"][value="events"]').check();
  const moved = form.evaluate(
    (element) =>
      new Promise<unknown>((resolve) =>
        element.addEventListener('jfb:page', (event) => resolve((event as CustomEvent).detail), { once: true }),
      ),
  );
  await page.getByRole('button', { name: 'Next' }).click();

  expect(await moved).toMatchObject({ alias: 'volunteer', number: 2, total: 2 });
  await expect(form.locator('[name="days"]').first()).toBeVisible();
  await expect(form.locator('[name="name"]')).toBeHidden();
  await axe(page, 'the second page of the element', true);

  await page.getByRole('button', { name: 'Back' }).click();
  await expect(form.locator('[name="name"]')).toHaveValue('Mira Okafor');
  await page.getByRole('button', { name: 'Next' }).click();
  await expect(form.locator('[name="days"]').first()).toBeVisible();

  await form.locator('[name="days"][value="mon"]').check();
  await form.locator('[data-jfb-alias="privacy"] input[type="checkbox"]').check();
  await page.getByRole('button', { name: 'Sign me up' }).click();
  await expect(page.locator('jfb-form [data-jfb-outcome]')).toBeFocused();
  expect(page.url(), 'it never left the page').toBe(address);
  expect(watched.errors).toEqual([]);
});

test('the element shows nothing on a page whose origin the API does not allow, and says why', async ({ page }) => {
  if (site === undefined) throw new Error('The static site did not start.');
  const watched = await watch(page);

  await page.goto(pageAt(site.otherOrigin, 'en-US'));

  await expect.poll(watched.events).toEqual([`jfb:error ${JSON.stringify({ code: 'network_error' })}`]);
  await expect(page.locator('jfb-form')).toBeEmpty();
  expect(watched.errors).toContain(
    `jfb-form: the form could not be loaded from ${E2E_BASE_URL}/jfb/api/v1: the API is not reachable, or it does not allow this page's origin.`,
  );
});
