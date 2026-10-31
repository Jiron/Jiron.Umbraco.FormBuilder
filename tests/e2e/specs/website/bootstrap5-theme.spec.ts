import type { Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { join } from 'node:path';
import { expect, test } from '../../fixtures.ts';
import { pageOf, pathOf } from '../../support/demo-content.ts';
import { repoRoot } from '../../support/paths.ts';

// The bootstrap5 theme on the demo's specimen, a form with a field of every type (ROADMAP 6.3d, ADR-0015, ADR-0108).
// The demo loads its copy of Bootstrap 5.3.8 on a page whose forms show in bootstrap5, as a Bootstrap site loads its
// own. In light, in Bootstrap's dark colour mode and right to left (an RTL page with Bootstrap's right-to-left build),
// at a phone's and a desktop's width, empty and with the errors of an empty post, axe finds no WCAG 2.2 A or AA
// violation and the page does not scroll sideways. The theme's stylesheet styles only its forms and reads Bootstrap's
// own variables; what Bootstrap has no class for (cards, buttons, stars, a grid's columns, fields side by side, the
// marks of an answer that needs a change, the legends' sizes, a long scale's rows) works as drawn. JavaScript stays on for axe, and the runtime is blocked: no
// rule of the theme reads whether scripts run, so each page renders as it does without JavaScript.

const SPECIMEN = pathOf(pageOf('demoSpecimen', 'en'), 'en');
const BOOTSTRAP = '/demo/vendor/bootstrap-5.3.8/bootstrap.min.css';
const RTL_BUILD = join(
  repoRoot,
  'demo/Jiron.Umbraco.FormBuilder.Demo/wwwroot/demo/vendor/bootstrap-5.3.8/bootstrap.rtl.min.css',
);

test.beforeEach(async ({ context }) => {
  await context.route('**/jfb/assets/runtime/**', (route) => route.abort());
});

const pageForm = (page: Page): Locator => page.locator('main form.jfb-form');

/** The page's text and ground colours. */
const bodyColours = (page: Page): Promise<string[]> =>
  page.evaluate(() => [getComputedStyle(document.body).color, getComputedStyle(document.body).backgroundColor]);

/** An RTL page, as a right-to-left Bootstrap site's: the root and the forms right to left, Bootstrap's RTL build. */
async function rightToLeft(page: Page): Promise<void> {
  await page.route(`**${BOOTSTRAP}`, (route) => route.fulfill({ path: RTL_BUILD, contentType: 'text/css' }));
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

for (const [variant, scheme, rtl] of [
  ['light', '', false],
  ['dark', 'dark', false],
  ['right to left', '', true],
] as const) {
  for (const width of [375, 1440]) {
    test(`the specimen in ${variant} at ${width} px, empty and with errors: axe is clean and nothing scrolls sideways`, async ({
      page,
    }) => {
      if (rtl) await rightToLeft(page);
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${SPECIMEN}?theme=bootstrap5&scheme=${scheme}`);
      await expect(pageForm(page)).toHaveClass(/(^| )jfb-theme-bootstrap5( |$)/);
      if (scheme) await expect(page.locator('html')).toHaveAttribute('data-bs-theme', 'dark');
      await check(page, width, `${variant} at ${width} px, empty`);

      const loaded = page.waitForEvent('load');
      await pageForm(page).locator('.jfb-navigation .jfb-button-primary').click();
      await loaded;
      await expect(pageForm(page).locator('.jfb-error-summary'), 'an empty post shows the error summary').toBeVisible();
      await check(page, width, `${variant} at ${width} px, with errors`);
    });
  }
}

test("the demo loads Bootstrap only for bootstrap5, and the theme's stylesheet styles only its forms", async ({
  page,
}) => {
  await page.goto(`${SPECIMEN}?theme=bootstrap5&scheme=`);
  await expect(page.locator(`link[href="${BOOTSTRAP}"]`), 'Bootstrap, as a Bootstrap site has it').toHaveCount(1);
  const stylesheet = await page.locator('link[href^="/jfb/assets/themes/bootstrap5.css?v="]').getAttribute('href');
  expect(stylesheet, 'and the stylesheet for what Bootstrap lacks').toBeTruthy();
  expect(await bodyColours(page), "the page is a Bootstrap site: Bootstrap's body colours").toEqual([
    'rgb(33, 37, 41)',
    'rgb(255, 255, 255)',
  ]);

  await page.goto(`${SPECIMEN}?theme=unstyled`);
  await expect(page.locator(`link[href="${BOOTSTRAP}"]`), 'no Bootstrap for another theme').toHaveCount(0);
  const look = () =>
    pageForm(page).evaluate((form) => {
      const style = (selector: string) => getComputedStyle(form.querySelector(selector)!);
      return [
        style('.jfb-fields').display,
        style('.jfb-choices-cards').display,
        style('.jfb-choices-buttons label').borderStyle,
        style('.jfb-matrix-head').display,
        style('.jfb-ranking').borderStyle,
      ];
    });
  const before = await look();
  await page.addStyleTag({ url: stylesheet! });
  expect(await look(), 'an unstyled form stays as it was with the bootstrap5 stylesheet on its page').toEqual(before);
});

test("the theme reads Bootstrap's own variables, in its colour modes", async ({ page }) => {
  const toggle = () =>
    pageForm(page)
      .locator('[data-jfb-alias="firstVisit"] label')
      .first()
      .evaluate((label) => getComputedStyle(label).color);
  await page.goto(`${SPECIMEN}?theme=bootstrap5&scheme=`);
  expect(await toggle(), "a choice drawn as a button, in Bootstrap's emphasis colour").toBe('rgb(5, 44, 101)');
  await page.goto(`${SPECIMEN}?theme=bootstrap5&scheme=dark`);
  expect(await toggle(), "and in Bootstrap's dark mode, its dark one").toBe('rgb(110, 168, 254)');
  expect(await bodyColours(page), "the page in Bootstrap's dark body colours").toEqual([
    'rgb(222, 226, 230)',
    'rgb(33, 37, 41)',
  ]);

  await page.goto(`${SPECIMEN}?theme=bootstrap5&scheme=`);
  const loaded = page.waitForEvent('load');
  await pageForm(page).locator('.jfb-navigation .jfb-button-primary').click();
  await loaded;
  const marks = await pageForm(page).evaluate((form) => ({
    control: getComputedStyle(form.querySelector('#jfb-1-displayName')!).borderColor,
    card: getComputedStyle(form.querySelector('[data-jfb-alias="room"] .jfb-choice')!).borderColor,
    heading: getComputedStyle(form.querySelector('.jfb-error-summary > h2')!).fontSize,
  }));
  expect(
    marks,
    "an answer that needs a change takes Bootstrap's invalid colour, the summary's heading a form's size",
  ).toEqual({
    control: 'rgb(220, 53, 69)',
    card: 'rgb(220, 53, 69)',
    heading: '20px',
  });
  await expect(pageForm(page).locator('input[type=file]')).toHaveClass(/(^| )form-control( |$)/);

  // One mark for one state: a refused text box under focus has the outline, and no red shadow beside it.
  const refused = pageForm(page).locator('#jfb-1-email');
  await page.keyboard.press('Tab');
  await refused.focus();
  await expect
    .poll(() =>
      refused.evaluate((element) => [getComputedStyle(element).outlineStyle, getComputedStyle(element).boxShadow]),
    )
    .toEqual(['solid', 'none']);
});

test('cards, buttons and stars choose as drawn, fields side by side line up, and a grid shows its columns once', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SPECIMEN}?theme=bootstrap5&scheme=`);
  const form = pageForm(page);

  // The whole card is the target, its picture included.
  await form
    .locator('[data-jfb-alias="room"] .jfb-choice')
    .nth(1)
    .click({ position: { x: 24, y: 24 } });
  await expect(form.locator('[data-jfb-alias="room"] input').nth(1)).toBeChecked();
  // A choice drawn as a button.
  await form.locator('[data-jfb-alias="firstVisit"] label', { hasText: 'No' }).click();
  await expect(
    form.locator('[data-jfb-alias="firstVisit"]').getByRole('radio', { name: 'No', exact: true }),
  ).toBeChecked();
  // Stars fill up to the chosen one, its number for screen readers only.
  await form.locator('[data-jfb-alias="rating"] label').nth(2).click();
  await page.mouse.move(0, 0);
  const stars = await form
    .locator('[data-jfb-alias="rating"] label')
    .evaluateAll((labels) =>
      labels.map((label) => getComputedStyle(label, '::before').maskImage.includes("fill='%23000'")),
    );
  expect(stars, 'three of five stars filled').toEqual([true, true, true, false, false]);
  for (const box of await form
    .locator('[data-jfb-alias="rating"] .jfb-rating-value')
    .evaluateAll((values) => values.map((value) => value.getBoundingClientRect().width))) {
    expect(box, 'the numbers for screen readers').toBeLessThanOrEqual(1);
  }

  // Fields side by side line up their controls, under labels with and without help.
  // Both read in one frame, as Bootstrap's smooth scrolling may still be moving the page.
  const [email, name] = await form.evaluate((element) =>
    ['#jfb-1-email', '#jfb-1-displayName'].map((id) => element.querySelector(id)?.getBoundingClientRect().top),
  );
  expect(email, 'the email is on the page').toEqual(expect.any(Number));
  expect(email, 'the email beside the name, which has help').toBe(name);

  // A grid's column heads over the lined-up columns, each answer's column for screen readers.
  await expect(form.locator('.jfb-matrix-head')).toBeVisible();
  await page.setViewportSize({ width: 375, height: 900 });
  await expect(form.locator('.jfb-matrix-head'), 'on a phone each answer names its column').toBeHidden();
  await expect(form.locator('.jfb-matrix label span').first()).toBeVisible();
});

test("a field's legend reads as a label and a group's as a heading, and a long scale wraps into even rows on a phone", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SPECIMEN}?theme=bootstrap5&scheme=`);
  const type = await pageForm(page).evaluate((form) => {
    const style = (selector: string) => getComputedStyle(form.querySelector(selector)!);
    return {
      label: style('label[for="jfb-1-displayName"]').fontSize,
      field: style('[data-jfb-alias="firstVisit"] legend').fontSize,
      group: parseFloat(style('.jfb-group > legend').fontSize),
      weight: style('.jfb-group > legend').fontWeight,
    };
  });
  expect(type.field, "a field's legend at a label's size, not Bootstrap's legend size").toBe(type.label);
  expect(type.group, "a group's legend larger").toBeGreaterThan(parseFloat(type.label));
  expect(type.weight, "in a heading's weight").toBe('500');

  // Eleven points: six in the first row and five in the second, not nine and two.
  await page.setViewportSize({ width: 375, height: 900 });
  const rows = await pageForm(page)
    .locator('[data-jfb-alias="recommend"] .jfb-choice > label')
    .evaluateAll((labels) => {
      const tops = labels.map((label) => Math.round(label.getBoundingClientRect().top));
      return [...new Set(tops)].map((top) => tops.filter((each) => each === top).length);
    });
  expect(rows, 'even rows').toEqual([6, 5]);
});
