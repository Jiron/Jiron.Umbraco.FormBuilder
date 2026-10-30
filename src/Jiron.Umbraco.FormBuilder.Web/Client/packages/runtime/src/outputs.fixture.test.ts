// The runtime's results held to the server (ROADMAP 6.4f, ADR-0114): each page the server rendered first and again
// after a post without JavaScript, in English and in German (tests/fixtures/rendering/calculations-<language>.json,
// which the integration test Rendering/CalculationFixtureTests writes), with every calculation's result and every
// slider's text blanked, shows them again as the runtime does over the page's reading: the server's page, element for
// element, its calculations in their formats, a row's in its row, none for an unreadable answer, an empty date or a
// failed division, and the sliders' values beside their thumbs and in their `aria-valuetext`.
import { FORMULAS } from '@jfb/logic/calc';
import { afterEach, describe, expect, it } from 'vitest';
import german from '../../../../../../tests/fixtures/rendering/calculations-de.json';
import english from '../../../../../../tests/fixtures/rendering/calculations-en.json';
import { engineOf } from './engine';
import { parseModel } from './model';
import { followSliders } from './outputs';
import { rulesOf } from './rules';

interface ServerPage {
  readonly name: string;
  readonly now: string;
  readonly first: string;
  readonly html: string;
}

const LANGUAGES: readonly (readonly [string, readonly ServerPage[]])[] = [
  ['English', english.cases],
  ['German', german.cases],
];

const STALE = 'stale';

afterEach(() => {
  document.body.innerHTML = '';
});

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

/** A server page's form with its model data, every output's text and every slider's spoken value made stale. */
function load(html: string): { form: HTMLFormElement; server: unknown } {
  document.body.innerHTML = html;
  const form = document.querySelector('form');
  const model = parseModel(form?.querySelector('script[type="application/json"][data-jfb-model]')?.textContent ?? '');
  if (form === null || model === null) throw new Error('not a form page');
  const server = treeOf(form);
  for (const output of form.querySelectorAll('output')) output.textContent = STALE;
  for (const slider of form.querySelectorAll('input[type="range"]')) slider.setAttribute('aria-valuetext', STALE);
  return { form, server };
}

describe.each(LANGUAGES)('the pages the server shows in %s', (_, pages) => {
  it.each(
    pages.flatMap((page) => [
      [`${page.name}, first`, page.first, page.now] as const,
      [page.name, page.html, page.now] as const,
    ]),
  )('%s: the runtime shows its results and sliders as the server did', (_, html, now) => {
    const { form, server } = load(html);
    const model = parseModel(form.querySelector('script[data-jfb-model]')?.textContent ?? '');
    if (model === null) throw new Error('no model');
    expect(treeOf(form)).not.toEqual(server);
    rulesOf(form, model, engineOf(model, FORMULAS), () => Date.parse(now));
    followSliders(form, model);
    expect(treeOf(form)).toEqual(server);
  });
});

describe('the fixture', () => {
  it('holds the cases each language posts, with what a result shows for them', () => {
    expect(LANGUAGES.map(([language, pages]) => [language, pages.map((page) => page.name)])).toEqual([
      ['English', ['answered', 'unreadable', 'nothing']],
      ['German', ['answered', 'unreadable', 'nothing']],
    ]);
    const textsOf = (html: string): Record<string, string> => {
      document.body.innerHTML = html;
      return Object.fromEntries(
        [...document.querySelectorAll('output.jfb-output')].map((output) => [output.id, output.textContent ?? '']),
      );
    };
    const [answered, unreadable, nothing] = english.cases.map((page) => textsOf(page.html));
    expect(answered).toMatchObject({
      'jfb-1-total': 'EUR 3,703.50',
      'jfb-1-days': '3 days',
      'jfb-1-cover': 'EUR 370.35',
    });
    expect(unreadable).toMatchObject({ 'jfb-1-total': '', 'jfb-1-days': '', 'jfb-1-planned': '80.0' });
    expect(nothing).toMatchObject({ 'jfb-1-total': 'EUR 0.00', 'jfb-1-percent': '' });
  });
});
