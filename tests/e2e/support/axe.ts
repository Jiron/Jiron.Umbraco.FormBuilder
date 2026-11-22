import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import AxeBuilder from '@axe-core/playwright';
import { expect, type Browser, type Frame, type Page } from '@playwright/test';
import type AxeCore from 'axe-core';

// The rules every check runs: WCAG 2.2 A and AA.
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

// Axe on the website's pages with and without JavaScript (ROADMAP 6.5a and 6.5b): the journeys and the copy of the
// answers check every page they show, also where the page runs no script.

/**
 * Waits until nothing on the page is fading or moving: axe measures what a visitor reads, and a colour halfway through
 * a transition (a choice just picked, an error fading in) reads as too little contrast. An animation that never ends,
 * a spinner, is left to run. A spec that calls axe itself calls this first.
 */
export async function settled(page: Page): Promise<void> {
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every((animation) => animation.playState !== 'running' || animation.effect?.getTiming().iterations === Infinity),
  );
}

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
    const result = await new AxeBuilder({ page: checked }).withTags(TAGS).analyze();
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
 * Umbraco's own are its to answer for, as on the Overview (section.spec.ts). It leaves frames alone (`iframes: false`,
 * in axe's own run): an email's preview shows the mail in a sandboxed frame that runs no script, which axe would wait
 * for without end, and the mail's markup is the email renderer's, which its own tests hold (ADR-0189); the visitor
 * preview's frame has `axeInFrame`.
 */
export async function axeInside(page: Page, element: string): Promise<void> {
  await expect(page.locator(`${element} [aria-busy="true"]`)).toHaveCount(0);
  // Axe measures what stands: no hover under a pointer left where a button appeared, and no frame of a fade.
  await page.mouse.move(0, 0);
  await page.waitForFunction(() => document.getAnimations().every((animation) => animation.playState !== 'running'));
  const result = await new AxeBuilder({ page })
    .withTags(TAGS)
    .options({ ancestry: true, iframes: false })
    .setLegacyMode(true)
    .analyze();
  const ours = result.violations.flatMap((violation) =>
    violation.nodes
      .filter((node) => JSON.stringify(node.ancestry).includes(element))
      .map((node) => `${violation.id}: ${JSON.stringify(node.target)}`),
  );
  expect(ours, `axe inside <${element}>`).toEqual([]);
}

/**
 * Axe's WCAG 2.2 A and AA rules on the document a frame shows, run in that frame as it stands: the visitor preview's,
 * which runs scripts of its own in a sandbox without the backoffice's origin, and which `axeInside` leaves alone with
 * every frame (ADR-0203). Axe's own script is evaluated there, as Playwright evaluates in any frame.
 */
export async function axeInFrame(frame: Frame, what: string): Promise<void> {
  await frame.waitForFunction(() => document.getAnimations().every((animation) => animation.playState !== 'running'));
  await frame.evaluate(readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8'));
  const violations = await frame.evaluate(async (tags) => {
    const result = await (window as unknown as { axe: typeof AxeCore }).axe.run(document, {
      runOnly: { type: 'tag', values: tags },
    });
    return result.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    );
  }, TAGS);
  expect(violations, `${what}: axe`).toEqual([]);
}
