// The field picker (ROADMAP 22.1, ADR-0256): the one way to add to a form. Every Add field and every insert point opens
// it as Umbraco's small sidebar: the search with the focus, then Layout (Group), the field types under their groups and
// the site's saved groups, in two columns where it has the room and in reading order for the Tab key, and in its footer
// Paste questions... and Close. A type the site can't offer stays in its place with its reason under its name, and
// can't be picked. The search narrows the lists as it is typed; Enter in it picks the first match, which is marked
// while a search is typed, and Arrow down goes to the first item. A pick answers the Design view, which adds it where
// the picker was opened.
import { css, html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import type { FieldTypeResponseModel } from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import type { FieldPick, FieldPickerModalData, FieldPickerModalValue } from './field-picker.modal.token';

/** A text as the search compares it: lower case, accents dropped. */
export const folded = (text: string) =>
  text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[^\p{L}\p{N} ]/gu, '');

/** One thing the picker lists: its icon, its name, what it picks, and why it can't be picked where it can't. */
interface Entry {
  readonly id: string;
  readonly icon: string;
  readonly name: string;
  readonly pick: FieldPick;
  readonly reason: string | null;
}

export class FieldPickerModalElement extends UmbModalBaseElement<FieldPickerModalData, FieldPickerModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _query: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      uui-input {
        width: 100%;
      }

      .groups {
        column-width: 13rem;
        column-gap: var(--uui-size-space-5);
        margin-block-start: var(--uui-size-space-5);
      }

      section {
        break-inside: avoid;
        padding-block-end: var(--uui-size-space-4);
      }

      h4 {
        margin: 0 0 var(--uui-size-space-1);
        padding-inline: var(--uui-size-space-2);
        font-size: var(--uui-type-default-size);
      }

      ul {
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .item {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        width: 100%;
        min-height: 36px;
        padding: 0 var(--uui-size-space-2);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
        transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
      }

      @media (hover: hover) {
        .item:hover:not([aria-disabled='true']) {
          background: var(--uui-color-surface-emphasis);
        }
      }

      .item:active:not([aria-disabled='true']) {
        transform: scale(0.98);
      }

      .item:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .item uui-icon {
        flex: none;
      }

      /*
       * What Enter in the search picks, while a search is typed: the backoffice's colour for what is current under its
       * contrast text, as the lists of the section mark their open row (Umbraco's own carries the text at 4.3:1 in the
       * dark theme). The mark stays under the pointer, which often rests where the first match appears.
       */
      .item[data-first],
      .item[data-first]:hover {
        background: var(--jfb-bo-color-current);
        color: var(--uui-color-current-contrast);
      }

      .item kbd {
        margin-inline-start: auto;
        padding: 0 var(--uui-size-space-2);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
        font-size: var(--uui-type-small-size);
      }

      .item[aria-disabled='true'] {
        color: var(--jfb-bo-color-text-muted);
        cursor: default;
      }

      .why {
        margin: 0;
        padding: 0 var(--uui-size-space-2) var(--uui-size-space-2)
          calc(var(--uui-size-space-2) + 16px + var(--uui-size-space-3));
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .none {
        margin: var(--uui-size-space-5) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      @media (prefers-reduced-motion: reduce) {
        .item:active:not([aria-disabled='true']) {
          transform: none;
        }
      }
    `,
  ];

  declare private _query: string;

  constructor() {
    super();
    this._query = '';
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.data?.closed?.();
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    this.data?.opened?.();
    // The search has the focus as the picker opens; again after the sidebar's own first frame, which may move it.
    this.#search()?.focus();
    requestAnimationFrame(() => this.#search()?.focus());
  }

  #search(): HTMLElement | null {
    return this.shadowRoot?.querySelector<HTMLElement>('uui-input') ?? null;
  }

  #pick(pick: FieldPick) {
    this.updateValue({ pick });
    this._submitModal();
  }

  #type(type: FieldTypeResponseModel): Entry {
    return {
      id: `type-${type.alias}`,
      icon: type.icon,
      name: this.localize.string(type.name),
      pick: { kind: 'field', type: type.alias },
      reason: type.availability.isAvailable
        ? null
        : this.localize.string(type.availability.reason ?? '#jfbDesigner_unavailable'),
    };
  }

  /** The lists, each under its heading, narrowed to what the search finds by name or alias; a heading without one goes. */
  #lists(): { readonly heading: string; readonly entries: readonly Entry[] }[] {
    const data = this.data;
    if (!data) return [];
    const query = folded(this._query.trim());
    const matches = (entry: Entry, alias = '') =>
      !query || folded(entry.name).includes(query) || folded(alias).includes(query);
    const group: Entry = {
      id: 'group',
      icon: 'icon-layout',
      name: this.localize.term('jfbDesigner_pickerGroup'),
      pick: { kind: 'group' },
      reason: null,
    };
    return [
      { heading: this.localize.term('jfbDesigner_pickerLayout'), entries: [group].filter((one) => matches(one)) },
      ...data.groups.map((each) => ({
        heading: this.localize.string(each.name),
        entries: data.types
          .filter((type) => type.paletteGroup === each.alias)
          .map((type) => ({ type, entry: this.#type(type) }))
          .filter(({ type, entry }) => matches(entry, type.alias))
          .map(({ entry }) => entry),
      })),
      {
        heading: this.localize.term('jfbDesigner_pickerSavedGroups'),
        entries: data.saved
          .map((saved): Entry => ({
            id: `saved-${saved.key}`,
            icon: 'icon-layers-alt',
            name: saved.name,
            pick: { kind: 'savedGroup', saved },
            reason: null,
          }))
          .filter((one) => matches(one)),
      },
    ].filter((list) => list.entries.length > 0);
  }

  #onKey(event: KeyboardEvent, first: Entry | undefined) {
    if (event.key === 'Enter') {
      event.preventDefault();
      // Enter picks the match marked, which is marked only while a search is typed.
      if (first && this._query.trim()) this.#pick(first.pick);
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.shadowRoot?.querySelector<HTMLElement>('.item')?.focus();
    } else if (event.key === 'Escape') {
      // A search box would take Escape to empty itself; in the picker, Escape closes it at once, as everywhere else in it.
      event.preventDefault();
      event.stopPropagation();
      this._rejectModal();
    }
  }

  #item(entry: Entry, first: Entry | undefined): TemplateResult {
    const marked = entry === first && this._query.trim() !== '';
    return html`<li>
      <button
        type="button"
        class="item"
        ?data-first=${marked}
        aria-disabled=${entry.reason ? 'true' : 'false'}
        aria-describedby=${entry.reason ? `why-${entry.id}` : nothing}
        @click=${() => {
          if (!entry.reason) this.#pick(entry.pick);
        }}
      >
        <uui-icon name=${entry.icon} aria-hidden="true"></uui-icon><span>${entry.name}</span>
        ${marked ? html`<kbd aria-hidden="true">${this.localize.term('jfbDesigner_pickerEnter')}</kbd>` : nothing}
      </button>
      ${entry.reason ? html`<p class="why" id=${`why-${entry.id}`}>${entry.reason}</p>` : nothing}
    </li>`;
  }

  override render() {
    const lists = this.#lists();
    const first = lists.flatMap((list) => list.entries).find((entry) => !entry.reason);
    const query = this._query.trim();
    return html`<umb-body-layout headline=${this.localize.term('jfbDesigner_pickerHeading')}>
      <uui-input
        type="search"
        label=${this.localize.term('jfbDesigner_pickerSearch')}
        placeholder=${this.localize.term('jfbDesigner_pickerSearch')}
        .value=${this._query}
        @input=${(event: Event) => (this._query = (event.target as HTMLInputElement).value)}
        @keydown=${(event: KeyboardEvent) => this.#onKey(event, first)}
      >
        <uui-icon name="icon-search" slot="prepend" aria-hidden="true"></uui-icon>
      </uui-input>
      <p class="visually-hidden" aria-live="polite">
        ${query && first ? this.localize.term('jfbDesigner_pickerEnterAdds', first.name) : ''}
      </p>
      ${
        lists.length > 0
          ? html`<div class="groups">
              ${lists.map(
                (list) =>
                  html`<section>
                    <h4>${list.heading}</h4>
                    <ul>
                      ${list.entries.map((entry) => this.#item(entry, first))}
                    </ul>
                  </section>`,
              )}
            </div>`
          : html`<p class="none" role="status">${this.localize.term('jfbDesigner_pickerNone', query)}</p>`
      }
      <uui-button
        slot="footer-info"
        look="outline"
        label=${this.localize.term('jfbDesigner_pasteQuestions')}
        @click=${() => this.#pick({ kind: 'paste' })}
      ></uui-button>
      <div slot="actions">
        <uui-button label=${this.localize.term('general_close')} @click=${this._rejectModal}></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-field-picker-modal'))
  customElements.define('jfb-field-picker-modal', FieldPickerModalElement);

export { FieldPickerModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-field-picker-modal': FieldPickerModalElement;
  }
}
