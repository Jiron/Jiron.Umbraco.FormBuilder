// "Insert merge tag" under a text that takes tags (ROADMAP 9.4c, ADR-0173): a button that opens a panel with a search
// box and the paths the place may use in the draft as it is, read each time it opens, grouped by their source. A path
// pressed is handed to the box as its tag and the panel closes; Escape closes it and gives the focus back to the button.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { MergeTagPathResponseModel } from '../../api';
import type { DesignDefinition } from '../definition';
import { groupPaths, readMergeTagPaths, tagOf, type MergeTagPlace } from './merge-tags';

let opened = 0;

export class MergeTagPickerElement extends UmbLitElement {
  static override properties = {
    place: {},
    form: {},
    culture: {},
    definition: { attribute: false },
    insert: { attribute: false },
    _paths: { state: true },
    _failed: { state: true },
    _query: { state: true },
  };

  declare place: MergeTagPlace;
  /** The form's key, whose draft the paths come from. */
  declare form: string;
  /** The language the labels are shown in. */
  declare culture: string;
  /** The draft as the designer holds it now. */
  declare definition: () => DesignDefinition;
  /** Puts a tag into the box, where its caret was, and gives the box the focus. */
  declare insert: (tag: string) => void;
  declare private _paths: MergeTagPathResponseModel[] | null;
  declare private _failed: boolean;
  declare private _query: string;

  readonly #id = `jfb-merge-tags-${++opened}`;

  constructor() {
    super();
    this.place = 'visitorText';
    this.form = '';
    this.culture = '';
    this.definition = () => ({ pages: [] });
    this.insert = () => undefined;
    this._paths = null;
    this._failed = false;
    this._query = '';
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbMergeTags_${key}`, ...args);

  // A word of the backoffice's own, or none where it has none for it.
  #word(key: string): string | undefined {
    const term = `jfbMergeTags_${key}`;
    const said = this.localize.term(term);
    return said && said !== term ? said : undefined;
  }

  get #panel() {
    return this.renderRoot.querySelector<HTMLElement & { hidePopover?: () => void }>(`#${this.#id}`);
  }

  async #open() {
    this._query = '';
    this._failed = false;
    this._paths = null;
    void this.updateComplete.then(() => this.renderRoot.querySelector<HTMLInputElement>('input')?.focus());
    const paths = await readMergeTagPaths(this, this.form, this.place, this.culture, this.definition());
    this._failed = paths === undefined;
    this._paths = paths ?? [];
  }

  #close(focusButton: boolean) {
    this.#panel?.hidePopover?.();
    if (focusButton) this.renderRoot.querySelector<HTMLElement>('.open')?.focus();
  }

  #pick(path: string) {
    this.#close(false);
    this.insert(tagOf(path));
  }

  protected override render(): TemplateResult {
    const name = this.#term('insert');
    return html`<button type="button" class="open" popovertarget=${this.#id}>
        <uui-icon name="icon-brackets" aria-hidden="true"></uui-icon>${name}
      </button>
      <uui-popover-container
        id=${this.#id}
        placement="bottom-start"
        @toggle=${(event: ToggleEvent) => {
          if (event.newState === 'open') void this.#open();
        }}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key !== 'Escape') return;
          // The panel closes, and the Design view's Escape, which clears the selection, waits for the next one.
          event.stopPropagation();
          event.preventDefault();
          this.#close(true);
        }}
      >
        <div class="panel" role="dialog" aria-label=${name}>
          <input
            type="search"
            aria-label=${this.#term('find')}
            placeholder=${this.#term('find')}
            autocomplete="off"
            .value=${this._query}
            @input=${(event: Event) => (this._query = (event.target as HTMLInputElement).value)}
          />
          ${this.#list()}
        </div>
      </uui-popover-container>`;
  }

  #list(): TemplateResult {
    if (this._failed) return html`<p class="note" role="status">${this.#term('failed')}</p>`;
    if (this._paths === null)
      return html`<p class="note" role="status"><uui-loader-bar></uui-loader-bar>${this.#term('loading')}</p>`;
    const groups = groupPaths(
      this._paths,
      {
        source: (source) => this.#word(`source_${source}`),
        path: (path) => this.#word(`path_${path.replaceAll('.', '_')}`),
        own: this.#term('source_own'),
      },
      this._query,
    );
    if (groups.length === 0) return html`<p class="note" role="status">${this.#term('none')}</p>`;
    return html`${groups.map(
      (group, at) =>
        html`<p class="heading" id=${`${this.#id}-${at}`}>${group.heading}</p>
          <ul aria-labelledby=${`${this.#id}-${at}`}>
            ${group.paths.map(
              (each) =>
                html`<li>
                  <button type="button" class="path" @click=${() => this.#pick(each.path)}>
                    <span>${each.label}</span>
                    ${each.label === each.path ? nothing : html`<code>${tagOf(each.path)}</code>`}
                  </button>
                </li>`,
            )}
          </ul>`,
    )}`;
  }

  static override styles = css`
    :host {
      display: block;
    }

    .open {
      display: inline-flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      padding: var(--uui-size-space-1) var(--uui-size-space-2);
      border: 0;
      border-radius: var(--uui-border-radius);
      background: none;
      color: var(--uui-color-interactive);
      font: inherit;
      font-size: var(--uui-type-small-size);
      font-weight: 600;
      cursor: pointer;
    }

    .open:hover {
      background: var(--uui-color-surface-emphasis);
      color: var(--uui-color-interactive-emphasis);
    }

    .open:focus-visible,
    .path:focus-visible,
    input:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 0;
    }

    .panel {
      display: grid;
      gap: var(--uui-size-space-2);
      box-sizing: border-box;
      inline-size: min(300px, calc(100vw - 32px));
      max-block-size: min(420px, 60vh);
      overflow: auto;
      padding: var(--uui-size-space-3);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface);
      box-shadow: var(--uui-shadow-depth-3);
      color: var(--uui-color-text);
    }

    input {
      box-sizing: border-box;
      inline-size: 100%;
      min-height: 36px;
      padding: var(--uui-size-space-2) var(--uui-size-space-3);
      border: 1px solid var(--jfb-bo-color-control-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface);
      color: inherit;
      font: inherit;
    }

    .heading {
      margin: var(--uui-size-space-2) 0 0;
      color: var(--jfb-bo-color-text-muted);
      font-size: var(--uui-type-small-size);
      font-weight: 600;
    }

    ul {
      display: grid;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .path {
      display: grid;
      gap: 2px;
      inline-size: 100%;
      padding: var(--uui-size-space-2);
      border: 0;
      border-radius: var(--uui-border-radius);
      background: none;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .path:hover {
      background: var(--uui-color-surface-emphasis);
    }

    code {
      color: var(--jfb-bo-color-text-muted);
      font-size: var(--uui-type-small-size);
      overflow-wrap: anywhere;
    }

    .note {
      margin: 0;
      color: var(--jfb-bo-color-text-muted);
      font-size: var(--uui-type-small-size);
    }

    @media (forced-colors: active) {
      .panel {
        border-color: CanvasText;
      }
    }
  `;
}

if (!customElements.get('jfb-merge-tag-picker')) customElements.define('jfb-merge-tag-picker', MergeTagPickerElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-merge-tag-picker': MergeTagPickerElement;
  }
}
