// One part of a list's filter (ROADMAP 11.1, IA 6.1, ADR-0207): the states, when entries were received, their actions, or a
// field with a test its kind allows. Umbraco's dialog, with Apply filter and Cancel; what can't be applied is said under it.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import type { EntryFieldFilterRequestModel, EntryListFilterRequestModel } from '../api';
import { jfbBackofficeTokens } from '../styles/tokens';
import { ACTION_FILTERS, FILTER_STATES, PRESETS } from './entry-list-model';
import type { EntryFilterModalData, EntryFilterModalValue } from './entry-modals';

type Received = NonNullable<EntryListFilterRequestModel['received']>;

/** The filter with one field's filter set at its place, or added after the others. */
export function withCondition(
  filter: EntryListFilterRequestModel,
  condition: EntryFieldFilterRequestModel,
  index: number | undefined,
): EntryListFilterRequestModel {
  const fields = [...filter.fields];
  if (index === undefined || index >= fields.length) fields.push(condition);
  else fields[index] = condition;
  return { ...filter, fields };
}

/** A number typed in a box, or undefined for none or one that isn't a number. */
const numberOf = (text: string): number | undefined => {
  const trimmed = text.trim().replace(',', '.');
  if (trimmed === '') return undefined;
  const number = Number(trimmed);
  return Number.isFinite(number) ? number : undefined;
};

