// The form picker's sidebar (ADR-0134, ADR-0135): a search field, the library's levels within the data type's limits,
// and Close and Choose. A folder is a button that opens its level in place and says whether it is open
// (aria-expanded); a form is a toggle button that says whether it is chosen (aria-pressed) and shows a check mark, so
// the state never rests on colour alone. Each level loads when it is first opened, a page at a time. In the folder
// mode of the allowed folders it lists folders only, without the search, and a folder row holds two buttons: the
// chevron opens it, the row chooses it. Nothing animates (ADR-0020).
import {
  css,
  html,
  nothing,
  repeat,
  styleMap,
  type PropertyValues,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import type { FormPickerModalData, FormPickerModalValue } from './form-picker-modal.token';
import { formPickerData, type FormPickerData, type PickerItem } from './picker-data';

/** How long the search waits after the last key before it asks, in milliseconds. */
export const SEARCH_DELAY = 300;

/** The top level's key in the map of levels. */
const ROOT = '';

/** A level of the library, or a search's results, as far as they are loaded. */
interface Loaded {
  state: 'loading' | 'ready' | 'failed';
  items: PickerItem[];
  total: number;
  /** Whether "Show more" is loading the next page. */
  loadingMore: boolean;
  /** Whether the last "Show more" failed. */
  moreFailed: boolean;
}

const loading: Loaded = { state: 'loading', items: [], total: 0, loadingMore: false, moreFailed: false };

export class FormPickerModalElement extends UmbModalBaseElement<FormPickerModalData, FormPickerModalValue> {
  static override properties = {
    _levels: { state: true },
    _open: { state: true },
    _text: { state: true },
    _results: { state: true },
    _searching: { state: true },
    _selection: { state: true },
  };

  declare private _levels: ReadonlyMap<string, Loaded>;
  declare private _open: ReadonlySet<string>;
  declare private _text: string;
  declare private _results: Loaded | undefined;
  declare private _searching: boolean;
  declare private _selection: readonly string[];

  /** The forms and folders this sidebar has seen or was given, by key, which the answer names. */
  #known = new Map<string, PickerItem>();
  #source?: FormPickerData;
  #timer?: ReturnType<typeof setTimeout>;
  /** Counts the searches, so that an answer to an older one is dropped. */
  #searchId = 0;
  /** The first item of the page "Show more" loaded last, which takes the focus once it shows. */
  #focusKey: string | undefined = undefined;

  constructor() {
    super();
    this._levels = new Map();
    this._open = new Set();
    this._text = '';
    this._results = undefined;
    this._searching = false;
    this._selection = [];
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
  }

  protected override willUpdate(changed: PropertyValues) {
    super.willUpdate(changed);
    // The modal context holds the value: the property's picks when the sidebar opens, then each change made here.
    if (changed.has('value')) {
      this._selection = [...(this.value?.selection ?? [])];
      for (const item of this.value?.items ?? []) this.#known.set(item.key, item);
    }
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    void this.#load(ROOT);
    // Umbraco puts the focus on the dialog itself (research note section 8); typing should search at once.
    if (!this.#folders) this.#searchField()?.focus();
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    if (this.#focusKey === undefined) return;
    const row = [...(this.shadowRoot?.querySelectorAll<HTMLElement>('[data-key]') ?? [])].find(
      (element) => element.dataset['key'] === this.#focusKey,
    );
    if (row) {
      this.#focusKey = undefined;
      row.focus();
    }
  }

  /** Whether the sidebar chooses folders, as the allowed folders do. */
  get #folders(): boolean {
    return this.data?.pick === 'folders';
  }

  get #data(): FormPickerData {
    this.#source ??= formPickerData(this, this.data?.dataTypeKey);
    return this.#source;
  }

  #searchField(): HTMLElement | null | undefined {
    return this.shadowRoot?.querySelector<HTMLElement>('#search');
  }

  #setLevel(key: string, level: Loaded) {
    this._levels = new Map(this._levels).set(key, level);
  }

  async #load(key: string, more = false) {
    const current = this._levels.get(key);
    const next = more && current?.state === 'ready';
    this.#setLevel(key, next ? { ...current, loadingMore: true, moreFailed: false } : loading);
    const page = await this.#data.children(key === ROOT ? undefined : key, next ? current.items.length : 0);
    if (!page) {
      this.#setLevel(
        key,
        next ? { ...current, loadingMore: false, moreFailed: true } : { ...loading, state: 'failed' },
      );
      return;
    }
    for (const item of page.items) this.#known.set(item.key, item);
    this.#focusKey = next ? this.#shown(page.items)[0]?.key : undefined;
    this.#setLevel(key, {
      state: 'ready',
      items: next ? [...current.items, ...page.items] : page.items,
      total: page.total,
      loadingMore: false,
      moreFailed: false,
    });
  }

  #toggleFolder(folder: PickerItem) {
    const open = new Set(this._open);
    if (open.delete(folder.key)) {
      this._open = open;
      return;
    }
    open.add(folder.key);
    this._open = open;
    if (this._levels.get(folder.key)?.state !== 'ready') void this.#load(folder.key);
  }

  /** The items of a level this sidebar lists: in the folder mode, its folders only. */
  #shown(items: readonly PickerItem[]): PickerItem[] {
    return this.#folders ? items.filter((item) => item.kind === 'folder') : [...items];
  }

  #toggleChosen(item: PickerItem) {
    this.#known.set(item.key, item);
    const chosen = this._selection.includes(item.key);
    let selection: string[];
    if (this.data?.multiple) {
      selection = chosen ? this._selection.filter((key) => key !== item.key) : [...this._selection, item.key];
    } else {
      selection = chosen ? [] : [item.key];
    }
    this._selection = selection;
    this.updateValue({ selection, items: selection.flatMap((key) => this.#known.get(key) ?? []) });
  }

  #onInput(event: Event) {
    this._text = (event.target as HTMLInputElement).value;
    clearTimeout(this.#timer);
    const text = this._text.trim();
    this.#searchId++;
    if (!text) {
      this._results = undefined;
      this._searching = false;
      return;
    }
    this._searching = true;
    this.#timer = setTimeout(() => void this.#search(text), SEARCH_DELAY);
  }

  async #search(text: string, more = false) {
    const id = more ? this.#searchId : ++this.#searchId;
    const current = this._results;
    const next = more && current?.state === 'ready';
    this._results = next ? { ...current, loadingMore: true, moreFailed: false } : loading;
    this._searching = !next;
    const page = await this.#data.search(text, next ? current.items.length : 0);
    if (id !== this.#searchId) return;
    this._searching = false;
    if (!page) {
      this._results = next ? { ...current, loadingMore: false, moreFailed: true } : { ...loading, state: 'failed' };
      return;
    }
    for (const item of page.items) this.#known.set(item.key, item);
    this.#focusKey = next ? page.items[0]?.key : undefined;
    this._results = {
      state: 'ready',
      items: next ? [...current.items, ...page.items] : page.items,
      total: page.total,
      loadingMore: false,
      moreFailed: false,
    };
  }

  #clearSearch() {
    clearTimeout(this.#timer);
    this.#searchId++;
    this._text = '';
    this._results = undefined;
    this._searching = false;
    this.#searchField()?.focus();
  }

  override render() {
    let headline = this.data?.multiple ? 'jfbFormPicker_headlineMultiple' : 'jfbFormPicker_headline';
    if (this.#folders) headline = 'jfbFormPicker_headlineFolders';
    return html`
      <umb-body-layout headline=${this.localize.term(headline)}>
        ${this.#folders ? nothing : this.#renderSearch()}
        <div class="visually-hidden" role="status">${this.#searchStatus()}</div>
        <uui-box>${this._text.trim() ? this.#renderResults() : this.#renderLevel(ROOT, 0)}</uui-box>
        <div slot="actions">
          <uui-button label=${this.localize.term('general_close')} @click=${() => this._rejectModal()}></uui-button>
          <uui-button
            label=${this.localize.term('general_choose')}
            look="primary"
            color="positive"
            ?disabled=${this._selection.length === 0}
            @click=${() => this._submitModal()}
          ></uui-button>
        </div>
      </umb-body-layout>
    `;
  }

  #renderSearch() {
    return html`
      <uui-input
        id="search"
        .value=${this._text}
        label=${this.localize.term('jfbFormPicker_search')}
        placeholder=${this.localize.term('jfbFormPicker_searchPlaceholder')}
        @input=${this.#onInput}
      >
        <div slot="prepend" class="search-icon">
          ${
            this._searching
              ? html`<uui-loader-circle></uui-loader-circle>`
              : html`<uui-icon name="search" aria-hidden="true"></uui-icon>`
          }
        </div>
        ${
          this._text
            ? html`<uui-button
                slot="append"
                compact
                label=${this.localize.term('jfbFormPicker_clearSearch')}
                @click=${this.#clearSearch}
              >
                <uui-icon name="icon-delete"></uui-icon>
              </uui-button>`
            : nothing
        }
      </uui-input>
    `;
  }

  /** What a screen reader hears once a search has answered. */
  #searchStatus(): string {
    const results = this._results;
    if (!this._text.trim() || results?.state !== 'ready') return '';
    return results.items.length > 0
      ? this.localize.term('jfbFormPicker_found', results.total)
      : this.localize.term('jfbFormPicker_noMatch', this._text.trim());
  }

  #renderLevel(key: string, depth: number): TemplateResult {
    const level = this._levels.get(key);
    if (!level || level.state === 'loading') return this.#renderPlaceholders(key === ROOT ? 3 : 1, depth);
    if (level.state === 'failed') return this.#renderFailure(this.#failedText(), () => this.#load(key), depth);
    const items = this.#shown(level.items);
    if (items.length === 0 && !this.#hasMore(level)) {
      return html`<p class="message" style=${styleMap(this.#indent(depth))}>
        ${this.localize.term(this.#emptyText(key))}
      </p>`;
    }
    return html`
      <ul class="level">
        ${repeat(
          items,
          (item) => item.key,
          (item) =>
            html`<li>${item.kind === 'folder' ? this.#renderFolder(item, depth) : this.#renderForm(item, depth)}</li>`,
        )}
      </ul>
      ${this.#renderMore(level, depth, () => this.#load(key, true))}
    `;
  }

  /** What an empty level says: that no form is published there yet, or, in the folder mode, no folder holds one. */
  #emptyText(key: string): string {
    if (this.#folders) return key === ROOT ? 'jfbFormPicker_noFolders' : 'jfbFormPicker_noSubfolders';
    if (key !== ROOT) return 'jfbFormPicker_emptyFolder';
    return this.data?.limited ? 'jfbFormPicker_noAllowedForms' : 'jfbFormPicker_noForms';
  }

  /**
   * Whether a level has more to load. A level lists its folders before its forms, so in the folder mode a page that
   * reached a form holds the last of the folders.
   */
  #hasMore(loaded: Loaded): boolean {
    if (loaded.items.length >= loaded.total) return false;
    return !this.#folders || loaded.items.every((item) => item.kind === 'folder');
  }

  #renderResults(): TemplateResult {
    const text = this._text.trim();
    const results = this._results;
    if (!results || results.state === 'loading') return this.#renderPlaceholders(3, 0);
    if (results.state === 'failed')
      return this.#renderFailure('jfbFormPicker_searchFailed', () => this.#search(text), 0);
    if (results.items.length === 0) {
      return html`<div class="message">
        <span>${this.localize.term('jfbFormPicker_noMatch', text)}</span>
        <uui-button
          look="secondary"
          compact
          label=${this.localize.term('jfbFormPicker_clearSearch')}
          @click=${this.#clearSearch}
        ></uui-button>
      </div>`;
    }
    return html`
      <ul class="level">
        ${repeat(
          results.items,
          (item) => item.key,
          (item) => html`<li>${this.#renderForm(item, 0)}</li>`,
        )}
      </ul>
      ${this.#renderMore(results, 0, () => this.#search(text, true))}
    `;
  }

  #renderFolder(folder: PickerItem, depth: number) {
    if (this.#folders) return this.#renderChoosableFolder(folder, depth);
    const open = this._open.has(folder.key);
    return html`
      <button
        type="button"
        class="row"
        style=${styleMap(this.#indent(depth))}
        aria-expanded=${open ? 'true' : 'false'}
        data-key=${folder.key}
        @click=${() => this.#toggleFolder(folder)}
      >
        <span class="expand" aria-hidden="true"><uui-symbol-expand ?open=${open}></uui-symbol-expand></span>
        <umb-icon class="icon" name="icon-folder"></umb-icon>
        <span class="text"><span class="name">${folder.name}</span></span>
      </button>
      ${open ? this.#renderLevel(folder.key, depth + 1) : nothing}
    `;
  }

  /** A folder of the folder mode: the chevron opens its level, the row chooses it. */
  #renderChoosableFolder(folder: PickerItem, depth: number) {
    const open = this._open.has(folder.key);
    const chosen = this._selection.includes(folder.key);
    return html`
      <div class="split" style=${styleMap(this.#indent(depth))}>
        <button
          type="button"
          class="toggle"
          aria-expanded=${open ? 'true' : 'false'}
          aria-label=${this.localize.term('jfbFormPicker_showFolders', folder.name)}
          @click=${() => this.#toggleFolder(folder)}
        >
          <uui-symbol-expand ?open=${open}></uui-symbol-expand>
        </button>
        <button
          type="button"
          class="row"
          aria-pressed=${chosen ? 'true' : 'false'}
          data-key=${folder.key}
          @click=${() => this.#toggleChosen(folder)}
        >
          <umb-icon class="icon" name="icon-folder"></umb-icon>
          <span class="text"><span class="name">${folder.name}</span></span>
          ${chosen ? html`<umb-icon class="check" name="icon-check"></umb-icon>` : nothing}
        </button>
      </div>
      ${open ? this.#renderLevel(folder.key, depth + 1) : nothing}
    `;
  }

  #renderForm(form: PickerItem, depth: number) {
    const chosen = this._selection.includes(form.key);
    return html`
      <button
        type="button"
        class="row"
        style=${styleMap(this.#indent(depth))}
        aria-pressed=${chosen ? 'true' : 'false'}
        data-key=${form.key}
        @click=${() => this.#toggleChosen(form)}
      >
        <span class="expand" aria-hidden="true"></span>
        <umb-icon class="icon" name="icon-document"></umb-icon>
        <span class="text">
          <span class="name">${form.name}</span>
          ${form.alias ? html`<span class="alias">${form.alias}</span>` : nothing}
        </span>
        ${chosen ? html`<umb-icon class="check" name="icon-check"></umb-icon>` : nothing}
      </button>
    `;
  }

  #renderMore(loaded: Loaded, depth: number, more: () => void) {
    if (loaded.moreFailed) return this.#renderFailure(this.#failedText(), more, depth);
    if (!this.#hasMore(loaded)) return nothing;
    return html`<div class="more" style=${styleMap(this.#indent(depth))}>
      <uui-button
        look="secondary"
        compact
        label=${this.localize.term('jfbFormPicker_showMore')}
        .state=${loaded.loadingMore ? 'waiting' : undefined}
        @click=${more}
      ></uui-button>
    </div>`;
  }

  #failedText(): string {
    return this.#folders ? 'jfbFormPicker_foldersFailed' : 'jfbFormPicker_loadFailed';
  }

  #renderFailure(message: string, retry: () => void, depth: number) {
    return html`<div class="message" role="alert" style=${styleMap(this.#indent(depth))}>
      <span>${this.localize.term(message)}</span>
      <uui-button
        look="secondary"
        compact
        label=${this.localize.term('jfbFormPicker_tryAgain')}
        @click=${retry}
      ></uui-button>
    </div>`;
  }

  /** Rows in the shape of those that are coming, so that nothing moves when they arrive. */
  #renderPlaceholders(count: number, depth: number) {
    return html`<div class="placeholders" aria-hidden="true">
      ${Array.from(
        { length: count },
        () =>
          html`<div class="row placeholder" style=${styleMap(this.#indent(depth))}>
            <span class="expand"></span><span class="icon block"></span
            ><span class="text"><span class="bar"></span></span>
          </div>`,
      )}
    </div>`;
  }

  #indent(depth: number): Record<string, string> {
    return { '--jfb-depth': String(depth) };
  }

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      #search {
        width: 100%;
        margin-bottom: var(--uui-size-space-4);
      }

      .search-icon {
        display: flex;
        align-items: center;
        padding-inline-start: var(--uui-size-space-3);
      }

      .level {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      .row {
        box-sizing: border-box;
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        width: 100%;
        min-height: var(--uui-size-12);
        margin: 0;
        padding-block: var(--uui-size-space-1);
        padding-inline: calc(var(--jfb-depth, 0) * var(--uui-size-space-5)) var(--uui-size-space-3);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      .row:hover,
      .toggle:hover {
        background-color: var(--uui-color-surface-emphasis);
      }

      .row:focus-visible,
      .toggle:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      /* UUI's selected colours fall short of AA in its dark theme (docs/ux/tokens.md); a chosen row keeps one colour. */
      .row[aria-pressed='true'],
      .row[aria-pressed='true']:hover {
        background-color: var(--jfb-bo-color-selected);
        color: var(--uui-color-selected-contrast);
      }

      /* The folder mode's row: the chevron takes the place the expand symbol has in a row of the forms. */
      .split {
        display: flex;
        align-items: stretch;
        padding-inline-start: calc(var(--jfb-depth, 0) * var(--uui-size-space-5));
      }

      .split .row {
        flex: 1;
        min-width: 0;
        padding-inline-start: var(--uui-size-space-2);
      }

      .toggle {
        flex: none;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: var(--uui-size-space-6);
        margin: 0;
        padding: 0;
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        cursor: pointer;
      }

      .expand {
        flex: none;
        display: inline-flex;
        justify-content: center;
        width: var(--uui-size-space-6);
      }

      .icon {
        flex: none;
      }

      .text {
        display: flex;
        flex: 1;
        flex-direction: column;
        min-width: 0;
      }

      .name {
        overflow-wrap: anywhere;
      }

      .alias {
        overflow-wrap: anywhere;
        color: var(--jfb-bo-color-text-muted);
        font-family: var(--jfb-bo-font-mono);
        font-size: var(--uui-type-small-size);
      }

      .row[aria-pressed='true'] .alias {
        color: inherit;
      }

      .check {
        flex: none;
      }

      .placeholder {
        cursor: default;
      }

      .placeholder:hover {
        background: none;
      }

      .block,
      .bar {
        display: block;
        border-radius: var(--uui-border-radius);
        background-color: var(--uui-color-surface-alt);
      }

      .block {
        width: 1em;
        height: 1em;
      }

      .bar {
        width: 40%;
        height: 0.75em;
      }

      .message {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding-block: var(--uui-size-space-3);
        padding-inline: calc(var(--uui-size-space-6) + var(--jfb-depth, 0) * var(--uui-size-space-5))
          var(--uui-size-space-3);
        color: var(--jfb-bo-color-text-muted);
      }

      .more {
        padding-block: var(--uui-size-space-2);
        padding-inline: calc(var(--uui-size-space-6) + var(--jfb-depth, 0) * var(--uui-size-space-5)) 0;
      }
    `,
  ];
}

customElements.define('jfb-form-picker-modal', FormPickerModalElement);

export { FormPickerModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-picker-modal': FormPickerModalElement;
  }
}
