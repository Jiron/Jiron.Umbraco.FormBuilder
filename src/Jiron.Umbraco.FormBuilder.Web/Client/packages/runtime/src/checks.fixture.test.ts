// The runtime held to the server (ROADMAP 6.4d, ADR-0112): each page the server rendered again after a post without
// JavaScript, in English and in German (tests/fixtures/rendering/errors-<language>.json, which the integration test
// Rendering/ErrorFixtureTests writes), read, decided and checked as the runtime does, gives the server's errors: the
// paragraph under each field and every link of the error summary, in the server's order.
import { afterEach, describe, expect, it } from 'vitest';
import german from '../../../../../../tests/fixtures/rendering/errors-de.json';
import english from '../../../../../../tests/fixtures/rendering/errors-en.json';
import { parseModel } from './model';
import { checkPage, type CheckScope } from './validation';

interface ServerPage {
  readonly name: string;
  readonly scope: string;
  readonly now: string;
  readonly html: string;
}

const LANGUAGES: readonly (readonly [string, readonly ServerPage[]])[] = [
  ['English', english.cases],
  ['German', german.cases],
];

const SCOPES: ReadonlySet<string> = new Set<CheckScope>(['send', 'page']);

afterEach(() => {
  document.body.innerHTML = '';
});

describe.each(LANGUAGES)('the pages the server shows in %s', (_, pages) => {
  it.each(pages.map((page) => [page.name, page] as const))('%s: the runtime gives its errors', async (_, page) => {
    document.body.innerHTML = page.html;
    const form = document.querySelector('form');
    const model = parseModel(form?.querySelector('script[type="application/json"][data-jfb-model]')?.textContent ?? '');
    if (form === null || model === null || !SCOPES.has(page.scope)) throw new Error(`${page.name}: not a form page`);
    const server = {
      inline: [...form.querySelectorAll('[data-jfb-error]')].map((paragraph) => ({
        id: paragraph.id,
        text: paragraph.textContent.trim(),
      })),
      summary: [...form.querySelectorAll('[data-jfb-summary] a')].map((link) => ({
        href: link.getAttribute('href'),
        text: link.textContent.trim(),
      })),
    };
    expect(server.summary.length, 'the server refused the post').toBeGreaterThan(0);

    const errors = await checkPage(form, model, page.scope as CheckScope, () => Date.parse(page.now));

    expect({
      inline: errors
        .filter((error) => error.code !== 'empty')
        .map((error) => ({ id: `${error.target}-error`, text: error.message })),
      summary: errors.map((error) => ({ href: `#${error.target}`, text: error.link })),
    }).toEqual(server);
  });
});
