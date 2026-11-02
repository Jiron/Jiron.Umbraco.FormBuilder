import type { Browser, Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { pressOnThePage } from '../../support/form.ts';
import { LANGUAGES, formPages, pathOf, type Language } from '../../support/demo-content.ts';

// Errors on the page (ROADMAP 6.4e, ADR-0113), on the demo's contact and feedback forms in English and German: an
// error shows as the visitor leaves a field they changed, worded as the server words it and said in the live region,
// and goes on the keystroke that fixes it; a refused send stays on the page with the server's inline errors and error
// summary for the same answers, the summary takes the focus and its links move it to their fields, `jfb:invalid`
// names fields and kinds but no answer, and the summary goes once everything is fixed; a required field the rules
// hide never stops the send. Only the page's language file loads, axe finds no WCAG 2.2 A or AA violation with the
// runtime's errors shown, and nothing writes an error to the console.

const pathOfForm = (alias: string, language: Language): string => {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo has no ${alias} page.`);
  return pathOf(shown.texts[language], language);
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** The errors a form shows: each inline error by its id with its text, and each summary link's target and text. */
const errorsOf = (form: Locator) =>
  form.evaluate((element) => ({
    inline: [...element.querySelectorAll('[data-jfb-error]')].map((each) => `${each.id}: ${each.textContent.trim()}`),
    heading: element.querySelector(':scope > [data-jfb-summary] h2')?.textContent.trim() ?? null,
    summary: [...element.querySelectorAll(':scope > [data-jfb-summary] a')].map(
      (link) => `${link.getAttribute('href') ?? ''} ${link.textContent.trim()}`,
    ),
  }));

/** The errors the server renders for answers posted without JavaScript: the page again, with the post's errors. */
async function renderedByTheServer(browser: Browser, path: string, answers: Readonly<Record<string, string>>) {
  const context = await browser.newContext({ javaScriptEnabled: false });
  try {
    const page = await context.newPage();
    await page.goto(path);
    for (const [name, value] of Object.entries(answers)) await formOn(page).locator(`[name="${name}"]`).fill(value);
    const loaded = page.waitForEvent('load');
    await pressOnThePage(formOn(page).locator('button[data-jfb-nav="submit"]'));
    await loaded;
    await expect(formOn(page).locator('[data-jfb-summary]'), 'the post was refused for its errors').toBeVisible();
    return await errorsOf(formOn(page));
  } finally {
    await context.close();
  }
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

/** What goes wrong on a page: its console errors and uncaught exceptions, and the language files it loads. */
function watch(page: Page) {
  const errors: string[] = [];
  const languages = new Set<string>();
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    const file = /\/chunks\/messages\.([a-z]+)-[^/]*\.js(\?|$)/.exec(request.url());
    if (file?.[1] !== undefined) languages.add(file[1]);
  });
  return { errors, languages };
}

for (const language of LANGUAGES) {
  test(`${language}: the contact form shows its errors as the visitor leaves a field and sends, as the server would, and clears them once fixed`, async ({
    page,
    browser,
  }) => {
    const watched = watch(page);
    const path = pathOfForm('contact', language);
    await page.goto(path);
    const form = formOn(page);
    const id = await form.getAttribute('id');
    const key = await form.getAttribute('data-jfb-form');
    const name = form.locator('[name="name"]');
    const email = form.locator('[name="email"]');
    const message = form.locator('[name="message"]');
    await page.evaluate(() => {
      const seen: unknown[] = [];
      Object.assign(window, { jfbInvalid: seen });
      document.addEventListener('jfb:invalid', (event) => seen.push((event as CustomEvent).detail));
    });

    await name.focus();
    await name.press('Tab');
    expect((await errorsOf(form)).inline, 'a field passed through shows nothing').toEqual([]);

    await email.fill('not an address');
    await email.press('Tab');
    const typo = await renderedByTheServer(browser, path, { email: 'not an address' });
    const emailError = typo.inline.find((each) => each.startsWith(`${id ?? ''}-email-error: `));
    expect(emailError, 'the server words the address').toBeDefined();
    await expect.poll(async () => (await errorsOf(form)).inline).toEqual([emailError]);
    await expect(email).toHaveAttribute('aria-invalid', 'true');
    await expect(email).toHaveAttribute('aria-describedby', new RegExp(`(^| )${id ?? ''}-email-error$`));
    await expect(form.locator('[data-jfb-live]')).toHaveText(
      typo.summary.find((each) => each.startsWith(`#${id ?? ''}-email `))?.replace(/^#\S+ /, '') ?? 'missing',
    );
    await axe(page, `${language}, an error shown as the visitor left its field`);

    await email.fill('ada@example.com');
    await expect.poll(async () => (await errorsOf(form)).inline, 'fixed on the keystroke').toEqual([]);
    await expect(email).not.toHaveAttribute('aria-invalid');

    const posts: string[] = [];
    page.on('request', (request) => {
      if (request.method() === 'POST') posts.push(request.url());
    });
    await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
    const summary = form.locator(':scope > [data-jfb-summary]');
    await expect(summary).toBeFocused();
    expect(await errorsOf(form), "the server's errors for the same answers").toEqual(
      await renderedByTheServer(browser, path, { email: 'ada@example.com' }),
    );
    expect(posts, 'nothing was sent').toEqual([]);
    expect(page.url()).toContain(path);
    await axe(page, `${language}, the summary after a refused send`);
    const invalid = await page.evaluate(() => (window as unknown as { jfbInvalid: unknown[] }).jfbInvalid);
    expect(invalid).toEqual([
      {
        key,
        alias: 'contact',
        scope: 'send',
        errors: [
          { field: 'name', kind: 'required' },
          { field: 'message', kind: 'required' },
        ],
      },
    ]);
    expect(JSON.stringify(invalid), 'no answer in the event').not.toContain('ada@example.com');

    await summary.locator('a').first().click();
    await expect(name, "the link moves the focus to its field's control").toBeFocused();

    await name.fill('Ada Lovelace');
    await message.fill('When does the reading room open on Sundays?');
    await expect
      .poll(async () => await errorsOf(form), 'everything fixed')
      .toEqual({
        inline: [],
        heading: null,
        summary: [],
      });
    expect([...watched.languages], "only the page's language file loads").toEqual([language]);
    expect(watched.errors, 'nothing wrote an error to the console').toEqual([]);
  });

  test(`${language}: a required field the rules hide never stops the send`, async ({ page }) => {
    const watched = watch(page);
    await page.goto(pathOfForm('feedback', language));
    const form = formOn(page);
    const days = form.locator('[data-jfb-alias="eveningDays"]');
    await expect(days, 'no evening chosen, so the evenings stay hidden').toBeHidden();
    for (const [name, value] of [
      ['visits', 'monthly'],
      ['lastVisit', '4'],
    ] as const) {
      const input = form.locator(`input[name="${name}"][value="${value}"]`);
      await form.locator(`label[for="${(await input.getAttribute('id')) ?? ''}"]`).click();
      await expect(input).toBeChecked();
    }
    await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
    await expect(page.locator('main [data-jfb-outcome]'), 'the answers were sent').toBeVisible();
    expect(watched.errors, 'nothing wrote an error to the console').toEqual([]);
  });
}
