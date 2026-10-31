import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { pageOf, pathOf } from '../../support/demo-content.ts';

// What the themes draw for the keyboard and for a refused answer (ROADMAP 6.8d, ADR-0106, ADR-0108), on the demo's
// specimen in the default and bootstrap5 themes, light and dark. A control the theme draws (a rating, a scale, yes or no
// as buttons, a choice card, a ranking's move) shows focus as a solid outline of 3:1 or more against what it stands on,
// which forced colours keep, as they do on Bootstrap's own controls. An error summary's link lands with the field's
// label below a site's sticky header, in every theme, with JavaScript and without, on a desktop and a phone, and a
// summary that takes the focus lands below it too. A refused scale and rating draw the error's colour, and lose it
// once answered.

const SPECIMEN = pathOf(pageOf('demoSpecimen', 'en'), 'en');
const THEMES = ['default', 'bootstrap5'] as const;
const SCHEMES = ['', 'dark'] as const;

/** The error's colour of each theme in each scheme: the default theme's token, Bootstrap's invalid colour. */
const ERROR: Readonly<Record<(typeof THEMES)[number], Readonly<Record<(typeof SCHEMES)[number], string>>>> = {
  default: { '': 'rgb(179, 38, 30)', dark: 'rgb(242, 165, 158)' },
  bootstrap5: { '': 'rgb(220, 53, 69)', dark: 'rgb(234, 134, 143)' },
};

/** Each control a theme draws itself, by the input or button that takes the focus. */
const DRAWN = [
  ['a rating', '[data-jfb-alias="rating"] input'],
  ['a scale', '[data-jfb-alias="recommend"] input'],
  ['yes or no as buttons', '[data-jfb-alias="firstVisit"] input'],
  ['a choice card', '[data-jfb-alias="room"] input'],
  ["a ranking's move", '[data-jfb-alias="priorities"] button'],
] as const;

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const send = (page: Page): Locator => formOn(page).locator('.jfb-navigation .jfb-button-primary');
const urlOf = (theme: string, scheme: string): string => `${SPECIMEN}?theme=${theme}&scheme=${scheme}`;

const channels = (colour: string): number[] => (colour.match(/[\d.]+/g) ?? []).map(Number);

