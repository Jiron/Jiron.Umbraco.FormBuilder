import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';

// The Answers of a form's Insights view (ROADMAP 11.5b, IA 5.5, ADR-0212): a form with an answer of every kind and 120
// entries, the first twelve sent before an option was taken away, opened by a user who may only view entries. The
// switch shows what they answered in the range: one choice with an older option and the visitors' own texts, a ranking
// by its average position, a grid row by row, a scale's groups and recommendation score, a checkbox's share of the
// entries, a repeatable group's rows, how often an email was given, and the free texts newest first, five and then
// twenty more, each opening its entry. The entries list's filter narrows them through its own dialog and chip; one
// switch shows every chart as its table, kept for the user; and neither way breaks a rule of axe.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';
const COUNT = 120;

/** The entries the seed sends, by what each answered first, as the host's answers form writes them. */
function heardOf(i: number): string {
  if (i % 11 === 0) return 'other';
  if (i <= 12 && i % 3 === 0) return 'radio';
  return ['search', 'friend', 'search', 'social', 'friend', 'search', 'newsletter'][i % 7] ?? '';
}

const entries = Array.from({ length: COUNT }, (_, index) => index + 1);
const share = (count: number, whole: number) => `${String(Math.round((count / whole) * 100))}% (${String(count)})`;

