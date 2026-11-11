// What the add-on's tests share: its English words registered as the backoffice would, and small helpers to wait for
// an element and read what it shows.
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import en from './lang/en';

/** Registers the add-on's English words, flat, as Umbraco's localization holds them. */
export function registerEnglish(): void {
  const flat: Record<string, unknown> = { $code: 'en', general_cancel: 'Cancel' };
  for (const [area, texts] of Object.entries(en as Record<string, Record<string, unknown>>)) {
    for (const [key, text] of Object.entries(texts)) flat[`${area}_${key}`] = text;
  }
  umbLocalizationManager.registerLocalization(flat as never);
}

/** A value a test needs, or the test fails saying which. */
export function must<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) throw new Error(`the test needs ${what}`);
  return value;
}

/** The element's shadow root. */
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

/** The element's text without runs of white space. */
export function text(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}
