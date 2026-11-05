// The Keyboard shortcuts dialog (ROADMAP 9.3c): what the designer's keys do, on the field, group or page the focus is
// on, opened with ? or from the canvas's "..." menu, closed with Close or Esc. The keys are named as this keyboard has
// them: Cmd on a Mac, Strg and Entf in German; between two keys that do it either way, screen readers hear "or".
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { modifierTerm } from './keys';

export class ShortcutsModalElement extends UmbModalBaseElement<never, never> {
  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      uui-dialog-layout {
        max-width: 560px;
      }

      .lead {
        margin: 0 0 var(--uui-size-space-5);
        color: var(--jfb-bo-color-text-muted);
      }

      dl {
        display: grid;
        grid-template-columns: max-content minmax(0, 1fr);
        gap: var(--uui-size-space-3) var(--uui-size-space-5);
        margin: 0;
      }

      dt {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-1);
        align-items: baseline;
      }

      dd {
        margin: 0;
      }

      kbd {
        min-width: 1.6em;
        padding: 1px 6px;
        border: 1px solid var(--uui-color-border-emphasis);
        border-block-end-width: 2px;
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
        font: 600 var(--uui-type-small-size) / 1.5 var(--uui-font-family);
        text-align: center;
      }

      .or {
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  // Each shortcut's keys, a key or "+" between keys held together, "/" between two keys that do it either way.
  #rows(): [readonly string[], string][] {
    const term = (key: string) => this.localize.term(key);
    const held = term(modifierTerm());
    const up = term('jfbDesigner_keyUp');
    const down = term('jfbDesigner_keyDown');
    return [
      [[held, '+', 'Z'], term('jfbDesigner_shortcutUndo')],
      [[held, '+', 'Y'], term('jfbDesigner_shortcutRedo')],
      [[held, '+', 'D'], term('jfbDesigner_shortcutDuplicate')],
      [[term('jfbDesigner_keyDelete')], term('jfbDesigner_shortcutDelete')],
      [['Alt', '+', up, '/', down], term('jfbDesigner_shortcutStep')],
      [[up, '/', down], term('jfbDesigner_shortcutArrows')],
      [['F2'], term('jfbDesigner_shortcutRename')],
      [['Esc'], term('jfbDesigner_shortcutEscape')],
      [['?'], term('jfbDesigner_shortcutHelp')],
    ];
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.localize.term('jfbDesigner_shortcuts')}>
      <p class="lead">${this.localize.term('jfbDesigner_shortcutsLead')}</p>
      <dl>
        ${this.#rows().map(
          ([keys, text]) =>
            html`<dt>
                ${keys.map((key) =>
                  key === '+'
                    ? html`<span class="or" aria-hidden="true">+</span>`
                    : key === '/'
                      ? html`<span class="or"
                          ><span aria-hidden="true">/</span
                          ><span class="visually-hidden">${this.localize.term('jfbDesigner_keyOr')}</span></span
                        >`
                      : html`<kbd>${key}</kbd>`,
                )}
              </dt>
              <dd>${text}</dd>`,
        )}
      </dl>
      <!-- Nothing to confirm here: Close in the plain look, as the other dialogs that only show something. -->
      <uui-button slot="actions" label=${this.localize.term('general_close')} @click=${this._rejectModal}></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-shortcuts-modal')) customElements.define('jfb-shortcuts-modal', ShortcutsModalElement);

export { ShortcutsModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-shortcuts-modal': ShortcutsModalElement;
  }
}
