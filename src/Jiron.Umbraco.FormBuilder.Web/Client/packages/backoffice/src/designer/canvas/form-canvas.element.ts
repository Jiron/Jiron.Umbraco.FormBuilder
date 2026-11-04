// The canvas (ROADMAP 9.3b, docs/ux/information-architecture.md section 5.1, ADR-0168): the form as visitors see it, in
// the default theme, one page at a time at the chosen width, with the designer's controls laid over it. Above it sit
// the width switch with Undo and Redo in its menu, and the pages as steps with Add page and After sending. Every page, group
// and field selects itself; an insert point between fields and the Add field at the end of a page open the palette
// for that place. Fields, groups and pages are dragged by their handles and steps, and the palette's types onto the
// page (ADR-0016, ADR-0169). On a site with more than one language the toolbar switches the language the canvas and
// the inspector show, and a field without a text in it carries a chip (ADR-0170). The canvas changes nothing itself:
// it says what was asked in a request, which the Design view does.
import { html, nothing, repeat, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { FieldViewModel } from '@jfb/element/answer';
import { writeNavigation, writeOutcome } from '@jfb/element/views';
import type { FieldTypeResponseModel } from '../../api';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { DesignDefinition, DesignField, DesignGroup, DesignPage, LocalizedText } from '../definition';
import type { Place } from '../editor-model';
import {
  fieldModel,
  formSettings,
  groupClasses,
  textIn,
  type PreviewContext,
  type ResolvedOption,
} from '../preview/field-models';
import type { CanvasLook } from '../preview/look';
import { fragmentOf, safeHtml, showsText } from '../preview/rich-text';
import { modifierTerm } from '../moves/keys';
import { CanvasDrag, type DragSource } from './canvas-drag';
import { FarGroups } from './canvas-far';
import { untranslated } from '../inspector/inspector-model';
import { chipsOf } from '../rules/rule-list';
import { inspectorRuleWords } from '../rules/rule-names';
import { FIELD_REQUEST, moreMenu, type FieldRequest, type FieldState, type FieldWords } from './canvas-field.element';
import './canvas-field.element';
import { canvasChrome } from './canvas.styles';
import { CANVAS_WIDTHS, type CanvasWidth } from './widths';

/** What the canvas shows: a page by its place, or what visitors get after sending. */
export type CanvasShown = number | 'after';

/** What the canvas asks the Design view to do. */
export type CanvasRequest =
  | FieldRequest
  | { readonly kind: 'addField'; readonly page: string }
  | { readonly kind: 'show'; readonly shown: CanvasShown }
  | { readonly kind: 'addPage' }
  | { readonly kind: 'width'; readonly width: CanvasWidth }
  | { readonly kind: 'undo' }
  | { readonly kind: 'redo' }
  | { readonly kind: 'paste' }
  | { readonly kind: 'changeOutcome' }
  | { readonly kind: 'shortcuts' }
  | { readonly kind: 'rulesOverview' }
  | { readonly kind: 'culture'; readonly culture: string }
  | { readonly kind: 'drop'; readonly source: DragSource; readonly place: Place }
  | { readonly kind: 'refused'; readonly source: DragSource; readonly reason: string };

/** Why the form can't take what is dragged at a place, in words; null where it can. */
export type RefusalOf = (source: DragSource, place: Place | null) => string | null;

/** Why the form refused an element's last move, shown at the element until the next change. */
export interface InPlaceRefusal {
  readonly key: string;
  readonly text: string;
}

/** The event a request travels in. */
export const CANVAS_REQUEST = 'jfb-canvas-request';

/**
 * Whether a page shows one sentence in place of its groups: it has no field and at most one group, without a title or
 * a text of its own. Its Add field stands for that group, so the group has no button of its own.
 */
export function pageIsBare(page: DesignPage, culture: string): boolean {
  return (
    page.groups.length <= 1 &&
    page.groups.every(
      (group) =>
        group.fields.length === 0 &&
        !textIn(group.title, culture) &&
        !showsText(safeHtml(textIn(group['description'] as LocalizedText | undefined, culture))),
    )
  );
}

export class FormCanvasElement extends UmbLitElement {
  static override properties = {
    definition: { attribute: false },
    selection: { attribute: false },
    shown: { attribute: false },
    look: { attribute: false },
    sheets: { attribute: false },
    types: { attribute: false },
    mediaUrls: { attribute: false },
    resolvedOptions: { attribute: false },
    width: { attribute: false },
    insertAt: { attribute: false },
    editing: { attribute: false },
    canUndo: { attribute: false },
    canRedo: { attribute: false },
    refusalOf: { attribute: false },
    refusal: { attribute: false },
    _narrowed: { state: true },
  };

  static override styles = [jfbBackofficeTokens, canvasChrome];

  declare definition: DesignDefinition;
  declare selection: string | null;
  declare shown: CanvasShown;
  declare look: CanvasLook | undefined;
  /** The default theme's stylesheets, adopted below the canvas's own. */
  declare sheets: readonly CSSStyleSheet[];
  declare types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The addresses of the media items the form's images show, by key. */
  declare mediaUrls: Readonly<Record<string, string>>;
  /** The options of fields that take them from elsewhere, as the server resolved them, by the field's key (ADR-0172). */
  declare resolvedOptions: ReadonlyMap<string, readonly ResolvedOption[]>;
  declare width: CanvasWidth;
  /** The insert point the palette is open for, which stays in sight. */
  declare insertAt: Place | null;
  /** The field whose label is edited in place. */
  declare editing: string | null;
  declare canUndo: boolean;
  declare canRedo: boolean;
  /** Asked while something is dragged: why the form can't take it where it would land. */
  declare refusalOf: RefusalOf;
  declare refusal: InPlaceRefusal | null;
  declare private _narrowed: boolean;

  #resize = new ResizeObserver(() => this.#measure());
  #observed: Element | null = null;
  /** The page's groups far from the view, whose drawing waits until they come near (canvas-far.ts). */
  readonly #far = new FarGroups();
  /** Each field's view model, kept while the field and what it is built with stay the same. */
  #models = new WeakMap<DesignField, { context: PreviewContext; columns: number; model: FieldViewModel | null }>();
  /**
   * Each field's words and state, kept while what they are made of stays the same: a field a change didn't touch gets
   * the same state, so it isn't drawn again, and a selection or a move on a form of 300 fields stays quick (section 4.12).
   */
  #words = new WeakMap<DesignField, { readonly parts: readonly unknown[]; readonly words: FieldWords }>();
  #states = new WeakMap<DesignField, { readonly parts: readonly unknown[]; readonly state: FieldState }>();
  #previewContext: { key: readonly unknown[]; context: PreviewContext & ReturnType<typeof formSettings> } | undefined;
  #chips: { readonly parts: readonly unknown[]; readonly chips: ReadonlyMap<string, readonly string[]> } | undefined;
  /**
   * The page's buttons, the same node while their classes and words stay the same. A new node at every render would be
   * taken out and put back at every selection, and the theme's rules that look inside the form (`:has()`) would then
   * restyle every field of the page (section 4.12).
   */
  #navigation: { readonly parts: readonly unknown[]; readonly node: HTMLElement } | undefined;

  constructor() {
    super();
    this.definition = { pages: [] };
    this.selection = null;
    this.shown = 0;
    this.sheets = [];
    this.types = {};
    this.mediaUrls = {};
    this.resolvedOptions = new Map();
    this.width = 'desktop';
    this.insertAt = null;
    this.editing = null;
    this.canUndo = false;
    this.canRedo = false;
    this.refusalOf = () => null;
    this.refusal = null;
    this._narrowed = false;
    this.addEventListener(FIELD_REQUEST, (event) => {
      event.stopPropagation();
      const request = (event as CustomEvent<FieldRequest>).detail;
      if (request.kind === 'drag') this.#dragField(request.event, request.handle, request.key);
      else this.#ask(request);
    });
  }

  readonly #drag = new CanvasDrag({
    root: () => this.renderRoot as ShadowRoot,
    shownPage: () => (typeof this.shown === 'number' ? (this.definition.pages[this.shown]?.key ?? null) : null),
    pageEnd: (source, page) => this.#pageEnd(source, page),
    refusalOf: (source, place) => this.refusalOf(source, place),
    show: (page) => {
      const index = this.definition.pages.findIndex((each) => each.key === page);
      if (index >= 0) this.#ask({ kind: 'show', shown: index });
    },
    drop: (source, place) => {
      this.#ask({ kind: 'drop', source, place });
      // The Design view changes the form as it hears the request, and the fields write themselves before the next
      // frame: what was dropped is where it lands by then.
      requestAnimationFrame(() => this.#drag.settle(this.selection));
    },
    refused: (source, reason) => this.#ask({ kind: 'refused', source, reason }),
  });

  /** Follows a press on one of the palette's types, which a drag onto the page adds where it lands. */
  dragNew(event: PointerEvent, handle: Element, type: string, label: string) {
    this.#drag.start(event, handle, { kind: 'new', type, label });
  }

  #dragField(event: PointerEvent, handle: Element, key: string) {
    const field = this.#find(key);
    this.#drag.start(event, handle, {
      kind: 'field',
      key,
      label: field ? this.#fieldLabel(field, this.#culture()) : key,
    });
  }

  #culture(): string {
    return this.look?.culture ?? '';
  }

  #find(key: string): DesignField | undefined {
    const visit = (fields: readonly DesignField[]): DesignField | undefined => {
      for (const field of fields) {
        if (field.key === key) return field;
        const inside = visit(field.fields ?? []);
        if (inside) return inside;
      }
      return undefined;
    };
    for (const page of this.definition.pages)
      for (const group of page.groups) {
        const found = visit(group.fields);
        if (found) return found;
      }
    return undefined;
  }

  // Where a drop on a page's step lands: a field at the end of the page's last group, a group at the page's end.
  #pageEnd(source: DragSource, key: string): Place | null {
    const page = this.definition.pages.find((each) => each.key === key);
    if (!page) return null;
    if (source.kind === 'group') return { container: page.key, index: page.groups.length };
    const group = page.groups.at(-1);
    return group ? { container: group.key, index: group.fields.length } : null;
  }

  /** The elements of the page shown in the order the keyboard goes through them: the page, each group, its fields. */
  #order(): string[] {
    const index = typeof this.shown === 'number' ? Math.min(this.shown, this.definition.pages.length - 1) : -1;
    const page = index >= 0 ? this.definition.pages[index] : undefined;
    if (!page) return [];
    const keys = [page.key];
    const fields = (list: readonly DesignField[]) => {
      for (const field of list) {
        keys.push(field.key);
        fields(field.fields ?? []);
      }
    };
    const bare = pageIsBare(page, this.#culture());
    for (const group of page.groups) {
      if (!bare) keys.push(group.key);
      fields(group.fields);
    }
    return keys;
  }

  /** Moves the focus from an element to the one before or after it on the page shown; nothing past either end. */
  focusNext(key: string, by: 1 | -1) {
    const order = this.#order();
    const next = order[order.indexOf(key) + by];
    if (order.includes(key) && next) this.focusElement(next);
  }

  override disconnectedCallback() {
    this.#resize.disconnect();
    this.#far.stop();
    this.#observed = null;
    super.disconnectedCallback();
  }

  /**
   * Moves the focus to an element's control: the button that selects a field, group or page, or its handle; a page's
   * step after a page moved by it. An element the canvas doesn't draw gives the focus to the page's Add field.
   */
  focusElement(key: string, control: 'select' | 'handle' | 'step' = 'select') {
    const root = this.shadowRoot;
    if (control === 'step') {
      root?.querySelector<HTMLElement>(`.bo-step[data-page="${key}"]`)?.focus();
      return;
    }
    const field = root?.querySelector<HTMLElement & { focusField?: (control: 'select' | 'handle') => void }>(
      `jfb-canvas-field[key="${key}"]`,
    );
    // A field the canvas shows is found in its group or in its repeatable group's row.
    if (field?.focusField) {
      field.focusField(control);
      field.scrollIntoView({ block: 'nearest' });
      return;
    }
    const found =
      (control === 'handle' ? root?.querySelector<HTMLElement>(`[data-handle="${key}"]`) : null) ??
      root?.querySelector<HTMLElement>(`[data-select="${key}"]`);
    if (found) found.focus();
    else this.focusAddField();
  }

  /** Moves the focus to the Add field of the page shown. */
  focusAddField() {
    this.shadowRoot?.querySelector<HTMLElement>('.bo-add-field')?.focus();
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const root = this.shadowRoot;
    if (changed.has('sheets') && root && 'adoptedStyleSheets' in root) {
      // The theme first, so the canvas's own rules for its controls come after it.
      const own = FormCanvasElement.elementStyles.flatMap((style) =>
        style instanceof CSSStyleSheet ? [style] : style.styleSheet ? [style.styleSheet] : [],
      );
      root.adoptedStyleSheets = [...this.sheets, ...own];
    }
    const viewport = root?.querySelector('.bo-viewport') ?? null;
    if (viewport !== this.#observed) {
      this.#resize.disconnect();
      this.#observed = viewport;
      if (viewport) this.#resize.observe(viewport);
      this.#far.within(viewport);
    }
    this.#far.watch(root?.querySelectorAll('.bo-page > [data-group]') ?? []);
    if (changed.has('width')) this.#measure();
  }

  #measure() {
    const viewport = this.shadowRoot?.querySelector<HTMLElement>('.bo-viewport');
    if (!viewport) return;
    const style = getComputedStyle(viewport);
    const room = viewport.clientWidth - parseFloat(style.paddingInlineStart) - parseFloat(style.paddingInlineEnd);
    // The sheet's own padding on each side, as canvas.styles.ts sets it.
    this._narrowed = room - 2 * 32 < CANVAS_WIDTHS[this.width];
  }

  #ask(request: CanvasRequest) {
    this.dispatchEvent(
      new CustomEvent<CanvasRequest>(CANVAS_REQUEST, { detail: request, bubbles: true, composed: true }),
    );
  }

  // What the fields are built with, the same object while nothing it is made of changes, so their models are kept.
  #context(look: CanvasLook): PreviewContext & ReturnType<typeof formSettings> {
    const key = [look, this.types, this.mediaUrls, this.resolvedOptions, this.definition['settings']];
    const kept = this.#previewContext;
    if (kept && kept.key.every((part, at) => part === key[at])) return kept.context;
    const culture = look.culture;
    const urls = this.mediaUrls;
    const resolved = this.resolvedOptions;
    const context = {
      look,
      types: this.types,
      culture,
      prefix: 'jfb-canvas',
      showRows: true,
      mediaUrl: (media: string) => (Object.hasOwn(urls, media) ? urls[media] : undefined),
      resolved: (field: DesignField) => resolved.get(field.key),
      ...formSettings(this.definition, look, culture),
    };
    this.#previewContext = { key, context };
    return context;
  }

  #model(field: DesignField, context: PreviewContext, columns: number): FieldViewModel | null {
    const kept = this.#models.get(field);
    if (kept && kept.context === context && kept.columns === columns) return kept.model;
    const model = fieldModel(field, context, columns);
    this.#models.set(field, { context, columns, model });
    return model;
  }

  #typeName(type: string): string {
    const found = Object.hasOwn(this.types, type) ? this.types[type] : undefined;
    return found ? this.localize.string(found.name) : type;
  }

  #fieldLabel(field: DesignField, culture: string): string {
    const type = field.type === 'heading' ? textIn((field['settings'] as { text?: LocalizedText })?.text, culture) : '';
    return textIn(field.label, culture) || type || field.alias || this.localize.term('jfbDesigner_untitledField');
  }

  #groupName(group: DesignGroup, index: number, culture: string): string {
    return textIn(group.title, culture) || this.localize.term('jfbDesigner_groupNumber', index + 1);
  }

  #pageName(page: DesignPage, index: number, culture: string): string {
    return textIn(page.title, culture) || this.localize.term('jfbDesigner_pageNumber', index + 1);
  }

  // Why a field visitors don't see shows a note in its place.
  #unseen(field: DesignField, model: FieldViewModel | null): string {
    const type = this.#typeName(field.type);
    if (!Object.hasOwn(this.types, field.type)) return this.localize.term('jfbDesigner_unseenGone', field.type);
    if (field.type === 'hidden') return this.localize.term('jfbDesigner_unseenHidden', type);
    if (field.type === 'captcha' && !model) return this.localize.term('jfbDesigner_unseenCaptcha', type);
    if (field.type === 'image') {
      // A picture chosen that the user may not open, or that is gone, is still not "no picture".
      const media = (field['settings'] as { media?: unknown } | undefined)?.media;
      const chosen = typeof media === 'string' && media !== '';
      return this.localize.term(chosen ? 'jfbDesigner_unseenPicture' : 'jfbDesigner_unseenImage', type);
    }
    if (!model) return this.localize.term('jfbDesigner_unseenOff', type);
    return this.localize.term('jfbDesigner_unseenEmpty', type);
  }

  #field(field: DesignField, context: PreviewContext, columns: number, next: string | undefined): TemplateResult {
    return html`<jfb-canvas-field
      .state=${this.#state(field, context, columns, null, next)}
      .classes=${context.look.classes}
    ></jfb-canvas-field>`;
  }

  // A field's state on the canvas, a repeatable group's with its own fields'; `container` is the repeatable group a
  // field is in, whose insert points count within it, and `next` the key of the field after it there.
  #state(
    field: DesignField,
    context: PreviewContext,
    columns: number,
    container: DesignField | null,
    next: string | undefined,
  ): FieldState {
    const model = this.#model(field, context, columns);
    const label = this.#fieldLabel(field, context.culture);
    const words = this.#wordsOf(field, context, model, label);
    const children = field.fields?.map((child, at) =>
      this.#state(child, context, columns, field, field.fields?.[at + 1]?.key),
    );
    const selected = this.selection === field.key;
    const editing = this.editing === field.key;
    const insertActive = this.#insertsAfter(field, container);
    const beforeSelected = next !== undefined && next === this.selection;
    const refusal = this.refusal?.key === field.key ? this.refusal.text : null;
    const firstActive = this.insertAt?.container === field.key && this.insertAt.index === 0;
    const chip = this.#untranslated(field, context.culture);
    const rules = this.#ruleChips().get(field.key) ?? null;
    const parts = [
      model,
      words,
      selected,
      editing,
      insertActive,
      beforeSelected,
      refusal,
      firstActive,
      chip,
      rules,
      ...(children ?? []),
    ];
    const kept = this.#states.get(field);
    if (kept && kept.parts.length === parts.length && kept.parts.every((part, at) => part === parts[at]))
      return kept.state;
    const state: FieldState = {
      key: field.key,
      model,
      words,
      selected,
      editing,
      // What the canvas shows, in its culture, which is where an edit goes back to.
      labelText: field.type === 'heading' ? label : textIn(field.label, context.culture),
      insertActive,
      beforeSelected,
      refusal,
      untranslated: chip,
      rules,
      ...(children ? { children, firstActive } : {}),
    };
    this.#states.set(field, { parts, state });
    return state;
  }

  /**
   * The rule chips of every page, group and field, in words (ADR-0178). An element whose chips say what they said keeps
   * its list, so a field a change didn't touch keeps its state and isn't drawn again (section 4.12).
   */
  #ruleChips(): ReadonlyMap<string, readonly string[]> {
    const culture = this.#culture();
    const parts = [this.definition, culture, this.localize.lang(), this.types, this.look?.languages];
    const kept = this.#chips;
    if (kept && kept.parts.every((part, at) => part === parts[at])) return kept.chips;
    const languages = (this.look?.languages ?? []).map((language) => ({
      culture: language.culture,
      name: language.name,
    }));
    const words = inspectorRuleWords(this.definition, this.types, culture, languages, (key, ...args) =>
      this.localize.term(key, ...args),
    );
    const chips = new Map<string, readonly string[]>();
    for (const [key, said] of chipsOf(this.definition, words)) {
      const before = kept?.chips.get(key);
      const same = before !== undefined && before.length === said.length && before.every((one, at) => one === said[at]);
      chips.set(key, same ? before : said);
    }
    this.#chips = { parts, chips };
    return chips;
  }

  /** An element's rule chips, beside its translation chip, at its top end; none while it is selected. */
  #chipsOf(key: string, selected: boolean) {
    const rules = this.#ruleChips().get(key);
    if (!rules || selected) return nothing;
    return html`<span class="bo-chips"
      >${rules.map(
        (said, at) =>
          html`<span class="bo-chip bo-rule-chip" id=${`jfb-rule-chip-${key}-${at}`}
            ><uui-icon name="icon-split" aria-hidden="true"></uui-icon><span>${said}</span></span
          >`,
      )}</span
    >`;
  }

  /** The ids of an element's rule chips, which describe the button that selects it. */
  #chipIds(key: string, selected: boolean): string | typeof nothing {
    const rules = this.#ruleChips().get(key);
    return rules && !selected ? rules.map((_, at) => `jfb-rule-chip-${key}-${at}`).join(' ') : nothing;
  }

  /** The site's default language, whose texts visitors see where the language shown has none. */
  #defaultCulture(): string {
    return this.look?.languages.find((language) => language.isDefault)?.culture ?? this.#culture();
  }

  // A language's name in the user's own: "German", for de-DE in an English backoffice.
  #languageName(culture: string): string {
    try {
      return (
        new Intl.DisplayNames([this.localize.lang()], { type: 'language' }).of(culture.split('-')[0] ?? culture) ??
        culture
      );
    } catch {
      return this.look?.languages.find((language) => language.culture === culture)?.name ?? culture;
    }
  }

  // The chip of a field without a text in the language shown, where that is not the default.
  #untranslated(field: DesignField, culture: string): string | null {
    if (culture.toLowerCase() === this.#defaultCulture().toLowerCase()) return null;
    const type = Object.hasOwn(this.types, field.type) ? this.types[field.type] : undefined;
    return untranslated(field, type, culture)
      ? this.localize.term('jfbDesigner_untranslated', this.#languageName(culture))
      : null;
  }

  #wordsOf(field: DesignField, context: PreviewContext, model: FieldViewModel | null, label: string): FieldWords {
    const parts = [context, model, this.localize.lang(), field.alias, this.types];
    const kept = this.#words.get(field);
    if (kept && kept.parts.every((part, at) => part === parts[at])) return kept.words;
    const words: FieldWords = {
      name: this.localize.term('jfbDesigner_fieldName', label, this.#typeName(field.type)),
      insertHere: this.localize.term('jfbDesigner_insertHere'),
      insertAfter: this.localize.term('jfbDesigner_insertAfter', label),
      move: this.localize.term('jfbDesigner_move', label),
      moveTo: this.localize.term('jfbDesigner_moveTo'),
      moveToNamed: this.localize.term('jfbDesigner_moveToNamed', label),
      duplicate: this.localize.term('jfbDesigner_duplicate'),
      duplicateNamed: this.localize.term('jfbDesigner_duplicateNamed', label),
      delete: this.localize.term('jfbDesigner_delete'),
      deleteNamed: this.localize.term('jfbDesigner_deleteNamed', label),
      more: this.localize.term('jfbDesigner_moreElement'),
      ...(Object.hasOwn(this.types, field.type) && this.types[field.type]?.mayChangeType
        ? { changeType: this.localize.term('jfbDesigner_changeType') }
        : {}),
      newPageAfter: this.localize.term('jfbDesigner_newPageAfter'),
      ...(field.alias ? { copyAlias: this.localize.term('jfbDesigner_copyAlias') } : {}),
      labelBox: this.localize.term('jfbDesigner_labelBox'),
      unseen: this.#unseen(field, model),
      ...(field.fields
        ? {
            insertFirst: field.fields[0]
              ? this.localize.term('jfbDesigner_insertBefore', this.#fieldLabel(field.fields[0], context.culture))
              : this.localize.term('jfbDesigner_insertInto', label),
          }
        : {}),
    };
    this.#words.set(field, { parts, words });
    return words;
  }

  // Whether the open insert point is the one below a field, in its group or its repeatable group.
  #insertsAfter(field: DesignField, container: DesignField | null): boolean {
    const at = this.insertAt;
    if (!at) return false;
    if (container) {
      const index = (container.fields ?? []).findIndex((each) => each.key === field.key);
      return at.container === container.key && at.index === index + 1;
    }
    for (const page of this.definition.pages)
      for (const group of page.groups) {
        const index = group.fields.findIndex((each) => each.key === field.key);
        if (index >= 0) return at.container === group.key && at.index === index + 1;
      }
    return false;
  }

  #tools(key: string, name: string, group = false): TemplateResult {
    return html`<div class="bo-tools">
      ${
        group
          ? html`<button
              type="button"
              aria-label=${this.localize.term('jfbDesigner_moveToNamed', name)}
              @click=${() => this.#ask({ kind: 'moveTo', key })}
            >
              <uui-icon name="icon-navigation" aria-hidden="true"></uui-icon
              ><span class="bo-tool-text">${this.localize.term('jfbDesigner_moveTo')}</span>
            </button>`
          : nothing
      }
      <button
        type="button"
        aria-label=${this.localize.term('jfbDesigner_duplicateNamed', name)}
        @click=${() => this.#ask({ kind: 'duplicate', key })}
      >
        <uui-icon name="icon-documents" aria-hidden="true"></uui-icon
        ><span class="bo-tool-text">${this.localize.term('jfbDesigner_duplicate')}</span>
      </button>
      <button
        type="button"
        aria-label=${this.localize.term('jfbDesigner_deleteNamed', name)}
        @click=${() => this.#ask({ kind: 'delete', key })}
      >
        <uui-icon name="icon-trash" aria-hidden="true"></uui-icon
        ><span class="bo-tool-text">${this.localize.term('jfbDesigner_delete')}</span>
      </button>
      ${
        group
          ? moreMenu(`jfb-more-${key}`, this.localize.term('jfbDesigner_moreElement'), [
              {
                label: this.localize.term('jfbDesigner_newPageAfter'),
                act: () => this.#ask({ kind: 'newPageAfter', key }),
              },
            ])
          : nothing
      }
    </div>`;
  }

  #group(group: DesignGroup, index: number, context: PreviewContext): TemplateResult {
    const culture = context.culture;
    const title = textIn(group.title, culture);
    const description = safeHtml(textIn(group['description'] as LocalizedText | undefined, culture));
    const name = this.#groupName(group, index, culture);
    const columns = Math.min(4, Math.max(1, Math.trunc(Number(group['columns'] ?? 1)) || 1));
    const selected = this.selection === group.key;
    const first = group.fields[0];
    const firstActive = this.insertAt?.container === group.key && this.insertAt.index === 0;
    const select = html`<button
        type="button"
        class="bo-select bo-select-group"
        data-select=${group.key}
        data-key=${group.key}
        data-control="select"
        aria-label=${this.localize.term('jfbDesigner_groupName', name)}
        aria-describedby=${this.#chipIds(group.key, selected)}
        aria-current=${selected ? 'true' : 'false'}
        @click=${() => this.#ask({ kind: 'select', key: group.key })}
      ></button>
      <button
        type="button"
        class="bo-handle bo-handle-group"
        data-handle=${group.key}
        data-key=${group.key}
        data-control="handle"
        aria-label=${this.localize.term('jfbDesigner_move', name)}
        @pointerdown=${(event: PointerEvent) =>
          this.#drag.start(event, event.currentTarget as Element, { kind: 'group', key: group.key, label: name })}
        @click=${() => this.#ask({ kind: 'select', key: group.key })}
      >
        <uui-icon name="icon-grip" aria-hidden="true"></uui-icon>
      </button>
      ${this.#refusalAt(group.key)} ${selected ? this.#tools(group.key, name, true) : nothing}
      ${this.#chipsOf(group.key, selected)}`;
    const help = showsText(description) ? html`<div class="jfb-help" inert>${fragmentOf(description)}</div>` : nothing;
    const fields = html`<div class="jfb-fields" data-fields=${group.key}>
      ${
        first
          ? html`<button
              type="button"
              class="bo-insert bo-insert-first"
              ?data-active=${firstActive}
              ?data-beside-tools=${first.key === this.selection}
              aria-label=${this.localize.term('jfbDesigner_insertBefore', this.#fieldLabel(first, culture))}
              @click=${() => this.#ask({ kind: 'insertFirst', container: group.key })}
            >
              <span class="bo-pill"
                ><uui-icon name="icon-add" aria-hidden="true"></uui-icon
                ><span class="bo-pill-text">${this.localize.term('jfbDesigner_insertHere')}</span></span
              >
            </button>`
          : html`<button
              type="button"
              class="bo-empty-group"
              ?data-active=${firstActive}
              aria-label=${this.localize.term('jfbDesigner_insertInto', name)}
              @click=${() => this.#ask({ kind: 'insertFirst', container: group.key })}
            >
              <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_insertHere')}
            </button>`
      }
      ${repeat(
        group.fields,
        (field) => field.key,
        (field, at) => this.#field(field, context, columns, group.fields[at + 1]?.key),
      )}
    </div>`;
    const classes = `${groupClasses(group)} bo-group${selected ? ' bo-selected' : ''}`;
    return title
      ? html`<fieldset class=${classes} data-group=${group.key}>
          ${select}
          <legend inert>${title}</legend>
          ${help}${fields}
        </fieldset>`
      : html`<div class=${classes} data-group=${group.key}>${select}${help}${fields}</div>`;
  }

  #page(page: DesignPage, index: number, context: PreviewContext & ReturnType<typeof formSettings>): TemplateResult {
    const culture = context.culture;
    const title = textIn(page.title, culture);
    const description = safeHtml(textIn(page['description'] as LocalizedText | undefined, culture));
    const name = this.#pageName(page, index, culture);
    const selected = this.selection === page.key;
    const bare = pageIsBare(page, culture);
    const formEmpty = this.definition.pages.every((each) => each.groups.every((group) => group.fields.length === 0));
    const navigation = this.#navigationOf(index, context);
    return html`<section class="jfb-page bo-page${selected ? ' bo-selected' : ''}" data-groups=${page.key}>
        <button
          type="button"
          class="bo-select bo-select-page"
          data-select=${page.key}
          data-key=${page.key}
          data-control="select"
          aria-label=${this.localize.term('jfbDesigner_pageName', name)}
          aria-describedby=${this.#chipIds(page.key, selected)}
          aria-current=${selected ? 'true' : 'false'}
          @click=${() => this.#ask({ kind: 'select', key: page.key })}
        ></button>
        ${this.#refusalAt(page.key)} ${selected ? this.#tools(page.key, name) : nothing}
        ${this.#chipsOf(page.key, selected)} ${title ? html`<h2 class="jfb-title" inert>${title}</h2>` : nothing}
        ${showsText(description) ? html`<div class="jfb-intro" inert>${fragmentOf(description)}</div>` : nothing}
        ${
          bare
            ? // The page's one group lies behind its sentence: a field dropped on the page goes into it.
              html`<p class="bo-empty" data-fields=${page.groups[0]?.key ?? nothing}>
                ${
                  formEmpty
                    ? html`${this.localize.term('jfbDesigner_emptyFormBefore')}
                        <button type="button" class="bo-link" @click=${() => this.#ask({ kind: 'paste' })}>
                          ${this.localize.term('jfbDesigner_emptyFormLink')}</button
                        >${this.localize.term('jfbDesigner_emptyFormAfter')}`
                    : this.localize.term('jfbDesigner_emptyPage')
                }
              </p>`
            : nothing
        }
        ${
          bare
            ? nothing
            : repeat(
                page.groups,
                (group) => group.key,
                (group, at) => this.#group(group, at, context),
              )
        }
        <button type="button" class="bo-add-field" @click=${() => this.#ask({ kind: 'addField', page: page.key })}>
          <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_addField')}
        </button>
      </section>
      ${navigation}`;
  }

  // The page's Back, Next or Send as the theme draws them, shown and never pressed.
  #navigationOf(index: number, context: PreviewContext & ReturnType<typeof formSettings>): HTMLElement {
    const back = index > 0 ? context.back || this.#word('back') : null;
    const last = index === this.definition.pages.length - 1;
    const forward = last ? context.send || this.#word('send') : context.next || this.#word('next');
    const parts = [context.look.classes, back, last, forward];
    const kept = this.#navigation;
    if (kept && kept.parts.every((part, at) => part === parts[at])) return kept.node;
    const node = writeNavigation(document, context.look.classes, {
      ...(back !== null ? { back: { label: back, value: 'back' } } : {}),
      ...(last ? { submit: { label: forward, value: 'submit' } } : { next: { label: forward, value: 'next' } }),
    });
    node.inert = true;
    this.#navigation = { parts, node };
    return node;
  }

  // Why the form refused a group's or a page's last move, beside its handle.
  #refusalAt(key: string) {
    return this.refusal?.key === key
      ? html`<p class="bo-refusal" aria-hidden="true">${this.refusal.text}</p>`
      : nothing;
  }

  #word(name: string): string {
    return this.look?.words[name] ?? name;
  }

  // What visitors get after sending: the outcome without rules, else the package's thanks.
  #after(look: CanvasLook): TemplateResult {
    const outcomes = (this.definition['outcomes'] ?? []) as readonly {
      kind?: string;
      message?: LocalizedText;
      rules?: readonly unknown[];
    }[];
    const outcome = outcomes.find((each) => (each.rules ?? []).length === 0) ?? outcomes[0];
    let shown: Node | TemplateResult;
    if (outcome?.kind === 'redirect')
      shown = html`<p class="bo-empty">${this.localize.term('jfbDesigner_afterRedirect')}</p>`;
    else {
      const message = safeHtml(textIn(outcome?.message, look.culture));
      const element = writeOutcome(document, look.classes, {
        message: showsText(message) ? message : `<p>${safeHtml(this.#word('thankYou'))}</p>`,
        isPending: false,
      });
      element.removeAttribute('tabindex');
      element.inert = true;
      shown = element;
    }
    return html`<div class="bo-after">
      ${shown}
      <uui-button
        look="outline"
        label=${this.localize.term('jfbDesigner_changeOutcome')}
        @click=${() => this.#ask({ kind: 'changeOutcome' })}
      ></uui-button>
    </div>`;
  }

  #toolbar(): TemplateResult {
    const held = this.localize.term(modifierTerm());
    return html`<div class="bo-bar">
      <div class="bo-widths" role="group" aria-label=${this.localize.term('jfbDesigner_canvasWidth')}>
        ${(Object.keys(CANVAS_WIDTHS) as CanvasWidth[]).map(
          (width) =>
            html`<button
              type="button"
              aria-pressed=${this.width === width ? 'true' : 'false'}
              @click=${() => this.#ask({ kind: 'width', width })}
            >
              ${this.localize.term(`jfbDesigner_width_${width}`)}
            </button>`,
        )}
      </div>
      ${this._narrowed ? html`<span class="bo-narrowed">${this.localize.term('jfbDesigner_narrowed')}</span>` : nothing}
      ${this.#languages()}
      <uui-button
        class="bo-more"
        compact
        popovertarget="jfb-canvas-more"
        label=${this.localize.term('jfbDesigner_moreCanvas')}
      >
        <uui-symbol-more></uui-symbol-more>
      </uui-button>
      <uui-popover-container id="jfb-canvas-more" placement="bottom-end">
        <div class="bo-menu">
          <button type="button" ?disabled=${!this.canUndo} @click=${() => this.#menu('undo')}>
            <span>${this.localize.term('jfbDesigner_undo')}</span><kbd>${held}+Z</kbd>
          </button>
          <button type="button" ?disabled=${!this.canRedo} @click=${() => this.#menu('redo')}>
            <span>${this.localize.term('jfbDesigner_redo')}</span><kbd>${held}+Y</kbd>
          </button>
          <button type="button" @click=${() => this.#menu('rulesOverview')}>
            <span>${this.localize.term('jfbDesigner_rulesOverview')}</span>
          </button>
          <button type="button" @click=${() => this.#menu('shortcuts')}>
            <span>${this.localize.term('jfbDesigner_shortcuts')}</span><kbd>?</kbd>
          </button>
        </div>
      </uui-popover-container>
    </div>`;
  }

  // The language the canvas and the inspector show, on a site with more than one.
  #languages(): TemplateResult | typeof nothing {
    const languages = this.look?.languages ?? [];
    if (languages.length < 2) return nothing;
    const shown = this.#culture().toLowerCase();
    return html`<label class="bo-language">
      <span>${this.localize.term('jfbDesigner_language')}</span>
      <select
        @change=${(event: Event) => this.#ask({ kind: 'culture', culture: (event.target as HTMLSelectElement).value })}
      >
        ${languages.map(
          (language) =>
            html`<option value=${language.culture} ?selected=${language.culture.toLowerCase() === shown}>
              ${language.name}
            </option>`,
        )}
      </select>
    </label>`;
  }

  #menu(kind: 'undo' | 'redo' | 'rulesOverview' | 'shortcuts') {
    this.shadowRoot?.querySelector<HTMLElement & { hidePopover?: () => void }>('#jfb-canvas-more')?.hidePopover?.();
    this.#ask({ kind });
  }

  #steps(culture: string): TemplateResult {
    return html`<nav class="bo-steps" aria-label=${this.localize.term('jfbDesigner_pages')}>
      ${this.definition.pages.map(
        (page, index) =>
          html`<button
            type="button"
            class="bo-step"
            data-page=${page.key}
            data-key=${page.key}
            data-control="step"
            aria-current=${this.shown === index ? 'step' : 'false'}
            @pointerdown=${(event: PointerEvent) =>
              this.#drag.start(event, event.currentTarget as Element, {
                kind: 'page',
                key: page.key,
                label: this.#pageName(page, index, culture),
              })}
            @click=${() => this.#ask({ kind: 'show', shown: index })}
          >
            ${this.#pageName(page, index, culture)}
          </button>`,
      )}
      <button type="button" class="bo-step bo-step-add" @click=${() => this.#ask({ kind: 'addPage' })}>
        <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_addPage')}
      </button>
      <button
        type="button"
        class="bo-step"
        aria-current=${this.shown === 'after' ? 'step' : 'false'}
        @click=${() => this.#ask({ kind: 'show', shown: 'after' })}
      >
        <uui-icon name="icon-flag" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_afterSending')}
      </button>
    </nav>`;
  }

  override render() {
    const look = this.look;
    if (!look) return html`<div class="bo-loading"><uui-loader></uui-loader></div>`;
    const context = this.#context(look);
    const pages = this.definition.pages;
    const index = this.shown === 'after' ? -1 : Math.min(this.shown, pages.length - 1);
    const page = index >= 0 ? pages[index] : undefined;
    const width = CANVAS_WIDTHS[this.width];
    return html`${this.#toolbar()} ${this.#steps(look.culture)}
      <div class="bo-viewport" id="jfb-canvas-form" tabindex="-1">
        <div class="bo-sheet" style="--bo-width: ${width}px">
          <div class="jfb-form jfb-theme-default" style="--jfb-size-form: ${width}px" lang=${look.culture}>
            ${page ? this.#page(page, index, context) : this.#after(look)}
          </div>
        </div>
      </div>`;
  }
}

if (!customElements.get('jfb-form-canvas')) customElements.define('jfb-form-canvas', FormCanvasElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-canvas': FormCanvasElement;
  }
}
