// A configuration key as code that wraps where a reader expects a key to: after each colon, never inside a word, which
// an `overflow-wrap: anywhere` box did on a phone ("AllowRetentionO" and "ff" on two lines, 16.6's review).
import { html, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';

/** The key as code, with a place to wrap after each colon. */
export function configKey(key: string): TemplateResult {
  const parts = key.split(':');
  return html`<code>${parts.map((part, at) => (at < parts.length - 1 ? html`${part}:<wbr />` : part))}</code>`;
}
