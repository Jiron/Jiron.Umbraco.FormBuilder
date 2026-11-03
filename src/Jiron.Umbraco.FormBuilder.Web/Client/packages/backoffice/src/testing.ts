// Helpers for the elements' tests (Vitest in jsdom, ADR-0134): the English texts registered as the backoffice
// registers a dictionary, a host element that provides contexts, a promise to answer by hand, and a wait for the work
// an element starts.
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import en from './lang/en';

/** Umbraco's own words the elements use, as Umbraco's English dictionary has them. */
const umbracoTerms = {
  general_cancel: 'Cancel',
  general_choose: 'Choose',
  general_close: 'Close',
  general_remove: 'Remove',
};

/** Registers the English dictionary, `<area>_<key>` as the backoffice flattens it, with Umbraco's words above. */
export function registerEnglish(): void {
  const flat: Record<string, unknown> = { $code: 'en', ...umbracoTerms };
  for (const [area, texts] of Object.entries(en as Record<string, Record<string, unknown>>)) {
    for (const [key, text] of Object.entries(texts)) flat[`${area}_${key}`] = text;
  }
  umbLocalizationManager.registerLocalization(flat as never);
}

/** An element that hosts controllers and contexts for the element under test, which goes inside it. */
export class TestHostElement extends UmbLitElement {}
if (!customElements.get('jfb-test-host')) customElements.define('jfb-test-host', TestHostElement);

/** A host in the document, ready to provide contexts. */
export function testHost(): TestHostElement {
  const host = document.createElement('jfb-test-host') as TestHostElement;
  document.body.append(host);
  return host;
}

/** A promise and the functions that settle it, for answering a request at the moment a test chooses. */
export function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

/** A value a test needs, or the test fails saying which. */
export function must<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) throw new Error(`the test needs ${what}`);
  return value;
}

/** The element's shadow root, which every element of the package has. */
export function shadow(element: Element): ShadowRoot {
  return must(element.shadowRoot, `the shadow root of <${element.localName}>`);
}

/** Lets the element's pending requests and renders settle. */
export async function settle(element?: { updateComplete: Promise<unknown> }): Promise<void> {
  for (let round = 0; round < 5; round++) {
    await new Promise((done) => setTimeout(done, 0));
    await element?.updateComplete;
  }
}

/** The elements of the element's shadow root that match the selector. */
export function all<E extends Element = HTMLElement>(element: Element, selector: string): E[] {
  return [...(element.shadowRoot?.querySelectorAll<E>(selector) ?? [])];
}

/** The element's text without runs of white space. */
export function text(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}
