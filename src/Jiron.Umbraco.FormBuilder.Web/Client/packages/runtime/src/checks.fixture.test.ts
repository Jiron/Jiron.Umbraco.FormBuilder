// The runtime held to the server (ROADMAP 6.4d and 6.4e, ADR-0112, ADR-0113): each page the server rendered again
// after a post without JavaScript, in English and in German, in the default theme and in bootstrap5
// (tests/fixtures/rendering/errors-<language>.json, which the integration test Rendering/ErrorFixtureTests writes), read,
// decided and checked as the runtime does, gives the server's errors: the paragraph under each field and every link of
// the error summary, in the server's order. Taken away as the runtime takes errors away, the page has the marks of the
// page the server first rendered; shown again as the runtime shows them, it is the server's page.
import { afterEach, describe, expect, it } from 'vitest';
import german from '../../../../../../tests/fixtures/rendering/errors-de.json';
import english from '../../../../../../tests/fixtures/rendering/errors-en.json';
import { clearFieldError, removeSummary, showFieldError, showSummary } from './display';
import { wordsFor } from './messages';
import { parseModel } from './model';
import { checkPage, type CheckScope } from './validation';

interface ServerPage {
  readonly name: string;
  readonly scope: string;
  readonly now: string;
  readonly first: string;
  readonly html: string;
}

const LANGUAGES: readonly (readonly [string, readonly ServerPage[]])[] = [
  ['English', english.cases],
  ['German', german.cases],
];

const SCOPES: ReadonlySet<string> = new Set<CheckScope>(['send', 'page']);
const MARKS = ['aria-invalid', 'aria-describedby'] as const;

afterEach(() => {
  document.body.innerHTML = '';
});

/** A server page's form with its model data and the errors the runtime finds on it. */
async function load(page: ServerPage) {
  document.body.innerHTML = page.html;
  const form = document.querySelector('form');
  const model = parseModel(form?.querySelector('script[type="application/json"][data-jfb-model]')?.textContent ?? '');
  if (form === null || model === null || !SCOPES.has(page.scope)) throw new Error(`${page.name}: not a form page`);
  const errors = await checkPage(form, model, page.scope as CheckScope, () => Date.parse(page.now));
  return { form, model, errors };
}

/** Markup as a tree of elements with their attributes in name order and their texts, the blanks between left out. */
function treeOf(node: Node): unknown {
  if (node.nodeType === Node.TEXT_NODE) return (node.textContent ?? '').trim().length === 0 ? null : node.textContent;
  if (!(node instanceof Element)) return null;
  const children = [...(node instanceof HTMLTemplateElement ? node.content : node).childNodes]
    .map(treeOf)
    .filter((each) => each !== null);
  const attributes = [...node.attributes].map((each) => `${each.name}=${each.value}`).sort();
  return { tag: node.localName, attributes, children };
}

/** The marks of each element with an id: whether it is invalid, and what describes it. */
const marksOf = (root: ParentNode): Record<string, readonly (string | null)[]> =>
  Object.fromEntries(
    [...root.querySelectorAll('[id]')].map((element) => [element.id, MARKS.map((name) => element.getAttribute(name))]),
  );

describe.each(LANGUAGES)('the pages the server shows in %s', (_, pages) => {
  it.each(pages.map((page) => [page.name, page] as const))('%s: the runtime gives its errors', async (_, page) => {
    const { form, errors } = await load(page);
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

    expect({
      inline: errors
        .filter((error) => error.code !== 'empty')
        .map((error) => ({ id: `${error.target}-error`, text: error.message })),
      summary: errors.map((error) => ({ href: `#${error.target}`, text: error.link })),
    }).toEqual(server);
  });

  it.each(pages.map((page) => [page.name, page] as const))(
    '%s: the runtime takes the errors away and shows them again as the server does',
    async (_, page) => {
      const { form, model, errors } = await load(page);
      const served = treeOf(form);
      const fieldOf = (target: string): HTMLElement => {
        const field = document.getElementById(target)?.closest<HTMLElement>('[data-jfb-field]');
        if (field == null) throw new Error(`${page.name}: no field holds ${target}`);
        return field;
      };

      for (const paragraph of [...form.querySelectorAll('[data-jfb-error]')]) {
        const field = paragraph.closest<HTMLElement>('[data-jfb-field]');
        if (field !== null) clearFieldError(field);
      }
      removeSummary(form);
      expect(form.querySelectorAll('[data-jfb-error], [data-jfb-summary], [aria-invalid]')).toHaveLength(0);
      const first = new DOMParser().parseFromString(page.first, 'text/html');
      const before = marksOf(first);
      const cleared = marksOf(form);
      expect(
        Object.fromEntries(Object.keys(cleared).flatMap((id) => (id in before ? [[id, cleared[id]]] : []))),
      ).toEqual(Object.fromEntries(Object.keys(cleared).flatMap((id) => (id in before ? [[id, before[id]]] : []))));

      for (const error of errors) if (error.code !== 'empty') showFieldError(form, fieldOf(error.target), error);
      const listed =
        model.form.settings?.validationSummary === false ? errors.filter((error) => error.code === 'empty') : errors;
      showSummary(form, listed, await wordsFor(model.culture));
      expect(treeOf(form)).toEqual(served);
    },
  );
});