export class EntryFilterModalElement extends UmbModalBaseElement<EntryFilterModalData, EntryFilterModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _states: { state: true },
    _preset: { state: true },
    _from: { state: true },
    _to: { state: true },
    _actions: { state: true },
    _test: { state: true },
    _values: { state: true },
    _text: { state: true },
    _low: { state: true },
    _high: { state: true },
    _problem: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
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

      .stack {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      .pair {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
        gap: var(--uui-size-space-4);
      }

      .pair div {
        display: grid;
        gap: var(--uui-size-space-2);
      }

      uui-input,
      uui-textarea,
      uui-select {
        width: 100%;
      }

      .options {
        display: grid;
        gap: var(--uui-size-space-3);
        max-block-size: 18rem;
        position: relative;
        overflow: auto;
      }

      .hint {
        margin: var(--uui-size-space-2) 0 0;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .problem {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _states: readonly string[];
  declare private _preset: string;
  declare private _from: string;
  declare private _to: string;
  declare private _actions: string;
  declare private _test: string;
  declare private _values: readonly string[];
  declare private _text: string;
  declare private _low: string;
  declare private _high: string;
  declare private _problem: string;

  constructor() {
    super();
    this._states = [];
    this._preset = 'today';
    this._from = '';
    this._to = '';
    this._actions = 'failed';
    this._test = '';
    this._values = [];
    this._text = '';
    this._low = '';
    this._high = '';
    this._problem = '';
  }

  override connectedCallback() {
    super.connectedCallback();
    const data = this.data;
    if (!data) return;
    const filter = data.filter;
    this._states = [...filter.states];
    const received = filter.received;
    this._preset = received ? (received.preset ?? 'range') : 'today';
    this._from = received?.from ?? '';
    this._to = received?.to ?? '';
    this._actions = filter.actions ?? 'failed';
    const condition = data.index === undefined ? undefined : filter.fields[data.index];
    this._test = condition?.test ?? data.field?.tests[0] ?? '';
    this._values = condition?.values ?? [];
    this._text = (condition?.values ?? []).join('\n');
    const kind = data.field?.kind;
    this._low = kind === 'date' ? (condition?.from ?? '') : condition?.min != null ? String(condition.min) : '';
    this._high = kind === 'date' ? (condition?.to ?? '') : condition?.max != null ? String(condition.max) : '';
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  #headline(): string {
    const data = this.data;
    // A part is named as the headline's language writes it within a sentence: "Filter by state", "Nach Status filtern".
    const what = data?.part === 'field' ? (data.field?.label ?? '') : this.#term(`edit_part_${data?.part ?? 'state'}`);
    return this.#term('edit_headline', what);
  }

  /** The states this list offers. */
  get #offered(): readonly string[] {
    return this.data?.states ?? FILTER_STATES;
  }

  // ---- Applying ------------------------------------------------------------------------------------------------------

  #apply() {
    const filter = this.#applied();
    if (!filter) return;
    this.value = { filter };
    this._submitModal();
  }

  /** The filter with this dialog's part, or undefined with what is wrong said. */
  #applied(): EntryListFilterRequestModel | undefined {
    const data = this.data;
    if (!data) return undefined;
    const filter = data.filter;
    switch (data.part) {
      case 'state':
        if (this._states.length === 0) return this.#refuse('edit_needsOne');
        return { ...filter, states: this.#offered.filter((state) => this._states.includes(state)) };
      case 'received': {
        if (this._preset !== 'range') return { ...filter, received: { preset: this._preset } };
        if (!this._from && !this._to) return this.#refuse('edit_needsBound');
        if (this._from && this._to && this._from > this._to) return this.#refuse('edit_order');
        const received: Received = {
          ...(this._from ? { from: this._from } : {}),
          ...(this._to ? { to: this._to } : {}),
        };
        return { ...filter, received };
      }
      case 'actions':
        return { ...filter, actions: this._actions };
      default:
        return this.#appliedField(filter);
    }
  }

  #appliedField(filter: EntryListFilterRequestModel): EntryListFilterRequestModel | undefined {
    const field = this.data?.field;
    if (!field) return undefined;
    const condition: EntryFieldFilterRequestModel = { field: field.key, test: this._test, values: [] };
    if (this._test === 'oneOf' || this._test === 'noneOf') {
      const values =
        field.options.length > 0
          ? [...this._values]
          : this._text
              .split('\n')
              .map((value) => value.trim())
              .filter((value) => value !== '');
      if (values.length === 0) return this.#refuse(field.options.length > 0 ? 'edit_needsOne' : 'edit_needsValue');
      return withCondition(filter, { ...condition, values: [...new Set(values)] }, this.data?.index);
    }

    if (this._test === 'startsWith') {
      const value = this._text.trim();
      if (!value) return this.#refuse('edit_needsValue');
      return withCondition(filter, { ...condition, values: [value] }, this.data?.index);
    }

    if (this._test === 'between') {
      if (field.kind === 'date') {
        if (!this._low && !this._high) return this.#refuse('edit_needsBound');
        if (this._low && this._high && this._low > this._high) return this.#refuse('edit_order');
        return withCondition(
          filter,
          { ...condition, ...(this._low ? { from: this._low } : {}), ...(this._high ? { to: this._high } : {}) },
          this.data?.index,
        );
      }

      const low = numberOf(this._low);
      const high = numberOf(this._high);
      if (low === undefined && high === undefined) return this.#refuse('edit_needsBound');
      if (low !== undefined && high !== undefined && low > high) return this.#refuse('edit_order');
      return withCondition(
        filter,
        { ...condition, ...(low !== undefined ? { min: low } : {}), ...(high !== undefined ? { max: high } : {}) },
        this.data?.index,
      );
    }

    return withCondition(filter, condition, this.data?.index);
  }

  #refuse(key: string): undefined {
    this._problem = this.#term(key);
    return undefined;
  }

  // ---- Drawing -------------------------------------------------------------------------------------------------------

  // A part whose headline names what its boxes choose (states, actions) gives them no second name: the headline
  // names the group for a screen reader.
  #states(): TemplateResult {
    return html`<fieldset aria-label=${this.#headline()}>
      ${this.#offered.map(
        (state) =>
          html`<uui-checkbox
            label=${this.#term(`state_${state}`)}
            .checked=${this._states.includes(state)}
            @change=${(event: Event) => {
              const checked = (event.target as HTMLInputElement).checked;
              this._states = checked ? [...this._states, state] : this._states.filter((one) => one !== state);
              this._problem = '';
            }}
          ></uui-checkbox>`,
      )}
    </fieldset>`;
  }

  #received(): TemplateResult {
    return html`<div class="stack">
      <fieldset>
        <legend>${this.#term('edit_span')}</legend>
        <uui-radio-group
          .value=${this._preset}
          @change=${(event: Event) => {
            this._preset = (event.target as HTMLInputElement).value;
            this._problem = '';
          }}
        >
          ${[...PRESETS, 'range'].map(
            (preset) => html`<uui-radio value=${preset} label=${this.#term(`received_${preset}`)}></uui-radio>`,
          )}
        </uui-radio-group>
      </fieldset>
      ${this._preset === 'range' ? this.#pair('date', this._from, this._to, 'edit_from', 'edit_to', true) : nothing}
    </div>`;
  }

  /** Two boxes side by side, From and To or Lowest and Highest; the first and the last bind the dialog's range. */
  #pair(type: 'date' | 'number', low: string, high: string, lowLabel: string, highLabel: string, days = false) {
    const set = (which: 'low' | 'high', value: string) => {
      if (days) {
        if (which === 'low') this._from = value;
        else this._to = value;
      } else if (which === 'low') this._low = value;
      else this._high = value;
      this._problem = '';
    };
    return html`<div class="pair">
      <div>
        <uui-label for="low">${this.#term(lowLabel)}</uui-label>
        <uui-input
          id="low"
          type=${type === 'date' ? 'date' : 'text'}
          inputmode=${type === 'number' ? 'decimal' : nothing}
          label=${this.#term(lowLabel)}
          .value=${low}
          @input=${(event: Event) => set('low', (event.target as HTMLInputElement).value)}
        ></uui-input>
      </div>
      <div>
        <uui-label for="high">${this.#term(highLabel)}</uui-label>
        <uui-input
          id="high"
          type=${type === 'date' ? 'date' : 'text'}
          inputmode=${type === 'number' ? 'decimal' : nothing}
          label=${this.#term(highLabel)}
          .value=${high}
          @input=${(event: Event) => set('high', (event.target as HTMLInputElement).value)}
        ></uui-input>
      </div>
    </div>`;
  }

  #actions(): TemplateResult {
    return html`<fieldset aria-label=${this.#headline()}>
      <uui-radio-group
        .value=${this._actions}
        @change=${(event: Event) => (this._actions = (event.target as HTMLInputElement).value)}
      >
        ${ACTION_FILTERS.map(
          (which) => html`<uui-radio value=${which} label=${this.#term(`actions_${which}`)}></uui-radio>`,
        )}
      </uui-radio-group>
    </fieldset>`;
  }

  #field(): TemplateResult | typeof nothing {
    const field = this.data?.field;
    if (!field) return nothing;
    const tests = field.tests.map((test) => ({
      name: this.#term(`test_${test}`),
      value: test,
      selected: test === this._test,
    }));
    return html`<div class="stack">
      <div>
        <uui-label for="test">${this.#term('edit_test')}</uui-label>
        <uui-select
          id="test"
          label=${this.#term('edit_test')}
          .options=${tests}
          @change=${(event: Event) => {
            this._test = (event.target as HTMLSelectElement).value;
            this._problem = '';
          }}
        ></uui-select>
      </div>
      ${this.#valuesOf(field)}
    </div>`;
  }

  #valuesOf(field: NonNullable<EntryFilterModalData['field']>): TemplateResult | typeof nothing {
    if (this._test === 'oneOf' || this._test === 'noneOf') {
      if (field.options.length > 0)
        return html`<fieldset>
          <legend>${this.#term('edit_options')}</legend>
          <div class="options">
            ${field.options.map(
              (option) =>
                html`<uui-checkbox
                  label=${option.label}
                  .checked=${this._values.includes(option.value)}
                  @change=${(event: Event) => {
                    const checked = (event.target as HTMLInputElement).checked;
                    this._values = checked
                      ? [...this._values, option.value]
                      : this._values.filter((value) => value !== option.value);
                    this._problem = '';
                  }}
                ></uui-checkbox>`,
            )}
          </div>
        </fieldset>`;
      return html`<div>
        <uui-label for="values">${this.#term('edit_values')}</uui-label>
        <uui-textarea
          id="values"
          label=${`${this.#term('edit_values')}. ${this.#term('edit_valuesHint')}`}
          .value=${this._text}
          @input=${(event: Event) => {
            this._text = (event.target as HTMLTextAreaElement).value;
            this._problem = '';
          }}
        ></uui-textarea>
        <p class="hint">${this.#term('edit_valuesHint')}</p>
      </div>`;
    }

    if (this._test === 'startsWith')
      return html`<div>
        <uui-label for="value">${this.#term('edit_value')}</uui-label>
        <uui-input
          id="value"
          label=${this.#term('edit_value')}
          maxlength="200"
          .value=${this._text}
          @input=${(event: Event) => {
            this._text = (event.target as HTMLInputElement).value;
            this._problem = '';
          }}
        ></uui-input>
      </div>`;

    if (this._test === 'between')
      return field.kind === 'date'
        ? this.#pair('date', this._low, this._high, 'edit_from', 'edit_to')
        : this.#pair('number', this._low, this._high, 'edit_lowest', 'edit_highest');
    return nothing;
  }

  #body(): TemplateResult | typeof nothing {
    switch (this.data?.part) {
      case 'state':
        return this.#states();
      case 'received':
        return this.#received();
      case 'actions':
        return this.#actions();
      default:
        return this.#field();
    }
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.#headline()}>
      <form
        @submit=${(event: Event) => {
          event.preventDefault();
          this.#apply();
        }}
      >
        <div class="stack">
          ${this.#body()} ${this._problem ? html`<p class="problem" role="alert">${this._problem}</p>` : nothing}
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

if (!customElements.get('jfb-entry-filter-modal'))
  customElements.define('jfb-entry-filter-modal', EntryFilterModalElement);

export { EntryFilterModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-filter-modal': EntryFilterModalElement;
  }
}
