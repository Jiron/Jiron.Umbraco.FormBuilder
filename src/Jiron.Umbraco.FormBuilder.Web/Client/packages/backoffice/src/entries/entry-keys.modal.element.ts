// The keyboard shortcuts of the entries lists (ROADMAP 11.1, section 4.13, IA 6.1, docs/ux/copy.md): a list of keys and what each
// does, in Umbraco's dialog with only Close.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../styles/tokens';

/** The keys, each with what it does. */
export const KEYS: readonly { key: string; does: string }[] = [
  { key: 'j', does: 'key_next' },
  { key: 'k', does: 'key_previous' },
  { key: 'Enter', does: 'key_open' },
  { key: 'Esc', does: 'key_close' },
  { key: 'a', does: 'key_approve' },
  { key: 'r', does: 'key_reject' },
  { key: 's', does: 'key_spam' },
  { key: '/', does: 'key_search' },
  { key: '?', does: 'key_help' },
];

export class EntryKeysModalElement extends UmbModalBaseElement<never, never> {
  static override styles = [
    jfbBackofficeTokens,
    css`
      dl {
        display: grid;
        grid-template-columns: max-content 1fr;
        gap: var(--uui-size-space-3) var(--uui-size-space-5);
        margin: 0;
      }

      dd {
        margin: 0;
      }

      kbd {
        display: inline-block;
        min-inline-size: 1.5em;
        padding: 0 var(--uui-size-space-2);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        font: inherit;
        text-align: center;
      }
    `,
  ];

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  override render() {
    return html`<uui-dialog-layout headline=${this.#term('keys_headline')}>
      <dl>
        ${KEYS.map(
          (row) =>
            html`<dt><kbd>${row.key}</kbd></dt>
              <dd>${this.#term(row.does)}</dd>`,
        )}
      </dl>
      <uui-button
        slot="actions"
        look="primary"
        label=${this.#term('keys_close')}
        @click=${this._rejectModal}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-entry-keys-modal')) customElements.define('jfb-entry-keys-modal', EntryKeysModalElement);

export { EntryKeysModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-keys-modal': EntryKeysModalElement;
  }
}
