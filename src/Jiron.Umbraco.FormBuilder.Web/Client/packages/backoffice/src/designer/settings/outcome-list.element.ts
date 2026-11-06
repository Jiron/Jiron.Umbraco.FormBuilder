// After sending's list of outcomes (ROADMAP 10.1b, IA 5.2, ADR-0185): each row the outcome's move handle, its name as
// the button that opens its sidebar, and a line that says when visitors get it, the form check's problems in it counted
// on that line; under the list the default message where some visitors get it, then Add outcome. A row moves by its
// handle with the pointer, or with Alt+Up and Alt+Down, which says where it went (ADR-0016); the view makes each move a
// change of the draft. The form check's problems in the list itself show above Add outcome, which they describe; at the
// most outcomes a form can have, a line says so in its place. The names of the pages the redirects lead to are read
// from Umbraco once, in all their languages, and shown in the language the view shows.
import { css, html, nothing, repeat, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UmbDocumentItemRepository } from '@umbraco-cms/backoffice/document';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import type { CheckSeverity } from '../check/form-check';
import { dragFrom } from '../drag/pointer-drag';
import type { RuleWordsContext } from '../rules/rule-words';
import { ruleWords } from '../rules/rule-words';
import {
  kindOf,
  leavesDefault,
  outcomeName,
  reachOf,
  targetOf,
  useRuleOf,
  type DesignOutcome,
  type Reach,
} from './outcome-model';
import { choiceStyles } from './choice.styles';
import { described, flagLine, whys, type Flag, type Shown } from './problem-lines';
import { DOT } from './settings-model';

/** A page's name in one of its languages; null for a page that has one name for all. */
interface PageName {
  readonly culture: string | null;
  readonly name: string;
}

export class OutcomeListElement extends UmbLitElement {
  static override properties = {
    outcomes: { attribute: false },
    culture: {},
    defaultMessage: {},
    words: { attribute: false },
    flags: { attribute: false },
    problems: { attribute: false },
    weight: { attribute: false },
    limit: {},
    open: { attribute: false },
    move: { attribute: false },
    _pages: { state: true },
    _drag: { state: true },
    _said: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    choiceStyles,
    css`
      :host {
        display: grid;
        gap: var(--uui-size-space-4);
      }

      .outcomes {
        display: grid;
        gap: var(--uui-size-space-2);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .outcome-row {
        position: relative;
        display: grid;
        grid-template-columns: auto auto minmax(0, 1fr);
        align-items: center;
        column-gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-2);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .outcome-row[data-dragged] {
        opacity: 0.5;
      }

      .outcome-row[data-line-before]::before,
      .outcome-row[data-line-after]::after {
        content: '';
        position: absolute;
        inset-inline: 0;
        border-block-start: 2px solid var(--jfb-bo-color-selected);
      }

      .outcome-row[data-line-before]::before {
        inset-block-start: calc(-1 * var(--uui-size-space-1) - 2px);
      }

      .outcome-row[data-line-after]::after {
        inset-block-end: calc(-1 * var(--uui-size-space-1) - 2px);
      }

      .row-handle {
        display: inline-grid;
        place-items: center;
        inline-size: 32px;
        block-size: 32px;
        margin: 0;
        padding: 0;
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--jfb-bo-color-text-muted);
        cursor: grab;
        touch-action: none;
      }

      /* The handle shows on hover and on focus where a pointer can hover, at once: rows are moved many times; a
         finger has no hover, so on touch it always shows (IA 5.2). */
      @media (hover: hover) and (pointer: fine) {
        .outcome-row:not(:hover, :focus-within, [data-dragged]) .row-handle {
          opacity: 0;
        }
      }

      .kind {
        color: var(--jfb-bo-color-text-muted);
      }

      .row-open {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        align-items: center;
        column-gap: var(--uui-size-space-3);
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
        .row-handle:hover,
        .row-open:hover {
          background: var(--uui-color-surface-emphasis);
          color: var(--uui-color-text);
        }
      }

      .row-handle:focus-visible,
      .row-open:focus-visible,
      .add:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .row-name {
        font-weight: 600;
        overflow-wrap: anywhere;
      }

      .row-line {
        display: block;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        overflow-wrap: anywhere;
      }

      .row-line[data-reach='never'] {
        color: var(--jfb-bo-color-warning-text);
      }

      .flag {
        color: var(--jfb-bo-color-danger-text);
        font-weight: 600;
      }

      .flag[data-severity='warning'] {
        color: var(--jfb-bo-color-warning-text);
      }

      .default {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .add {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        justify-self: start;
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-2);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 600;
        cursor: pointer;
      }

      @media (hover: hover) {
        .add:hover {
          color: var(--uui-color-interactive-emphasis);
        }
      }

      @media (forced-colors: active) {
        .outcome-row[data-line-before]::before,
        .outcome-row[data-line-after]::after {
          border-color: Highlight;
        }
      }
    `,
  ];

