// The Form filter of the Inbox and Spam (ROADMAP 11.1, IA 6.1, ADR-0209): Umbraco's dialog with a search over the forms the
// user may view entries of, by name. The chosen forms stay on top, ticked, whatever the search finds below them; up to 100
// may be chosen, as the server filters by. Apply filter answers the chosen forms with their names, so the chip can name
// them at once; Cancel leaves the filter as it was.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../styles/tokens';
import { findForms } from './entries-api';
import { MOST_FORMS, type EntryFormsModalData, type EntryFormsModalValue, type NamedForm } from './entry-modals';

/** How long the search waits for the user to stop typing before it asks. */
const SEARCH_PAUSE_MS = 250;

export class EntryFormsModalElement extends UmbModalBaseElement<EntryFormsModalData, EntryFormsModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _chosen: { state: true },
    _found: { state: true },
    _search: { state: true },
    _state: { state: true },
    _problem: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      .stack {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      uui-input {
        width: 100%;
      }

      uui-input uui-icon {
        margin-inline-start: var(--uui-size-space-3);
        color: var(--jfb-bo-color-text-muted);
      }

      fieldset {
        display: grid;
        gap: var(--uui-size-space-3);
        max-block-size: 18rem;
        overflow: auto;
        margin: 0;
        padding: 0;
        border: 0;
      }

      legend {
        margin-block-end: var(--uui-size-space-3);
        padding: 0;
        font-weight: 700;
      }

      .said,
      .note {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .note {
        font-size: var(--uui-type-small-size);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .problem {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _chosen: readonly NamedForm[];
  declare private _found: readonly NamedForm[];
  declare private _search: string;
  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _problem: string;

  #reads = 0;
  #timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    super();
    this._chosen = [];
    this._found = [];
    this._search = '';
    this._state = 'loading';
    this._problem = '';
  }

  override connectedCallback() {
    super.connectedCallback();
    this._chosen = [...(this.data?.chosen ?? [])];
    void this.#find();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  async #find() {
    const read = ++this.#reads;
    this._state = 'loading';
    const found = await findForms(this, this._search);
    if (read !== this.#reads) return;
    if (!found) {
      this._state = 'failed';
      return;
    }

    this._found = found.map(({ key, name }) => ({ key, name }));
    this._state = 'ready';
  }

  #typed(text: string) {
    this._search = text;
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => void this.#find(), SEARCH_PAUSE_MS);
  }

  #tick(form: NamedForm, ticked: boolean) {
    this._problem = '';
    this._chosen = ticked
      ? [...this._chosen.filter((one) => one.key !== form.key), form]
      : this._chosen.filter((one) => one.key !== form.key);
  }

  #apply() {
    if (this._chosen.length === 0) {
      this._problem = this.#term('edit_needsOne');
      return;
    }

    this.value = { forms: this._chosen };
    this._submitModal();
  }

  #box(form: NamedForm, full: boolean): TemplateResult {
    const ticked = this._chosen.some((one) => one.key === form.key);
    return html`<uui-checkbox
      label=${form.name}
      .checked=${ticked}
      ?disabled=${full && !ticked}
      @change=${(event: Event) => this.#tick(form, (event.target as HTMLInputElement).checked)}
    ></uui-checkbox>`;
  }

  #list(): TemplateResult {
    const full = this._chosen.length >= MOST_FORMS;
    const chosen = new Set(this._chosen.map((form) => form.key));
    const others = this._found.filter((form) => !chosen.has(form.key));
    const search = this._search.trim();
    return html`<fieldset>
        <legend>${this.#term('forms_legend')}</legend>
        ${this._chosen.map((form) => this.#box(form, full))} ${others.map((form) => this.#box(form, full))}
      </fieldset>
      ${
        this._state === 'loading'
          ? html`<p class="said" role="status">${this.#term('forms_finding')}</p>`
          : this._state === 'failed'
            ? html`<div class="failed" role="alert">
                <span>${this.#term('forms_failed')}</span>
                <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#find()}></uui-button>
              </div>`
            : others.length === 0 && search
              ? html`<p class="said" role="status">${this.#term('forms_none', search)}</p>`
              : nothing
      }
      ${full ? html`<p class="note" role="status">${this.#term('forms_most')}</p>` : nothing}`;
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.#term('edit_headline', this.#term('edit_part_form'))}>
      <form
        @submit=${(event: Event) => {
          event.preventDefault();
          this.#apply();
        }}
      >
        <div class="stack">
          <uui-input
            type="search"
            label=${this.#term('forms_search')}
            placeholder=${this.#term('forms_search')}
            .value=${this._search}
            @input=${(event: Event) => this.#typed((event.target as HTMLInputElement).value)}
            @keydown=${(event: KeyboardEvent) => {
              // Enter in the search searches at once; it applies the filter only from a box or the button.
              if (event.key !== 'Enter') return;
              event.preventDefault();
              clearTimeout(this.#timer);
              void this.#find();
            }}
          >
            <uui-icon slot="prepend" name="icon-search" aria-hidden="true"></uui-icon>
          </uui-input>
          ${this.#list()} ${this._problem ? html`<p class="problem" role="alert">${this._problem}</p>` : nothing}
        </div>
      </form>
      <uui-button slot="actions" label=${this.#term('edit_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.#term('edit_apply')}
        @click=${() => this.#apply()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-entry-forms-modal'))
  customElements.define('jfb-entry-forms-modal', EntryFormsModalElement);

export { EntryFormsModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-forms-modal': EntryFormsModalElement;
  }
}
