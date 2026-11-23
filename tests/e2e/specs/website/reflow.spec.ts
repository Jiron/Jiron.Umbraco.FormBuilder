import type { Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { LANGUAGES, formPages, pathOf } from '../../support/demo-content.ts';
import { fontsOf } from '../../support/fonts.ts';

// Reflow and text spacing on the website (ROADMAP 16.3; WCAG 2.2 1.4.4, 1.4.10 and 1.4.12): the demo's specimen, which
// holds every kind of control as it reads without JavaScript's help, its contact form, and its room hire, whose upload
// the runtime takes over, in every built-in theme, at 640 CSS pixels (a 1280-pixel window at 200%) and at 320 (at 400%),
// in English and German. The page never scrolls sideways and nothing of a form reaches past
// the window; and with WCAG's text spacing applied (lines 1.5 high, paragraphs 2 em apart, letters 0.12 em and words
// 0.16 em) the page still keeps to the window and no text of a form is cut off.

const THEMES = ['default', 'bootstrap5', 'unstyled'] as const;
const WIDTHS = [640, 320] as const;

/** WCAG 1.4.12's spacing, as a reader's own style sheet sets it. */
const TEXT_SPACING =
  '* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } ' +
  'p { margin-bottom: 2em !important; }';

/** The pages of a language, and whether the runtime takes over an upload on it, which the measures wait for. */
const pagesOf = (language: (typeof LANGUAGES)[number]): { path: string; upload: boolean }[] =>
  formPages()
    .filter((shown) => shown.form === 'specimen' || shown.form === 'contact' || shown.form === 'roomHire')
    .map((shown) => ({ path: pathOf(shown.texts[language], language), upload: shown.form === 'roomHire' }));

/**
 * What reaches past the window's right edge, of the forms or of the whole page, by tag, id and class. What a box clips to
 * one pixel out of view on purpose (the honeypot, a visually hidden text) shows nothing, wherever its contents lie: in a
 * wide font the honeypot's label pushed its input past 320 pixels inside its clipped box (ROADMAP 26.1).
 */
async function reaching(page: Page, width: number, within = 'form.jfb-form'): Promise<string[]> {
  return page.evaluate(
    ([limit, root]) =>
      [...document.querySelectorAll(`${root}, ${root} *`)]
        .filter((element) => {
          const box = element.getBoundingClientRect();
          return box.width > 1 && box.right > limit + 0.5 && element.closest('.jfb-hp, .jfb-visually-hidden') === null;
        })
        .map(
          (element) => `${element.localName}${element.id ? '#' + element.id : ''}.${[...element.classList].join('.')}`,
        )
        .slice(0, 12),
    [width, within] as const,
  );
}

/**
 * The boxes of a form that clip their text: they hide what overflows them and hold more than they show. A box that is
 * out of view on purpose (the honeypot, a visually hidden label, one pixel or less) is none.
 */
async function cutOff(page: Page): Promise<string[]> {
  return page.evaluate(() =>
    [...document.querySelectorAll('form.jfb-form *')]
      .filter((element) => {
        if (!(element instanceof HTMLElement) || element.closest('.jfb-hp')) return false;
        const style = getComputedStyle(element);
        const box = element.getBoundingClientRect();
        if (box.width <= 1 || box.height <= 1 || style.clipPath !== 'none' || style.display === 'none') return false;
        const hides = (value: string) => value === 'hidden' || value === 'clip';
        return (
          (hides(style.overflowX) && element.scrollWidth > element.clientWidth + 1) ||
          (hides(style.overflowY) && element.scrollHeight > element.clientHeight + 1)
        );
      })
      .filter(
        (element) => (element.textContent ?? '').trim().length > 0 || element.querySelector('input, select, textarea'),
      )
      .map((element) => `${element.localName}${element.id ? '#' + element.id : ''}.${[...element.classList].join('.')}`)
      .slice(0, 12),
  );
}

async function keepsToTheWindow(page: Page, width: number, what: string): Promise<void> {
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
    `${what}: the page scrolls sideways: ${(await reaching(page, width, 'body')).join(', ')}`,
  ).toBeLessThanOrEqual(width);
  const reached = await reaching(page, width);
  // Where text wraps depends on the machine's fonts: a failure names them.
  const fonts = reached.length > 0 ? await fontsOf(page, reached[0]!.replace(/\..*$/, '')) : '';
  expect(reached, `${what}: a form reaches past the window; ${fonts}`).toEqual([]);
}

for (const width of WIDTHS) {
  test.describe(`${width} px`, () => {
    test.use({ viewport: { width, height: 800 } });
    for (const theme of THEMES) {
      test(`${theme}: the specimen, the contact form and the room hire reflow at ${width} px and keep their text with WCAG's text spacing`, async ({
        page,
      }) => {
        for (const language of LANGUAGES) {
          for (const { path, upload } of pagesOf(language)) {
            const what = `${path} in ${theme} at ${width} px`;
            const response = await page.goto(`${path}?theme=${theme}`);
            expect(response?.status(), what).toBe(200);
            await expect(page.locator('main form.jfb-form'), what).toHaveCount(1);
            if (upload) {
              // The form's logic wakes as the visitor first reaches it, and only then takes over the upload.
              await page.locator('main form.jfb-form').locator('input, select, textarea').first().focus();
              await expect(page.locator('main .jfb-upload').first(), what).toHaveAttribute('data-jfb-enhanced', '');
              await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
            }
            await keepsToTheWindow(page, width, what);
            await page.addStyleTag({ content: TEXT_SPACING });
            await keepsToTheWindow(page, width, `${what} with text spacing`);
            expect(await cutOff(page), `${what} with text spacing: text is cut off`).toEqual([]);
          }
        }
      });
    }
  });
}