/** The relative luminance of an `rgb()` colour, as WCAG defines it. */
function luminance(colour: string): number {
  const [red = 0, green = 0, blue = 0] = channels(colour).map((channel) => {
    const part = channel / 255;
    return part <= 0.04045 ? part / 12.92 : ((part + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

/** The contrast of two opaque `rgb()` colours. */
function contrast(one: string, other: string): number {
  const [lighter, darker] = [luminance(one), luminance(other)].sort((a, b) => b - a) as [number, number];
  return (lighter + 0.05) / (darker + 0.05);
}

/** Bootstrap's own controls, whose focus the theme also outlines (6.8h). */
const BOOTSTRAPS_OWN = [
  ['a text box', '[name="displayName"]'],
  ['a dropdown', '[name="branch"]'],
  ['a list box', '[name="topics"]'],
  ['a radio button', '[name="format"]'],
  ['a box', '[name="reminder"]'],
  ['the switch', '[name="newsletter"]'],
  ['the slider', '[name="hours"]'],
  ['the main button', '.jfb-navigation .jfb-button-primary'],
] as const;

/**
 * What the focus draws for a control, read while it has the focus: itself when the control is its own, or the card or
 * label a drawn radio button shows it on.
 */
const ringOf = (control: Locator, own = false) =>
  control.evaluate((element, isOwn) => {
    const drawn =
      (isOwn ? element : null) ??
      element.closest('.jfb-choices-cards > .jfb-choice') ??
      (element.matches('[type="radio"]') ? element.ownerDocument.querySelector(`label[for="${element.id}"]`) : null) ??
      element;
    let ground: Element | null = drawn.parentElement;
    let colour = 'rgba(0, 0, 0, 0)';
    while (ground !== null) {
      colour = getComputedStyle(ground).backgroundColor;
      if (colour !== 'rgba(0, 0, 0, 0)' && colour !== 'transparent') break;
      ground = ground.parentElement;
    }
    const style = getComputedStyle(drawn);
    return {
      focused: element.matches(':focus-visible'),
      style: style.outlineStyle,
      width: Number.parseFloat(style.outlineWidth),
      colour: style.outlineColor,
      ground: ground === null ? 'rgb(255, 255, 255)' : colour,
    };
  }, own);

/** Moves the focus to a control the way the keyboard leaves it, so it shows as focus. */
async function focusOn(page: Page, control: Locator): Promise<void> {
  await page.keyboard.press('Tab');
  await control.focus();
}

for (const theme of THEMES) {
  for (const scheme of SCHEMES) {
    test(`${theme}${scheme === '' ? '' : ` ${scheme}`}: each control the theme draws shows focus at 3:1 or more`, async ({
      page,
    }) => {
      await page.goto(urlOf(theme, scheme));
      const own = theme === 'bootstrap5' ? BOOTSTRAPS_OWN : [];
      for (const [what, selector] of [...DRAWN, ...own]) {
        const control = formOn(page).locator(selector).first();
        await focusOn(page, control);
        const ring = await ringOf(
          control,
          own.some(([, ownSelector]) => ownSelector === selector),
        );
        expect(ring.focused, `${what}: the focus shows`).toBe(true);
        expect([ring.style, ring.width], `${what}: a solid outline, not a shadow`).toEqual(['solid', 2]);
        expect(channels(ring.colour).length, `${what}: an opaque colour, ${ring.colour}`).toBe(3);
        expect(contrast(ring.colour, ring.ground), `${what}: ${ring.colour} on ${ring.ground}`).toBeGreaterThanOrEqual(
          3,
        );
      }
    });

    test(`${theme}${scheme === '' ? '' : ` ${scheme}`}: a refused scale and rating draw the error's colour until answered`, async ({
      page,
    }) => {
      const form = formOn(page);
      const point = form.locator('[data-jfb-alias="recommend"] .jfb-choice > label').first();
      const star = form.locator('[data-jfb-alias="rating"] .jfb-choice > label').first();
      const border = () => point.evaluate((label) => getComputedStyle(label).borderInlineStartColor);
      const fill = () => star.evaluate((label) => getComputedStyle(label, '::before').backgroundColor);

      await page.goto(urlOf(theme, scheme));
      expect(await border(), 'the scale before a send').not.toBe(ERROR[theme][scheme]);
      expect(await fill(), 'the rating before a send').not.toBe(ERROR[theme][scheme]);
      await form.locator('[name="displayName"]').fill('Mira Okafor');
      await send(page).click();
      await expect(form.locator('.jfb-error-summary')).toBeVisible();

      await expect.poll(border, 'the scale, refused').toBe(ERROR[theme][scheme]);
      await expect.poll(fill, 'the rating, refused').toBe(ERROR[theme][scheme]);

      await form.locator('[data-jfb-alias="recommend"] label').nth(3).click();
      await star.click();
      await page.mouse.move(0, 0);
      await expect.poll(border, 'the scale, answered').not.toBe(ERROR[theme][scheme]);
      await expect.poll(fill, 'the rating, answered').not.toBe(ERROR[theme][scheme]);
    });
  }

  test(`${theme}: forced colours keep the focus on every kind of control`, async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(urlOf(theme, ''));
    const controls = [
      ...DRAWN,
      ['a text box', '[name="displayName"]'],
      ['a select', '[name="branch"]'],
      ['a button', '.jfb-navigation .jfb-button-primary'],
    ] as const;
    for (const [what, selector] of controls) {
      const control = formOn(page).locator(selector).first();
      await focusOn(page, control);
      const ring = await ringOf(control);
      expect(ring.focused, `${what}: the focus shows`).toBe(true);
      expect(ring.style, `${what}: an outline, as forced colours drop a shadow`).toBe('solid');
      expect(ring.width, `${what}: at least 2 px`).toBeGreaterThanOrEqual(2);
    }
  });

  test(`${theme}: a site's taller header sets its own margin, and a summary that takes the focus lands below it`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(urlOf(theme, ''));
    await page.addStyleTag({
      content: `:root { --jfb-scroll-margin: 10rem; } body::before { content: ""; position: fixed; inset: 0 0 auto; z-index: 10; block-size: 9rem; background: #777; }`,
    });
    const form = formOn(page);
    await form.locator('[name="displayName"]').fill('Mira Okafor');
    await send(page).click();
    const summary = form.locator('.jfb-error-summary');
    await expect(summary).toBeFocused();
    await expect
      .poll(() => summary.evaluate((element) => element.getBoundingClientRect().top), 'the summary, below the header')
      .toBeGreaterThanOrEqual(144);

    const label = form.locator('[data-jfb-alias="recommend"] > fieldset > legend');
    await form.locator('.jfb-error-summary a[href="#jfb-1-recommend"]').focus();
    await page.keyboard.press('Enter');
    await expect(form.locator('#jfb-1-recommend')).toBeFocused();
    await expect
      .poll(() => label.evaluate((element) => element.getBoundingClientRect().top), 'the label, below the header')
      .toBeGreaterThanOrEqual(144);
  });
}

for (const theme of THEMES) {
  test.describe(`${theme} without JavaScript`, () => {
    test.use({ javaScriptEnabled: false, contextOptions: { reducedMotion: 'reduce' } });

    test("a card's radio button shows no ring of its own inside the card's", async ({ page }) => {
      await page.goto(urlOf(theme, ''));
      const radio = formOn(page).locator('[data-jfb-alias="room"] input').first();
      await focusOn(page, radio);
      expect(await radio.evaluate((element) => getComputedStyle(element).outlineStyle)).not.toBe('solid');
    });

    test("a refused rating's stars leave the error's colour once one is chosen", async ({ page }) => {
      await page.goto(urlOf(theme, ''));
      const form = formOn(page);
      await form.locator('[name="displayName"]').fill('Mira Okafor');
      await send(page).click();
      await expect(form.locator('.jfb-error-summary')).toBeVisible();
      // The last star: the chosen ones and those before them take the chosen colour whatever the error says.
      const star = form.locator('[data-jfb-alias="rating"] .jfb-choice > label').last();
      const fill = () => star.evaluate((label) => getComputedStyle(label, '::before').backgroundColor);
      await expect.poll(fill, 'refused').toBe(ERROR[theme]['']);

      await form.locator('[data-jfb-alias="rating"] label').nth(2).click();
      await page.mouse.move(0, 0);
      await expect.poll(fill, 'one chosen, with the error still on the page').not.toBe(ERROR[theme]['']);
    });
  });
}

for (const theme of [...THEMES, 'unstyled'] as const) {
  test.describe(`${theme} without JavaScript, landing`, () => {
    test.use({ javaScriptEnabled: false, contextOptions: { reducedMotion: 'reduce' } });

    test("a post's landing keeps the form's top clear of a sticky header", async ({ page }) => {
      await page.setViewportSize({ width: 1280, height: 500 });
      await page.goto(urlOf(theme, ''));
      const form = formOn(page);
      await form.locator('[name="displayName"]').fill('Mira Okafor');
      await send(page).click();
      await expect(form.locator('.jfb-error-summary')).toBeVisible();
      expect(new URL(page.url()).hash, 'the post lands on the form').toBe('#jfb-1');
      await expect
        .poll(() => form.evaluate((element) => element.getBoundingClientRect().top), "the form's top, below 5rem")
        .toBeGreaterThanOrEqual(80);
    });
  });

  for (const [width, height] of [
    [1920, 768],
    [1280, 500],
    [667, 375],
  ] as const) {
    for (const javaScriptEnabled of [true, false]) {
      test.describe(`${theme} at ${width} by ${height} ${javaScriptEnabled ? 'with' : 'without'} JavaScript`, () => {
        test.use({ javaScriptEnabled, viewport: { width, height }, contextOptions: { reducedMotion: 'reduce' } });

        test("a link to a picture's choice leaves the control that takes the focus in the window", async ({ page }) => {
          await page.goto(urlOf(theme, ''));
          const form = formOn(page);
          await form.locator('[name="displayName"]').fill('Mira Okafor');
          await send(page).click();
          await expect(form.locator('.jfb-error-summary')).toBeVisible();
          await form.locator('.jfb-error-summary a[href="#jfb-1-room"]').focus();
          await page.keyboard.press('Enter');
          const control = form.locator('#jfb-1-room');
          await expect
            .poll(async () => {
              const box = await control.evaluate((element) => element.getBoundingClientRect().toJSON() as DOMRect);
              return box.top >= 0 && box.bottom <= height;
            }, 'in the window')
            .toBe(true);
        });
      });
    }
  }

  for (const [width, height] of [
    [1280, 800],
    [375, 667],
  ] as const) {
    for (const javaScriptEnabled of [true, false]) {
      test.describe(`${theme} at ${width} px ${javaScriptEnabled ? 'with' : 'without'} JavaScript`, () => {
        // Without Bootstrap's smooth scrolling, since the landing counts here and not the way there.
        test.use({ javaScriptEnabled, viewport: { width, height }, contextOptions: { reducedMotion: 'reduce' } });

        // A header of 5rem is what the default margin leaves room for; the landing is measured, so none need be drawn.
        test("a summary's link lands with the label clear of a sticky header", async ({ page }) => {
          await page.goto(urlOf(theme, ''));
          const form = formOn(page);
          await form.locator('[name="displayName"]').fill('Mira Okafor');
          await send(page).click();
          await expect(form.locator('.jfb-error-summary')).toBeVisible();

          for (const alias of ['room', 'rating', 'recommend', 'opinions', 'contact']) {
            const label = form.locator(
              `[data-jfb-alias="${alias}"] > label, [data-jfb-alias="${alias}"] > fieldset > legend`,
            );
            await form.locator(`.jfb-error-summary a[href="#jfb-1-${alias}"]`).focus();
            await page.keyboard.press('Enter');
            await expect
              .poll(async () => {
                const box = await label.evaluate((element) => element.getBoundingClientRect().toJSON() as DOMRect);
                return box.top >= 80 && box.bottom <= height;
              }, `${alias}: its label is in view, below the header`)
              .toBe(true);
          }
        });
      });
    }
  }

  test.describe(`${theme} with JavaScript, a long legend`, () => {
    test.use({ viewport: { width: 1280, height: 800 }, contextOptions: { reducedMotion: 'reduce' } });

    // A control's own margin clears a short legend, so only one taller than that margin tells the two landings apart.
    test("a summary's link lands at a long legend's start, not at its control", async ({ page }) => {
      await page.goto(urlOf(theme, ''));
      const form = formOn(page);
      await form.locator('[name="displayName"]').fill('Mira Okafor');
      await send(page).click();
      await expect(form.locator('.jfb-error-summary')).toBeVisible();
      const legend = form.locator('[data-jfb-alias="recommend"] > fieldset > legend');
      await legend.evaluate((element: HTMLElement) => {
        element.style.display = 'block';
        element.style.minHeight = '16rem';
      });

      await form.locator('.jfb-error-summary a[href="#jfb-1-recommend"]').focus();
      await page.keyboard.press('Enter');

      await expect
        .poll(() => legend.evaluate((element) => element.getBoundingClientRect().top), "the legend's start, below 5rem")
        .toBeGreaterThanOrEqual(80);
      await expect(form.locator('[data-jfb-alias="recommend"] input').first()).toBeFocused();
    });
  });
}
