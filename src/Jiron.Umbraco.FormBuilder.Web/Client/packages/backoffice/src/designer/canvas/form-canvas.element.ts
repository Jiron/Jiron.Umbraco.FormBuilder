// The canvas (ROADMAP 9.3b and 22.1, docs/ux/information-architecture.md section 5.1, ADR-0168, ADR-0256): the form as
// visitors see it, in the default theme, one page at a time at the chosen width, with the designer's controls laid over
// it. One row stands above it: the pages as tabs, "+" (Add page) and After sending at its start; the width, the
// language on a site with more than one, the form check's badge and "..." at its end. The row becomes two where the
// pages need the room, and a tab shrinks before the tabs scroll. The open page's tab holds its menu: Rename, Duplicate
// and Delete, which a form's only page doesn't offer. Every group and field selects itself, and a click on the sheet's
// empty room clears the selection; an insert point between fields and the Add field at the end of a page open the field
// picker for that place. Fields, groups and pages are dragged by their handles and tabs (ADR-0016, ADR-0169). A field,
// group or page without a text in the language shown carries a chip (ADR-0170, ADR-0228), a page's describing its tab.
// After sending shows what visitors get, and under it the outcomes, where the Design view slots them there. The canvas
// changes nothing itself: it says what was asked in a request, which the Design view does.
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
import { followedKeys } from '../moves/moves';
import { CanvasDrag, type DragSource } from './canvas-drag';
import { FarGroups } from './canvas-far';
import { untranslated } from '../inspector/inspector-model';
import { groupLacks, pageLacks } from '../translations';
import { withDictionary, type DictionaryLookups } from '../dictionary-texts';
import { chipsOf } from '../rules/rule-list';
import { inspectorRuleWords } from '../rules/rule-names';
import { holdsAlways, outcomesOf, useRuleOf } from '../outcomes/outcome-model';
import {
  FIELD_REQUEST,
  moreMenu,
  popoverMenu,
  popoverMenuStyles,
  type FieldRequest,
  type FieldState,
  type FieldWords,
} from './canvas-field.element';
import './canvas-field.element';
import { canvasChrome } from './canvas.styles';
import { languageButton, languageButtonStyles } from './language-button';
import { CANVAS_WIDTHS, type CanvasWidth } from './widths';

/** Umbraco's device icons for the widths, as its own preview shows them. */
const WIDTH_ICONS: Readonly<Record<CanvasWidth, string>> = {
  phone: 'icon-iphone',
  tablet: 'icon-ipad',
  desktop: 'icon-display',
};

/** The most problems the form check's badge counts; more read as this with a plus, so it keeps its width. */
const MOST_COUNTED = 99;

/** What the canvas shows: a page by its place, or what visitors get after sending. */
export type CanvasShown = number | 'after';

/** What the canvas asks the Design view to do. */
export type CanvasRequest =
  | FieldRequest
  /** A click on the sheet's empty room, which lets the selection go. */
  | { readonly kind: 'clear' }
  | { readonly kind: 'addField'; readonly page: string }
  /** Rename, from the open page's tab: the page's Title in the inspector. */
  | { readonly kind: 'renamePage'; readonly key: string }
  | { readonly kind: 'show'; readonly shown: CanvasShown }
  | { readonly kind: 'addPage' }
  | { readonly kind: 'width'; readonly width: CanvasWidth }
  | { readonly kind: 'undo' }
  | { readonly kind: 'redo' }
  | { readonly kind: 'paste' }
  | { readonly kind: 'shortcuts' }
  | { readonly kind: 'rulesOverview' }
  /** A site's designer action from the row's "...", by its manifest's alias (ADR-0018). */
  | { readonly kind: 'designerAction'; readonly alias: string }
  | { readonly kind: 'formCheck' }
  /** A group saved as a template, from its "..." (ADR-0217). */
  | { readonly kind: 'saveGroup'; readonly key: string }
  | { readonly kind: 'culture'; readonly culture: string }
  | { readonly kind: 'drop'; readonly source: DragSource; readonly place: Place }
  | { readonly kind: 'refused'; readonly source: DragSource; readonly reason: string };

