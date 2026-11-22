import type { Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { settled } from '../../support/axe.ts';
import { pageOf, pathOf } from '../../support/demo-content.ts';

// The unstyled theme on the demo's specimen, a form with a field of every type (ROADMAP 6.3c, ADR-0015, ADR-0107):
// semantic markup and the functional base rules only. Left to right and right to left, at a phone's and a desktop's
// width, empty and with the errors of an empty post, axe finds no WCAG 2.2 A or AA violation and the page does not
// scroll sideways. The markup reads without a theme: a rating's points show their numbers, a ranking's moves their
// arrows and its places once, a scale's end words sit apart under its ends, and a grid's column heads stay hidden, since
// each answer names its column; the default theme still draws its own in their place. JavaScript stays on for axe, and
// the runtime is blocked: no base rule reads whether scripts run, so each page renders as it does without JavaScript.

const SPECIMEN = pathOf(pageOf('demoSpecimen', 'en'), 'en');

test.beforeEach(async ({ context }) => {
  await context.route('**/jfb/assets/runtime/**', (route) => route.abort());
});

const pageForm = (page: Page): Locator => page.locator('main form.jfb-form');

/** Sets the page and its forms right to left as each page loads, as a right-to-left site's pages are. */
async function rightToLeft(page: Page): Promise<void> {
  await page.addInitScript(() => {
    document.addEventListener('DOMContentLoaded', () => {
      document.documentElement.dir = 'rtl';
      for (const form of document.querySelectorAll('form.jfb-form')) form.setAttribute('dir', 'rtl');
    });
  });
}

/** The elements whose box reaches past the window's edges, for a page that scrolls sideways. */
async function outsideTheWindow(page: Page, width: number): Promise<string[]> {
  return page.evaluate(
    (right) =>
      [...document.querySelectorAll('body *')]
        .filter((element) => {
          const box = element.getBoundingClientRect();
          return box.right > right + 0.5 || box.left < -0.5;
        })
        .map((element) => `${element.localName}.${[...element.classList].join('.')}`)
        .slice(0, 12),
    width,
  );
}

/** Axe's WCAG 2.2 A and AA rules on the whole page, and its width. */
async function check(page: Page, width: number, what: string): Promise<void> {
  const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(
    scrollWidth,
    `${what} scrolls sideways: ${(await outsideTheWindow(page, width)).join(', ')}`,
  ).toBeLessThanOrEqual(width);
  await settled(page);
  const axe = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    axe.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    ),
    `${what}: axe`,
  ).toEqual([]);
}