  /** The outcomes in their order. */
  declare outcomes: readonly DesignOutcome[];
  /** The language the view shows. */
  declare culture: string;
  /** The message visitors get where no outcome applies, in the language shown. */
  declare defaultMessage: string;
  /** Says a rule in words; none says only when the outcome applies. */
  declare words: RuleWordsContext | null;
  /** The form check's problems in each outcome, by its key. */
  declare flags: ReadonlyMap<string, Flag>;
  /** The form check's problems in the list itself, such as too many outcomes. */
  declare problems: readonly Shown[];
  /** A problem's weight in words, said before a warning or a suggestion. */
  declare weight: (severity: CheckSeverity) => string;
  /** The most outcomes a form can have; 0 for no limit. */
  declare limit: number;
  /** Opens an outcome's sidebar by its key; null for a new one. */
  declare open: (key: string | null) => void;
  /** Moves the outcome at `from` to `to`, as a change of the draft. */
  declare move: (from: number, to: number) => void;
  declare private _pages: ReadonlyMap<string, readonly PageName[]>;
  declare private _drag: { readonly from: number; readonly to: number } | null;
  declare private _said: string;

  /** The pages whose names are asked for, so each is asked once. */
  readonly #asked = new Set<string>();
  /** The outcome whose handle takes the focus once it is drawn in its new place. */
  #moved: string | null = null;

  constructor() {
    super();
    this.outcomes = [];
    this.culture = '';
    this.defaultMessage = '';
    this.words = null;
    this.flags = new Map();
    this.problems = [];
    this.weight = () => '';
    this.limit = 0;
    this.open = () => undefined;
    this.move = () => undefined;
    this._pages = new Map();
    this._drag = null;
    this._said = '';
  }

  /**
   * Puts the focus on an outcome's name, or on Add outcome where it is gone, or on the last outcome where the list has
   * the most it can; true where there was one to focus.
   */
  async focusOutcome(key: string | null): Promise<boolean> {
    await this.updateComplete;
    const root = this.shadowRoot;
    const target =
      (key ? root?.querySelector<HTMLElement>(`[data-key="${key}"] .row-open`) : null) ??
      root?.querySelector<HTMLElement>('.add') ??
      [...(root?.querySelectorAll<HTMLElement>('.row-open') ?? [])].at(-1);
    target?.focus();
    return target !== undefined && target !== null;
  }