test('the Answers summarise each kind of answer, filter by an answer, list the free texts and keep their tables', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(420_000);
  const api = await apiAs('admin');
  const seeded = await api.post('/jfb-testing/answers-form', {
    data: { name: `Event feedback ${String(Date.now())}`, count: COUNT },
    timeout: 300_000,
  });
  expect(seeded.ok(), await seeded.text()).toBe(true);
  const { key } = (await seeded.json()) as { key: string };

  await loginAs('viewer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/insights`);
  const view = page.locator('jfb-form-insights-view');
  const modes = view.getByRole('group', { name: 'View' });
  await expect(modes.getByRole('button', { name: 'Overview' })).toHaveAttribute('aria-pressed', 'true', {
    timeout: 60_000,
  });
  await modes.getByRole('button', { name: 'Answers' }).click();
  await expect(modes.getByRole('button', { name: 'Answers' })).toHaveAttribute('aria-pressed', 'true');
  const answers = view.locator('jfb-insight-answers');
  await expect(answers.locator('.lead')).toHaveText(`Answers from ${String(COUNT)} entries.`, { timeout: 60_000 });
  await expect(view.getByRole('switch', { name: 'Compare with the previous period' })).toHaveCount(0);
  const box = (heading: string) => answers.locator(`uui-box[headline="${heading}"]`);

  // One choice: the live options, the radio advert of the twelve before it went, and the visitors' own words.
  const heard = box('How did you hear about us?');
  await expect(heard.locator('.count')).toHaveText(`${String(COUNT)} answers`);
  const said = (where: ReturnType<typeof box>, name: string) =>
    where.locator('jfb-insight-bars li', { hasText: name }).locator('.said');
  const count = (answer: string) => entries.filter((i) => heardOf(i) === answer).length;
  await expect(said(heard, 'A friend')).toHaveText(share(count('friend'), COUNT));
  await expect(said(heard, 'Radio advert (no longer an option)')).toHaveText(share(count('radio'), COUNT));
  await expect(said(heard, 'Other, in their own words')).toHaveText(share(count('other'), COUNT));

  // Several choices add up past the whole, which the box says.
  await expect(box('Which sessions did you join?').locator('.note')).toHaveText(
    'Several could be chosen, so shares total over 100%.',
  );

  // A ranking by its average position: content first in three of five orders, second in the others.
  const ranking = box('Rank what matters most to you').locator('jfb-insight-bars li');
  await expect(ranking.first()).toContainText('Content');
  await expect(ranking.first().locator('.said')).toHaveText('average position 1.4');
  await expect(ranking.nth(1).locator('.said')).toHaveText('average position 2.2');

  // A grid row by row, across the width.
  await expect(box('How was the day?').locator('h3')).toHaveText(['Talks', 'Organisation', 'Food']);

  // A scale from 0 to 10: 20 at 0 to 6, 30 at 7 or 8, 70 at 9 or 10.
  const scale = box('How likely are you to recommend us to a friend?');
  await expect(said(scale, '0 to 6')).toHaveText(share(20, COUNT));
  await expect(said(scale, '9 and 10')).toHaveText(share(70, COUNT));
  await expect(scale.locator('.score')).toHaveText('Recommendation score: +42');

  // A checkbox, ticked in one entry of three; an unticked box stores nothing, so the share is of the entries.
  await expect(said(box('Send me the newsletter'), 'Not ticked')).toHaveText(share(80, COUNT));
  // An email says how often it was given; a repeatable group's meal is answered per row.
  await expect(box('Email address').locator('.line')).toHaveText('Answered in 80% of the entries.');
  const meal = box('Meal');
  await expect(meal.locator('.count')).toHaveText('90 answers');
  await expect(meal.locator('.note')).toHaveText('One answer for each row of Guests.');
  await axeInside(page, 'jfb-form-insights-view');

  // The free texts: five newest, then twenty more, then the last five, and each opens its entry.
  const comments = box('Anything else you would like to tell us?');
  const links = comments.locator('.texts a');
  await expect(links).toHaveCount(5);
  await comments.getByRole('button', { name: 'Show more' }).click();
  await expect(links).toHaveCount(25);
  await expect(links.nth(5)).toBeFocused();
  await comments.getByRole('button', { name: 'Show more' }).click();
  await expect(links).toHaveCount(30);
  await expect(comments.getByRole('button', { name: 'Show more' })).toHaveCount(0);

  // The entries list's filter: those who heard of it from a friend.
  await view.getByRole('button', { name: 'Filter', exact: true }).click();
  await view.locator('#filter-menu').getByRole('button', { name: 'How did you hear about us?', exact: true }).click();
  const filter = page.locator('jfb-entry-filter-modal');
  await filter.locator('uui-checkbox[label="A friend"]').click();
  await filter.getByRole('button', { name: 'Apply filter', exact: true }).click();
  await expect(view.locator('.chip-text')).toHaveText(['How did you hear about us?: A friend']);
  await expect(answers.locator('.lead')).toHaveText(`Answers from ${String(count('friend'))} entries.`);
  await expect(said(heard, 'A friend')).toHaveText(share(count('friend'), count('friend')));
  await view.getByRole('button', { name: 'Remove the filter How did you hear about us?: A friend' }).click();
  await expect(answers.locator('.lead')).toHaveText(`Answers from ${String(COUNT)} entries.`);

  // Every chart as its table, with one switch, kept for the user.
  const kept = page.waitForResponse(
    (response) => response.url().endsWith('/insights/tables') && response.request().method() === 'PUT',
  );
  // Umbraco's toggle keeps its input out of sight, so the press goes to the toggle, as a pointer's does.
  const tables = view.locator('uui-toggle[label="Show as tables"]');
  await tables.click();
  expect((await kept).ok(), 'the choice is kept').toBe(true);
  await expect(view.getByRole('switch', { name: 'Show as tables' })).toBeChecked();
  await expect(heard.locator('table tbody tr', { hasText: 'A friend' })).toContainText(String(count('friend')));
  await expect(box('How was the day?').locator('thead th')).toHaveText(['Row', 'Poor', 'Fair', 'Good', 'Excellent']);
  await axeInside(page, 'jfb-form-insights-view');
  await page.reload();
  await view.getByRole('group', { name: 'View' }).getByRole('button', { name: 'Answers' }).click();
  await expect(box('How did you hear about us?').locator('table')).toBeVisible({ timeout: 60_000 });
  await expect(view.getByRole('switch', { name: 'Show as tables' })).toBeChecked();
  await tables.click();
  await expect(box('How did you hear about us?').locator('jfb-insight-bars')).toBeVisible();

  // A free text opens its entry.
  const first = box('Anything else you would like to tell us?').locator('.texts a').first();
  const said1 = (await first.textContent())?.trim() ?? '';
  await first.click();
  await expect(page.locator('jfb-entry-panel')).toContainText(said1, { timeout: 60_000 });
});
