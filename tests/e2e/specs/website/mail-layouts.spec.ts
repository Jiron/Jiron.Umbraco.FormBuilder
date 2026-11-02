import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../../fixtures.ts';
import { bodyOf, headerOf } from '../../support/mail.ts';

// Every mail layout as a phone shows it, rendered from what arrived in the pickup folder (ROADMAP 8.2, ADR-0159): a long
// unbroken word stays inside 375 px wherever text can go, and the dark scheme moves the colours.

const word = 'x'.repeat(300);

test.describe('mail layouts', () => {
  test('a long word stays inside a phone, in every layout and either scheme', async ({
    page,
    request,
    waitForMail,
  }) => {
    const marker = `layouts-${Date.now()}`;
    const answer = await request.post('/jfb-testing/mail-layouts', { data: { marker, word } });
    expect(answer.ok(), await answer.text()).toBe(true);
    const { subjects } = (await answer.json()) as { subjects: string[] };
    expect(subjects).toHaveLength(4);

    await page.setViewportSize({ width: 375, height: 812 });
    for (const subject of subjects) {
      const mail = await waitForMail((each) => headerOf(each.raw, 'Subject') === subject, 60_000);
      const html = bodyOf(mail.raw, 'text/html');
      await page.setContent(html);
      for (const colorScheme of ['light', 'dark'] as const) {
        await page.emulateMedia({ colorScheme });
        const outside = await page.evaluate(() => {
          const width = document.documentElement.clientWidth;
          return {
            width,
            scrolled: document.documentElement.scrollWidth,
            sticking: [...document.body.querySelectorAll('*')]
              .filter((element) => element.getBoundingClientRect().right > width + 0.5)
              .map((element) => element.tagName),
            words: [...document.body.querySelectorAll('p, a, li, h2, th, td, pre, code, blockquote')].length,
          };
        });
        expect(outside.width, subject).toBe(375);
        expect(outside.scrolled, `${subject} scrolls sideways in ${colorScheme}`).toBeLessThanOrEqual(375);
        expect(outside.sticking, `${subject} in ${colorScheme}`).toEqual([]);
        expect(outside.words, `${subject} holds the word in every kind of element`).toBeGreaterThanOrEqual(8);

        const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
        expect(
          axe.violations.map((violation) => violation.id),
          `${subject} in ${colorScheme}`,
        ).toEqual([]);
      }
    }
  });

  test('the dark scheme moves the colours, and the light one keeps the inline ones', async ({
    page,
    request,
    waitForMail,
  }) => {
    const marker = `schemes-${Date.now()}`;
    const answer = await request.post('/jfb-testing/mail-layouts', { data: { marker, word: 'Words' } });
    expect(answer.ok(), await answer.text()).toBe(true);
    const mail = await waitForMail((each) => headerOf(each.raw, 'Subject') === `${marker} card`, 60_000);
    await page.setContent(bodyOf(mail.raw, 'text/html'));

    const colours = () =>
      page.evaluate(() => ({
        page: getComputedStyle(document.querySelector('.jfb-page')!).backgroundColor,
        surface: getComputedStyle(document.querySelector('.jfb-surface')!).backgroundColor,
        text: getComputedStyle(document.querySelector('p.jfb-text')!).color,
        link: getComputedStyle(document.querySelector('a.jfb-link')!).color,
      }));

    await page.emulateMedia({ colorScheme: 'light' });
    expect(await colours()).toEqual({
      page: 'rgb(242, 243, 245)',
      surface: 'rgb(255, 255, 255)',
      text: 'rgb(28, 31, 36)',
      link: 'rgb(31, 79, 191)',
    });

    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await colours()).toEqual({
      page: 'rgb(18, 20, 23)',
      surface: 'rgb(28, 31, 36)',
      text: 'rgb(232, 234, 237)',
      link: 'rgb(157, 188, 255)',
    });
  });
});
