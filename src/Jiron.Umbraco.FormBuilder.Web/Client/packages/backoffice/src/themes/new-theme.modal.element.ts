// The create dialog of themes (ROADMAP 12.1, IA 7, ADR-0215): the theme in code the new theme builds on, asked first as
// Umbraco's create dialog asks, each with whether it has a dark scheme. Choosing one is the action; the list screen then
// opens the new theme's workspace.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import type { NewThemeModalData, NewThemeModalValue } from './new-theme.modal.token';
import { withThemeLabel } from './theme-label';

export class NewThemeModalElement extends UmbModalBaseElement<NewThemeModalData, NewThemeModalValue> {
  static override styles = css`
    p {
      margin: 0 0 var(--uui-size-space-4);
    }
  `;

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbThemes_${key}`, ...args);

  #choose(baseTheme: string) {
    this.updateValue({ baseTheme });
    this._submitModal();
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.#term('newTheme')}>
      <p>${this.#term('newThemeHelp')}</p>
      <uui-ref-list>
        ${(this.data?.bases ?? []).map(
          (base) =>
            html`<umb-ref-item
              name=${withThemeLabel(base, (key) => this.localize.term(key)).label}
              detail=${base.darkScheme ? this.#term('hasDark') : ''}
              icon="icon-palette"
              @open=${() => this.#choose(base.alias)}
            ></umb-ref-item>`,
        )}
      </uui-ref-list>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-new-theme-modal')) customElements.define('jfb-new-theme-modal', NewThemeModalElement);

export { NewThemeModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-new-theme-modal': NewThemeModalElement;
  }
}
