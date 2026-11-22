import type { Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { settled } from '../../support/axe.ts';
import { pageOf, pathOf } from '../../support/demo-content.ts';
import { sideBySide, sinkMarkup } from '../../support/sink.ts';

// The default theme on the demo's specimen, a form with a field of every type (ROADMAP 6.3b, ADR-0022, ADR-0106). In
// light, dark and right to left, at a phone's and a desktop's width, empty and with the errors of an empty post, axe
// finds no WCAG 2.2 A or AA violation and the page does not scroll sideways; the dark scheme comes from a scheme class
// on the form or on any ancestor, or from the visitor's system with jfb-scheme-auto; and the theme's stylesheet styles
// only the forms of its theme. JavaScript stays on for axe, and the runtime is blocked: no rule of the theme reads
// whether scripts run, so each page renders as it does without JavaScript.

const SPECIMEN = pathOf(pageOf('demoSpecimen', 'en'), 'en');
const INK = { light: 'rgb(29, 36, 51)', dark: 'rgb(232, 234, 238)' };

test.beforeEach(async ({ context }) => {
  await context.route('**/jfb/assets/runtime/**', (route) => route.abort());
});

const pageForm = (page: Page) => page.locator('main form.jfb-form');

/** The elements whose box reaches past the window's right edge, for a page that scrolls sideways. */
async function widerThanTheWindow(page: Page, width: number): Promise<string[]> {
  return page.evaluate(
    (right) =>
      [...document.querySelectorAll('body *')]
        .filter((element) => element.getBoundingClientRect().right > right + 0.5)
        .map((element) => `${element.localName}.${[...element.classList].join('.')}`)
        .slice(0, 12),
    width,
  );
}

/** Axe's WCAG 2.2 A and AA rules on the whole page and its width, after the forms take the direction asked for. */
async function check(page: Page, width: number, rtl: boolean, what: string): Promise<void> {
  if (rtl) {
    await page.evaluate(() => {
      for (const form of document.querySelectorAll('form.jfb-form')) form.setAttribute('dir', 'rtl');
    });
  }
  const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(
    scrollWidth,
    `${what} scrolls sideways: ${(await widerThanTheWindow(page, width)).join(', ')}`,
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

for (const [variant, scheme, rtl] of [
  ['light', '', false],
  ['dark', 'dark', false],
  ['right to left', '', true],
] as const) {
  for (const width of [375, 1440]) {
    test(`the specimen in ${variant} at ${width} px, empty and with errors: axe is clean and nothing scrolls sideways`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${SPECIMEN}?theme=default&scheme=${scheme}`);
      await expect(pageForm(page)).toHaveClass(/(^| )jfb-theme-default( |$)/);
      await check(page, width, rtl, `${variant} at ${width} px, empty`);

      const loaded = page.waitForEvent('load');
      await pageForm(page).locator('.jfb-navigation .jfb-button-primary').click();
      await loaded;
      await expect(pageForm(page).locator('.jfb-error-summary'), 'an empty post shows the error summary').toBeVisible();
      await check(page, width, rtl, `${variant} at ${width} px, with errors`);
    });
  }
}

test('the dark scheme comes from the class on the form or on an ancestor, or from the system with jfb-scheme-auto', async ({
  page,
}) => {
  const ink = () => pageForm(page).evaluate((form) => getComputedStyle(form).color);

  await page.goto(`${SPECIMEN}?theme=default&scheme=`);
  expect(await ink(), 'light without a class').toBe(INK.light);
  await pageForm(page).evaluate((form) => form.classList.add('jfb-scheme-dark'));
  expect(await ink(), 'dark with the class on the form').toBe(INK.dark);

  await page.goto(`${SPECIMEN}?scheme=dark`);
  await expect(page.locator('html'), "the demo's switch puts the class on the root").toHaveClass('jfb-scheme-dark');
  expect(await ink(), 'dark with the class on an ancestor').toBe(INK.dark);

  await page.goto(`${SPECIMEN}?scheme=auto`);
  await page.emulateMedia({ colorScheme: 'light' });
  expect(await ink(), 'auto on a light system').toBe(INK.light);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await ink(), 'auto on a dark system').toBe(INK.dark);
});

test("the default stylesheet styles only the default theme's forms", async ({ page }) => {
  await page.goto(`${SPECIMEN}?theme=default`);
  const stylesheet = await page.locator('link[href^="/jfb/assets/themes/default.css"]').getAttribute('href');
  expect(stylesheet, 'a default form brings its stylesheet').toBeTruthy();
  // What the theme sets on a text input and on the form, as the browser computes it.
  const look = () =>
    pageForm(page).evaluate((form) => {
      const input = getComputedStyle(form.querySelector('input.jfb-control')!);
      const own = getComputedStyle(form);
      return [
        input.minHeight,
        input.borderRadius,
        input.paddingInlineStart,
        input.backgroundColor,
        own.display,
        own.color,
      ];
    });
  expect((await look())[0], 'a default form takes it').toBe('48px');

  await page.goto(`${SPECIMEN}?theme=unstyled`);
  await expect(
    page.locator('link[href^="/jfb/assets/themes/default.css"]'),
    'an unstyled page brings none',
  ).toHaveCount(0);
  const before = await look();
  await page.addStyleTag({ url: stylesheet! });
  expect(await look(), 'and an unstyled form stays as it was with the default stylesheet on its page').toEqual(before);
  expect(before[0]).not.toBe('48px');
});

test("fields side by side line up their controls, and an option's description stays with its label", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SPECIMEN}?theme=default`);
  const top = (selector: string) =>
    pageForm(page)
      .locator(selector)
      .evaluate((element) => element.getBoundingClientRect().top);
  expect(await top('#jfb-1-email'), 'the email beside the name, which has help').toBe(await top('#jfb-1-displayName'));
  const dates = await Promise.all(['#jfb-1-visitDay', '#jfb-1-visitTime', '#jfb-1-returnAt'].map(top));
  expect(new Set(dates).size, 'the dates, under labels of one and two lines').toBe(1);

  const gaps = await pageForm(page)
    .locator('input[value="readings"]')
    .evaluate((input) => {
      const choice = input.parentElement!;
      const label = choice.querySelector('label')!.getBoundingClientRect();
      const help = choice.querySelector('.jfb-help')!.getBoundingClientRect();
      const next = choice.nextElementSibling!.getBoundingClientRect();
      return { own: help.top - label.bottom, next: next.top - help.bottom };
    });
  expect(gaps.own, "the description is nearer its option's label than the next option").toBeLessThan(gaps.next);
});

test('fields drawn as a fieldset share the rows of fields beside them, and each part keeps its own height', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SPECIMEN}?theme=default`);
  // The server's sink in the specimen's place; the specimen's columns hold no fieldset.
  await pageForm(page).evaluate((form, html) => (form.outerHTML = html), sinkMarkup('sink'));
  const parts = await sideBySide(pageForm(page));
  expect(parts.sliderHelp, "the slider's help beside the signature's").toBe(parts.signatureHelp);
  expect(parts.countryTop, "the country's help beside the file's").toBe(parts.fileTop);
  expect(parts.email, 'the email box as high as a box, beside a long text').toBeLessThan(parts.textarea / 2);
  expect(parts.country, 'the country list as high as a list, beside a taller drop frame').toBeLessThan(parts.file / 2);
});

test('a choice drawn as a button sinks when pressed, as a button does', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto(`${SPECIMEN}?theme=default`);
  const pressed = async (target: ReturnType<typeof pageForm>, property: 'translate' | 'transform') => {
    await target.scrollIntoViewIfNeeded();
    await target.hover();
    await page.mouse.down();
    const value = await target.evaluate((element, name) => getComputedStyle(element).getPropertyValue(name), property);
    await page.mouse.up();
    return value;
  };
  expect(await pressed(pageForm(page).locator('.jfb-choices-buttons label').first(), 'translate'), 'Yes').toBe(
    '0px 1px',
  );
  expect(
    await pressed(pageForm(page).locator('.jfb-choices-scale label').first(), 'translate'),
    'a step of the scale',
  ).toBe('0px 1px');
  expect(
    await pressed(pageForm(page).locator('.jfb-repeater .jfb-button').first(), 'transform'),
    'a button, Add a guest',
  ).toBe('matrix(1, 0, 0, 1, 0, 1)');
});
