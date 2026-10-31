import { expect, test, type Locator, type Page } from '@playwright/test';
import { axe } from '../../support/axe.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { fillForm, press, submitForm } from '../../support/form.ts';

// The journeys through the demo's forms of several pages (ROADMAP 6.5a, ADR-0100, ADR-0122), without and with
// JavaScript: the job application moves through its three pages with Next and Back, every page checked on the server
// before the next and every answer kept, its steps naming where the visitor is; the specimen shows its summary page,
// the answers of its first page with a "Change" for each, and is sent from there, its progress a bar. A third journey
// takes the job application's three pages right to left, set on the form after each page loads, as the theme checks of
// 6.3 set it, since the demo has no language written that way. Axe is clean on every page of them.

/** The English page of a demo form. */
function pathOfForm(alias: string): string {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo shows no ${alias} form.`);
  return pathOf(shown.texts.en, 'en');
}

/** The page's own form; the newsletter in the footer is another. */
const formOf = (page: Page): Locator => page.locator('main form.jfb-form');

/** The title of the page the form shows. */
const titleOf = (form: Locator): Locator => form.locator('[data-jfb-page]:not([hidden]) > .jfb-title');

/** Picks a choice by its label, as a visitor does: a card's label covers its control. */
async function choose(form: Locator, name: string, value: string): Promise<void> {
  const input = form.locator(`input[name="${name}"][value="${value}"]`);
  await form.locator(`label[for="${(await input.getAttribute('id')) ?? ''}"]`).click();
  await expect(input).toBeChecked();
}

/** The steps' names, and the one the visitor is on. */
async function stepsOf(form: Locator): Promise<{ names: string[]; current: string; done: string[] }> {
  const steps = form.locator('.jfb-progress-steps li');
  return {
    names: (await steps.allInnerTexts()).map((text) => text.trim()),
    current: (await form.locator('.jfb-progress-steps li[aria-current="step"]').innerText()).trim(),
    done: (await form.locator('.jfb-progress-steps li[data-jfb-done]').allInnerTexts()).map((text) => text.trim()),
  };
}

const REFEREES = {
  'referees[0].refName': 'Grace Hopper',
  'referees[0].refContact': 'grace@visitor.jfb.test',
  'referees[1].refName': 'Mary Somerville',
  'referees[1].refContact': '+44 20 7946 0000',
};

/** The job application's three pages, Next refused while a page needs an answer, Back keeping every answer. */
async function applicationJourney(page: Page, what: string, javaScript: boolean, rtl: boolean): Promise<void> {
  if (rtl) {
    await page.addInitScript(() =>
      document.addEventListener('DOMContentLoaded', () => {
        for (const form of document.querySelectorAll('.jfb-form')) form.setAttribute('dir', 'rtl');
      }),
    );
  }
  await page.goto(pathOfForm('application'));
  const form = formOf(page);
  const names = ['About you', 'Your references', 'Your letter'];
  await expect(titleOf(form)).toHaveText('About you');
  expect(await stepsOf(form)).toEqual({ names, current: 'About you', done: [] });
  if (rtl) await expect(form).toHaveAttribute('dir', 'rtl');
  await axe(page, `${what}: the first page`, javaScript);

  await press(form, 'next');
  await expect(form.locator('[data-jfb-summary]')).toBeVisible();
  await expect(titleOf(form)).toHaveText('About you');
  await axe(page, `${what}: the first page with its errors`, javaScript);

  await form.locator('[name="fullName.first"]').fill('Ada');
  await form.locator('[name="fullName.last"]').fill('Lovelace');
  await fillForm(form, { email: 'ada@visitor.jfb.test', post: 'assistant' });
  await press(form, 'next');
  await expect(titleOf(form)).toHaveText('Your references');
  expect(await stepsOf(form)).toEqual({ names, current: 'Your references', done: ['About you'] });
  if (rtl) {
    // The steps and the buttons run from the inline start, which is the right.
    const first = await form.locator('.jfb-progress-steps li').first().boundingBox();
    const last = await form.locator('.jfb-progress-steps li').last().boundingBox();
    expect(first!.x, 'the first step on the right').toBeGreaterThan(last!.x);
    const next = await form.locator('.jfb-navigation button[data-jfb-nav="next"]').boundingBox();
    const back = await form.locator('.jfb-navigation button[data-jfb-nav="back"]').boundingBox();
    expect(next!.x, 'Next before Back, on the right').toBeGreaterThan(back!.x);
  }
  await axe(page, `${what}: the second page`, javaScript);

  await fillForm(form, REFEREES);
  await press(form, 'next');
  await expect(titleOf(form)).toHaveText('Your letter');
  expect(await stepsOf(form)).toEqual({ names, current: 'Your letter', done: ['About you', 'Your references'] });
  await fillForm(form, { letter: 'I ran a reading club for six years.' });
  await axe(page, `${what}: the third page`, javaScript);

  await press(form, 'back');
  await expect(titleOf(form)).toHaveText('Your references');
  await expect(form.locator('[name="referees[1].refName"]')).toHaveValue('Mary Somerville');
  await press(form, 'next');
  await expect(form.locator('[name="letter"]')).toHaveValue('I ran a reading club for six years.');
  await fillForm(form, { privacy: true });
  const sent = await submitForm(form);
  expect(sent.state, `${what}: sent`).toBe('sent');
  if (rtl) await expect(page.locator('main .jfb-form[data-jfb-form]')).toHaveAttribute('dir', 'rtl');
  await axe(page, `${what}: the outcome`, javaScript);
}

/** A row of the summary page: its term, its answer and its Change, by the term. */
function rowOf(form: Locator, term: string): { row: Locator; answer: Locator; change: Locator } {
  const row = form
    .locator('.jfb-summary dl > div')
    .filter({ has: form.page().locator(`dt:text-is(${JSON.stringify(term)})`) });
  return { row, answer: row.locator('dt + dd'), change: row.locator('dd > button') };
}

/** The specimen's first page answered where it needs answers, then its summary page. */
async function toSummary(page: Page): Promise<Locator> {
  await page.goto(pathOfForm('specimen'));
  const form = formOf(page);
  await expect(form.locator('.jfb-progress-bar progress')).toHaveAttribute('value', '1');
  await fillForm(form, {
    displayName: 'Mira Okafor',
    email: 'mira.okafor@visitor.jfb.test',
    branch: 'Riverside',
    copies: 2,
    privacy: true,
  });
  await choose(form, 'room', 'reading');
  await choose(form, 'rating', '4');
  await choose(form, 'recommend', '8');
  await form.locator('[name="contact.first"]').fill('Mira');
  await form.locator('[name="contact.last"]').fill('Okafor');
  await press(form, 'next');
  await expect(titleOf(form)).toHaveText('Check your answers');
  return form;
}

for (const javaScriptEnabled of [false, true]) {
  test.describe(javaScriptEnabled ? 'with JavaScript' : 'without JavaScript', () => {
    test.use({ javaScriptEnabled });

    test('the job application moves through its three pages, each checked before the next, and keeps every answer', async ({
      page,
    }) => {
      await applicationJourney(
        page,
        javaScriptEnabled ? 'with JavaScript' : 'without JavaScript',
        javaScriptEnabled,
        false,
      );
    });

    test("the specimen's summary page lists the first page's answers, changes one on its page, and sends from there", async ({
      page,
    }) => {
      const form = await toSummary(page);
      await expect(form.locator('.jfb-progress-bar progress')).toHaveAttribute('value', '2');
      await expect(form.locator('.jfb-progress-text')).toHaveText('Step 2 of 2');
      await expect(form.locator('.jfb-summary > section > h3')).toHaveText(['Every field type']);
      await expect(rowOf(form, 'Name on your library card').answer).toHaveText('Mira Okafor');
      await expect(rowOf(form, 'Branch').answer).toHaveText('Riverside');
      await expect(rowOf(form, 'Your website').answer).toHaveText('Not answered');
      await expect(rowOf(form, 'Your website').answer).toHaveAttribute('data-jfb-empty', '');
      await expect(rowOf(form, 'Deposit').answer).toHaveText('EUR 4.00');
      await expect(rowOf(form, 'Deposit').change, 'a calculation has no Change').toHaveCount(0);
      await expect(form.locator('.jfb-summary dt:text-is("Source")'), 'a hidden field is not listed').toHaveCount(0);
      await expect(form.locator('[name="anythingElse"]')).toBeVisible();
      await axe(page, 'the summary page', javaScriptEnabled);

      const email = rowOf(form, 'Email address');
      await expect(email.answer).toHaveText('mira.okafor@visitor.jfb.test');
      await expect(email.change).toHaveAccessibleName('Change Email address');
      await email.change.click();
      await expect(titleOf(form)).toHaveText('Every field type');
      expect(new URL(page.url()).hash).toBe('#jfb-1-email');
      await expect(form.locator('[name="email"]')).toBeInViewport();
      await expect(form.locator('[data-jfb-summary]'), 'no check on the way back').toHaveCount(0);
      await expect(form.locator('[name="displayName"]')).toHaveValue('Mira Okafor');
      await fillForm(form, { email: 'mira@visitor.jfb.test' });
      await press(form, 'next');
      await expect(titleOf(form)).toHaveText('Check your answers');
      await expect(rowOf(form, 'Email address').answer).toHaveText('mira@visitor.jfb.test');

      await fillForm(form, { anythingElse: 'See you on Saturday.' });
      const sent = await submitForm(form);
      expect(sent.state).toBe('sent');
    });
  });
}

test("the job application's three pages right to left", async ({ page }) => {
  await applicationJourney(page, 'right to left', true, true);
});

test('on a phone the summary drops each answer under its question, which keeps the row', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 800 });
  const form = await toSummary(page);
  const { row, answer } = rowOf(form, 'Name on your library card');
  const term = await row.locator('dt').boundingBox();
  const shown = await answer.boundingBox();
  const box = await row.boundingBox();
  expect(shown!.y, 'the answer under its question').toBeGreaterThan(term!.y + term!.height - 1);
  expect(shown!.width, 'the answer keeps more than half the row').toBeGreaterThan(box!.width / 2);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
  await axe(page, 'the summary page at 375 px', true);
});
