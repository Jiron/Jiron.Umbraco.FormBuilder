import type { Locator, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { fillForm } from '../../support/form.ts';
import { pageOf, pathOf, formPages } from '../../support/demo-content.ts';

// The keyboard pass over the themes (ROADMAP 6.8e), on the demo's specimen in every theme and both schemes. Every kind
// of control shows that it has the focus, measured as pixels that change, also in forced colours, and axe stays clean.
// Tab reaches each field in the page's order and never the honeypot. The keyboard alone answers a rating, a scale, yes
// or no, cards, a box, the switch, the slider and a ranking, adds and removes a repeatable group's row, follows the
// summary's links, and Enter in a text box sends, with JavaScript and without. With reduced motion nothing slides or
// rises, and right to left the Back arrow, the switch and the stars mirror.

const SPECIMEN = pathOf(pageOf('demoSpecimen', 'en'), 'en');
const THEMES = ['default', 'unstyled', 'bootstrap5'] as const;
const SCHEMES = ['', 'dark'] as const;
const BOOTSTRAP = '/demo/vendor/bootstrap-5.3.8/bootstrap.min.css';
const RTL_BUILD = '/demo/vendor/bootstrap-5.3.8/bootstrap.rtl.min.css';

/** Every kind of control on the specimen, by the element that takes the focus. */
const KINDS = [
  ['a text box', '[name="displayName"]'],
  ['an email address', '[name="email"]'],
  ["an email's repeat", '[name="email.confirm"]'],
  ['a number', '[name="copies"]'],
  ['a phone number', '[name="phone"]'],
  ['a web address', '[name="website"]'],
  ['a text area', '[name="message"]'],
  ['a date', '[name="visitDay"]'],
  ['a time', '[name="visitTime"]'],
  ['a date and time', '[name="returnAt"]'],
  ['a dropdown', '[name="branch"]'],
  ['a list box', '[name="topics"]'],
  ['a radio button', '[name="format"]'],
  ['a choice card', '[name="room"]'],
  ['a box of a group', '[name="interests"]'],
  ['the "Other" text', '[name="interests.other"]'],
  ['one box', '[name="reminder"]'],
  ['yes or no as buttons', '[name="firstVisit"]'],
  ['the switch', '[name="newsletter"]'],
  ['a country', '[name="country"]'],
  ['a consent', '[name="privacy"]'],
  ['a rating', '[name="rating"]'],
  ['a scale', '[name="recommend"]'],
  ['the slider', '[name="hours"]'],
  ["a grid's cell", '[name^="opinions."]'],
  ["a ranking's move", '[data-jfb-alias="priorities"] button'],
  ["a name's part", '[name="contact.first"]'],
  ["an address's select", '[name="address.country"]'],
  ['a file', '[name="attachments"]'],
  ["a signature's typed name", '[name="signature.typed"]'],
  ["a repeatable group's Add", '[data-jfb-alias="guests"] button'],
  ['the main button', '.jfb-navigation .jfb-button-primary'],
] as const;

/** The fields that take the focus, in the page's order. */
const ORDER = [
  'displayName',
  'email',
  'cardNumber',
  'copies',
  'phone',
  'website',
  'message',
  'visitDay',
  'visitTime',
  'returnAt',
  'branch',
  'topics',
  'format',
  'room',
  'interests',
  'reminder',
  'firstVisit',
  'newsletter',
  'country',
  'privacy',
  'rating',
  'recommend',
  'hours',
  'opinions',
  'priorities',
  'contact',
  'address',
  'attachments',
  'signature',
  'guests',
];

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const urlOf = (theme: string, scheme = ''): string => `${SPECIMEN}?theme=${theme}&scheme=${scheme}`;
const field = (form: Locator, alias: string): Locator => form.locator(`[data-jfb-alias="${alias}"]`);

/** The pixels of a control's field or of the navigation, as they stand. */
const shot = (region: Locator): Promise<Buffer> => region.screenshot({ animations: 'disabled', caret: 'hide' });

/** Holds that a control shows the focus: its field's pixels change when it takes it from the keyboard. */
async function holdsFocusShown(page: Page, what: string, selector: string): Promise<void> {
  const control = formOn(page).locator(selector).first();
  const region = control.locator('xpath=ancestor-or-self::*[@data-jfb-field or contains(@class, "jfb-navigation")][1]');
  await control.evaluate((element: HTMLElement) => element.blur());
  await page.keyboard.press('Shift+Tab');
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await control.scrollIntoViewIfNeeded();
  const before = await shot(region);
  await control.focus();
  expect(await control.evaluate((element) => element.matches(':focus-visible')), `${what}: the focus shows`).toBe(true);
  const after = await shot(region);
  expect(before.equals(after), `${what}: its pixels change with the focus`).toBe(false);
}

/** The checked value of a group of radio buttons. */
const checkedOf = (form: Locator, name: string) =>
  form
    .locator(`input[name="${name}"]:checked`)
    .evaluateAll((inputs) => inputs.map((each) => (each as HTMLInputElement).value));

/** Records every slide and rise that runs from now on: what moves, in the page's own frames. */
const recordMoves = (page: Page) =>
  page.evaluate(() => {
    const moves = new Set<string>();
    (window as unknown as { jfbMoves: Set<string> }).jfbMoves = moves;
    const look = (): void => {
      for (const animation of document.getAnimations()) {
        const effect = animation.effect as KeyframeEffect | null;
        for (const frame of effect?.getKeyframes() ?? []) {
          const translate = String(frame['translate'] ?? '');
          const transform = String(frame['transform'] ?? '');
          if (
            /[1-9]/.test(translate) ||
            (transform !== '' && transform !== 'none' && !/^matrix\(1, 0, 0, 1, 0, 0\)$/.test(transform))
          )
            moves.add(
              `${effect?.target?.localName ?? ''}.${effect?.target?.className ?? ''}: ${translate}${transform}`,
            );
        }
      }
      requestAnimationFrame(look);
    };
    look();
  });

const movesOf = (page: Page): Promise<string[]> =>
  page.evaluate(() => [...(window as unknown as { jfbMoves: Set<string> }).jfbMoves]);

/** What a refused send, a press, a page and a new row would move: each done as the runtime or a visitor does it. */
async function moveEverything(page: Page): Promise<void> {
  const form = formOn(page);
  await recordMoves(page);
  await form.locator('[name="displayName"]').fill('Mira Okafor');
  await form.locator('.jfb-navigation .jfb-button-primary').click();
  await expect(form.locator('.jfb-error-summary')).toBeVisible();
  const label = field(form, 'recommend').locator('label').first();
  await label.scrollIntoViewIfNeeded();
  const box = await label.boundingBox();
  await page.mouse.move((box?.x ?? 0) + 4, (box?.y ?? 0) + 4);
  await page.mouse.down();
  await page.waitForTimeout(150);
  await page.mouse.move(0, 0);
  await page.mouse.up();
  await form.evaluate(async (element: HTMLFormElement) => {
    element.setAttribute('data-jfb-direction', 'next');
    const shown = element.querySelector<HTMLElement>(':scope > .jfb-page');
    if (shown === null) return;
    shown.setAttribute('hidden', '');
    void shown.offsetHeight;
    shown.removeAttribute('hidden');
    await new Promise((resolve) => setTimeout(resolve, 150));
  });
}

/** A right-to-left page: the root and the forms, with Bootstrap's own right-to-left build. */
async function rightToLeft(page: Page): Promise<void> {
  await page.route(`**${BOOTSTRAP}`, (route) =>
    route.fulfill({ status: 302, headers: { location: RTL_BUILD }, contentType: 'text/css' }),
  );
  await page.addInitScript(() => {
    document.addEventListener('DOMContentLoaded', () => {
      document.documentElement.dir = 'rtl';
      for (const form of document.querySelectorAll('form.jfb-form')) form.setAttribute('dir', 'rtl');
    });
  });
}

/** Answers the specimen's first page where it needs answers. */
async function answerFirstPage(form: Locator): Promise<void> {
  await fillForm(form, {
    displayName: 'Mira Okafor',
    email: 'mira.okafor@visitor.jfb.test',
    branch: 'Riverside',
    privacy: true,
  });
  await form.locator('[name="email.confirm"]').fill('mira.okafor@visitor.jfb.test');
  for (const [name, value] of [
    ['room', 'reading'],
    ['rating', '4'],
    ['recommend', '8'],
  ] as const) {
    const input = form.locator(`input[name="${name}"][value="${value}"]`);
    await form.locator(`label[for="${(await input.getAttribute('id')) ?? ''}"]`).click();
  }
  await form.locator('[name="contact.first"]').fill('Mira');
  await form.locator('[name="contact.last"]').fill('Okafor');
}

for (const theme of THEMES) {
  for (const scheme of SCHEMES) {
    test(`${theme}${scheme === '' ? '' : ` ${scheme}`}: every kind of control shows the focus, and axe stays clean`, async ({
      page,
    }) => {
      test.setTimeout(180_000);
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto(urlOf(theme, scheme));
      for (const [what, selector] of KINDS) await holdsFocusShown(page, what, selector);

      const result = await new AxeBuilder({ page })
        .include('main form.jfb-form')
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
        .analyze();
      expect(result.violations.map((violation) => violation.id)).toEqual([]);
    });
  }

  test(`${theme}: every kind of control shows the focus in forced colours`, async ({ page }) => {
    test.setTimeout(180_000);
    await page.emulateMedia({ forcedColors: 'active' });
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(urlOf(theme));
    for (const [what, selector] of KINDS) await holdsFocusShown(page, what, selector);
  });

  test(`${theme}: Tab reaches each field in the page's order, and never the honeypot`, async ({ page }) => {
    await page.goto(urlOf(theme));
    const form = formOn(page);
    const main = form.locator('.jfb-navigation .jfb-button-primary');
    await form.locator('[name="displayName"]').focus();
    const reached: string[] = ['displayName'];
    for (let press = 0; press < 150; press += 1) {
      await page.keyboard.press('Tab');
      const stop = await page.evaluate(() => {
        const active = document.activeElement as HTMLElement;
        return {
          alias: active.closest<HTMLElement>('[data-jfb-field]')?.dataset['jfbAlias'] ?? null,
          honeypot: active.closest('.jfb-hp') !== null,
          shown: active.checkVisibility(),
        };
      });
      expect(stop.honeypot, 'never the honeypot').toBe(false);
      expect(stop.shown, `${stop.alias ?? 'a stop'} is on the page`).toBe(true);
      if (stop.alias !== null && reached.at(-1) !== stop.alias) reached.push(stop.alias);
      if (await main.evaluate((element) => element === document.activeElement)) break;
    }
    expect(reached).toEqual(ORDER);
    await expect(main, 'the last stop is the main button').toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(main, 'and Shift+Tab goes back').not.toBeFocused();
  });

  test(`${theme}: the keyboard alone answers each choice, the switch and the slider`, async ({ page }) => {
    await page.goto(urlOf(theme));
    const form = formOn(page);
    for (const name of ['format', 'room', 'firstVisit', 'rating', 'recommend']) {
      const inputs = form.locator(`input[name="${name}"]`);
      const values = await inputs.evaluateAll((each) => each.map((input) => (input as HTMLInputElement).value));
      await inputs.first().focus();
      await page.keyboard.press('Space');
      expect(await checkedOf(form, name), `${name}: Space chooses the first`).toEqual([values[0]]);
      await page.keyboard.press('ArrowDown');
      expect(await checkedOf(form, name), `${name}: an arrow chooses the next`).toEqual([values[1]]);
    }

    for (const name of ['reminder', 'newsletter']) {
      const box = form.locator(`input[name="${name}"]`).first();
      const before = await box.isChecked();
      await box.focus();
      await page.keyboard.press('Space');
      expect(await box.isChecked(), `${name}: Space changes it`).toBe(!before);
    }

    const slider = form.locator('[name="hours"]');
    const start = Number(await slider.inputValue());
    await slider.focus();
    await page.keyboard.press('ArrowRight');
    expect(Number(await slider.inputValue()), 'an arrow moves the slider').toBeGreaterThan(start);

    await form.locator('[name="displayName"]').focus();
    await page.keyboard.type('Mira');
    await expect(form.locator('[name="displayName"]')).toHaveValue('Mira');
  });

  test(`${theme}: the keyboard alone moves a ranking's options and adds and removes a row, keeping the answers`, async ({
    page,
  }) => {
    await page.goto(urlOf(theme));
    const form = formOn(page);
    const places = form.locator('.jfb-ranking > li');
    const first = await places.nth(0).locator('span').nth(1).textContent();
    await form.locator('[name="displayName"]').fill('Mira Okafor');

    const moved = page.waitForEvent('load');
    await field(form, 'priorities').locator('button').nth(1).focus();
    await page.keyboard.press('Enter');
    await moved;
    await expect(places.nth(1).locator('span').nth(1), 'Enter on a move moves the option').toHaveText(first ?? '');
    await expect(form.locator('[name="displayName"]'), 'the answers stay').toHaveValue('Mira Okafor');

    const rows = field(form, 'guests').locator('.jfb-row');
    await expect(rows).toHaveCount(0);
    const added = page.waitForEvent('load');
    await field(form, 'guests').locator('button').first().focus();
    await page.keyboard.press('Enter');
    await added;
    await expect(rows, 'Enter on Add adds a row').toHaveCount(1);

    const name = rows.locator('input[type="text"]').first();
    await name.focus();
    await page.keyboard.type('Ada');
    await page.keyboard.press('Tab');
    await expect(rows.locator('input[type="radio"]').first(), "Tab goes on to the row's next field").toBeFocused();
    await holdsFocusShown(page, "a row's field", '.jfb-row input[type="text"]');

    const removed = page.waitForEvent('load');
    await rows.locator('button').first().focus();
    await page.keyboard.press('Enter');
    await removed;
    await expect(rows, 'Enter on Remove removes it').toHaveCount(0);
  });

  test(`${theme}: Enter in a text box asks for the next page, not a move, and the summary's links take the focus`, async ({
    page,
  }) => {
    await page.goto(urlOf(theme));
    const form = formOn(page);
    const order = () => form.locator('.jfb-ranking > li').allTextContents();
    const before = await order();
    await form.locator('[name="displayName"]').fill('Mira Okafor');
    await form.locator('[name="displayName"]').focus();
    await page.keyboard.press('Enter');
    const summary = form.locator('.jfb-error-summary');
    await expect(summary, 'the refusal takes the focus').toBeFocused();
    expect(await order(), 'and no move ran').toEqual(before);

    await page.keyboard.press('Tab');
    const link = summary.locator('a').first();
    await expect(link).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(form.locator('[name="email"]'), "the link's control takes the focus").toBeFocused();
  });

  for (const javaScriptEnabled of [true, false]) {
    test.describe(`${theme} ${javaScriptEnabled ? 'with' : 'without'} JavaScript`, () => {
      test.use({ javaScriptEnabled });

      test('Enter in a text box sends the form', async ({ page }) => {
        const contact = formPages().find((each) => each.form === 'contact');
        if (contact === undefined) throw new Error('The demo has no contact page.');
        await page.goto(`${pathOf(contact.texts.en, 'en')}?theme=${theme}&scheme=`);
        const form = formOn(page);
        await fillForm(form, { name: 'Mira Okafor', email: 'mira.okafor@visitor.jfb.test', message: 'A question.' });
        await form.locator('[name="name"]').focus();
        await page.keyboard.press('Enter');
        await expect(page.locator('main .jfb-outcome'), 'sent').toBeVisible();
      });
    });
  }

  test.describe(`${theme} with reduced motion`, () => {
    test.use({ contextOptions: { reducedMotion: 'reduce' } });

    test('nothing slides or rises', async ({ page }) => {
      await page.goto(urlOf(theme));
      await moveEverything(page);
      expect(await movesOf(page)).toEqual([]);
    });
  });

  test(`${theme} right to left: the stars ${theme === 'unstyled' ? '' : 'and the switch '}mirror`, async ({ page }) => {
    await rightToLeft(page);
    await page.goto(urlOf(theme));
    const form = formOn(page);
    await expect(form).toHaveAttribute('dir', 'rtl');

    const points = field(form, 'rating').locator('.jfb-choice');
    const lefts = await points.evaluateAll((each) => each.map((point) => point.getBoundingClientRect().left));
    expect(lefts, 'the first star stands on the right').toEqual([...lefts].sort((a, b) => b - a));
    expect(new Set(lefts).size, 'each in its own place').toBe(lefts.length);
    if (theme === 'unstyled') return;

    await field(form, 'rating').locator('label').nth(2).click();
    await page.mouse.move(0, 0);
    const marks = await field(form, 'rating')
      .locator('label')
      .evaluateAll((labels) => labels.map((label) => getComputedStyle(label, '::before').maskImage));
    expect(marks[0], 'the stars fill from the first').toBe(marks[2]);
    expect(marks[3], 'up to the chosen one').not.toBe(marks[2]);

    const toggle = form.locator('[name="newsletter"]').first();
    await toggle.check();
    await expect
      .poll(
        () =>
          toggle.evaluate((input) =>
            input.classList.contains('jfb-switch') && getComputedStyle(input, '::before').content !== 'none'
              ? getComputedStyle(input, '::before').translate
              : getComputedStyle(input).backgroundPosition,
          ),
        'the knob of a switch that is on stands on the left',
      )
      .toMatch(theme === 'default' ? /^-20px/ : /^0%/);
  });
}

test.describe('the default theme, moving', () => {
  test('a slide and a rise do run without reduced motion, which is what the check above would see', async ({
    page,
  }) => {
    await page.goto(urlOf('default'));
    await moveEverything(page);
    expect((await movesOf(page)).length).toBeGreaterThan(0);
  });

  test("right to left, the Back button's arrow points the other way", async ({ page }) => {
    await rightToLeft(page);
    await page.goto(urlOf('default'));
    const form = formOn(page);
    await answerFirstPage(form);
    const loaded = page.waitForEvent('load');
    await form.locator('.jfb-navigation .jfb-button-primary').click();
    await loaded;
    const back = form.locator('.jfb-icon-back');
    await expect(back).toBeVisible();
    expect(await back.evaluate((icon) => getComputedStyle(icon).transform)).toBe('matrix(-1, 0, 0, 1, 0, 0)');
  });
});
