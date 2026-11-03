import AxeBuilder from '@axe-core/playwright';
import { expect, type Browser, type Page } from '@playwright/test';

// Axe on the website's pages with and without JavaScript (ROADMAP 6.5a and 6.5b): the journeys and the copy of the
// answers check every page they show, also where the page runs no script.

/**
 * Axe's WCAG 2.2 A and AA rules on the whole page, once the theme's fades have ended. Axe runs as a script of the page,
 * which a page without JavaScript runs none of: its markup is then checked in a twin with the same address, the same
 * size and the same stylesheets, whose own scripts are blocked, so neither the runtime nor the site's scripts run.
 */
export async function axe(page: Page, what: string, javaScript: boolean): Promise<void> {
  const browser = page.context().browser();
  if (browser === null) throw new Error('The page has no browser.');
  const checked = javaScript ? page : await twinOf(page, browser);
  try {
    await checked.waitForFunction(() =>
      document.getAnimations().every((animation) => animation.playState !== 'running'),
    );
    const result = await new AxeBuilder({ page: checked })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(
      result.violations.map(
        (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
      ),
      `${what}: axe`,
    ).toEqual([]);
  } finally {
    if (checked !== page) await checked.context().close();
  }
}

/** A page with the markup the given page holds now, at its address, with every script of its own blocked. */
async function twinOf(page: Page, browser: Browser): Promise<Page> {
  const markup = await page.content();
  const address = page.url();
  const context = await browser.newContext({ viewport: page.viewportSize(), locale: 'en-GB', javaScriptEnabled: true });
  const twin = await context.newPage();
  await twin.route('**/*', (route) => {
    const request = route.request();
    if (request.isNavigationRequest()) return route.fulfill({ body: markup, contentType: 'text/html; charset=utf-8' });
    return request.resourceType() === 'script' ? route.abort() : route.continue();
  });
  await twin.goto(address);
  await expect(twin.locator('main .jfb-form[data-jfb-form]'), 'the twin holds the page as it is').toHaveCount(1);
  return twin;
}

/**
 * Axe's WCAG 2.2 A and AA rules on a backoffice page, counting what lies inside one of Jiron Form Builder's elements:
 * Umbraco's own are its to answer for, as on the Overview (section.spec.ts).
 */
export async function axeInside(page: Page, element: string): Promise<void> {
  await expect(page.locator(`${element} [aria-busy="true"]`)).toHaveCount(0);
  // Axe measures what stands: no hover under a pointer left where a button appeared, and no frame of a fade.
  await page.mouse.move(0, 0);
  await page.waitForFunction(() => document.getAnimations().every((animation) => animation.playState !== 'running'));
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .options({ ancestry: true })
    .analyze();
  const ours = result.violations.flatMap((violation) =>
    violation.nodes
      .filter((node) => JSON.stringify(node.ancestry).includes(element))
      .map((node) => `${violation.id}: ${JSON.stringify(node.target)}`),
  );
  expect(ours, `axe inside <${element}>`).toEqual([]);
}
