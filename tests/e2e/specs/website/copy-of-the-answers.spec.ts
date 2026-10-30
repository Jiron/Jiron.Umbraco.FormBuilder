import { expect, test, type Locator, type Page } from '@playwright/test';
import { axe } from '../../support/axe.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { fillForm, press, submitForm } from '../../support/form.ts';

// The copy of the answers (ROADMAP 6.5b, ADR-0123): the job application's outcome offers it, so the page after a send
// without JavaScript shows the answers below the outcome once, page by page, as the summary page writes them without
// "Change", with "Fill in the form again"; in print media only the answers and the outcome show, in ink on white. The
// same page again (a reload, the back button) keeps the outcome and shows a line in the answers' place, and an expired
// token shows the form as for any visitor, nothing of the entry. With JavaScript the print button shows and prints.
// Axe is clean on the copy.

/** The English page of a demo form. */
function pathOfForm(alias: string): string {
  const shown = formPages().find((each) => each.form === alias);
  if (shown === undefined) throw new Error(`The demo shows no ${alias} form.`);
  return pathOf(shown.texts.en, 'en');
}

/** The page's own form, or the outcome in its place; the newsletter in the footer is another. */
const wrapperOf = (page: Page): Locator => page.locator('main .jfb-form[data-jfb-form]');

/** A row of the copy: its term and its answer, by the term. */
function rowOf(copy: Locator, term: string): { row: Locator; answer: Locator } {
  const row = copy.locator('dl > div').filter({ has: copy.page().locator(`dt:text-is(${JSON.stringify(term)})`) });
  return { row, answer: row.locator('dt + dd') };
}

/** A letter only this test sends, so its answers are its own whatever other tests send meanwhile. */
const letterOf = (what: string): string => `I mend books on Saturdays (${what}, ${Date.now().toString(36)}).`;

/** The job application through its three pages, sent; the outcome's page. */
async function sendApplication(page: Page, letter: string): Promise<void> {
  await page.goto(pathOfForm('application'));
  const form = page.locator('main form.jfb-form');
  await form.locator('[name="fullName.first"]').fill('Ada');
  await form.locator('[name="fullName.last"]').fill('Lovelace');
  await fillForm(form, { email: 'ada@visitor.jfb.test', post: 'assistant' });
  await press(form, 'next');
  await fillForm(form, {
    'referees[0].refName': 'Grace Hopper',
    'referees[0].refContact': 'grace@visitor.jfb.test',
    'referees[1].refName': 'Mary Somerville',
    'referees[1].refContact': '+44 20 7946 0000',
  });
  await press(form, 'next');
  await fillForm(form, { letter, privacy: true });
  const sent = await submitForm(form);
  expect(sent.state, 'sent').toBe('sent');
}

