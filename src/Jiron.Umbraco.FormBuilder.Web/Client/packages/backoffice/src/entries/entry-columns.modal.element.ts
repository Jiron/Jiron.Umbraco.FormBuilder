// The column chooser of a form's Entries view (ROADMAP 11.1, IA 6.1, ADR-0207): a sidebar of the form's fields to tick, the
// live version's in their order and then those it removed, up to 20. Every entry shows its details, which one line says;
// sensitive fields are no columns, which one line says too, only where the form has one. Show columns keeps the choice
// for the user, the same on every device.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../styles/tokens';
import { MOST_COLUMNS, type EntryColumnsModalData, type EntryColumnsModalValue } from './entry-modals';

export class EntryColumnsModalElement extends UmbModalBaseElement<EntryColumnsModalData, EntryColumnsModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _columns: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      p {
        margin: 0 0 var(--uui-size-space-5);
        color: var(--jfb-bo-color-text-muted);
      }

      fieldset {
        display: grid;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding: 0;
        border: 0;
      }

      legend {
        margin-block-end: var(--uui-size-space-3);
        padding: 0;
        font-weight: 700;
      }

      .removed {
        color: var(--jfb-bo-color-text-muted);
      }

      .note {
        margin: var(--uui-size-space-5) 0 0;
        font-size: var(--uui-type-small-size);
      }

      .actions {
        display: flex;
        flex-wrap: wrap;
        justify-content: flex-end;
        gap: var(--uui-size-space-2);
      }
    `,
  ];

  declare private _columns: readonly string[];

  constructor() {
    super();
    this._columns = [];
  }

  override connectedCallback() {
    super.connectedCallback();
    this._columns = [...(this.data?.columns ?? [])];
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  #tick(key: string, ticked: boolean) {
    // In the order of the form, as the list shows them.
    const order = (this.data?.fields ?? []).map((field) => field.key);
    const chosen = new Set(this._columns);
    if (ticked) chosen.add(key);
    else chosen.delete(key);
    this._columns = order.filter((one) => chosen.has(one));
  }

  #apply() {
    this.value = { columns: this._columns };
    this._submitModal();
  }

  override render() {
    const fields = (this.data?.fields ?? []).filter((field) => field.column);
    const sensitive = (this.data?.fields ?? []).some((field) => field.sensitive && !field.parent);
    const full = this._columns.length >= MOST_COLUMNS;
    return html`<umb-body-layout headline=${this.#term('chooser_headline')}>
      <uui-box>
        <p>${this.#term('chooser_details')}</p>
        ${
          fields.length === 0
            ? html`<p>${this.#term('chooser_none')}</p>`
            : html`<fieldset>
                <legend>${this.#term('chooser_fields')}</legend>
                ${fields.map((field) => {
                  const ticked = this._columns.includes(field.key);
                  const label = field.removed ? `${field.label} (${this.#term('chooser_removed')})` : field.label;
                  return html`<uui-checkbox
                    class=${field.removed ? 'removed' : ''}
                    label=${label}
                    .checked=${ticked}
                    ?disabled=${full && !ticked}
                    @change=${(event: Event) => this.#tick(field.key, (event.target as HTMLInputElement).checked)}
                  ></uui-checkbox>`;
                })}
              </fieldset>`
        }
        ${full ? html`<p class="note" role="status">${this.#term('chooser_most')}</p>` : nothing}
        ${sensitive ? html`<p class="note">${this.#term('chooser_sensitive')}</p>` : nothing}
      </uui-box>
      <div slot="actions" class="actions">
        <uui-button label=${this.#term('chooser_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.#term('chooser_apply')}
          @click=${() => this.#apply()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-entry-columns-modal'))
  customElements.define('jfb-entry-columns-modal', EntryColumnsModalElement);

export { EntryColumnsModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-columns-modal': EntryColumnsModalElement;
  }
}
