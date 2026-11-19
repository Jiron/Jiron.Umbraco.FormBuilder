/**
 * How the element writes the views' markup (ADR-0128): elements made with DOM methods, each attribute as a Razor view
 * writes it, and the hooks of `FormBuilderHtml` (a text's `data-jfb-text`, a label that recalls answers, a field's
 * mark). A text is always text; only a rich text the server sanitised is parsed, through a `<template>`.
 */
import { proto } from '@jfb/logic/proto';

/**
 * An attribute's value as a Razor view writes it: left out for null, undefined and false, its own name for true (as
 * `hidden="@isHidden"` is), and a text or a number as it is. `FLAG` writes it empty, as `FormBuilderHtml.Flag` does.
 */
export type AttributeValue = string | number | boolean | null | undefined;

/** What an element holds: nodes, texts, nothing, or a list of those. */
export type Child = Node | string | null | undefined | false | readonly Child[];

/** A flag's value when it is on, as `FormBuilderHtml.Flag` writes it: the attribute, empty. */
export const FLAG = '';

/** A flag, as `FormBuilderHtml.Flag` writes it: empty when on, left out when off. */
export const flag = (on: boolean): string | null => (on ? FLAG : null);

/** An element with its attributes, in the order given, and what it holds. */
export function el(
  document: Document,
  tag: string,
  attributes: Readonly<Record<string, AttributeValue>> = {},
  ...children: Child[]
): HTMLElement {
  const element = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes)) {
    if (value === null || value === undefined || value === false) continue;
    proto(element).setAttribute(name, value === true ? name : String(value));
  }
  append(element, children);
  return element;
}

/** Appends what an element holds, texts as text nodes. */
export function append(parent: Node, children: readonly Child[]): void {
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    if (typeof child === 'string')
      proto(parent).appendChild((proto(parent).ownerDocument ?? (parent as Document)).createTextNode(child));
    else if (Array.isArray(child)) append(parent, child);
    else proto(parent).appendChild(child as Node);
  }
}

/** A rich text the server sanitised, as its nodes. */
export function html(document: Document, text: string | undefined): DocumentFragment {
  const template = document.createElement('template');
  // eslint-disable-next-line no-restricted-properties -- the server's sanitised rich text, as its Razor views write it (ADR-0128)
  template.innerHTML = text ?? '';
  return template.content;
}

/** The hook of a text that recalls answers, `data-jfb-text="<owner> <name>"`, when the owner's texts include it. */
export const hookOf = (owner: string, name: string, recalling: readonly string[]): string | null =>
  recalling.includes(name) ? `${owner} ${name}` : null;

/**
 * A field's label, as `FormBuilderHtml.Label` writes it: the text, or, when it recalls answers, a span of its own with
 * its hook, hidden while empty.
 */
export function labelText(document: Document, owner: string, text: string | undefined, recalls: boolean): Child {
  if (!recalls) return text ?? '';
  return el(
    document,
    'span',
    { 'data-jfb-text': `${owner} label`, hidden: flag((text ?? '').length === 0) },
    text ?? '',
  );
}

/** What a mark is written from: a field's, a part's or a grid row's. */
export interface Marked {
  mark: 'none' | 'optional' | 'required';
  markText: string;
  isMarkShown: boolean;
  markShownWhile?: string;
  /** What a screen reader hears in the asterisk's place, where the controls cannot carry `required` (6.8c). */
  markSpoken?: string;
}

/**
 * A label's mark, as `FormBuilderHtml.Mark` writes it after the label's text: a space, then the asterisk hidden from
 * assistive technology or "(optional)", with the field's state it shows in on a form with rules, hidden in the other.
 */
export function mark(document: Document, marked: Marked): Child {
  if (marked.mark === 'none') return null;
  return [
    ' ',
    el(
      document,
      'span',
      {
        class: 'jfb-mark',
        'aria-hidden': marked.mark === 'required' && marked.markSpoken === undefined ? 'true' : null,
        'data-jfb-mark': marked.markShownWhile,
        hidden: flag(!marked.isMarkShown),
      },
      marked.markSpoken === undefined
        ? marked.markText
        : [
            el(document, 'span', { 'aria-hidden': 'true' }, marked.markText),
            el(document, 'span', { class: 'jfb-visually-hidden' }, marked.markSpoken),
          ],
    ),
  ];
}