test.describe('without JavaScript', () => {
  test.use({ javaScriptEnabled: false });

  test('the copy of the answers shows once after sending and in print, and a reused or expired token shows none', async ({
    page,
    request,
  }) => {
    const letter = letterOf('no JavaScript');
    await sendApplication(page, letter);
    const sentAt = page.url();
    const wrapper = wrapperOf(page);
    const copy = wrapper.locator(':scope > .jfb-summary');
    const links = wrapper.locator(':scope > [data-jfb-copy]');

    await expect(wrapper.locator(':scope > [data-jfb-outcome]')).toContainText('Thank you, Ada.');
    await expect(copy.locator('section > h3')).toHaveText(['About you', 'Your references', 'Your letter']);
    await expect(rowOf(copy, 'Your name').answer).toHaveText('Ada Lovelace');
    await expect(rowOf(copy, 'Email address').answer).toHaveText('ada@visitor.jfb.test');
    await expect(rowOf(copy, 'Which post are you applying for?').answer).toHaveText('Library assistant (60%)');
    const referees = rowOf(copy, 'Who can tell us about your work?').answer;
    await expect(referees.locator('ol > li')).toHaveCount(2);
    await expect(referees).toContainText('Grace Hopper');
    await expect(referees).toContainText('Mary Somerville');
    await expect(rowOf(copy, 'Why would you like to work with us?').answer).toHaveText(letter);
    await expect(rowOf(copy, 'Your CV').answer, 'a question the visitor left').toHaveText('Not answered');
    await expect(rowOf(copy, 'Could you start earlier than the posting says?').answer, 'a switch left off').toHaveText(
      'No',
    );
    await expect(copy.locator('button'), 'no Change: the entry is sent').toHaveCount(0);
    await expect(links.locator('button[data-jfb-print]'), 'without JavaScript the browser prints').toBeHidden();
    const again = links.locator('a');
    await expect(again).toHaveText('Fill in the form again');
    await expect(again).toHaveAttribute(
      'href',
      `${new URL(sentAt).pathname}#${(await wrapper.getAttribute('id')) ?? ''}`,
    );
    await axe(page, 'the copy of the answers', false);

    await page.emulateMedia({ media: 'print' });
    await expect(copy, 'the answers print').toBeVisible();
    await expect(wrapper.locator(':scope > [data-jfb-outcome]'), 'with the outcome').toBeVisible();
    await expect(links, 'the links stay on screen').toBeHidden();
    const name = rowOf(copy, 'Your name');
    expect(await name.answer.evaluate((element) => getComputedStyle(element).color), 'ink on white').toBe(
      'rgb(29, 36, 51)',
    );
    expect(
      await name.row.evaluate((element) => getComputedStyle(element).breakInside),
      'a row keeps to one sheet',
    ).toBe('avoid');
    await page.emulateMedia({ media: 'screen' });

    // The same address again, as a reload or the back button asks for it; a goto would only move to the fragment.
    const reused = await page.reload();
    expect(reused?.headers()['cache-control'], 'never stored').toBe('no-store');
    await expect(wrapper.locator(':scope > [data-jfb-outcome]'), 'the outcome again').toContainText('Thank you, Ada.');
    await expect(wrapper.locator('.jfb-summary'), 'a reused token shows no answers').toHaveCount(0);
    await expect(wrapper.locator(':scope > [data-jfb-copy-shown]')).toHaveText(
      'Your answers show only once, right after sending.',
    );
    await expect(page.locator('main')).not.toContainText(letter);
    await expect(page.locator('main')).not.toContainText('ada@visitor.jfb.test');
    await axe(page, 'the page after sending, seen again', false);

    const response = await request.post('/jfb-testing/sent-expired', {
      data: { form: 'application', value: new URL(sentAt).searchParams.get('jfb-sent') },
    });
    expect(response.ok(), await response.text()).toBe(true);
    const expired = new URL(sentAt);
    expired.searchParams.set('jfb-sent', ((await response.json()) as { value: string }).value);
    await page.goto(expired.toString());
    await expect(
      page.locator('main form.jfb-form'),
      'an expired token shows the form as for any visitor',
    ).toBeVisible();
    await expect(
      page.locator('main :is([data-jfb-outcome], .jfb-summary, [data-jfb-copy], [data-jfb-copy-shown])'),
    ).toHaveCount(0);
    await expect(page.locator('main')).not.toContainText(letter);
  });
});

test('with JavaScript the print button shows below the copy and prints it, and the copy fits a phone', async ({
  page,
}) => {
  await page.addInitScript(() => {
    const counted = window as Window & { printed?: number };
    counted.printed = 0;
    window.print = () => {
      counted.printed = (counted.printed ?? 0) + 1;
    };
  });
  await sendApplication(page, letterOf('JavaScript'));
  const wrapper = wrapperOf(page);
  const print = wrapper.locator(':scope > [data-jfb-copy] > button[data-jfb-print]');
  await expect(print).toBeVisible();
  await expect(print).toHaveAccessibleName('Print your answers');
  await print.click();
  expect(await page.evaluate(() => (window as Window & { printed?: number }).printed), 'printed once').toBe(1);
  await axe(page, 'the copy with its print button', true);

  await page.setViewportSize({ width: 375, height: 800 });
  const { row, answer } = rowOf(wrapper.locator(':scope > .jfb-summary'), 'Why would you like to work with us?');
  const term = await row.locator('dt').boundingBox();
  const shown = await answer.boundingBox();
  expect(shown!.y, 'the answer under its question').toBeGreaterThan(term!.y + term!.height - 1);
  expect(shown!.width, 'the answer keeps more than half the row').toBeGreaterThan((await row.boundingBox())!.width / 2);
  const button = await print.boundingBox();
  const link = await wrapper.locator(':scope > [data-jfb-copy] > a').boundingBox();
  expect(link!.y, 'the link on a row of its own').toBeGreaterThanOrEqual(button!.y + button!.height - 1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
  await axe(page, 'the copy at 375 px', true);
});
