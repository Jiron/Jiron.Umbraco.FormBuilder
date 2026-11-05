// The palette (ROADMAP 9.3b, docs/ux/information-architecture.md section 5.1): search, Recently used, Layout (Group, New
// page), the field types in their groups, and Paste questions... at the end. A pick goes to the Design view, which puts
// it at the open insert point, after the selected element or at the end of the page shown. Beside the canvas it can
// fold to a rail; on a narrow window it opens over the canvas with a heading and Close, and Esc closes it. Beside the
// canvas, a type can also be dragged onto the page, where it lands as a field (ADR-0169).
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { FieldGroupResponseModel, FieldTypeResponseModel } from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';

/** What the palette asks for. */
export type PalettePick =
  | { readonly kind: 'field'; readonly type: string }
  | {
      readonly kind: 'drag';
      readonly type: string;
      readonly label: string;
      readonly event: PointerEvent;
      readonly handle: Element;
    }
  | { readonly kind: 'group' }
  | { readonly kind: 'page' }
  | { readonly kind: 'paste' }
  | { readonly kind: 'unavailable'; readonly reason: string }
  | { readonly kind: 'fold' }
  | { readonly kind: 'unfold' }
  | { readonly kind: 'close' };

/** The event a pick travels in. */
export const PALETTE_PICK = 'jfb-palette-pick';

/** How the palette shows: beside the canvas, folded to a rail, or over the canvas. */
export type PaletteMode = 'docked' | 'rail' | 'overlay';

/** How many recently used types the palette keeps. */
export const MOST_RECENT = 3;

const RECENT_KEY = 'jfb.designer.recent';

/** The types picked last, most recent first, as this browser keeps them; none when it keeps nothing. */
export function readRecent(): string[] {
  try {
    const kept = JSON.parse(localStorage.getItem(RECENT_KEY) ?? '[]') as unknown;
    return Array.isArray(kept)
      ? kept.filter((type): type is string => typeof type === 'string').slice(0, MOST_RECENT)
      : [];
  } catch {
    return [];
  }
}

/** Keeps a type as the most recently used; the list after it. */
export function keepRecent(type: string): string[] {
  const recent = [type, ...readRecent().filter((each) => each !== type)].slice(0, MOST_RECENT);
  try {
    localStorage.setItem(RECENT_KEY, JSON.stringify(recent));
  } catch {
    // A browser that keeps nothing only forgets the list.
  }
  return recent;
}

/** A text as search compares it: lower case, accents dropped. */
export const folded = (text: string) =>
  text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[^\p{L}\p{N} ]/gu, '');

export class PaletteElement extends UmbLitElement {
  static override properties = {
    types: { attribute: false },
    groups: { attribute: false },
    recent: { attribute: false },
    mode: { reflect: true },
    _query: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: flex;
        flex-direction: column;
        min-height: 0;
        border-inline-end: 1px solid var(--uui-color-border);
        background: var(--uui-color-surface);
      }

