// One trigger's actions in the Actions view (ROADMAP 10.2a, IA 5.3, ADR-0188): a card for each, in the order they run:
// its move handle, the type's icon, its name as the button that opens its sidebar with a line of what it does and its
// rule's words under it, the form check's problems in it counted on that line; then its switch and "..." with Duplicate,
// Move to another trigger... and Delete. A card moves by its handle with the pointer, or with Alt+Up and Alt+Down, which
// says where it went (ADR-0016); the view makes each change a change of the draft. Read-only, a card still opens its
// sidebar, and says only whether it is on.
import { css, html, nothing, repeat, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { pressOf } from '../../styles/press';
import { dragFrom } from '../drag/pointer-drag';
import { controlStyles } from '../inspector/control.styles';
import { flagLine, type Flag } from '../settings/problem-lines';
import { DOT } from '../settings/settings-model';
import type { RunLine } from './run-words';

/** A card as the view gives it: what it shows of an action, in words. */
export interface ActionCard {
  readonly key: string;
  readonly name: string;
  readonly icon: string;
  /** What it does, in one line; empty where that would only repeat its name. */
  readonly summary: string;
  /** Its rule in words; null for an action without one. */
  readonly rule: string | null;
  readonly on: boolean;
  /** The form check's problems in it. */
  readonly flag?: Flag | undefined;
  /** The mark of an action whose texts lack the language shown (ADR-0228); none where they don't. */
  readonly untranslated?: string | undefined;
  /** What its type's test does, said before it runs; none for a kind the backoffice does not know. */
  readonly testHint?: string | undefined;
  /** Its last run in words (ADR-0195); none for an action that has not run. */
  readonly run?: RunLine | undefined;
}

/** What a card's "..." asks for. */
export type CardRequest = 'duplicate' | 'moveTrigger' | 'delete';

export class ActionListElement extends UmbLitElement {
  static override properties = {
    cards: { attribute: false },
    label: {},
    readOnly: { type: Boolean, attribute: 'read-only' },
    test: { attribute: false },
    details: { attribute: false },
    _testing: { state: true },
    full: { type: Boolean },
    open: { attribute: false },
    move: { attribute: false },
    toggle: { attribute: false },
    request: { attribute: false },
    _drag: { state: true },
    _said: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    pressOf('.more'),
    jfbVisuallyHidden,
    controlStyles,
    css`
      :host {
        display: block;
        container: jfb-action-list / inline-size;
      }

      .cards {
        display: grid;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .card {
        position: relative;
        display: grid;
        grid-template-columns: auto auto minmax(0, 1fr) auto;
        align-items: center;
        column-gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-2) var(--uui-size-space-3) var(--uui-size-space-2) var(--uui-size-space-2);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .card[data-dragged] {
        opacity: 0.5;
      }

      .card[data-line-before]::before,
      .card[data-line-after]::after {
        content: '';
        position: absolute;
        inset-inline: 0;
        border-block-start: 2px solid var(--jfb-bo-color-selected);
      }

      .card[data-line-before]::before {
        inset-block-start: calc(-1 * var(--uui-size-space-2) - 1px);
      }

      .card[data-line-after]::after {
        inset-block-end: calc(-1 * var(--uui-size-space-2) - 1px);
      }

      .handle,
      .no-handle {
        inline-size: 32px;
        block-size: 32px;
      }

      .handle {
        display: inline-grid;
        place-items: center;
        margin: 0;
        padding: 0;
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--jfb-bo-color-text-muted);
        cursor: grab;
        touch-action: none;
      }

      /* The handle shows on hover and on focus where a pointer can hover, at once: cards are moved many times; a
         finger has no hover, so on touch it always shows (IA 5.3). */
      @media (hover: hover) and (pointer: fine) {
        .card:not(:hover, :focus-within, [data-dragged]) .handle {
          opacity: 0;
        }
      }

      .kind {
        color: var(--jfb-bo-color-text-muted);
      }

      .open {
        display: grid;
        gap: 2px;
        min-block-size: 44px;
        padding: var(--uui-size-space-1) var(--uui-size-space-2);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: inherit;
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      @media (hover: hover) {
        .handle:hover,
        .open:hover,
        .more:hover {
          background: var(--uui-color-surface-emphasis);
          color: var(--uui-color-text);
        }
      }

      .handle:focus-visible,
      .open:focus-visible,
      .more:focus-visible,
      .menu button:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .name {
        font-weight: 600;
        overflow-wrap: anywhere;
      }

      .line {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        overflow-wrap: anywhere;
      }

      .line .untranslated {
        color: var(--jfb-bo-color-warning-text);
      }

      .flag {
        color: var(--jfb-bo-color-danger-text);
        font-weight: 600;
      }

      .flag[data-severity='warning'] {
        color: var(--jfb-bo-color-warning-text);
      }

      /* The rule: information, not a problem, in Umbraco's own neutral surface (IA 5.3's rule chip). */
      .rule {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        justify-self: start;
        max-inline-size: 100%;
        margin-block-start: 2px;
        padding: 0 var(--uui-size-space-2);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--jfb-bo-radius-round);
        background: var(--uui-color-surface-alt);
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        line-height: 1.5;
      }

      .rule > uui-icon {
        flex: none;
      }

      .rule > span {
        overflow-wrap: anywhere;
      }

      /* On the card's first row, though the last run under the name comes before it, as it is read. */
      .end {
        grid-row: 1;
        grid-column: 4;
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      /* Test sits on a card wide enough to keep its name readable beside it; a narrow card offers it in its "...", once
         either way (IA 5.3), and puts its switch and "..." on a row of their own under the text and the last run, so
         the name and its line keep the card's width on a phone. */
      @container jfb-action-list (width < 26rem) {
        .end .test {
          display: none;
        }

        .end {
          grid-row: 3;
          grid-column: 3 / -1;
          justify-self: end;
        }
      }

      @container jfb-action-list (width >= 26rem) {
        .menu .test-item {
          display: none;
        }
      }

      .state {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      /* The last run, on a line of its own under the name, its summary and its rule, as they are: muted, a failure in the
         danger colour and an uncertain run in the warning colour, with its Details (ADR-0195). It takes the width under
         the switch too, so a long line wraps less on a phone. */
      .run {
        grid-row: 2;
        grid-column: 3 / -1;
        margin: 0;
        padding: 0 var(--uui-size-space-2) var(--uui-size-space-1);
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        overflow-wrap: anywhere;
      }

      .run[data-tone='danger'] {
        color: var(--jfb-bo-color-danger-text);
      }

      .run[data-tone='warning'] {
        color: var(--jfb-bo-color-warning-text);
      }

      /* A taller target than its words, under the card's 44 px button, without moving the line. */
      .details {
        margin: -4px 0 -4px var(--uui-size-space-1);
        padding: 4px 0;
        border: 0;
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 600;
        text-decoration: underline;
        text-underline-offset: 2px;
        cursor: pointer;
      }

      @media (hover: hover) {
        .details:hover {
          color: var(--uui-color-interactive-emphasis);
        }
      }

      .details:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
        border-radius: 2px;
      }

      .more {
        display: inline-grid;
        place-items: center;
        inline-size: 32px;
        block-size: 32px;
        margin: 0;
        padding: 0;
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        cursor: pointer;
      }

      .menu {
        display: grid;
        min-inline-size: 220px;
        padding: var(--uui-size-space-1);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-3);
      }

      .menu button {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-3);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      @media (hover: hover) {
        .menu button:hover {
          background: var(--uui-color-surface-emphasis);
        }
      }

      @media (forced-colors: active) {
        .card[data-line-before]::before,
        .card[data-line-after]::after {
          border-color: Highlight;
        }
      }
    `,
  ];

  /** The trigger's cards, in the order they run. */
  declare cards: readonly ActionCard[];
  /** The list's name: the trigger's heading. */
  declare label: string;
  /** Without `manageActions`: no handles, switches or "..." (IA 5.3). */
  declare readOnly: boolean;
  /** Whether the form holds the most actions it can have, so "..." offers no Duplicate. */
  declare full: boolean;
  /** Opens an action's sidebar by its key. */
  declare open: (key: string) => void;
  /** Moves the card at `from` to `to` among the trigger's, as a change of the draft. */
  declare move: (from: number, to: number) => void;
  /** Switches an action on or off. */
  declare toggle: (key: string, on: boolean) => void;
  /** What a card's "..." asks for. */
  declare request: (key: string, request: CardRequest) => void;
  /**
   * Tests an action as the draft holds it (ADR-0190), calling `started` once it runs, after a yes where it asks first;
   * without it, no card offers Test.
   */
  declare test: ((key: string, started: () => void) => Promise<void>) | undefined;
  /** Opens the Details of an action's last run, a failed or uncertain one (ADR-0195). */
  declare details: ((key: string) => void) | undefined;
  declare private _testing: ReadonlySet<string>;
  declare private _drag: { readonly from: number; readonly to: number } | null;
  declare private _said: string;

  /** The action whose handle takes the focus once it is drawn in its new place. */
  #moved: string | null = null;

  /** The actions whose test was pressed and has not answered, its question included. */
  readonly #pending = new Set<string>();

  constructor() {
    super();
    this.cards = [];
    this.label = '';
    this.readOnly = false;
    this.full = false;
    this.open = () => undefined;
    this.move = () => undefined;
    this.toggle = () => undefined;
    this.request = () => undefined;
    this.test = undefined;
    this.details = undefined;
    this._testing = new Set();
    this._drag = null;
    this._said = '';
  }

  /** Puts the focus on an action's name; false where the list has no such action. */
  async focusAction(key: string): Promise<boolean> {
    await this.updateComplete;
    const target = this.shadowRoot?.querySelector<HTMLElement>(`[data-key="${key}"] .open`);
    target?.focus();
    target?.scrollIntoView?.({ block: 'nearest' });
    return Boolean(target);
  }

  /** The action whose card holds the focus, its menu included, or null. */
  focusedKey(): string | null {
    return this.shadowRoot?.activeElement?.closest<HTMLElement>('[data-key]')?.dataset['key'] ?? null;
  }

  /** Says a change in the list's live region. */
  announce(text: string) {
    // The same words twice in a row are said again: the live region hears a change.
    this._said = this._said === text ? `${text} ` : text;
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    if (this.#moved) {
      this.shadowRoot?.querySelector<HTMLElement>(`[data-key="${this.#moved}"] .handle`)?.focus();
      this.#moved = null;
    }
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActions_${key}`, ...args);

  // ---- Moving -------------------------------------------------------------------------------------------------------

  #keyMove(event: KeyboardEvent, at: number) {
    if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
    event.preventDefault();
    event.stopPropagation();
    const to = event.key === 'ArrowUp' ? at - 1 : at + 1;
    if (to < 0 || to >= this.cards.length) {
      this.announce(this.#term('edge'));
      return;
    }
    this.#move(at, to);
  }

  #move(from: number, to: number) {
    const card = this.cards[from];
    if (!card) return;
    this.#moved = card.key;
    this.move(from, to);
    this.announce(this.#term('moved', card.name, to + 1, this.cards.length));
  }

  // The place a pointer at y would put a card: before the first card whose middle lies below it, or after the last.
  #placeAt(y: number): number {
    const cards = [...(this.shadowRoot?.querySelectorAll<HTMLElement>('.card') ?? [])];
    const below = cards.findIndex((card) => {
      const box = card.getBoundingClientRect();
      return y < box.top + box.height / 2;
    });
    return below < 0 ? cards.length : below;
  }

  #drag(event: PointerEvent, from: number) {
    dragFrom(event, event.currentTarget as Element, {
      begin: () => (this._drag = { from, to: from }),
      over: (_x, y) => {
        const to = this.#placeAt(y);
        if (this._drag?.to !== to) this._drag = { from, to };
      },
      drop: (_x, y) => {
        const to = this.#placeAt(y);
        this._drag = null;
        // Before itself or the card after it is where it is.
        if (to === from || to === from + 1) return;
        this.#move(from, to > from ? to - 1 : to);
      },
      cancel: () => (this._drag = null),
    });
  }

  // ---- Test ---------------------------------------------------------------------------------------------------------

  // A card's test: once it runs, after a yes where it asks first, its button is busy until it answers and the list says
  // so; a second press waits for the first.
  async #test(key: string) {
    const test = this.test;
    if (!test || this.#pending.has(key)) return;
    this.#pending.add(key);
    try {
      await test(key, () => {
        this._testing = new Set([...this._testing, key]);
        const card = this.cards.find((one) => one.key === key);
        if (card) this.announce(this.#term('testingNamed', card.name));
      });
    } finally {
      this.#pending.delete(key);
      this._testing = new Set([...this._testing].filter((each) => each !== key));
    }
  }

  // ---- The menu -----------------------------------------------------------------------------------------------------

  #ask(key: string, request: CardRequest) {
    this.#closeMenu(key);
    this.request(key, request);
  }

  #closeMenu(key: string) {
    this.shadowRoot
      ?.querySelector<HTMLElement & { hidePopover?: () => void }>(`[data-key="${key}"] uui-popover-container`)
      ?.hidePopover?.();
  }

  #menu(card: ActionCard, at: number) {
    const id = `more-${at}`;
    return html`<button
        type="button"
        class="more"
        popovertarget=${id}
        aria-label=${this.#term('more', card.name)}
        title=${this.#term('more', card.name)}
      >
        <uui-symbol-more></uui-symbol-more>
      </button>
      <uui-popover-container
        id=${id}
        placement="bottom-end"
        @toggle=${(event: ToggleEvent) => {
          if (event.newState === 'open') (event.target as HTMLElement).querySelector<HTMLElement>('button')?.focus();
        }}
      >
        <div class="menu">
          ${
            this.test
              ? html`<button
                  type="button"
                  class="test-item"
                  title=${card.testHint ?? nothing}
                  @click=${() => {
                    this.#closeMenu(card.key);
                    void this.#test(card.key);
                  }}
                >
                  <uui-icon name="icon-play" aria-hidden="true"></uui-icon>${this.#term('test')}
                </button>`
              : nothing
          }
          ${
            this.full
              ? nothing
              : html`<button type="button" @click=${() => this.#ask(card.key, 'duplicate')}>
                  <uui-icon name="icon-documents" aria-hidden="true"></uui-icon>${this.#term('duplicate')}
                </button>`
          }
          <button type="button" @click=${() => this.#ask(card.key, 'moveTrigger')}>
            <uui-icon name="icon-split" aria-hidden="true"></uui-icon>${this.#term('moveTrigger')}
          </button>
          <button type="button" @click=${() => this.#ask(card.key, 'delete')}>
            <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>${this.#term('delete')}
          </button>
        </div>
      </uui-popover-container>`;
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  // The last run's line, with Details where it has them; named for the action, as several cards offer Details.
  #run(card: ActionCard, run: RunLine) {
    return html`<p class="run" data-tone=${run.tone}>
      ${run.text}${
        run.details && this.details
          ? html`<button
              type="button"
              class="details"
              aria-label=${this.#term('runDetailsOf', card.name)}
              @click=${() => this.details?.(card.key)}
            >
              ${this.#term('runDetails')}
            </button>`
          : nothing
      }
    </p>`;
  }

  override render() {
    const cards = this.cards;
    const drag = this._drag;
    const count = cards.length;
    if (count === 0) return nothing;
    return html`<ol class="cards" aria-label=${this.label}>
        ${repeat(
          cards,
          // Each card stays its action's as the list changes, the focus on it too.
          (card) => card.key,
          (card, at) =>
            html`<li
              class="card"
              data-key=${card.key}
              ?data-line-before=${drag !== null && drag.to === at && drag.to !== drag.from && drag.to !== drag.from + 1}
              ?data-line-after=${drag !== null && at === count - 1 && drag.to === count && drag.from !== count - 1}
              ?data-dragged=${drag?.from === at}
            >
              ${
                this.readOnly
                  ? html`<span class="no-handle"></span>`
                  : html`<button
                      type="button"
                      class="handle"
                      aria-label=${this.#term('move', card.name)}
                      @pointerdown=${(event: PointerEvent) => this.#drag(event, at)}
                      @keydown=${(event: KeyboardEvent) => this.#keyMove(event, at)}
                    >
                      <uui-icon name="icon-grip" aria-hidden="true"></uui-icon>
                    </button>`
              }
              <uui-icon class="kind" name=${card.icon} aria-hidden="true"></uui-icon>
              <button type="button" class="open" @click=${() => this.open(card.key)}>
                <span class="name">${card.name}</span>
                ${
                  card.summary || card.flag?.count || card.untranslated
                    ? html`<span class="line"
                        >${flagLine(
                          card.flag,
                          (number) => this.#term('problems', number),
                          card.summary || card.untranslated ? DOT : undefined,
                        )}${
                          card.untranslated
                            ? html`<span class="untranslated">${card.untranslated}</span
                                >${card.summary ? ` ${DOT} ` : nothing}`
                            : nothing
                        }${card.summary}</span
                      >`
                    : nothing
                }
                ${
                  card.rule
                    ? html`<span class="rule"
                        ><uui-icon name="icon-split" aria-hidden="true"></uui-icon><span>${card.rule}</span></span
                      >`
                    : nothing
                }
              </button>
              ${card.run ? this.#run(card, card.run) : nothing}
              <div class="end">
                ${
                  this.readOnly
                    ? card.on
                      ? nothing
                      : html`<span class="state">${this.#term('off')}</span>`
                    : html`${
                          this.test
                            ? html`<uui-button
                                class="test"
                                look="outline"
                                compact
                                label=${this.#term('testNamed', card.name)}
                                title=${card.testHint ?? nothing}
                                .state=${this._testing.has(card.key) ? 'waiting' : undefined}
                                @click=${() => void this.#test(card.key)}
                                >${this.#term('test')}</uui-button
                              >`
                            : nothing
                        }
                        <input
                          type="checkbox"
                          role="switch"
                          aria-label=${this.#term('use', card.name)}
                          .checked=${card.on}
                          @change=${(event: Event) => this.toggle(card.key, (event.target as HTMLInputElement).checked)}
                        />
                        ${this.#menu(card, at)}`
                }
              </div>
            </li>`,
        )}
      </ol>
      <div class="visually-hidden" role="status" aria-live="polite">${this._said}</div>`;
  }
}

if (!customElements.get('jfb-action-list')) customElements.define('jfb-action-list', ActionListElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-action-list': ActionListElement;
  }
}
