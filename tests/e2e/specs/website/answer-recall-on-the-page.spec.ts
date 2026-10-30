import type { Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { LANGUAGES, formPages, pathOf, type Language } from '../../support/demo-content.ts';
import { signInMember } from '../../support/demo-members.ts';

// Answer recall on the page (ROADMAP 6.4g, ADR-0115), on the demo's workshop booking in English and German, signed in
// as a member: the question whether anyone comes along names the visitor as they type their name, the notes' help names
// the workshop chosen, and the closing text block lists the guests, "none" once the guests are hidden again; the error
// summary's link names the question as it reads now. The merge tags load as their own chunk, once, and never on a form
// without a text that recalls answers. axe finds no WCAG 2.2 A or AA violation, and nothing writes an error to the
// console.

const pathOfForm = (alias: string, language: Language): string => {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo has no ${alias} page.`);
  return pathOf(shown.texts[language], language);
};

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** Picks a choice by its label, as a visitor does: a card's or a button's label covers its control. */
async function choose(form: Locator, name: string, value: string): Promise<void> {
  const input = form.locator(`input[name="${name}"][value="${value}"]`);
  const id = await input.getAttribute('id');
  await form.locator(`label[for="${id ?? ''}"]`).click();
  await expect(input).toBeChecked();
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

/** What a page does: its console errors and uncaught exceptions, and the runtime's chunks it asks for. */
function watch(page: Page) {
  const errors: string[] = [];
  const chunks: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    const chunk = /\/chunks\/([a-z.]+)-[^/]*\.js(\?|$)/.exec(request.url());
    if (chunk?.[1] !== undefined) chunks.push(chunk[1]);
  });
  return { errors, chunks };
}

const WORDS: Record<
  Language,
  { question: (name: string) => string; help: (workshop: string) => string; guests: (who: string) => string }
> = {
  en: {
    question: (name) => `Is anyone coming with ${name}?`,
    help: (workshop) => `For ${workshop}: access needs, allergies or a question.`,
    guests: (who) => `Guests on this booking: ${who}. We hold every place until ten minutes before the start.`,
  },
  de: {
    question: (name) => `Kommt jemand mit ${name}?`,
    help: (workshop) => `F\u00fcr ${workshop}: Barrierefreiheit, Allergien oder eine Frage.`,
    guests: (who) => `G\u00e4ste dieser Buchung: ${who}. Wir halten jeden Platz bis zehn Minuten vor Beginn frei.`,
  },
};

const DEFAULTS: Record<Language, { you: string; workshop: string; none: string; bikes: string }> = {
  en: { you: 'you', workshop: 'the workshop', none: 'none', bikes: 'Bike repair basics' },
  de: { you: 'Ihnen', workshop: 'den Workshop', none: 'keine', bikes: 'Fahrrad-Reparatur f\u00fcr Einsteiger' },
};

for (const language of LANGUAGES) {
  test(`${language}: the workshop booking's question, help and closing text follow the answers as the visitor types, a hidden group recalls nothing, and the error summary names the question as it reads now`, async ({
    page,
    memberOf,
  }) => {
    const member = memberOf(0);
    await signInMember(page, member.email, member.password);
    const { errors, chunks } = watch(page);
    const words = WORDS[language];
    const defaults = DEFAULTS[language];
    await page.goto(pathOfForm('workshop', language));
    const form = formOn(page);
    const question = form.locator('[data-jfb-alias="bringGuests"] legend [data-jfb-text]');
    const help = form.locator('#jfb-1-notes-help');
    const closing = form.locator('[data-jfb-type="paragraph"] .jfb-text');
    await expect(question, "the server's text, with no name yet").toHaveText(words.question(defaults.you));
    await expect(help).toHaveText(words.help(defaults.workshop));
    await expect(closing).toHaveText(words.guests(defaults.none));

    await form.locator('input[name="name"]').fill('Anna');
    await expect(question, 'a label that follows the answer above it').toHaveText(words.question('Anna'));
    await choose(form, 'workshop', 'bikes');
    await expect(help, 'a help text that follows the workshop chosen').toHaveText(words.help(defaults.bikes));
    await axe(page, `${language}: the booking with its texts recalled`);

    await form.locator('button[data-jfb-nav="submit"]').click();
    const summary = form.locator(':scope > [data-jfb-summary]');
    await expect(summary, 'the runtime refused the send for the question').toBeFocused();
    await expect(summary.locator('a'), "the summary's link names the question as it reads now").toHaveText([
      new RegExp(`^${words.question('Anna').replace(/[?]/g, '[?]')} \\S`),
    ]);
    await axe(page, `${language}: the summary with a recalled label`);

    expect(
      chunks.filter((chunk) => chunk === 'recall'),
      'the merge tags, once',
    ).toEqual(['recall']);
    await choose(form, 'bringGuests', 'yes');
    const guests = form.locator('[data-jfb-alias="guests"]');
    await expect(guests).toBeVisible();
    const added = page.waitForEvent('load');
    await guests.locator('button[name="jfb-nav"]').last().click();
    await added;
    await expect(question, 'the page the server sent back recalls the name too').toHaveText(words.question('Anna'));
    await form.locator('input[name="guests[0].guestName"]').fill('Ben');
    await expect(closing, 'a text block that follows the rows').toHaveText(words.guests('Ben'));
    await choose(form, 'bringGuests', 'no');
    await expect(guests).toBeHidden();
    await expect(closing, 'the guests the rules hide recall nothing').toHaveText(words.guests(defaults.none));
    await axe(page, `${language}: the booking with its guests hidden`);

    expect(errors, 'console errors').toEqual([]);
  });
}

test('a form without a text that recalls answers never loads the merge tags', async ({ page }) => {
  const { errors, chunks } = watch(page);
  await page.goto(pathOfForm('contact', 'en'));
  const form = formOn(page);
  const loaded = page.waitForRequest(/\/chunks\/form-[^/]*\.js/);
  await form.locator('input, textarea').first().fill('Ada');
  await loaded;
  await form.locator('button[data-jfb-nav="submit"]').click();
  await expect(form.locator('[data-jfb-summary]'), 'the runtime refused the send').toBeVisible();
  await page.waitForLoadState('networkidle');
  expect(chunks).toContain('form');
  expect(chunks).not.toContain('recall');
  expect(errors, 'console errors').toEqual([]);
});