      .head {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-4);
        border-bottom: 1px solid var(--uui-color-border);
      }

      :host([mode='overlay']) .head {
        flex-wrap: wrap;
      }

      h2 {
        flex: 1 1 auto;
        margin: 0;
        font-size: var(--jfb-bo-type-title);
      }

      uui-input {
        flex: 1 1 auto;
        min-width: 0;
      }

      :host([mode='overlay']) uui-input {
        order: 1;
        flex-basis: 100%;
      }

      .scroll {
        flex: 1 1 auto;
        min-height: 0;
        overflow: auto;
      }

      section {
        padding: var(--uui-size-space-4) var(--uui-size-space-4) var(--uui-size-space-3);
        border-bottom: 1px solid var(--uui-color-border);
      }

      h3 {
        margin: 0 0 var(--uui-size-space-2);
        font-size: var(--uui-type-default-size);
      }

      ul {
        display: grid;
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
      }

      @media (hover: hover) {
        .item:hover {
          background: var(--uui-color-surface-emphasis);
        }
      }

      /* A type dragged sideways onto the page; the list still scrolls up and down under a finger. */
      .item[data-drags] {
        touch-action: pan-y;
        user-select: none;
      }

      .item {
        transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
      }

      .item:active {
        transform: scale(0.98);
      }

      .item:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .item[aria-disabled='true'] {
        color: var(--jfb-bo-color-text-muted);
        cursor: default;
      }

      .item[aria-disabled='true']:active {
        transform: none;
      }

      .item uui-icon {
        flex: none;
      }

      .paste {
        margin: var(--uui-size-space-4);
      }

      .none {
        margin: 0;
        padding: var(--uui-size-space-5) var(--uui-size-space-4);
        color: var(--jfb-bo-color-text-muted);
      }

      :host([mode='rail']) {
        align-items: center;
        gap: var(--uui-size-space-2);
        padding-block: var(--uui-size-space-4);
      }

      @media (prefers-reduced-motion: reduce) {
        .item:active {
          transform: none;
        }
      }
    `,
  ];

  declare types: readonly FieldTypeResponseModel[];
  declare groups: readonly FieldGroupResponseModel[];
  declare recent: readonly string[];
  declare mode: PaletteMode;
  declare private _query: string;

  constructor() {
    super();
    this.types = [];
    this.groups = [];
    this.recent = [];
    this.mode = 'docked';
    this._query = '';
  }

  /** Moves the focus to the search, as the palette opened for a place does. */
  focusSearch() {
    void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('uui-input')?.focus());
  }

  /** Moves the focus to the rail's button that opens the palette, as folding it does: the button pressed is gone. */
  focusUnfold() {
    void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('uui-button')?.focus());
  }

  #pick(pick: PalettePick) {
    this.dispatchEvent(new CustomEvent<PalettePick>(PALETTE_PICK, { detail: pick, bubbles: true, composed: true }));
  }

  #name(type: FieldTypeResponseModel): string {
    return this.localize.string(type.name);
  }

  #item(icon: string, label: string, pick: PalettePick, unavailable?: string): TemplateResult {
    // Over the canvas, the palette hides the page a type would be dropped on: there a click adds it.
    const type = pick.kind === 'field' && !unavailable && this.mode !== 'overlay' ? pick.type : null;
    return html`<li>
      <button
        type="button"
        class="item"
        aria-disabled=${unavailable ? 'true' : 'false'}
        title=${unavailable ?? nothing}
        ?data-drags=${type !== null}
        @pointerdown=${(event: PointerEvent) => {
          if (type) this.#pick({ kind: 'drag', type, label, event, handle: event.currentTarget as Element });
        }}
        @click=${() => this.#pick(unavailable ? { kind: 'unavailable', reason: unavailable } : pick)}
      >
        <uui-icon name=${icon} aria-hidden="true"></uui-icon><span>${label}</span>
        ${unavailable ? html`<span class="visually-hidden">${unavailable}</span>` : nothing}
      </button>
    </li>`;
  }

  #type(type: FieldTypeResponseModel): TemplateResult {
    const reason = type.availability.isAvailable
      ? undefined
      : this.localize.string(type.availability.reason ?? '#jfbDesigner_unavailable');
    return this.#item(type.icon, this.#name(type), { kind: 'field', type: type.alias }, reason);
  }

  #section(heading: string, items: readonly TemplateResult[]): TemplateResult | typeof nothing {
    if (items.length === 0) return nothing;
    return html`<section>
      <h3>${heading}</h3>
      <ul>
        ${items}
      </ul>
    </section>`;
  }

  #layout(): TemplateResult[] {
    return [
      this.#item('icon-layout', this.localize.term('jfbDesigner_paletteGroup'), { kind: 'group' }),
      this.#item('icon-split', this.localize.term('jfbDesigner_paletteNewPage'), { kind: 'page' }),
    ];
  }

  override render() {
    if (this.mode === 'rail')
      return html`<uui-button
          compact
          look="secondary"
          label=${this.localize.term('jfbDesigner_paletteUnfold')}
          @click=${() => this.#pick({ kind: 'unfold' })}
        >
          <uui-icon name="icon-navigation-right"></uui-icon>
        </uui-button>
        <uui-button
          compact
          label=${this.localize.term('jfbDesigner_paletteSearch')}
          @click=${() => this.#pick({ kind: 'unfold' })}
        >
          <uui-icon name="icon-search"></uui-icon>
        </uui-button>`;

    const query = folded(this._query.trim());
    const matches = (label: string, alias = '') => !query || folded(label).includes(query) || alias.includes(query);
    const byAlias = new Map(this.types.map((type) => [type.alias, type]));
    const recent = this.recent.flatMap((alias) => byAlias.get(alias) ?? []);
    const layout = this.#layout().filter((_, at) =>
      matches(this.localize.term(at === 0 ? 'jfbDesigner_paletteGroup' : 'jfbDesigner_paletteNewPage')),
    );
    const groups = this.groups.map((group) => ({
      name: this.localize.string(group.name),
      types: this.types.filter((type) => type.paletteGroup === group.alias && matches(this.#name(type), type.alias)),
    }));
    const found = layout.length + groups.reduce((sum, group) => sum + group.types.length, 0);
    const heading = this.localize.term('jfbDesigner_paletteHeading');

    return html`<div class="head">
        ${this.mode === 'overlay' ? html`<h2>${heading}</h2>` : html`<h2 class="visually-hidden">${heading}</h2>`}
        <uui-input
          type="search"
          label=${this.localize.term('jfbDesigner_paletteSearch')}
          placeholder=${this.localize.term('jfbDesigner_paletteSearch')}
          .value=${this._query}
          @input=${(event: Event) => (this._query = (event.target as HTMLInputElement).value)}
        >
          <uui-icon name="icon-search" slot="prepend" aria-hidden="true"></uui-icon>
        </uui-input>
        ${
          this.mode === 'overlay'
            ? html`<uui-button
                compact
                label=${this.localize.term('general_close')}
                @click=${() => this.#pick({ kind: 'close' })}
              >
                <uui-icon name="icon-wrong"></uui-icon>
              </uui-button>`
            : html`<uui-button
                compact
                label=${this.localize.term('jfbDesigner_paletteFold')}
                @click=${() => this.#pick({ kind: 'fold' })}
              >
                <uui-icon name="icon-navigation-left"></uui-icon>
              </uui-button>`
        }
      </div>
      <div class="scroll">
        ${
          query
            ? nothing
            : this.#section(
                this.localize.term('jfbDesigner_paletteRecent'),
                recent.map((type) => this.#type(type)),
              )
        }
        ${this.#section(this.localize.term('jfbDesigner_paletteLayout'), layout)}
        ${groups.map((group) =>
          this.#section(
            group.name,
            group.types.map((type) => this.#type(type)),
          ),
        )}
        ${found === 0 ? html`<p class="none" role="status">${this.localize.term('jfbDesigner_paletteNone', this._query.trim())}</p>` : nothing}
        <uui-button
          class="paste"
          look="outline"
          label=${this.localize.term('jfbDesigner_pasteQuestions')}
          @click=${() => this.#pick({ kind: 'paste' })}
        ></uui-button>
      </div>`;
  }
}

if (!customElements.get('jfb-designer-palette')) customElements.define('jfb-designer-palette', PaletteElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-designer-palette': PaletteElement;
  }
}
