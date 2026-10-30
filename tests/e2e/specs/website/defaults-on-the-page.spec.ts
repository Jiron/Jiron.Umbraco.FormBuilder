import type { Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { LANGUAGES, formPages, pathOf, type Language } from '../../support/demo-content.ts';
import { signInMember } from '../../support/demo-members.ts';

// Defaults with merge tags on the page (ROADMAP 6.4h, ADR-0118), on the demo's workshop booking in English and German,
// signed in as a member: the workshop's default takes the address's query value the form allows, so the events page's
// link to a workshop opens the booking with that workshop chosen before the visitor touches anything, and the help
// that recalls the workshop names it at once; a value no option has, or none, chooses nothing. The server's markup
// leaves the choice empty for every visitor. axe finds no WCAG 2.2 A or AA violation, and nothing writes an error to
// the console.

const pathOfForm = (alias: string, language: Language): string => {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo has no ${alias} page.`);
  return pathOf(shown.texts[language], language);
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');

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

/** Console errors and uncaught exceptions on a page. */
function errorsOn(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

/** Waits until the runtime has filled in the defaults the server marked: it drops each mark as it fills. */
async function filled(form: Locator): Promise<void> {
  await expect(form.locator('[data-jfb-default]'), 'the runtime filled the defaults in').toHaveCount(0);
}

const HELP: Record<Language, (workshop: string) => string> = {
  en: (workshop) => `For ${workshop}: access needs, allergies or a question.`,
  de: (workshop) => `F\u00fcr ${workshop}: Barrierefreiheit, Allergien oder eine Frage.`,
};

const WORKSHOPS: Record<Language, { bikes: string; mending: string; none: string }> = {
  en: { bikes: 'Bike repair basics', mending: 'Mending clothes', none: 'the workshop' },
  de: { bikes: 'Fahrrad-Reparatur f\u00fcr Einsteiger', mending: 'Kleidung flicken', none: 'den Workshop' },
};

for (const language of LANGUAGES) {
  test(`${language}: the events page's link opens the booking with its workshop chosen before the visitor touches it, and the help names it at once`, async ({
    page,
    memberOf,
  }) => {
    const member = memberOf(0);
    await signInMember(page, member.email, member.password);
    const errors = errorsOn(page);
    const path = pathOfForm('workshop', language);
    const served = await (await page.request.get(path + '?workshop=bikes')).text();
    expect(served, 'the server marks the field and leaves its choice for the page').toContain('data-jfb-default');
    expect(served).not.toMatch(/value="bikes"[^>]*checked/);

    await page.goto(path);
    await page.locator('a[href="?workshop=mending#jfb-1"]').click();
    await page.waitForURL(/[?]workshop=mending#jfb-1$/);
    const form = formOn(page);
    const words = WORKSHOPS[language];
    await expect(form.locator('input[name="workshop"][value="mending"]'), 'chosen from the address').toBeChecked();
    await expect(form.locator('input[name="workshop"][value="bikes"]')).not.toBeChecked();
    await expect(form.locator('#jfb-1-notes-help'), 'the help recalls the workshop chosen').toHaveText(
      HELP[language](words.mending),
    );
    await axe(page, `${language}: the booking with its workshop chosen`);

    await page.goto(path + '?workshop=bikes');
    await expect(form.locator('input[name="workshop"][value="bikes"]')).toBeChecked();
    await expect(form.locator('#jfb-1-notes-help')).toHaveText(HELP[language](words.bikes));
    expect(errors, 'console errors').toEqual([]);
  });
}

test('a value no option has, or none, chooses nothing, and the visitor chooses as before', async ({
  page,
  memberOf,
}) => {
  const member = memberOf(0);
  await signInMember(page, member.email, member.password);
  const errors = errorsOn(page);
  const path = pathOfForm('workshop', 'en');
  const form = formOn(page);
  for (const query of ['?workshop=attic', '']) {
    await page.goto(path + query);
    await filled(form);
    await expect(form.locator('input[name="workshop"]:checked'), query).toHaveCount(0);
    await expect(form.locator('#jfb-1-notes-help')).toHaveText(HELP.en(WORKSHOPS.en.none));
  }
  const choice = form.locator('input[name="workshop"][value="bikes"]');
  await form.locator(`label[for="${(await choice.getAttribute('id')) ?? ''}"]`).click();
  await expect(choice).toBeChecked();
  await expect(form.locator('#jfb-1-notes-help')).toHaveText(HELP.en(WORKSHOPS.en.bikes));
  expect(errors, 'console errors').toEqual([]);
});
