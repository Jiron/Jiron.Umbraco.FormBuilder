/**
 * What the element's tests read (ADR-0128): the whole forms of `tests/fixtures/rendering/element-<language>.json`,
 * which the integration test `Rendering/ElementFixtureTests` writes, and a tree as a canonical text, one element per
 * line, so the server's markup and the element's compare element for element with a diff that says where they part.
 */
import german from '../../../../../../tests/fixtures/rendering/element-de.json';
import english from '../../../../../../tests/fixtures/rendering/element-en.json';
import type { JfbElementAnswer } from './answer';

/** One form of the fixture, in one theme and one culture. */
export interface ElementCase {
  readonly name: string;
  readonly theme: string;
  readonly culture: string;
  readonly now: string;
  /** The theme's classes by slot, as the theme gives them (ADR-0105). */
  readonly themeClasses: Readonly<Record<string, string>>;
  /** What only a post without JavaScript uses, which the element leaves out. */
  readonly postOnly: {
    readonly inputs: readonly string[];
    readonly inputPrefixes: readonly string[];
    readonly attributes: readonly string[];
  };
  /** The names the contract gives controls beside a field's alias (ADR-0015). */
  readonly names: {
    readonly nav: string;
    readonly consentPrefix: string;
    readonly rowsPrefix: string;
    readonly orderPrefix: string;
  };
  readonly answer: JfbElementAnswer;
  /** The server's markup, its model data's script empty, since the answer holds the same text. */
  readonly html: string;
}

/** The fixture's cases by language. */
export const LANGUAGES = [
  ['English', english.cases],
  ['German', german.cases],
] as unknown as readonly (readonly [string, readonly ElementCase[]])[];

/** The server's markup for a case, without what only a post without JavaScript uses. */
export function serverMarkup(document: Document, element: ElementCase): Element {
  const template = document.createElement('template');
  // eslint-disable-next-line no-restricted-properties -- test support, never shipped: the fixture's server markup, parsed inert
  template.innerHTML = element.html;
  const root = template.content.firstElementChild;
  if (root === null) throw new Error(`${element.name}: no markup`);
  // A row in a repeater's template holds what a post uses as the rows do.
  const reduce = (tree: ParentNode): void => {
    for (const attribute of element.postOnly.attributes) {
      for (const each of tree.querySelectorAll(`[${attribute}]`)) each.removeAttribute(attribute);
    }
    for (const input of tree.querySelectorAll('input[type="hidden"]')) {
      const name = input.getAttribute('name') ?? '';
      if (
        element.postOnly.inputs.includes(name) ||
        element.postOnly.inputPrefixes.some((prefix) => name.startsWith(prefix))
      )
        input.remove();
    }
    for (const nested of tree.querySelectorAll('template')) reduce(nested.content);
  };
  for (const attribute of element.postOnly.attributes) root.removeAttribute(attribute);
  reduce(root);
  return root;
}

/**
 * A tree as a canonical text: each element on its own line, indented by its depth, with its attributes sorted by name
 * and their exact values; each text with its white space run together, the white space of the views' indentation (a
 * run with a line break) left out; a template's content as its children; a model data's script without its text,
 * which a test compares on its own. Adjacent texts are one text, as a parser reads them.
 */
export function canonical(tree: Node): string[] {
  const copy = tree.cloneNode(true);
  copy.normalize();
  for (const template of copy instanceof Element ? copy.querySelectorAll('template') : [])
    (template as HTMLTemplateElement).content.normalize();
  return lines(copy, 0);
}

function lines(node: Node, depth: number): string[] {
  const indent = '  '.repeat(depth);
  if (node.nodeType === Node.TEXT_NODE) {
    const text = (node.textContent ?? '')
      .replace(/^\s*\n\s*/, '')
      .replace(/\s*\n\s*$/, '')
      .replace(/\s+/g, ' ');
    return text.length === 0 ? [] : [`${indent}"${text}"`];
  }
  if (!(node instanceof Element)) return [];
  const attributes = [...node.attributes]
    .map((attribute) => `${attribute.name}=${JSON.stringify(attribute.value)}`)
    .sort()
    .join(' ');
  const shown = [`${indent}<${node.localName}${attributes.length > 0 ? ' ' + attributes : ''}>`];
  if (node.localName === 'script' && node.hasAttribute('data-jfb-model')) return shown;
  const children = node instanceof HTMLTemplateElement ? node.content.childNodes : node.childNodes;
  for (const child of children) shown.push(...lines(child, depth + 1));
  return shown;
}
