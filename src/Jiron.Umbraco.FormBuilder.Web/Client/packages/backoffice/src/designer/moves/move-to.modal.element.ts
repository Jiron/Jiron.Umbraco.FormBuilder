// The Move to... dialog (ROADMAP 9.3c, ADR-0169): where the selected field or group goes, to the end of a group (a
// field, by page) or of a page (a group). Its places are the browser's own radio buttons, so each is reached and said
// as screen readers expect; a place the form refuses can't be chosen and says why, and where the element is now says so.
// Move stays off until a place is chosen, and Enter on a chosen place moves it.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { MoveToModalData, MoveToModalValue, MoveToPlace } from './move-to.modal.token';

export class MoveToModalElement extends UmbModalBaseElement<MoveToModalData, MoveToModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _chosen: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      uui-dialog-layout {
        max-width: 480px;
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
        min-width: 0;
      }

      legend {
        margin-block-end: var(--uui-size-space-3);
        padding: 0;
        font-weight: 700;
      }

      h3 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-2);
        font-size: var(--uui-type-default-size);
        color: var(--jfb-bo-color-text-muted);
      }

      .section:first-of-type h3 {
        margin-block-start: 0;
      }

      ul {
        display: grid;
        gap: var(--uui-size-space-1);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      label {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        column-gap: var(--uui-size-space-3);
        align-items: baseline;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border-radius: var(--uui-border-radius);
        cursor: pointer;
      }

      label:hover:not([data-disabled]) {
        background: var(--uui-color-surface-emphasis);
      }

      label[data-disabled] {
        color: var(--jfb-bo-color-text-muted);
        cursor: default;
      }

      .inside {
        margin-inline-start: var(--uui-size-space-6);
      }

      input {
        margin: 0;
        accent-color: var(--uui-color-selected);
      }

      input:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 2px;
      }

      .name {
        overflow-wrap: anywhere;
      }

      .why {
        grid-column: 2;
        font-size: var(--uui-type-small-size);
      }

      .help {
        margin: var(--uui-size-space-5) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  declare private _chosen: string | null;

  override connectedCallback() {
    super.connectedCallback();
    this._chosen = null;
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    // The first place that can be chosen has the focus, so the arrow keys go through the places at once. The dialog
    // takes the focus itself as it opens, so this waits for the frame after it.
    requestAnimationFrame(() => this.shadowRoot?.querySelector<HTMLInputElement>('input:not([disabled])')?.focus());
  }

  /** Moves to the place chosen; false while none is. */
  #move(): boolean {
    if (!this._chosen) return false;
    this.updateValue({ key: this._chosen });
    this._submitModal();
    return true;
  }

  #place(place: MoveToPlace, index: string) {
    const why = place.refusal ? `why-${index}` : undefined;
    return html`<li>
      <label class=${place.inside ? 'inside' : ''} ?data-disabled=${place.disabled}>
        <input
          type="radio"
          name="place"
          value=${place.key}
          ?disabled=${place.disabled}
          .checked=${this._chosen === place.key}
          aria-describedby=${why ?? nothing}
          @change=${() => (this._chosen = place.key)}
        />
        <span class="name">${place.name}</span>
        ${
          // Said as the place's description, not again as part of its name.
          place.refusal ? html`<span class="why" id=${why ?? ''} aria-hidden="true">${place.refusal}</span>` : nothing
        }
      </label>
    </li>`;
  }

  override render() {
    const data = this.data;
    const name = data?.name ?? '';
    return html`<uui-dialog-layout headline=${this.localize.term('jfbDesigner_moveToHeadline', name)}>
      <fieldset
        @keydown=${(event: KeyboardEvent) => {
          if (event.key === 'Enter' && this.#move()) event.preventDefault();
        }}
      >
        <legend>${this.localize.term('jfbDesigner_moveToLead')}</legend>
        ${(data?.sections ?? []).map((section, at) =>
          section.heading
            ? html`<div class="section" role="group" aria-labelledby=${`page-${at}`}>
                <h3 id=${`page-${at}`}>${section.heading}</h3>
                <ul>
                  ${section.places.map((place, index) => this.#place(place, `${at}-${index}`))}
                </ul>
              </div>`
            : html`<ul>
                ${section.places.map((place, index) => this.#place(place, `${at}-${index}`))}
              </ul>`,
        )}
      </fieldset>
      <p class="help">${this.localize.term('jfbDesigner_moveToHelp')}</p>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        ?disabled=${!this._chosen}
        label=${this.localize.term('jfbDesigner_moveToConfirm')}
        @click=${() => this.#move()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-move-to-modal')) customElements.define('jfb-move-to-modal', MoveToModalElement);

export { MoveToModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-move-to-modal': MoveToModalElement;
  }
}