  /** Says a change in the list's live region. */
  announce(text: string) {
    // The same words twice in a row are said again: the live region hears a change.
    this._said = this._said === text ? `${text} ` : text;
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    if (changed.has('outcomes')) void this.#readPages();
    if (this.#moved) {
      this.shadowRoot?.querySelector<HTMLElement>(`[data-key="${this.#moved}"] .row-handle`)?.focus();
      this.#moved = null;
    }
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbSettings_${key}`, ...args);

  // The names of the pages the redirects lead to, each in all its languages, asked for once.
  async #readPages() {
    const keys = this.outcomes
      .map((outcome) => targetOf(outcome))
      .flatMap((target) => (target.kind === 'page' && !this.#asked.has(target.key) ? [target.key] : []));
    if (keys.length === 0) return;
    for (const key of keys) this.#asked.add(key);
    const { data } = await new UmbDocumentItemRepository(this).requestItems(keys);
    if (!data?.length) return;
    const pages = new Map(this._pages);
    for (const item of data)
      pages.set(
        item.unique,
        item.variants.flatMap((one) => (one.name ? [{ culture: one.culture ?? null, name: one.name }] : [])),
      );
    this._pages = pages;
  }

  // The pages' names in the language shown where the page has it, else its name for all, else its first.
  #pageNames(): ReadonlyMap<string, string> {
    const culture = this.culture.toLowerCase();
    const names = new Map<string, string>();
    for (const [key, variants] of this._pages) {
      const variant =
        variants.find((one) => one.culture?.toLowerCase() === culture) ??
        variants.find((one) => one.culture === null) ??
        variants[0];
      if (variant) names.set(key, variant.name);
    }
    return names;
  }

  #nameOf(outcome: DesignOutcome, pages = this.#pageNames()): string {
    return outcomeName(outcome, this.culture, this.defaultMessage, pages, this.#term);
  }

  #lineOf(outcome: DesignOutcome, reach: Reach): string {
    const rule = useRuleOf(outcome);
    if (reach === 'ruled' && rule && this.words) return ruleWords(this.words, rule);
    return this.#term(`outcome_reach_${reach}`);
  }

  // ---- Moving -------------------------------------------------------------------------------------------------------

  #keyMove(event: KeyboardEvent, at: number) {
    if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
    event.preventDefault();
    event.stopPropagation();
    const to = event.key === 'ArrowUp' ? at - 1 : at + 1;
    if (to < 0 || to >= this.outcomes.length) {
      this.announce(this.#term('outcome_edge'));
      return;
    }
    this.#move(at, to);
  }

  #move(from: number, to: number) {
    const outcome = this.outcomes[from];
    if (!outcome) return;
    this.#moved = outcome.key;
    this.move(from, to);
    this.announce(this.#term('outcome_moved', this.#nameOf(outcome), to + 1, this.outcomes.length));
  }

  // The place a pointer at y would put a row: before the first row whose middle lies below it, or after the last.
  #placeAt(y: number): number {
    const rows = [...(this.shadowRoot?.querySelectorAll<HTMLElement>('.outcome-row') ?? [])];
    const below = rows.findIndex((row) => {
      const box = row.getBoundingClientRect();
      return y < box.top + box.height / 2;
    });
    return below < 0 ? rows.length : below;
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
        // Before itself or the row after it is where it is.
        if (to === from || to === from + 1) return;
        this.#move(from, to > from ? to - 1 : to);
      },
      cancel: () => (this._drag = null),
    });
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  override render() {
    const outcomes = this.outcomes;
    const reach = reachOf(outcomes);
    const drag = this._drag;
    const count = outcomes.length;
    const pages = this.#pageNames();
    const full = this.limit > 0 && count >= this.limit;
    return html`
      ${
        count > 0
          ? html`<ol class="outcomes" aria-label=${this.#term('outcome_list')}>
              ${repeat(
                outcomes,
                // Each row stays its outcome's as the list changes, the focus on it too.
                (outcome) => outcome.key,
                (outcome, at) => {
                  const name = this.#nameOf(outcome, pages);
                  const shown = reach[at] ?? 'ruled';
                  return html`<li
                    class="outcome-row"
                    data-key=${outcome.key}
                    ?data-line-before=${drag !== null && drag.to === at && drag.to !== drag.from && drag.to !== drag.from + 1}
                    ?data-line-after=${drag !== null && at === count - 1 && drag.to === count && drag.from !== count - 1}
                    ?data-dragged=${drag?.from === at}
                  >
                    <button
                      type="button"
                      class="row-handle"
                      aria-label=${this.#term('outcome_move', name)}
                      @pointerdown=${(event: PointerEvent) => this.#drag(event, at)}
                      @keydown=${(event: KeyboardEvent) => this.#keyMove(event, at)}
                    >
                      <uui-icon name="icon-grip" aria-hidden="true"></uui-icon>
                    </button>
                    <uui-icon
                      class="kind"
                      name=${kindOf(outcome) === 'redirect' ? 'icon-out' : 'icon-chat'}
                      aria-hidden="true"
                    ></uui-icon>
                    <button type="button" class="row-open" @click=${() => this.open(outcome.key)}>
                      <span>
                        <span class="row-name">${name}</span>
                        <span class="row-line" data-reach=${shown}
                          >${flagLine(
                            this.flags.get(outcome.key),
                            (count) => this.#term('problems', count),
                            DOT,
                          )}${this.#lineOf(outcome, shown)}</span
                        >
                      </span>
                      <uui-icon name="icon-navigation-right" aria-hidden="true"></uui-icon>
                    </button>
                  </li>`;
                },
              )}
            </ol>`
          : nothing
      }
      ${
        leavesDefault(outcomes)
          ? html`<p class="default">
              ${this.#term(count > 0 ? 'outcome_defaultOtherwise' : 'outcome_defaultOnly', this.defaultMessage)}
            </p>`
          : nothing
      }
      ${whys('outcomes', this.problems, this.weight)}
      ${
        full
          ? html`<p class="default">${this.#term('outcome_limit', this.limit)}</p>`
          : html`<button
              type="button"
              class="add"
              aria-describedby=${described('outcomes', false, this.problems) ?? nothing}
              @click=${() => this.open(null)}
            >
              <uui-icon name="icon-add" aria-hidden="true"></uui-icon>
              <span>${this.#term('outcome_addOutcome')}</span>
            </button>`
      }
      <div class="visually-hidden" role="status" aria-live="polite">${this._said}</div>
    `;
  }
}

if (!customElements.get('jfb-outcome-list')) customElements.define('jfb-outcome-list', OutcomeListElement);
