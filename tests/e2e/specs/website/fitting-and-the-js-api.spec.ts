import type { Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf, type Language } from '../../support/demo-content.ts';

// Fitting a form to its page, and the developer surface (ROADMAP 6.4l, ADR-0022, section 4.9). The ground check: a form
// with no scheme class on a dark ground gets `jfb-scheme-dark` and passes contrast, a form the site set stays as it is, and
// a light page changes nothing. Step folding: six steps fold where a word of a name would break, and unfold when the window
// grows. `window.JironFormBuilder`: `init(root)` for a form added later, `get(form)` with `validate()`, `submit()` and
// `values()`, and every DOM event of 6.4. The HTML of a page is changed on its way to the browser where the demo has no
// such page, as all of this reads only what the page shows.

const AE = String.fromCharCode(0xe4);

const pathOfForm = (alias: string, language: Language = 'en'): string => {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo has no ${alias} page.`);
  return pathOf(shown.texts[language], language);
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** Changes a page's HTML on its way to the browser. */
async function rewrite(page: Page, change: (html: string) => string): Promise<void> {
  await page.route(
    (url) => !url.pathname.startsWith('/jfb/') && !/\.[a-z0-9]+$/i.test(url.pathname),
    async (route) => {
      if (route.request().resourceType() !== 'document') return route.fallback();
      const response = await route.fetch();
      return route.fulfill({ response, body: change(await response.text()) });
    },
  );
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

test.describe('the ground check', () => {
  test.use({ colorScheme: 'light' });

  test('a form with no scheme class on a dark ground gets the dark scheme, and passes contrast', async ({ page }) => {
    await rewrite(page, (html) => html.replace(/(<html[^>]*?) class="jfb-scheme-dark"/, '$1'));
    await page.goto(`${pathOfForm('contact')}?scheme=dark`);
    expect(await page.locator('html').getAttribute('class'), 'the site set no scheme').toBeNull();

    await expect(formOn(page)).toHaveClass(/(^| )jfb-scheme-dark( |$)/);
    await page.waitForFunction(() => document.getAnimations().every((each) => each.playState !== 'running'));
    await axe(page, 'a form on a dark ground');
  });

  test('a form the site set to light stays light on the same ground', async ({ page }) => {
    await rewrite(page, (html) =>
      html
        .replace(/(<html[^>]*?) class="jfb-scheme-dark"/, '$1')
        .replace(/<form class="jfb-form /g, '<form class="jfb-form jfb-scheme-light '),
    );
    await page.goto(`${pathOfForm('contact')}?scheme=dark`);

    await expect(formOn(page)).toHaveClass(/jfb-scheme-light/);
    await expect(formOn(page)).not.toHaveClass(/jfb-scheme-dark/);
  });

  test("a form under a site's own scheme class is left alone", async ({ page }) => {
    await rewrite(page, (html) => html.replace(/(<html[^>]*?) class="jfb-scheme-dark"/, '$1 class="jfb-scheme-light"'));
    await page.goto(`${pathOfForm('contact')}?scheme=dark`);
    await expect(page.locator('html')).toHaveClass('jfb-scheme-light');

    await expect(formOn(page)).not.toHaveClass(/jfb-scheme-dark/);
  });

  test('a light page changes nothing', async ({ page }) => {
    await page.goto(pathOfForm('contact'));
    const before = await formOn(page).getAttribute('class');
    await page.waitForFunction(() => 'JironFormBuilder' in window);

    expect(before).not.toContain('jfb-scheme');
    expect(await formOn(page).getAttribute('class')).toBe(before);
  });

  test('an outcome stands on the same ground as its form', async ({ page }) => {
    await rewrite(page, (html) => html.replace(/(<html[^>]*?) class="jfb-scheme-dark"/, '$1'));
    await page.goto(`${pathOfForm('contact')}?scheme=dark`);
    const form = formOn(page);
    await form.locator('[name="name"]').fill('Mira Okafor');
    await form.locator('[name="email"]').fill('mira@visitor.jfb.test');
    await form.locator('[name="message"]').fill('Is the reading room open on Sundays?');
    await form.locator('button[data-jfb-nav="submit"]').click();

    const outcome = page.locator('main [data-jfb-outcome]');
    await expect(outcome).toBeFocused();
    await expect(outcome.locator('xpath=..')).toHaveClass(/jfb-scheme-dark/);
  });
});

test('six steps fold where a word of a name would break inside it, and unfold when the window grows', async ({
  page,
}) => {
  // A compound as long as a step is wide in a window of 800 pixels, and shorter than one in a window of 1440.
  const long = `Datenschutzerkl${AE}rung`;
  const names = ['Kontakt', 'Anschrift', long, 'Termine', 'Hinweise', 'Absenden'];
  await rewrite(page, (html) =>
    html.replace(
      /(<div class="[^"]*jfb-progress-steps[^"]*">[\s\S]*?<ol[^>]*>)[\s\S]*?(<\/ol>)/,
      (_all, open: string, close: string) =>
        open + names.map((name) => `<li><span>${name}</span></li>`).join('') + close,
    ),
  );
  await page.setViewportSize({ width: 800, height: 900 });
  await page.goto(pathOfForm('application', 'de'));
  const steps = page.locator('main .jfb-progress-steps');
  await expect(steps.locator('ol > li')).toHaveCount(6);

  await expect(steps, 'folded').toHaveAttribute('data-jfb-compact', '');
  await expect(steps.locator('.jfb-progress-text'), 'with "Step 1 of 6" in their place').toBeVisible();
  await axe(page, 'folded steps');

  await page.setViewportSize({ width: 1440, height: 900 });
  await expect(steps, 'unfolded').not.toHaveAttribute('data-jfb-compact');
  const fits = await steps
    .locator('ol > li > span')
    .evaluateAll((each) => each.every((name) => name.scrollWidth <= name.clientWidth + 1));
  expect(fits, 'no name breaks out of its step').toBe(true);

  await page.setViewportSize({ width: 800, height: 900 });
  await expect(steps, 'and folded again').toHaveAttribute('data-jfb-compact', '');
});

test.describe('window.JironFormBuilder', () => {
  /** Records every event of 6.4 a site hears, with its detail. */
  async function listen(page: Page): Promise<void> {
    await page.addInitScript(() => {
      const seen: { type: string; detail: unknown }[] = [];
      Object.assign(window, { jfbHeard: seen });
      for (const type of ['jfb:ready', 'jfb:invalid', 'jfb:submitting', 'jfb:submitted', 'jfb:error']) {
        document.addEventListener(type, (event) => seen.push({ type, detail: (event as CustomEvent).detail }));
      }
    });
  }

  const heard = (page: Page): Promise<{ type: string; detail: Record<string, unknown> }[]> =>
    page.evaluate(
      () => (window as unknown as { jfbHeard: { type: string; detail: Record<string, unknown> }[] }).jfbHeard,
    );

  test('get(form) checks, reads and sends as a visitor does, with every event of the page', async ({ page }) => {
    await listen(page);
    await page.goto(pathOfForm('contact'));
    const form = formOn(page);

    const empty = await form.evaluate((element) => window.JironFormBuilder?.get(element)?.validate());
    expect(empty, 'an empty form does not pass').toBe(false);
    await expect(form.locator(':scope > [data-jfb-summary]')).toBeFocused();

    await form.locator('[name="name"]').fill('Mira Okafor');
    await form.locator('[name="email"]').fill('mira@visitor.jfb.test');
    await form.locator('[name="message"]').fill('Is the reading room open on Sundays?');
    expect(await form.evaluate((element) => window.JironFormBuilder?.get(element)?.validate())).toBe(true);
    const values = (await form.evaluate((element) => window.JironFormBuilder?.get(element)?.values())) as Record<
      string,
      unknown
    >;
    expect(values['name']).toEqual(['Mira Okafor']);
    expect(values['email']).toEqual(['mira@visitor.jfb.test']);

    await page.route('**/jfb/api/v1/forms/*/entries', (route) => route.fulfill({ status: 500, json: {} }), {
      times: 1,
    });
    expect(await form.evaluate((element) => window.JironFormBuilder?.get(element)?.submit())).toBe(true);
    await expect(form.locator('[data-jfb-notice="server_error"]')).toBeFocused();

    expect(await form.evaluate((element) => window.JironFormBuilder?.get(element)?.submit())).toBe(true);
    await expect(page.locator('main [data-jfb-outcome]')).toBeFocused();

    const events = await heard(page);
    expect(new Set(events.map((each) => each.type))).toEqual(
      new Set(['jfb:ready', 'jfb:invalid', 'jfb:submitting', 'jfb:submitted', 'jfb:error']),
    );
    const key = await page.locator('main .jfb-form').getAttribute('data-jfb-form');
    for (const event of events.filter((each) => each.detail['alias'] === 'contact')) {
      expect(event.detail['key'], event.type).toBe(key);
    }
    expect(events.find((each) => each.type === 'jfb:error')?.detail['code']).toBe('server_error');
    expect(JSON.stringify(events), 'no event holds an answer').not.toContain('Mira');
  });

  test('init(root) enhances a form added later, once, and get is none for what is no form', async ({ page }) => {
    await listen(page);
    await page.goto(pathOfForm('contact'));
    const form = formOn(page);
    const key = await form.getAttribute('data-jfb-form');
    const place = new URL(page.url()).pathname;
    const markup = await (
      await page.request.get(`/jfb/api/v1/forms/${key ?? ''}/render?instance=jfb-9&page=${encodeURIComponent(place)}`)
    ).text();

    const ready = await page.evaluate((html) => {
      const holder = document.createElement('div');
      holder.id = 'later';
      const parsed = document.createElement('template');
      parsed.innerHTML = html;
      holder.append(parsed.content);
      document.querySelector('main')?.append(holder);
      const before = (window as unknown as { jfbHeard: unknown[] }).jfbHeard.length;
      window.JironFormBuilder?.init(holder);
      window.JironFormBuilder?.init(holder);
      return (window as unknown as { jfbHeard: unknown[] }).jfbHeard.length - before;
    }, markup);
    expect(ready, 'one jfb:ready, for two calls').toBe(1);

    const later = page.locator('#later form.jfb-form');
    await later.locator('[name="name"]').fill('Later');
    expect(await later.evaluate((element) => window.JironFormBuilder?.get(element)?.validate())).toBe(false);
    await expect(later.locator(':scope > [data-jfb-summary]')).toBeFocused();
    expect(
      await page.evaluate(() => [
        window.JironFormBuilder?.get(document.body),
        window.JironFormBuilder?.get(document.createElement('form')),
      ]),
    ).toEqual([null, null]);
  });

  test('submit() is false on a page before the last, as it shows no send button', async ({ page }) => {
    await page.goto(pathOfForm('application'));

    expect(await formOn(page).evaluate((element) => window.JironFormBuilder?.get(element)?.submit())).toBe(false);
  });
});
