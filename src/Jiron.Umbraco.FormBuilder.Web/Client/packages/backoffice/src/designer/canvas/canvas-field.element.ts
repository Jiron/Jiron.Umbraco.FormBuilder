// One field on the canvas (ROADMAP 9.3b, ADR-0168): the field as visitors see it, written by the headless element's
// views into this element, which is the field's wrapper itself, so the theme's rules for a group's fields hold. What
// the views write takes no input (inert); on top of it lie the designer's own controls: a button that selects the
// field, the insert point below it and, while it is selected, its Duplicate and Delete. A field a visitor doesn't see
// (a hidden field, a heading without text) shows a quiet note in its place, so it can still be found and selected. A
// repeatable group shows its first row with each of its fields as a canvas field of its own, so they can be chosen.
// Its handle, "Move Email address", drags it and, with Alt+Up and Alt+Down, moves it (ADR-0016, ADR-0169); Move to...
// in its bar opens the dialog of places, and a move the form refuses says why at the field. The bar's "..." starts a new
// page after the field and copies its alias (ADR-0170); a field without a text in the language shown carries a chip.
import {
  html,
  LitElement,
  nothing,
  render,
  type PropertyValues,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import type { FieldViewModel, ThemeClassesModel } from '@jfb/element/answer';
import { writeField } from '@jfb/element/views';
import { asEnhanced } from '../preview/field-models';

/** What a field asks the canvas to do; the canvas passes each on as a designer request. */
export type FieldRequest =
  | { readonly kind: 'select'; readonly key: string }
  | { readonly kind: 'insertAfter'; readonly key: string }
  | { readonly kind: 'insertFirst'; readonly container: string }
  | { readonly kind: 'duplicate'; readonly key: string }
  | { readonly kind: 'delete'; readonly key: string }
  | { readonly kind: 'editLabel'; readonly key: string }
  | { readonly kind: 'label'; readonly key: string; readonly text: string | null }
  | { readonly kind: 'moveTo'; readonly key: string }
  | { readonly kind: 'changeType'; readonly key: string }
  | { readonly kind: 'newPageAfter'; readonly key: string }
  | { readonly kind: 'copyAlias'; readonly key: string }
  | { readonly kind: 'drag'; readonly key: string; readonly event: PointerEvent; readonly handle: Element };

/** The event a field's request travels in, up to the canvas. */
export const FIELD_REQUEST = 'jfb-field-request';

/** The words a field's controls say, in the backoffice's language. */
export interface FieldWords {
  /** The field's name for screen readers: "Email address, email field". */
  readonly name: string;
  /** What an insert point says: "Add a field here". */
  readonly insertHere: string;
  /** Its name for screen readers: "Add a field here, after Email address". */
  readonly insertAfter: string;
  /** The handle's name: "Move Email address". */
  readonly move: string;
  readonly moveTo: string;
  /** Move to...'s name for screen readers, its visible text first: "Move to..., Email address". */
  readonly moveToNamed: string;
  readonly duplicate: string;
  /** Duplicate's name for screen readers: "Duplicate Email address". */
  readonly duplicateNamed: string;
  readonly delete: string;
  readonly deleteNamed: string;
  /** The "..."'s name: "More actions". */
  readonly more: string;
  /** Change type..., for a field whose type may become another (ADR-0059). */
  readonly changeType?: string;
  readonly newPageAfter: string;
  /** Copy alias, for a field with an alias. */
  readonly copyAlias?: string;
  /** What the label's box says while it is edited in place. */
  readonly labelBox: string;
  /** The note in the place of a field visitors don't see: "Hidden field: visitors don't see it". */
  readonly unseen: string;
  /** A repeatable group's first insert point's name: "Add a field here, before Guest name". */
  readonly insertFirst?: string;
}

/** Everything a canvas field is drawn with. */
export interface FieldState {
  readonly key: string;
  /** The view model the views write; null for a field a first render leaves out. */
  readonly model: FieldViewModel | null;
  readonly words: FieldWords;
  readonly selected: boolean;
  /** Whether its label is being edited in place. */
  readonly editing: boolean;
  /** The text an edit in place starts from. */
  readonly labelText: string;
  /** Whether the palette is open for the insert point below it, which then stays in sight. */
  readonly insertActive: boolean;
  /** Whether the field after it is selected, whose bar lies over the insert point below it. */
  readonly beforeSelected?: boolean;
  /** A repeatable group's fields, drawn in its first row. */
  readonly children?: readonly FieldState[];
  /** Whether the palette is open for the place before a repeatable group's first field. */
  readonly firstActive?: boolean;
  /** Why the form refused its last move, shown at the field until the next change. */
  readonly refusal?: string | null;
  /** The chip on a field without a text in the language shown: "No German text yet". */
  readonly untranslated?: string | null;
}

/**
 * The "..." of a selected element's bar and its menu. The menu takes the focus as it opens, Escape closes it and gives
 * the focus back to the "...", and a choice closes it first.
 */
export function moreMenu(
  id: string,
  name: string,
  items: readonly { readonly label: string; readonly act: () => void }[],
): TemplateResult {
  return html`<button type="button" class="bo-more-element" popovertarget=${id} aria-label=${name}>
      <uui-symbol-more></uui-symbol-more>
    </button>
    <uui-popover-container
      id=${id}
      placement="bottom-end"
      @toggle=${(event: ToggleEvent) => {
        if (event.newState === 'open') (event.target as HTMLElement).querySelector<HTMLElement>('button')?.focus();
      }}
      @keydown=${(event: KeyboardEvent) => {
        if (event.key !== 'Escape') return;
        // The menu closes, and the Design view's Escape, which clears the selection, waits for the next one.
        event.stopPropagation();
        event.preventDefault();
        const menu = event.currentTarget as HTMLElement & { hidePopover?: () => void };
        menu.hidePopover?.();
        (menu.getRootNode() as ParentNode).querySelector<HTMLElement>(`[popovertarget="${id}"]`)?.focus();
      }}
    >
      <div class="bo-menu">
        ${items.map(
          (item) =>
            html`<button
              type="button"
              @click=${(event: Event) => {
                (
                  (event.currentTarget as HTMLElement).closest('uui-popover-container') as
                    (HTMLElement & { hidePopover?: () => void }) | null
                )?.hidePopover?.();
                item.act();
              }}
            >
              <span>${item.label}</span>
            </button>`,
        )}
      </div>
    </uui-popover-container>`;
}

/** The element down a path of children, each step the first child that matches its selector. */
export function childAt(parent: Element | null, ...steps: string[]): Element | null {
  let at = parent;
  for (const step of steps) at = at ? ([...at.children].find((child) => child.matches(step)) ?? null) : null;
  return at;
}

/** Where a label's text can be edited in place: the label or legend, one box's own label, a heading's text. */
export function labelTarget(field: Element, model: FieldViewModel | null): Element | null {
  if (!model) return null;
  switch (model.labelElement) {
    case 'label':
      return childAt(field, 'label.jfb-label');
    case 'legend':
      return childAt(field, 'fieldset', 'legend');
    case 'choice':
      return childAt(field, '.jfb-choice', 'label');
    default:
      return model.type === 'heading' ? childAt(field, '.jfb-heading') : null;
  }
}

/** Whether what the views wrote shows a visitor anything: an element of it not written hidden. */
export function showsSomething(written: Element): boolean {
  return [...written.children].some(
    (child) => !child.hasAttribute('hidden') && !(child instanceof HTMLTemplateElement),
  );
}

export class CanvasFieldElement extends LitElement {
  static override properties = {
    state: { attribute: false },
    classes: { attribute: false },
  };

  declare state: FieldState | undefined;
  declare classes: ThemeClassesModel | undefined;

  /** What the field was last written from, so that a render that changes nothing leaves the markup alone. */
  #written: { model: FieldViewModel | null; editing: boolean; classes: ThemeClassesModel | undefined } | undefined;
  /** Whether the label is being edited in place, which hides the button that selects the field. */
  #editingIn = false;
  readonly #chrome = document.createElement('div');
  /** A repeatable group's fields, kept from one writing to the next. */
  readonly #nested = new Map<string, CanvasFieldElement>();

  constructor() {
    super();
    this.#chrome.className = 'bo-chrome';
  }

  /** The key of the field shown. */
  get key(): string {
    return this.state?.key ?? '';
  }

  // The field's markup lives in the element itself, where the theme's rules reach it, and this element writes it; Lit's
  // own rendering goes to a root no one sees, so it never meets the nodes written here.
  protected override createRenderRoot() {
    return document.createElement('div');
  }

  /** Moves the focus to the button that selects the field, or to its handle. */
  focusField(control: 'select' | 'handle' = 'select') {
    (childAt(this.#chrome, control === 'handle' ? '.bo-handle' : '.bo-select') as HTMLElement | null)?.focus();
  }

  /** The canvas field of a field in this repeatable group, when it shows one. */
  nestedField(key: string): CanvasFieldElement | undefined {
    for (const nested of this.#nested.values()) {
      if (nested.key === key) return nested;
      const deeper = nested.nestedField(key);
      if (deeper) return deeper;
    }
    return undefined;
  }

  protected override update(changed: PropertyValues) {
    super.update(changed);
    const state = this.state;
    if (!state) return;
    this.setAttribute('key', state.key);
    this.toggleAttribute('selected', state.selected);
    const written = this.#written;
    if (
      !written ||
      written.model !== state.model ||
      written.editing !== state.editing ||
      written.classes !== this.classes
    )
      this.#write(state);
    else this.#placeChildren(state);
    this.#renderChrome(state);
  }

  protected override render() {
    return nothing;
  }

  #write(state: FieldState) {
    this.#written = { model: state.model, editing: state.editing, classes: this.classes };
    const model = state.model;
    const written =
      model && this.classes ? writeField({ document, api: '' }, this.classes, model) : document.createElement('div');
    asEnhanced(written);
    const seen = model !== null && showsSomething(written);
    this.className = ['bo-field', model ? model.cssClass : 'jfb-field', seen ? '' : 'bo-unseen'].join(' ').trim();
    const nodes: Node[] = [...written.childNodes];
    if (!seen) {
      const note = document.createElement('p');
      note.className = 'bo-note';
      note.textContent = state.words.unseen;
      nodes.splice(0, nodes.length, note);
    }
    // The controls stay where they are, so a button that has the focus keeps it.
    for (const child of [...this.childNodes]) if (child !== this.#chrome) child.remove();
    if (this.#chrome.parentNode !== this) this.append(this.#chrome);
    for (const node of nodes) this.insertBefore(node, this.#chrome);

    const live: Element[] = [];
    const rowFields = state.children ? this.#rowFields() : null;
    if (rowFields) live.push(rowFields);
    const target = state.editing && seen ? labelTarget(this, model) : null;
    if (target) live.push(target);
    this.#editingIn = target !== null;
    if (state.editing && !target) queueMicrotask(() => this.#request({ kind: 'label', key: state.key, text: null }));
    this.#makeInert(live);
    this.#placeChildren(state);
    if (target) this.#editIn(target, state);
  }

  // The fields of a repeatable group's first row, where its own fields go.
  #rowFields(): Element | null {
    return childAt(
      childAt(this, 'fieldset')?.querySelector('.jfb-repeater') ?? null,
      'fieldset.jfb-row',
      '.jfb-fields',
    );
  }

  // A repeatable group's fields, each a canvas field of its own in its first row, after the insert point before them.
  #placeChildren(state: FieldState) {
    const fields = state.children ? this.#rowFields() : null;
    if (!fields || !state.children) return;
    fields.setAttribute('data-fields', state.key);
    const kept = new Set(state.children.map((child) => child.key));
    for (const [key, nested] of this.#nested) {
      if (kept.has(key)) continue;
      nested.remove();
      this.#nested.delete(key);
    }
    const hosts = state.children.map((child) => {
      let host = this.#nested.get(child.key);
      if (!host) {
        host = document.createElement('jfb-canvas-field') as CanvasFieldElement;
        this.#nested.set(child.key, host);
      }
      host.classes = this.classes;
      host.state = child;
      return host;
    });
    const first = this.#firstPoint(state);
    const wanted: Element[] = [first, ...hosts];
    if (wanted.some((element, at) => fields.children[at] !== element) || fields.children.length !== wanted.length)
      fields.replaceChildren(...wanted);
  }

  #first: HTMLElement | undefined;

  // The place before a repeatable group's first field: an insert point, or, with no field yet, an area that says so.
  #firstPoint(state: FieldState): HTMLElement {
    const empty = (state.children ?? []).length === 0;
    const container = state.key;
    this.#first ??= document.createElement('div');
    const point = this.#first;
    point.className = 'bo-row-start';
    render(
      empty
        ? html`<button
            type="button"
            class="bo-empty-group"
            ?data-active=${state.firstActive === true}
            aria-label=${state.words.insertFirst ?? state.words.insertHere}
            @click=${() => this.#request({ kind: 'insertFirst', container })}
          >
            <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${state.words.insertHere}
          </button>`
        : html`<button
            type="button"
            class="bo-insert bo-insert-first"
            ?data-active=${state.firstActive === true}
            ?data-beside-tools=${state.children?.[0]?.selected === true}
            aria-label=${state.words.insertFirst ?? state.words.insertHere}
            @click=${() => this.#request({ kind: 'insertFirst', container })}
          >
            <span class="bo-pill"
              ><uui-icon name="icon-add" aria-hidden="true"></uui-icon
              ><span class="bo-pill-text">${state.words.insertHere}</span></span
            >
          </button>`,
      point,
      { host: this },
    );
    return point;
  }

  // Everything the views wrote is inert, but for the way down to what stays live: the label being edited, the row
  // that holds a repeatable group's own canvas fields.
  #makeInert(live: readonly Element[]) {
    const inert = (parent: Element) => {
      for (const child of parent.children) {
        if (child === this.#chrome) continue;
        const holds = live.some((element) => child === element || child.contains(element));
        (child as HTMLElement).inert = !holds;
        if (holds && !live.includes(child)) inert(child);
      }
    };
    inert(this);
  }

  // The label's text becomes a box with it, the mark staying beside it.
  #editIn(target: Element, state: FieldState) {
    const box = document.createElement('input');
    box.className = 'bo-label-box';
    box.value = state.labelText;
    box.setAttribute('aria-label', state.words.labelBox);
    box.setAttribute('autocomplete', 'off');
    const mark = childAt(target, '.jfb-mark');
    target.classList.remove('jfb-visually-hidden');
    target.replaceChildren(box, ...(mark ? [' ', mark] : []));
    let done = false;
    const finish = (keep: boolean) => {
      if (done) return;
      done = true;
      this.#request({ kind: 'label', key: state.key, text: keep ? box.value : null });
    };
    box.addEventListener('keydown', (event) => {
      event.stopPropagation();
      if (event.key === 'Enter') {
        event.preventDefault();
        finish(true);
      } else if (event.key === 'Escape') {
        event.preventDefault();
        finish(false);
      }
    });
    box.addEventListener('blur', () => finish(true));
    requestAnimationFrame(() => {
      box.focus();
      box.select();
    });
  }

  #request(request: FieldRequest) {
    this.dispatchEvent(
      new CustomEvent<FieldRequest>(FIELD_REQUEST, { detail: request, bubbles: true, composed: true }),
    );
  }

  #renderChrome(state: FieldState) {
    const { words, key } = state;
    render(
      html`
        <button
          type="button"
          class="bo-select"
          data-key=${key}
          data-control="select"
          aria-label=${words.name}
          aria-describedby=${state.untranslated && !state.selected ? `jfb-chip-${key}` : nothing}
          aria-current=${state.selected ? 'true' : 'false'}
          ?hidden=${state.editing && this.#editingIn}
          @click=${() => this.#request({ kind: 'select', key })}
          @dblclick=${() => this.#request({ kind: 'editLabel', key })}
        ></button>
        <button
          type="button"
          class="bo-handle"
          data-key=${key}
          data-control="handle"
          aria-label=${words.move}
          ?hidden=${state.editing && this.#editingIn}
          @pointerdown=${(event: PointerEvent) =>
            this.#request({ kind: 'drag', key, event, handle: event.currentTarget as Element })}
          @click=${() => this.#request({ kind: 'select', key })}
        >
          <uui-icon name="icon-grip" aria-hidden="true"></uui-icon>
        </button>
        ${state.refusal ? html`<p class="bo-refusal" aria-hidden="true">${state.refusal}</p>` : nothing}
        ${
          state.untranslated && !state.selected
            ? html`<span class="bo-chip" id=${`jfb-chip-${key}`}>${state.untranslated}</span>`
            : nothing
        }
        ${
          state.selected && !(state.editing && this.#editingIn)
            ? html`<div class="bo-tools">
                <button
                  type="button"
                  aria-label=${words.moveToNamed}
                  @click=${() => this.#request({ kind: 'moveTo', key })}
                >
                  <uui-icon name="icon-navigation" aria-hidden="true"></uui-icon
                  ><span class="bo-tool-text">${words.moveTo}</span>
                </button>
                <button
                  type="button"
                  aria-label=${words.duplicateNamed}
                  @click=${() => this.#request({ kind: 'duplicate', key })}
                >
                  <uui-icon name="icon-documents" aria-hidden="true"></uui-icon
                  ><span class="bo-tool-text">${words.duplicate}</span>
                </button>
                <button
                  type="button"
                  aria-label=${words.deleteNamed}
                  @click=${() => this.#request({ kind: 'delete', key })}
                >
                  <uui-icon name="icon-trash" aria-hidden="true"></uui-icon
                  ><span class="bo-tool-text">${words.delete}</span>
                </button>
                ${moreMenu(`jfb-more-${key}`, words.more, [
                  ...(words.changeType
                    ? [{ label: words.changeType, act: () => this.#request({ kind: 'changeType', key }) }]
                    : []),
                  { label: words.newPageAfter, act: () => this.#request({ kind: 'newPageAfter', key }) },
                  ...(words.copyAlias
                    ? [{ label: words.copyAlias, act: () => this.#request({ kind: 'copyAlias', key }) }]
                    : []),
                ])}
              </div>`
            : nothing
        }
        <button
          type="button"
          class="bo-insert bo-insert-after"
          ?data-active=${state.insertActive}
          ?data-beside-tools=${state.beforeSelected === true}
          aria-label=${words.insertAfter}
          @click=${() => this.#request({ kind: 'insertAfter', key })}
        >
          <span class="bo-pill"
            ><uui-icon name="icon-add" aria-hidden="true"></uui-icon
            ><span class="bo-pill-text">${words.insertHere}</span></span
          >
        </button>
      `,
      this.#chrome,
      { host: this },
    );
  }
}

if (!customElements.get('jfb-canvas-field')) customElements.define('jfb-canvas-field', CanvasFieldElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-canvas-field': CanvasFieldElement;
  }
}