for (const rtl of [false, true]) {
  const variant = rtl ? 'right to left' : 'left to right';
  for (const width of [375, 1440]) {
    test(`the specimen ${variant} at ${width} px, empty and with errors: axe is clean and nothing scrolls sideways`, async ({
      page,
    }) => {
      if (rtl) await rightToLeft(page);
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${SPECIMEN}?theme=unstyled&scheme=`);
      await expect(pageForm(page)).toHaveClass(/(^| )jfb-theme-unstyled( |$)/);
      await expect(pageForm(page)).toHaveAttribute('dir', rtl ? 'rtl' : 'ltr');
      await check(page, width, `${variant} at ${width} px, empty`);

      const loaded = page.waitForEvent('load');
      await pageForm(page).locator('.jfb-navigation .jfb-button-primary').click();
      await loaded;
      await expect(pageForm(page).locator('.jfb-error-summary'), 'an empty post shows the error summary').toBeVisible();
      await check(page, width, `${variant} at ${width} px, with errors`);
    });
  }
}

/** The box of each element the selector finds in the form, as the browser lays it out. */
const boxes = (page: Page, selector: string) =>
  pageForm(page)
    .locator(selector)
    .evaluateAll((elements) =>
      elements.map((element) => {
        const box = element.getBoundingClientRect();
        return { left: box.left, right: box.right, top: box.top, width: box.width, height: box.height };
      }),
    );

test('without a theme, a rating, a scale, a grid and a ranking say what they mean, and once', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SPECIMEN}?theme=unstyled&scheme=`);
  const rating = pageForm(page).locator('[data-jfb-alias="rating"]');

  // A rating's points show their numbers; screen readers hear the words at its ends with the first and the last, as a
  // scale's, with no space before the comma.
  await expect(rating.locator('.jfb-rating-value')).toHaveText(['1', '2', '3', '4', '5']);
  for (const box of await boxes(page, '[data-jfb-alias="rating"] .jfb-rating-value')) {
    expect(box.width, 'each number shows').toBeGreaterThan(4);
  }
  for (const name of ['1, Poor', '2', '3', '4', '5, Excellent']) {
    await expect(rating.getByRole('radio', { name, exact: true })).toHaveCount(1);
  }

  const scale = pageForm(page).locator('[data-jfb-alias="recommend"]');
  for (const name of ['0, Not likely', '1', '9', '10, Very likely']) {
    await expect(scale.getByRole('radio', { name, exact: true }), 'no space before the comma').toHaveCount(1);
  }

  // A scale's points in a row, and its end words apart under its two ends.
  const points = await boxes(page, '[data-jfb-alias="recommend"] .jfb-choice');
  expect(new Set(points.map((point) => point.top)).size, 'the eleven points on one line at this width').toBe(1);
  for (const [alias, words] of [
    ['rating', 'Poor Excellent'],
    ['recommend', 'Not likely Very likely'],
  ]) {
    const text = await pageForm(page)
      .locator(`[data-jfb-alias="${alias}"] .jfb-ends`)
      .evaluate((ends) => ends.textContent);
    expect(text, 'the end words apart in the text itself, for a page without a stylesheet').toBe(words);
  }
  const ends = await boxes(page, '[data-jfb-alias="recommend"] .jfb-ends > span');
  expect(ends, 'two end words').toHaveLength(2);
  const [low, high, first, last] = [ends[0]!, ends[1]!, points[0]!, points[points.length - 1]!];
  expect(low.top, 'the end words on one line').toBe(high.top);
  expect(high.left - low.right, 'apart').toBeGreaterThan(16);
  expect(Math.abs(low.left - first.left), 'the low word under the first point').toBeLessThanOrEqual(1);
  expect(high.right, 'the high word under the last point').toBeGreaterThanOrEqual(last.right - 1);

  // A grid's column heads stay hidden: each answer's label names its column.
  await expect(pageForm(page).locator('.jfb-matrix-head')).toBeHidden();
  await expect(pageForm(page).locator('.jfb-matrix label').first()).toBeVisible();

  // A ranking's moves show their arrows, and its places show once.
  const ranking = pageForm(page).locator('ol.jfb-ranking');
  expect(await ranking.evaluate((list) => getComputedStyle(list).listStyleType)).toBe('none');
  await expect(ranking).toHaveAttribute('role', 'list');
  for (const arrow of await pageForm(page).locator('.jfb-ranking :is(.jfb-icon-up, .jfb-icon-down)').all()) {
    const drawn = await arrow.evaluate((icon) => {
      const style = getComputedStyle(icon);
      return { width: icon.getBoundingClientRect().width, mask: style.maskImage };
    });
    expect(drawn.width, 'an arrow has a size').toBeGreaterThanOrEqual(16);
    expect(drawn.mask, 'and its symbol').toContain('data:image/svg+xml');
  }
  const [up, down] = await pageForm(page)
    .locator('.jfb-ranking > li')
    .first()
    .locator(':is(.jfb-icon-up, .jfb-icon-down)')
    .evaluateAll((icons) => icons.map((icon) => getComputedStyle(icon).maskImage));
  expect(up, 'up and down are different arrows').not.toBe(down);

  // In forced colours (a Windows contrast theme) the arrows take the buttons' text colour, which the browser would
  // otherwise paint over.
  await page.emulateMedia({ forcedColors: 'active' });
  const forced = await pageForm(page)
    .locator('.jfb-ranking .jfb-icon-up')
    .first()
    .evaluate((icon) => ({
      adjust: getComputedStyle(icon).forcedColorAdjust,
      arrow: getComputedStyle(icon).backgroundColor,
      text: getComputedStyle(icon.closest('button')!).color,
    }));
  expect(forced.adjust, 'the arrow keeps its own colour').toBe('none');
  expect(forced.arrow, "the button's text colour").toBe(forced.text);
});

test('the default theme still draws its own symbols in their place', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SPECIMEN}?theme=default&scheme=`);
  for (const box of await boxes(page, '[data-jfb-alias="rating"] .jfb-rating-value')) {
    expect(box.width * box.height, "a rating's numbers are for screen readers only").toBeLessThanOrEqual(1);
  }
  const star = await pageForm(page)
    .locator('[data-jfb-alias="rating"] label')
    .first()
    .evaluate((label) => getComputedStyle(label, '::before').maskImage);
  expect(star, 'the stars show instead').toContain('data:image/svg+xml');
  await expect(pageForm(page).locator('.jfb-matrix-head'), 'the column heads over the lined-up columns').toBeVisible();
  const stars = await boxes(page, '[data-jfb-alias="rating"] .jfb-choice');
  expect(stars, 'five stars').toHaveLength(5);
  expect(stars[1]!.left - stars[0]!.right, 'the stars side by side, as 6.3b drew them').toBe(0);
});
