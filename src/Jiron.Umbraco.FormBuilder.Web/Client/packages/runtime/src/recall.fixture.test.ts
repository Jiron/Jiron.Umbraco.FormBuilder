// The runtime's recalled texts held to the server (ROADMAP 6.4g, ADR-0115): each page the server rendered first and
// again after a post without JavaScript, in English and in German (tests/fixtures/rendering/recall-<language>.json,
// which the integration test Rendering/RecallFixtureTests writes), with every text the views mark made stale, shows them
// again as the runtime does over the page's reading: the server's page, element for element, each text where the
// server put it, an empty one hidden, a progress step without its page's title showing the page's number, a sensitive
// answer masked, a field the rules hide recalling nothing, and a rich text's values escaped.
import { afterEach, describe, expect, it } from 'vitest';
import german from '../../../../../../tests/fixtures/rendering/recall-de.json';
import english from '../../../../../../tests/fixtures/rendering/recall-en.json';
import { engineOf } from './engine';
import { parseModel } from './model';
import { textsOf } from './recall';
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

/**
 * Markup as a tree of elements with their attributes in name order and their texts, the blanks between left out; `hidden`
 * by its presence, as Razor writes it `hidden="hidden"` and the DOM `hidden=""`, which mean the same.
 */
function treeOf(node: Node): unknown {
  if (node.nodeType === Node.TEXT_NODE) return (node.textContent ?? '').trim().length === 0 ? null : node.textContent;
  if (!(node instanceof Element)) return null;
  const children = [...(node instanceof HTMLTemplateElement ? node.content : node).childNodes]
    .map(treeOf)
    .filter((each) => each !== null);
  const attributes = [...node.attributes]
    .map((each) => (each.name === 'hidden' ? 'hidden' : `${each.name}=${each.value}`))
    .sort();
  return { tag: node.localName, attributes, children };
}

/** Makes a marked text stale, as an earlier answer would have left it: its texts, its placeholder and its `hidden`. */
function stale(element: HTMLElement): void {
  if ((element.dataset['jfbText'] ?? '').endsWith(' placeholder')) {
    element.setAttribute('placeholder', STALE);
    return;
  }
  if (element.children.length === 0) element.textContent = STALE;
  else {
    const walker = element.ownerDocument.createTreeWalker(element, NodeFilter.SHOW_TEXT);
    for (let text = walker.nextNode(); text !== null; text = walker.nextNode()) text.textContent = STALE;
  }
  element.toggleAttribute('hidden');
}

/** A server page's form with its model data, every marked text made stale. */
function load(html: string) {
  document.body.innerHTML = html;
  const form = document.querySelector('form');
  const model = parseModel(form?.querySelector('script[type="application/json"][data-jfb-model]')?.textContent ?? '');
  if (form === null || model === null) throw new Error('not a form page');
  const server = treeOf(form);
  const marked = [...form.querySelectorAll<HTMLElement>('[data-jfb-text]')];
  marked.forEach(stale);
  return { form, model, server, marked };
}

describe.each(LANGUAGES)('the pages the server shows in %s', (_, pages) => {
  it.each(
    pages.flatMap((page) => [
      [`${page.name}, first`, page.first, page.now] as const,
      [page.name, page.html, page.now] as const,
    ]),
  )('%s: the runtime shows its recalled texts as the server did', (_, html, now) => {
    const { form, model, server, marked } = load(html);
    expect(marked.length).toBeGreaterThan(0);
    expect(treeOf(form)).not.toEqual(server);
    rulesOf(form, model, engineOf(model, null, textsOf(model)), () => Date.parse(now));
    expect(treeOf(form)).toEqual(server);
  });
});

describe('the fixture', () => {
  /** A page's recalled texts by their hooks: each as text, a rich text as markup, and whether it is hidden. */
  const textsOf = (html: string): Record<string, string> => {
    document.body.innerHTML = html;
    return Object.fromEntries(
      [...document.querySelectorAll<HTMLElement>('form [data-jfb-text]')].map((element) => {
        const hook = element.dataset['jfbText'] ?? '';
        const name = hook.slice(hook.indexOf(' ') + 1);
        const shown =
          name === 'placeholder'
            ? (element.getAttribute('placeholder') ?? '')
            : element.children.length > 0
              ? element.innerHTML
              : (element.textContent ?? '');
        return [`${element.localName} ${name}`, element.hidden ? `(hidden) ${shown}` : shown];
      }),
    );
  };

  it('holds the cases each language posts, with what their texts recall', () => {
    expect(LANGUAGES.map(([language, pages]) => [language, pages.map((page) => page.name)])).toEqual([
      ['English', ['answered', 'hidden', 'nothing']],
      ['German', ['answered', 'hidden', 'nothing']],
    ]);
    const masked = String.fromCharCode(0x2022).repeat(3);
    const [answered, hidden, nothing] = english.cases.map((page) => textsOf(page.html));
    expect(answered).toMatchObject({
      'p help': `For the Large room: by the window; code ${masked}; 1,234.5 seats on 11/2/2026`,
      'div settings.html': '<p>Guests: <b>Ben, Cleo</b>, ages 16 or over, Under 16</p>',
      'textarea placeholder': 'Ada <Lovelace>',
      'h2 title': 'Ada <Lovelace>',
    });
    expect(hidden, 'a note the rules hide, and an age they hide in its row, recall nothing').toMatchObject({
      'p help': 'For the Small room: no note; code ; 7 seats on ',
      'div settings.html': '<p>Guests: <b>Ben</b>, ages 16 or over</p>',
    });
    expect(nothing, 'a text that is only a tag is hidden, and the step shows the page').toMatchObject({
      'h3 settings.text': '(hidden) ',
      'h2 title': '(hidden) ',
      'span title': 'Page 2',
      'textarea placeholder': 'Your notes',
    });
    expect(textsOf(german.cases[0]?.html ?? '')['p help']).toBe(
      `For the Large room: by the window; code ${masked}; 1.234,5 seats on 02.11.2026`,
    );
    expect(textsOf(german.cases[2]?.html ?? '')['span title']).toBe('Seite 2');
  });
});