/** A site's designer action as the row's "..." lists it. */
export interface DesignerActionItem {
  readonly alias: string;
  /** Its label: a backoffice key such as `#acme_translate`, or the text. */
  readonly label: string;
}

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
 * What the form check's badge says (ADR-0181): nothing before the first check; checking until the first answer; the
 * problems, errors and warnings, of the last answer; or that the check gave none.
 */
export type CheckBadge =
  | { readonly kind: 'none' }
  | { readonly kind: 'checking' }
  | { readonly kind: 'failed' }
  | { readonly kind: 'checked'; readonly errors: number; readonly warnings: number };

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
    lookups: { attribute: false },
    width: { attribute: false },
    insertAt: { attribute: false },
    editing: { attribute: false },
    canUndo: { attribute: false },
    canRedo: { attribute: false },
    refusalOf: { attribute: false },
    refusal: { attribute: false },
    check: { attribute: false },
    saveGroups: { attribute: false },
    designerActions: { attribute: false },
    _narrowed: { state: true },
  };

  static override styles = [jfbBackofficeTokens, popoverMenuStyles, languageButtonStyles, canvasChrome];

  declare definition: DesignDefinition;
  /** The dictionary items the draft's texts name in the language shown, as the server finds them (ADR-0228). */
  declare lookups: DictionaryLookups;
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
  /** The insert point the field picker is open for, which stays marked. */
  declare insertAt: Place | null;
  /** The field whose label is edited in place. */
  declare editing: string | null;
  declare canUndo: boolean;
  declare canRedo: boolean;
  /** Asked while something is dragged: why the form can't take it where it would land. */
  declare refusalOf: RefusalOf;
  declare refusal: InPlaceRefusal | null;
  /** What the form check's badge says. */
  declare check: CheckBadge;
  /** Whether a group's "..." offers Save group as template: the user may manage the library. */
  declare saveGroups: boolean;
  /** The site's designer actions, after the menu's own items (ADR-0018): each by its manifest's alias and label. */
  declare designerActions: readonly DesignerActionItem[];
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
    this.lookups = new Map();
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
    this.check = { kind: 'none' };
    this.saveGroups = false;
    this.designerActions = [];
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
    shownPage: () => (typeof this.shown === 'number' ? (this.#drawn.pages[this.shown]?.key ?? null) : null),
    pageEnd: (source, page) => this.#pageEnd(source, page),
    refusalOf: (source, place) => this.refusalOf(source, place),
    show: (page) => {
      const index = this.#drawn.pages.findIndex((each) => each.key === page);
      if (index >= 0) this.#ask({ kind: 'show', shown: index });
    },
    drop: (source, place) => {
      this.#ask({ kind: 'drop', source, place });
      // The Design view changes the form as it hears the request, and the fields write themselves before the next
      // frame: what was dropped is where it lands by then.
      requestAnimationFrame(() => this.#drag.settle());
    },
    refused: (source, reason) => this.#ask({ kind: 'refused', source, reason }),
  });

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

  #drawnOf:
    | {
        readonly definition: DesignDefinition;
        readonly culture: string;
        readonly lookups: DictionaryLookups;
        readonly drawn: DesignDefinition;
        own?: ReadonlyMap<string, DesignPage | DesignGroup | DesignField>;
      }
    | undefined;

  /**
   * The draft as the canvas draws it: a text that names a dictionary item shows the item's text in the language shown
   * (ADR-0228). What holds no such text stays the same object, so what was drawn for it is kept.
   */
  get #drawn(): DesignDefinition {
    const culture = this.#culture();
    const kept = this.#drawnOf;
    if (kept && kept.definition === this.definition && kept.culture === culture && kept.lookups === this.lookups)
      return kept.drawn;
    const drawn = withDictionary(this.definition, culture, this.lookups);
    this.#drawnOf = { definition: this.definition, culture, lookups: this.lookups, drawn };
    return drawn;
  }

  /**
   * A page, group or field as the draft holds it, by the drawn one's key: a text that names a dictionary item holds its
   * key there, not the item's text. An edit on the canvas starts from it, and the translation marks read it, as the
   * dictionary holds such a text's words in each language (ADR-0228).
   */
  #ownOf<T extends DesignPage | DesignGroup | DesignField>(drawn: T): T {
    const shown = this.#drawn;
    const kept = this.#drawnOf;
    if (!kept || shown === this.definition) return drawn;
    if (!kept.own) {
      const own = new Map<string, DesignPage | DesignGroup | DesignField>();
      const visit = (fields: readonly DesignField[]) => {
        for (const each of fields) {
          own.set(each.key, each);
          if (each.fields) visit(each.fields);
        }
      };
      for (const page of this.definition.pages) {
        own.set(page.key, page);
        for (const group of page.groups) {
          own.set(group.key, group);
          visit(group.fields);
        }
      }
      kept.own = own;
    }
    return (kept.own.get(drawn.key) as T | undefined) ?? drawn;
  }

  /** A field's label as the draft holds it, which an edit on the canvas starts from: its key, not the item's text. */
  #labelOf(field: DesignField): DesignField['label'] {
    return this.#ownOf(field).label;
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
    for (const page of this.#drawn.pages)
      for (const group of page.groups) {
        const found = visit(group.fields);
        if (found) return found;
      }
    return undefined;
  }

  // Where a drop on a page's tab lands: a field at the end of the page's last group, a group at the page's end.
  #pageEnd(source: DragSource, key: string): Place | null {
    const page = this.#drawn.pages.find((each) => each.key === key);
    if (!page) return null;
    if (source.kind === 'group') return { container: page.key, index: page.groups.length };
    const group = page.groups.at(-1);
    return group ? { container: group.key, index: group.fields.length } : null;
  }

  /** The elements of the page shown in the order the keyboard goes through them: each group, then its fields. */
  #order(): string[] {
    const index = typeof this.shown === 'number' ? Math.min(this.shown, this.#drawn.pages.length - 1) : -1;
    const page = index >= 0 ? this.#drawn.pages[index] : undefined;
    if (!page) return [];
    const keys: string[] = [];
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
   * Moves the focus to an element's control: the button that selects a field or group, or its handle; a page's tab, the
   * page's one control. An element the canvas doesn't draw gives the focus to the page's Add field.
   */
  focusElement(key: string, control: 'select' | 'handle' | 'tab' = 'select') {
    const root = this.shadowRoot;
    const tab = root?.querySelector<HTMLElement>(`.bo-tab-name[data-key="${key}"]`);
    if (tab || control === 'tab') {
      tab?.focus();
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
    // The sheet's own padding and border on each side, as canvas.styles.ts sets them.
    this._narrowed = room - 2 * (32 + 1) < CANVAS_WIDTHS[this.width];
  }

  #ask(request: CanvasRequest) {
    this.dispatchEvent(
      new CustomEvent<CanvasRequest>(CANVAS_REQUEST, { detail: request, bubbles: true, composed: true }),
    );
  }

  // What the fields are built with, the same object while nothing it is made of changes, so their models are kept.
  #context(look: CanvasLook): PreviewContext & ReturnType<typeof formSettings> {
    const key = [look, this.types, this.mediaUrls, this.resolvedOptions, this.#drawn['settings']];
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
      ...formSettings(this.#drawn, look, culture),
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
      labelText: field.type === 'heading' ? label : textIn(this.#labelOf(field), context.culture),
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
    const parts = [this.#drawn, culture, this.localize.lang(), this.types, this.look?.languages, this.resolvedOptions];
    const kept = this.#chips;
    if (kept && kept.parts.every((part, at) => part === parts[at])) return kept.chips;
    const languages = (this.look?.languages ?? []).map((language) => ({
      culture: language.culture,
      name: language.name,
    }));
    const words = inspectorRuleWords(
      this.#drawn,
      this.types,
      culture,
      languages,
      (key, ...args) => this.localize.term(key, ...args),
      this.resolvedOptions,
    );
    const chips = new Map<string, readonly string[]>();
    for (const [key, said] of chipsOf(this.#drawn, words)) {
      const before = kept?.chips.get(key);
      const same = before !== undefined && before.length === said.length && before.every((one, at) => one === said[at]);
      chips.set(key, same ? before : said);
    }
    this.#chips = { parts, chips };
    return chips;
  }

  /**
   * A page's or group's chips at its top end: its translation chip, then its rule chips; none while it is selected, as
   * the inspector says it then (ADR-0228).
   */
  #chipsOf(key: string, selected: boolean, untranslated: string | null) {
    const rules = this.#ruleChips().get(key) ?? [];
    if (selected || (rules.length === 0 && !untranslated)) return nothing;
    return html`<span class="bo-chips"
      >${untranslated ? html`<span class="bo-chip" id=${`jfb-chip-${key}`}>${untranslated}</span>` : nothing}${rules.map(
        (said, at) =>
          html`<span class="bo-chip bo-rule-chip" id=${`jfb-rule-chip-${key}-${at}`}
            ><uui-icon name="icon-split" aria-hidden="true"></uui-icon><span>${said}</span></span
          >`,
      )}</span
    >`;
  }

  /** The ids of a page's or group's chips, which describe the button that selects a group and a page's tab. */
  #chipIds(key: string, selected: boolean, untranslated: string | null): string | typeof nothing {
    if (selected) return nothing;
    const ids = [
      ...(untranslated ? [`jfb-chip-${key}`] : []),
      ...(this.#ruleChips().get(key) ?? []).map((_, at) => `jfb-rule-chip-${key}-${at}`),
    ];
    return ids.length > 0 ? ids.join(' ') : nothing;
  }

  /** The chip of a page or group without a text in the language shown, where that is not the default (ADR-0228). */
  #containerUntranslated(lacking: boolean, culture: string): string | null {
    if (!lacking || culture.toLowerCase() === this.#defaultCulture().toLowerCase()) return null;
    return this.localize.term('jfbDesigner_untranslated', this.#languageName(culture));
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
    return untranslated(this.#ownOf(field), type, culture)
      ? this.localize.term('jfbDesigner_untranslated', this.#languageName(culture))
      : null;
  }

  // What "Move what follows to a new page" is offered on, found once for each draft drawn.
  #followed: { readonly of: DesignDefinition; readonly keys: ReadonlySet<string> } | undefined;

  #follows(key: string): boolean {
    const drawn = this.#drawn;
    if (this.#followed?.of !== drawn) this.#followed = { of: drawn, keys: followedKeys(drawn) };
    return this.#followed.keys.has(key);
  }

  #wordsOf(field: DesignField, context: PreviewContext, model: FieldViewModel | null, label: string): FieldWords {
    const follows = this.#follows(field.key);
    const parts = [context, model, this.localize.lang(), field.alias, this.types, follows];
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
      ...(follows ? { newPageAfter: this.localize.term('jfbDesigner_newPageAfter') } : {}),
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
    for (const page of this.#drawn.pages)
      for (const group of page.groups) {
        const index = group.fields.findIndex((each) => each.key === field.key);
        if (index >= 0) return at.container === group.key && at.index === index + 1;
      }
    return false;
  }

  // A selected group's bar: Move to..., Duplicate, Delete and its "...".
  #tools(key: string, name: string): TemplateResult {
    const follows = this.#follows(key);
    return html`<div class="bo-tools">
      <button
        type="button"
        aria-label=${this.localize.term('jfbDesigner_moveToNamed', name)}
        @click=${() => this.#ask({ kind: 'moveTo', key })}
      >
        <uui-icon name="icon-navigation" aria-hidden="true"></uui-icon
        ><span class="bo-tool-text">${this.localize.term('jfbDesigner_moveTo')}</span>
      </button>
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
        follows || this.saveGroups
          ? moreMenu(`jfb-more-${key}`, this.localize.term('jfbDesigner_moreElement'), [
              ...(follows
                ? [
                    {
                      label: this.localize.term('jfbDesigner_newPageAfter'),
                      act: () => this.#ask({ kind: 'newPageAfter', key }),
                    },
                  ]
                : []),
              ...(this.saveGroups
                ? [
                    {
                      label: this.localize.term('jfbDesigner_saveGroupAsTemplate'),
                      act: () => this.#ask({ kind: 'saveGroup', key }),
                    },
                  ]
                : []),
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
    const untranslated = this.#containerUntranslated(groupLacks(this.#ownOf(group), culture), culture);
    const first = group.fields[0];
    const firstActive = this.insertAt?.container === group.key && this.insertAt.index === 0;
    const select = html`<button
        type="button"
        class="bo-select bo-select-group"
        data-select=${group.key}
        data-key=${group.key}
        data-control="select"
        aria-label=${this.localize.term('jfbDesigner_groupName', name)}
        aria-describedby=${this.#chipIds(group.key, selected, untranslated)}
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
      ${this.#refusalAt(group.key)} ${selected ? this.#tools(group.key, name) : nothing}
      ${this.#chipsOf(group.key, selected, untranslated)}`;
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

  /** A page's chip where it has no text in the language shown (ADR-0228). */
  #pageUntranslated(page: DesignPage, culture: string): string | null {
    return this.#containerUntranslated(pageLacks(this.#ownOf(page), culture), culture);
  }

  #page(page: DesignPage, index: number, context: PreviewContext & ReturnType<typeof formSettings>): TemplateResult {
    const culture = context.culture;
    const title = textIn(page.title, culture);
    const description = safeHtml(textIn(page['description'] as LocalizedText | undefined, culture));
    const untranslated = this.#pageUntranslated(page, culture);
    const bare = pageIsBare(page, culture);
    const formEmpty = this.#drawn.pages.every((each) => each.groups.every((group) => group.fields.length === 0));
    const navigation = this.#navigationOf(index, context);
    return html`<section class="jfb-page bo-page" data-groups=${page.key}>
        ${this.#refusalAt(page.key)} ${this.#chipsOf(page.key, false, untranslated)}
        ${title ? html`<h2 class="jfb-title" inert>${title}</h2>` : nothing}
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
    const last = index === this.#drawn.pages.length - 1;
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

  // What visitors get after sending: the first outcome for every visitor (its rule off or always met), else the first,
  // else the package's thanks.
  #after(look: CanvasLook): Node | TemplateResult {
    const outcomes = outcomesOf(this.#drawn);
    const outcome = outcomes.find((each) => holdsAlways(useRuleOf(each))) ?? outcomes[0];
    if (outcome?.kind === 'redirect')
      return html`<p class="bo-empty">${this.localize.term('jfbDesigner_afterRedirect')}</p>`;
    const message = safeHtml(textIn(outcome?.message, look.culture));
    const element = writeOutcome(document, look.classes, {
      message: showsText(message) ? message : `<p>${safeHtml(this.#word('thankYou'))}</p>`,
      isPending: false,
    });
    element.removeAttribute('tabindex');
    element.inert = true;
    return element;
  }

  // The one row above the sheet (ADR-0256): the pages at its start, the tools at its end. It keeps the Desktop sheet's
  // width whatever width is chosen, so the width buttons never move under the pointer; where the pages need the room,
  // the tools go to a second row, at its end.
  #row(culture: string): TemplateResult {
    return html`<div class="bo-bar">
      <div class="bo-row" style="--bo-row: ${CANVAS_WIDTHS.desktop}px">
        ${this.#pages(culture)}
        <div class="bo-end">${this.#widths()} ${this.#languages()} ${this.#checkBadge()} ${this.#more()}</div>
      </div>
    </div>`;
  }

  // The pages as tabs, then "+" and After sending, which never leave the row. The open page's tab holds its menu, and a
  // form's only page offers no Delete there. A tab drags its page among the tabs and takes a dropped field or group at
  // that page's end; the keys on it act on its page (the Design view's).
  #pages(culture: string): TemplateResult {
    const pages = this.#drawn.pages;
    return html`<nav class="bo-pages" aria-label=${this.localize.term('jfbDesigner_pages')}>
      <div class="bo-tabs">
        ${pages.map((page, index) => {
          const name = this.#pageName(page, index, culture);
          const open = this.shown === index;
          const key = page.key;
          return html`<span class="bo-tab" data-page=${key} ?data-open=${open}>
            <button
              type="button"
              class="bo-tab-name"
              data-key=${key}
              data-control="tab"
              title=${name}
              aria-current=${open ? 'step' : 'false'}
              aria-describedby=${open ? this.#chipIds(key, false, this.#pageUntranslated(page, culture)) : nothing}
              @pointerdown=${(event: PointerEvent) =>
                this.#drag.start(event, event.currentTarget as Element, { kind: 'page', key, label: name })}
              @click=${() => this.#ask({ kind: 'show', shown: index })}
            >
              <span>${name}</span>
            </button>
            ${
              open
                ? moreMenu(
                    `jfb-page-more-${key}`,
                    this.localize.term('jfbDesigner_pageMore', name),
                    [
                      {
                        label: this.localize.term('jfbDesigner_renamePage'),
                        act: () => this.#ask({ kind: 'renamePage', key }),
                      },
                      {
                        label: this.localize.term('jfbDesigner_duplicate'),
                        act: () => this.#ask({ kind: 'duplicate', key }),
                      },
                      ...(pages.length > 1
                        ? [
                            {
                              label: this.localize.term('jfbDesigner_delete'),
                              act: () => this.#ask({ kind: 'delete', key }),
                            },
                          ]
                        : []),
                    ],
                    'bottom-start',
                  )
                : nothing
            }
          </span>`;
        })}
      </div>
      <button
        type="button"
        class="bo-add-page"
        aria-label=${this.localize.term('jfbDesigner_addPage')}
        title=${this.localize.term('jfbDesigner_addPage')}
        @click=${() => this.#ask({ kind: 'addPage' })}
      >
        <uui-icon name="icon-add" aria-hidden="true"></uui-icon>
      </button>
      <button
        type="button"
        class="bo-after-step"
        aria-current=${this.shown === 'after' ? 'step' : 'false'}
        @click=${() => this.#ask({ kind: 'show', shown: 'after' })}
      >
        <uui-icon name="icon-flag" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_afterSending')}
      </button>
    </nav>`;
  }

  // The width the form is shown at, as Umbraco's device icons, each named for screen readers and the pointer's tip.
  #widths(): TemplateResult {
    return html`<div class="bo-widths" role="group" aria-label=${this.localize.term('jfbDesigner_canvasWidth')}>
      ${(Object.keys(CANVAS_WIDTHS) as CanvasWidth[]).map((width) => {
        const name = this.localize.term(`jfbDesigner_width_${width}`);
        return html`<button
          type="button"
          aria-label=${name}
          title=${name}
          aria-pressed=${this.width === width ? 'true' : 'false'}
          @click=${() => this.#ask({ kind: 'width', width })}
        >
          <uui-icon name=${WIDTH_ICONS[width]} aria-hidden="true"></uui-icon>
        </button>`;
      })}
    </div>`;
  }

  #more(): TemplateResult {
    const held = this.localize.term(modifierTerm());
    const ask = (kind: 'undo' | 'redo' | 'rulesOverview' | 'shortcuts') => () => this.#ask({ kind });
    return html`<uui-button
        class="bo-more"
        compact
        popovertarget="jfb-canvas-more"
        label=${this.localize.term('jfbDesigner_moreCanvas')}
      >
        <uui-symbol-more></uui-symbol-more>
      </uui-button>
      ${popoverMenu('jfb-canvas-more', [
        { label: this.localize.term('jfbDesigner_undo'), keys: `${held}+Z`, disabled: !this.canUndo, act: ask('undo') },
        { label: this.localize.term('jfbDesigner_redo'), keys: `${held}+Y`, disabled: !this.canRedo, act: ask('redo') },
        { label: this.localize.term('jfbDesigner_rulesOverview'), act: ask('rulesOverview') },
        { label: this.localize.term('jfbDesigner_shortcuts'), keys: '?', act: ask('shortcuts') },
        ...this.designerActions.map((action) => ({
          label: this.localize.string(action.label),
          alias: action.alias,
          act: () => this.#ask({ kind: 'designerAction', alias: action.alias }),
        })),
      ])}`;
  }

  // The form check's badge (IA 5.1, ADR-0256): its icon and the number of problems the last check found, errors and
  // warnings, a check mark alone where there are none, which it opens in a sidebar; its words are its name and its tip.
  // It keeps one width through all it says, so the row never moves while the form is edited, and it changes quietly: no
  // motion, nothing said aloud. Before the first check its place is kept empty.
  #checkBadge(): TemplateResult {
    const check = this.check;
    if (check.kind === 'none') return html`<span class="bo-check" data-state="none" aria-hidden="true"></span>`;
    const problems = check.kind === 'checked' ? check.errors + check.warnings : 0;
    const state =
      check.kind !== 'checked' ? check.kind : check.errors > 0 ? 'error' : check.warnings > 0 ? 'warning' : 'clear';
    // A warning's icon differs from an error's in its shape, not only its colour, as in the sidebar.
    const icon =
      state === 'clear' ? 'icon-check' : state === 'warning' ? 'icon-info' : state === 'checking' ? null : 'icon-alert';
    // Checking shows an ellipsis, quiet; a check that gave no answer its alert, quiet, without a number.
    const shown = state === 'checking' ? '\u2026' : problems > MOST_COUNTED ? `${MOST_COUNTED}+` : problems || '';
    const words =
      check.kind === 'checking'
        ? this.localize.term('jfbDesigner_checkBadgeChecking')
        : check.kind === 'failed'
          ? this.localize.term('jfbDesigner_checkBadgeFailed')
          : this.localize.term('jfbDesigner_checkBadge', problems);
    return html`<button
      type="button"
      class="bo-check"
      data-state=${state}
      aria-haspopup="dialog"
      aria-label=${words}
      title=${words}
      @click=${() => this.#ask({ kind: 'formCheck' })}
    >
      ${
        state === 'clear'
          ? html`<span class="bo-check-disc" aria-hidden="true"><uui-icon name="icon-check"></uui-icon></span>`
          : icon
            ? html`<uui-icon name=${icon} aria-hidden="true"></uui-icon>`
            : nothing
      }<span aria-hidden="true">${shown}</span>
    </button>`;
  }

  // The language the canvas and the inspector show, on a site with more than one: a button that shows its culture and
  // opens the languages by name.
  #languages(): TemplateResult | typeof nothing {
    return languageButton(
      this.localize,
      'jfb-canvas-languages',
      this.look?.languages ?? [],
      this.#culture(),
      (culture) => this.#ask({ kind: 'culture', culture }),
    );
  }

  // A click on the sheet's empty room, or beside the sheet, lets the selection go; a click on a control is the control's.
  readonly #onRoom = (event: MouseEvent) => {
    if (this.selection === null) return;
    const control = event
      .composedPath()
      .some(
        (node) =>
          node instanceof Element &&
          node.matches('button, a, input, select, textarea, label, [contenteditable="true"]'),
      );
    if (!control) this.#ask({ kind: 'clear' });
  };

  // While the look is read: the sheet's shape, still, without the row above it (IA 5.1).
  #waiting() {
    return html`<div class="bo-viewport bo-waiting" aria-busy="true">
      <div class="bo-sheet" style="--bo-width: ${CANVAS_WIDTHS[this.width]}px">
        <span class="bo-bone bo-bone-title"></span>
        ${[58, 42, 50].map(
          (width) =>
            html`<span class="bo-bone" style="inline-size: ${width / 2}%"></span
              ><span class="bo-bone bo-bone-box"></span>`,
        )}
        <span class="bo-said">${this.localize.term('general_loading')}</span>
      </div>
    </div>`;
  }

  override render() {
    const look = this.look;
    if (!look) return this.#waiting();
    const context = this.#context(look);
    const pages = this.#drawn.pages;
    const index = this.shown === 'after' ? -1 : Math.min(this.shown, pages.length - 1);
    const page = index >= 0 ? pages[index] : undefined;
    const width = CANVAS_WIDTHS[this.width];
    return html`${this.#row(look.culture)}
      <div class="bo-viewport" id="jfb-canvas-form" tabindex="-1" @click=${this.#onRoom}>
        <p
          class="bo-narrowed"
          style="--bo-width: ${width}px"
          ?data-shown=${this._narrowed}
          aria-hidden=${this._narrowed ? 'false' : 'true'}
        >
          ${this.localize.term('jfbDesigner_narrowed')}
        </p>
        <div class="bo-sheet" style="--bo-width: ${width}px">
          <div class="jfb-form jfb-theme-default" style="--jfb-size-form: ${width}px" lang=${look.culture}>
            ${page ? this.#page(page, index, context) : this.#after(look)}
          </div>
        </div>
        ${
          // After sending's outcomes, where the Design view slots them under what visitors get (ADR-0256).
          page ? nothing : html`<div class="bo-under" style="--bo-width: ${width}px"><slot name="after"></slot></div>`
        }
      </div>`;
  }
}

if (!customElements.get('jfb-form-canvas')) customElements.define('jfb-form-canvas', FormCanvasElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-canvas': FormCanvasElement;
  }
}
