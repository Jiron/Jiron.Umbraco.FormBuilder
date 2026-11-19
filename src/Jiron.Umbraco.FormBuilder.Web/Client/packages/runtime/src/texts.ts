/**
 * The visitor texts that recall answers, shown on the page (ROADMAP 6.4g, ADR-0115). The views mark each element that
 * shows such a text `data-jfb-text="<owner> <name>"`; it gets the text as the renderer gives it and `hidden` while the
 * text is empty, or its `data-jfb-fallback` instead where it has one. A placeholder's hook is on its control, whose
 * attribute it writes. An element is written only where its text differs, so the page's own observers see only real
 * changes.
 */
import { proto } from '@jfb/logic/proto';
import { parseSanitised } from './html';
import type { FormModel } from './model';

/** Shows the texts the renderer gave, by `<owner> <name>`, on the elements that show them. */
export function showTexts(form: HTMLFormElement, model: FormModel, texts: ReadonlyMap<string, string>): void {
  const html = new Set(
    (model.mergeTags?.texts ?? [])
      .filter((each) => each.context === 'html')
      .map((each) => `${each.owner} ${each.text}`),
  );
  for (const element of proto(form).querySelectorAll<HTMLElement>('[data-jfb-text]')) {
    const hook = element.dataset['jfbText'] ?? '';
    const text = texts.get(hook);
    if (text === undefined) continue;
    if (hook.endsWith(' placeholder')) {
      if (text.length === 0) element.removeAttribute('placeholder');
      else if (element.getAttribute('placeholder') !== text) element.setAttribute('placeholder', text);
      continue;
    }
    const content = text.length > 0 ? text : (element.dataset['jfbFallback'] ?? '');
    if (html.has(hook)) writeHtml(element, content);
    else if (element.textContent !== content) element.textContent = content;
    element.toggleAttribute('hidden', content.length === 0);
  }
}

/** Whether a node is a line break, which the `html` filter writes for each break in an answer. */
const isBreak = (node: Node): boolean => node.nodeName === 'BR';

/** A tree's elements but its line breaks, nested as they stand, whatever their texts. */
const skeleton = (node: ParentNode): string =>
  [...node.children]
    .filter((child) => !isBreak(child))
    .map((child) => `<${child.localName}>${skeleton(child)}`)
    .join('') + '/';

/** A node's texts and elements, in order; comments left out. */
const partsOf = (node: Node): ChildNode[] =>
  [...node.childNodes].filter((child) => child.nodeType === Node.TEXT_NODE || child instanceof Element);

/** A node's texts and the places of its line breaks and other elements, in order. */
const textsOf = (node: Node): string =>
  partsOf(node)
    .map((child) => (child.nodeType === Node.TEXT_NODE ? `t${child.textContent}` : isBreak(child) ? 'b' : 'e'))
    .join('\n');

/**
 * Brings an element's rich text to a rendering of its template without letting markup from a string into the page
 * (ROADMAP section 4.10): the rendering is parsed in an inert `<template>`, where nothing runs or loads, and only its
 * texts and the values of the attributes the page's elements already have are copied over, never a node. A rendering
 * differs from the page's markup only there, as every value is escaped for HTML, and in the line breaks an answer
 * recalled through `| html` brings, which are made anew, bare (6.8b); one whose other elements differ,
 * which no template renders, is left as the page has it.
 */
function writeHtml(element: HTMLElement, markup: string): void {
  const given = parseSanitised(element.ownerDocument, markup);
  if (skeleton(element) !== skeleton(given)) return;
  const theirs = [given, ...given.querySelectorAll(':not(br)')];
  [element, ...element.querySelectorAll(':not(br)')].forEach((own, at) => {
    const other = theirs[at];
    if (other === undefined) return;
    if (at > 0)
      for (const { name, value } of [...own.attributes]) {
        const wanted = (other as Element).getAttribute(name);
        if (wanted !== null && wanted !== value && !name.startsWith('on')) own.setAttribute(name, wanted);
      }
    if (textsOf(own) === textsOf(other)) return;
    const kept = [...own.children].filter((child) => !isBreak(child));
    own.replaceChildren(
      ...partsOf(other).map((child) =>
        child.nodeType === Node.TEXT_NODE
          ? own.ownerDocument.createTextNode(child.textContent ?? '')
          : isBreak(child)
            ? own.ownerDocument.createElement('br')
            : (kept.shift() ?? ''),
      ),
    );
  });
}
