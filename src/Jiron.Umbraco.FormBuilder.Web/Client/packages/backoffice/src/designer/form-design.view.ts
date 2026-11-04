// The Design view (ROADMAP 9.3b, docs/ux/information-architecture.md section 5.1): the palette beside the canvas. It
// takes the canvas's and the palette's requests and makes each an edit of the editor model through the workspace, says
// what happened for screen readers, offers Undo after a delete, and moves the focus to what an edit left selected. A
// link that names a field (`?field=`, ADR-0166) opens the view on that field's page with the field selected. Moves
// (ADR-0016, ADR-0169) come from a drop, Move to... and Alt+Up and Alt+Down; each says where the element is now, a move
// the form refuses says why at the element, and the focus stays on the element moved. Up and Down go through the
// elements of the page, and ? opens the Keyboard shortcuts. The inspector (ADR-0170) shows the selected element beside
// the canvas, or over it up to 700 px; up to 1024 px the palette folds to its rail while it does. Its changes are edits
// too, typing in one box one step to undo, and an alias that entries know changes only after a question. A field that
// takes its options from elsewhere shows them as the server resolves the draft's (ADR-0172).
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { ImageCropModeModel } from '@umbraco-cms/backoffice/external/backend-api';
import { UmbImagingRepository } from '@umbraco-cms/backoffice/imaging';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { confirmText } from '../ui/confirm';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { FieldGroupResponseModel, FieldTypeResponseModel } from '../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../ui/undo-notification.element';
import type { DragSource } from './canvas/canvas-drag';
import {
  CANVAS_REQUEST,
  pageIsBare,
  type CanvasRequest,
  type CanvasShown,
  type InPlaceRefusal,
} from './canvas/form-canvas.element';
import { CANVAS_WIDTHS, type CanvasWidth } from './canvas/widths';
import { isDragging } from './drag/pointer-drag';
import type { FormCanvasElement } from './canvas/form-canvas.element';
import './canvas/form-canvas.element';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { allFields, locate, pageOf, type DesignDefinition, type DesignField, type LocalizedText } from './definition';
import { resolveOptions } from './inspector/option-data';
import type { ResolvedOption } from './preview/field-models';
import type { EditorState } from './editor-model';
import {
  change,
  duplicate,
  insert,
  move,
  removeElement,
  select,
  splitPage,
  step,
  type Edit,
  type Place,
  type Refusal,
} from './editor-model';
import type { EditorView, FormWorkspaceContext } from './form-workspace.context';
import { CHANGE_TYPE_MODAL, type ChangeTypeTarget } from './change-type/change-type.modal.token';
import { changeWords } from './change-type/change-type-words';
import { changedField, previewChange } from './change-type/type-change';
import { MOVE_TO_MODAL, type MoveToModalData } from './moves/move-to.modal.token';
import { dropEdit, endOf, moveToPlaces, positionOf, type MovePlace } from './moves/moves';
import { SHORTCUTS_MODAL } from './moves/shortcuts.modal.token';
import { addFields, addGroup, newField, newPage, placeFor } from './new-elements';
import {
  keepRecent,
  PALETTE_PICK,
  readRecent,
  type PaletteElement,
  type PaletteMode,
  type PalettePick,
} from './palette/palette.element';
import './palette/palette.element';
import { PASTE_QUESTIONS_MODAL } from './palette/paste-questions.modal.token';
import { textIn } from './preview/field-models';
import { ownText, withKey, withText } from './inspector/setting-values';
import { readLook, themeSheets, type CanvasLook } from './preview/look';
import {
  INSPECTOR_REQUEST,
  type ElementAction,
  type InspectorMode,
  type InspectorRequest,
} from './inspector/inspector.element';
import './inspector/inspector.element';
import type { InspectorElement } from './inspector/inspector.element';

/**
 * Up to this width, the palette opens over the canvas; wider, it stays beside it (IA 5.1). The narrowest workspace,
 * 620 px, is in this range; Umbraco's own header keeps the view from getting narrower.
 */
export const NARROW = 700;

/** Whether a view this wide puts the palette over the canvas. */
export const isNarrow = (width: number) => width <= NARROW;

/**
 * Up to this width, beside the inspector, the palette folds to its rail while an element is selected, so the canvas
 * keeps its width (IA 5.1); clearing the selection opens it again.
 */
export const CROWDED = 1024;

/** Whether a view this wide, but not narrow, folds the palette while an element is selected. */
export const isCrowded = (width: number) => width > NARROW && width <= CROWDED;

const WIDTH_KEY = 'jfb.designer.width';

/**
 * How long the canvas waits after a field's options change before it asks for them: typing in a source's settings
 * changes them with each key, and a half-typed address must not be fetched.
 */
export const OPTIONS_PAUSE = 400;

/** How wide an image the canvas asks for: no sheet is wider. */
const MEDIA_WIDTH = 1200;

/** The types of an `input` that takes no text, whose keys the Design view's shortcuts may use. */
const NOT_TEXT: ReadonlySet<string> = new Set([
  'checkbox',
  'radio',
  'button',
  'submit',
  'reset',
  'range',
  'color',
  'file',
  'image',
]);

/** The control of an element the focus goes back to: the button that selects it, its handle, or a page's step. */
type Control = 'select' | 'handle' | 'step';

/** What an edit is applied with: the type a refusal names, what the live region says instead, the control focused. */
interface Applied {
  readonly type?: string;
  readonly announce?: (state: EditorState) => string;
  readonly control?: Control;
}

/** The element that has the focus, followed into the shadow roots it lies in. */
function deepFocus(): HTMLElement | null {
  let found = document.activeElement;
  while (found?.shadowRoot?.activeElement) found = found.shadowRoot.activeElement;
  return found instanceof HTMLElement ? found : null;
}

/** The canvas width this browser chose last; Desktop at first. */
export function readWidth(): CanvasWidth {
  try {
    const kept = localStorage.getItem(WIDTH_KEY);
    return kept && Object.hasOwn(CANVAS_WIDTHS, kept) ? (kept as CanvasWidth) : 'desktop';
  } catch {
    return 'desktop';
  }
}

/** The media items a definition's images and option cards show. */
export function mediaOf(definition: DesignDefinition): string[] {
  const keys = new Set<string>();
  const visit = (field: DesignField) => {
    const media = (field['settings'] as { media?: unknown } | undefined)?.media;
    if (field.type === 'image' && typeof media === 'string') keys.add(media);
    for (const item of (field['options'] as { items?: readonly { image?: unknown }[] } | undefined)?.items ?? [])
      if (typeof item.image === 'string') keys.add(item.image);
    for (const child of field.fields ?? []) visit(child);
  };
  for (const page of definition.pages) for (const group of page.groups) for (const field of group.fields) visit(field);
  return [...keys];
}

export class FormDesignViewElement extends UmbLitElement {
  static override properties = {
    _view: { state: true },
    _types: { state: true },
    _groups: { state: true },
    _look: { state: true },
    _sheets: { state: true },
    _media: { state: true },
    _resolved: { state: true },
    _shown: { state: true },
    _width: { state: true },
    _insertAt: { state: true },
    _editing: { state: true },
    _folded: { state: true },
    _open: { state: true },
    _narrow: { state: true },
    _crowded: { state: true },
    _inspectorFolded: { state: true },
    _culture: { state: true },
    _recent: { state: true },
    _said: { state: true },
    _failed: { state: true },
    _refusal: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        position: relative;
        display: grid;
        grid-template-columns: auto minmax(0, 1fr) auto;
        grid-template-rows: minmax(0, 1fr);
        height: 100%;
        /* The view takes the width it is given, so it can measure when that is too narrow for the docked palette. */
        min-width: 0;
        min-height: 0;
      }

      jfb-designer-palette {
        grid-column: 1;
        grid-row: 1;
        width: 264px;
        min-height: 0;
      }

      jfb-designer-palette[mode='rail'] {
        width: 56px;
      }

      jfb-designer-palette[mode='overlay'] {
        position: absolute;
        inset-block: 0;
        inset-inline-start: 0;
        z-index: 10;
        width: min(320px, 100%);
        box-shadow: var(--uui-shadow-depth-3);
      }

      :host([narrow]) {
        grid-template-columns: minmax(0, 1fr);
      }

      jfb-form-canvas,
      .failed {
        grid-column: 2;
        grid-row: 1;
        min-width: 0;
        min-height: 0;
      }

      :host([narrow]) :is(jfb-form-canvas, .failed) {
        grid-column: 1;
      }

      jfb-designer-inspector {
        grid-column: 3;
        grid-row: 1;
        width: 320px;
        min-height: 0;
      }

      jfb-designer-inspector[mode='rail'] {
        width: 56px;
      }

      /* Over a narrow canvas the inspector takes the view, with the element's bar in its header. */
      jfb-designer-inspector[mode='overlay'] {
        position: absolute;
        inset: 0;
        z-index: 10;
        width: auto;
        border-inline-start: 0;
      }

      /* Over the canvas while the palette lies on it: a click on it closes the palette. */
      .scrim {
        position: absolute;
        inset: 0;
        z-index: 9;
        background: color-mix(in srgb, var(--uui-color-text) 16%, transparent);
      }

      .failed {
        display: grid;
        gap: var(--uui-size-space-4);
        align-content: start;
        justify-items: start;
        padding: var(--uui-size-space-6);
      }

      .failed p {
        margin: 0;
      }

      .skip {
        position: absolute;
        inset-block-start: var(--uui-size-space-2);
        inset-inline-start: var(--uui-size-space-2);
        z-index: 20;
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-3);
        border: 0;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 700;
        text-decoration: underline;
        cursor: pointer;
        transform: translateY(-200%);
      }

      .skip:focus {
        transform: none;
        outline: 2px solid var(--uui-color-focus);
      }
    `,
  ];

  declare private _view: EditorView | undefined;
  declare private _types: Readonly<Record<string, FieldTypeResponseModel>>;
  declare private _groups: readonly FieldGroupResponseModel[];
  declare private _look: CanvasLook | undefined;
  declare private _sheets: readonly CSSStyleSheet[];
  declare private _media: Readonly<Record<string, string>>;
  /** The options of fields that take them from elsewhere, as the server resolved the draft's, by field (ADR-0172). */
  declare private _resolved: ReadonlyMap<string, readonly ResolvedOption[]>;
  declare private _shown: CanvasShown;
  declare private _width: CanvasWidth;
  declare private _insertAt: Place | null;
  declare private _editing: string | null;
  declare private _folded: boolean;
  declare private _open: boolean;
  declare private _narrow: boolean;
  /** Whether the view is up to 1024 px wide, but not narrow: the palette folds while an element is selected. */
  declare private _crowded: boolean;
  declare private _inspectorFolded: boolean;
  /** The language the canvas and the inspector show; null for the site's default. */
  declare private _culture: string | null;
  declare private _recent: readonly string[];
  declare private _said: string;
  /** Whether what the canvas draws with could not be read. */
  declare private _failed: boolean;
  /** Why the form refused the last move, shown at the element until the next change. */
  declare private _refusal: InPlaceRefusal | null;

  #context: FormWorkspaceContext | undefined;
  /** The page the palette adds to when it was opened from a page's Add field and the page has no group. */
  #addTo: string | null = null;
  /** Whether the selection a link named was brought into sight, or the user changed something first. */
  #shownLink = false;
  /** Whether Change type's dialog is opening or open, so a second ask does nothing. */
  #changingType = false;
  /** The field selected whose changes of type were read ahead, with its type. */
  #readAhead: string | null = null;
  #mediaRead = new Set<string>();
  /** What each field's options were last asked for as, so a field is asked again only when they or the language change. */
  #optionsAsked = new Map<string, string>();
  /** The asks waiting for the typing to pause, by field. */
  #optionsWaiting = new Map<string, ReturnType<typeof setTimeout>>();
  #imaging: UmbImagingRepository | undefined;
  /** The toast that offers Undo after a delete, closed by the next change: its Undo would take back another. */
  #undoToast: { close(): void } | undefined;
  /** What opened the palette over the canvas, where the focus goes back when it closes without a pick. */
  #opener: HTMLElement | null = null;
  /** The palette's types, the same list while the types stay the same, so the palette isn't drawn again for nothing. */
  #typeList: {
    readonly of: Readonly<Record<string, FieldTypeResponseModel>>;
    readonly list: FieldTypeResponseModel[];
  } = {
    of: {},
    list: [],
  };
  #resize = new ResizeObserver(([entry]) => {
    const width = entry?.contentRect.width;
    const narrow = width !== undefined && isNarrow(width);
    const crowded = width !== undefined && isCrowded(width);
    if (narrow !== this._narrow) this._narrow = narrow;
    if (crowded !== this._crowded) this._crowded = crowded;
  });

  constructor() {
    super();
    this._types = {};
    this._groups = [];
    this._sheets = [];
    this._media = {};
    this._resolved = new Map();
    this._shown = 0;
    this._width = readWidth();
    this._insertAt = null;
    this._editing = null;
    this._folded = false;
    this._open = false;
    this._narrow = false;
    this._crowded = false;
    this._inspectorFolded = false;
    this._culture = null;
    this._recent = readRecent();
    this._said = '';
    this._failed = false;
    this._refusal = null;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.editor, (view) => this.#viewChanged(view), 'editor');
      this.observe(context?.fieldTypes, (types) => (this._types = types ?? {}), 'types');
      this.observe(context?.paletteGroups, (groups) => (this._groups = groups ?? []), 'groups');
    });
    void this.#readLook();
    this.addEventListener(CANVAS_REQUEST, (event) => void this.#onCanvas((event as CustomEvent<CanvasRequest>).detail));
    this.addEventListener(PALETTE_PICK, (event) => void this.#onPalette((event as CustomEvent<PalettePick>).detail));
    this.addEventListener(
      INSPECTOR_REQUEST,
      (event) => void this.#onInspector((event as CustomEvent<InspectorRequest>).detail),
    );
    this.addEventListener('keydown', (event) => this.#onKey(event));
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#resize.observe(this);
  }

  override disconnectedCallback() {
    this.#resize.disconnect();
    for (const waiting of this.#optionsWaiting.values()) clearTimeout(waiting);
    this.#optionsWaiting.clear();
    super.disconnectedCallback();
  }

  async #readLook() {
    this._failed = false;
    const wanted = this._culture;
    const look = await readLook(this, wanted ?? undefined);
    // A language chosen meanwhile wins over the answer for the one before it.
    if (wanted !== this._culture) return;
    if (!look) {
      this._failed = true;
      return;
    }
    this._look = look;
    if (this._view) this.#readOptions(this._view.state.definition);
    this._sheets = await themeSheets(look.stylesheets);
  }

  #viewChanged(view: EditorView | undefined) {
    this._view = view;
    if (!view) return;
    const pages = view.state.definition.pages;
    if (typeof this._shown === 'number' && this._shown >= pages.length) this._shown = Math.max(0, pages.length - 1);
    // A link's field opens on its page.
    const selection = view.state.selection;
    if (!this.#shownLink && selection) {
      const index = pages.findIndex((page) => page.key === pageOf(view.state.definition, selection)?.key);
      if (index >= 0) this._shown = index;
    }
    void this.#readMedia(view.state.definition);
    this.#readOptions(view.state.definition);
    // A field's changes of type are read ahead as it is selected, so Change type opens at once (ADR-0174); once, not
    // at every edit, so a read that fails is not asked again and again.
    const found = selection ? locate(view.state.definition, selection) : undefined;
    const ahead = found?.kind === 'field' ? `${found.element.key} ${found.element.type}` : null;
    if (ahead !== this.#readAhead) {
      this.#readAhead = ahead;
      if (found?.kind === 'field' && this.#fieldType(found.element.type)?.mayChangeType)
        void this.#context?.changesOf(found.element.type);
    }
  }

  // The options of each field that takes them from elsewhere, as the server resolves the draft's in the canvas's
  // language (ADR-0172); a field is asked again only when its options or the language change, once the changes pause,
  // and an answer for what it no longer holds is dropped. A source that can't give its options shows none.
  #readOptions(definition: DesignDefinition) {
    const form = this.#context?.getUnique();
    const culture = this._look?.culture;
    if (!form || culture === undefined) return;
    for (const field of allFields(definition)) {
      const source = (field['options'] as { source?: string } | undefined)?.source;
      if (source === undefined || source === 'items') continue;
      const asked = JSON.stringify([field.type, field['options'], culture]);
      const before = this.#optionsAsked.get(field.key);
      if (before === asked) continue;
      this.#optionsAsked.set(field.key, asked);
      clearTimeout(this.#optionsWaiting.get(field.key));
      const ask = () => {
        this.#optionsWaiting.delete(field.key);
        void resolveOptions(this, form, field, culture).then((answer) => {
          if (this.#optionsAsked.get(field.key) !== asked) return;
          const options = answer?.isAvailable ? answer.options : [];
          this._resolved = new Map(this._resolved).set(field.key, options);
          const images = options.flatMap((option) => (option.image ? [option.image] : []));
          if (images.length > 0) void this.#readMediaKeys(images);
        });
      };
      // A field's first options are asked for at once.
      if (before === undefined) ask();
      else this.#optionsWaiting.set(field.key, setTimeout(ask, OPTIONS_PAUSE));
    }
  }

  async #readMedia(definition: DesignDefinition) {
    await this.#readMediaKeys(mediaOf(definition));
  }

  async #readMediaKeys(keys: readonly string[]) {
    const wanted = keys.filter((key) => !this.#mediaRead.has(key));
    if (wanted.length === 0) return;
    for (const key of wanted) this.#mediaRead.add(key);
    // Through Umbraco's imaging, which a user of the Content section may ask as a media picker's thumbnails do; its
    // media URLs need the Media section. An image at most the canvas's widest sheet across.
    this.#imaging ??= new UmbImagingRepository(this);
    const { data } = await this.#imaging.requestResizedItems(wanted, {
      width: MEDIA_WIDTH,
      mode: ImageCropModeModel.MAX,
    });
    const found = Object.fromEntries((data ?? []).flatMap((item) => (item.url ? [[item.unique, item.url]] : [])));
    if (Object.keys(found).length > 0) this._media = { ...this._media, ...found };
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    this.toggleAttribute('narrow', this._narrow);
    const selection = this._view?.state.selection;
    if (!this.#shownLink && selection && this._look) {
      this.#shownLink = true;
      void this.#focus(selection);
    }
  }

  get #canvas(): FormCanvasElement | null {
    return this.shadowRoot?.querySelector('jfb-form-canvas') ?? null;
  }

  get #palette(): PaletteElement | null {
    return this.shadowRoot?.querySelector('jfb-designer-palette') ?? null;
  }

  get #inspector(): InspectorElement | null {
    return this.shadowRoot?.querySelector('jfb-designer-inspector') ?? null;
  }

  async #focus(key: string, control: Control = 'select') {
    await this.updateComplete;
    // Over a narrow canvas the selected element is the inspector's, and the canvas under it is out of reach.
    if (this.#inspectorMode() === 'overlay' && this._view?.state.selection === key) {
      this.#inspector?.focusHeading();
      return;
    }
    const canvas = this.#canvas;
    if (!canvas) return;
    await canvas.updateComplete;
    canvas.focusElement(key, control);
  }

  // Umbraco's toasts; none where the backoffice has no notification context, so a toast never stops an edit.
  #notifications() {
    return this.getContext(UMB_NOTIFICATION_CONTEXT).catch(() => undefined);
  }

  #say(text: string) {
    // The same words twice are still said: the region is emptied first.
    this._said = '';
    requestAnimationFrame(() => (this._said = text));
  }

  #refusalText(refusal: Refusal, type?: string): string {
    return this.localize.term(`jfbDesigner_refusal_${refusal}`, type ?? '');
  }

  async #refuse(refusal: Refusal, type?: string) {
    if (refusal === 'unchanged') return;
    const message = this.#refusalText(refusal, type);
    this.#say(message);
    (await this.#notifications())?.peek('warning', { data: { message } });
  }

  // A move the form refuses says why at the element and to screen readers, without a toast, until the next change.
  #refuseInPlace(key: string | null, text: string) {
    this._refusal = key ? { key, text } : null;
    this.#say(text);
  }

  /** Applies an edit, says it, and moves the focus to what it left selected. */
  async #apply(edit: Edit, said: string, applied: Applied = {}): Promise<boolean> {
    const context = this.#context;
    if (!context) return false;
    // The user's own first change ends the wait for a link's selection, so its focus is never taken.
    this.#shownLink = true;
    this._refusal = null;
    const result = context.edit(edit, said);
    if (!result.ok) {
      await this.#refuse(result.refusal, applied.type);
      return false;
    }
    this.#closeUndo();
    const announced = applied.announce?.(result.state) ?? said;
    if (announced) this.#say(announced);
    const { selection, dropped } = this.#settle(result.state);
    if (selection) {
      const page = pageOf(result.state.definition, selection);
      const index = result.state.definition.pages.findIndex((each) => each.key === page?.key);
      if (index >= 0) this._shown = index;
      await this.#focus(selection, applied.control);
    } else if (dropped) {
      await this.#focusAddField();
    }
    return true;
  }

  // A page without fields draws its one bare group as the page's sentence, so a selection on that group, which a
  // delete of the page's last field leaves, is let go: Delete would take a group the user can't see.
  #settle(state: EditorState): { selection: string | null; dropped: boolean } {
    const selection = state.selection;
    if (!selection) return { selection: null, dropped: false };
    const found = locate(state.definition, selection);
    if (found?.kind !== 'group' || !pageIsBare(found.container, this._look?.culture ?? ''))
      return { selection, dropped: false };
    this.#context?.edit(select(state, null), '');
    return { selection: null, dropped: true };
  }

  async #focusAddField() {
    await this.updateComplete;
    await this.#canvas?.updateComplete;
    this.#canvas?.focusAddField();
  }

  #closeUndo() {
    this.#undoToast?.close();
    this.#undoToast = undefined;
  }

  #state() {
    return this.#context?.getEditorState();
  }

  #typeName(type: string): string {
    const found = Object.hasOwn(this._types, type) ? this._types[type] : undefined;
    return found ? this.localize.string(found.name) : type;
  }

  #label(key: string): string {
    const state = this.#state();
    const found = state ? locate(state.definition, key) : undefined;
    if (!found) return '';
    const culture = this._look?.culture ?? '';
    if (found.kind === 'page')
      return textIn(found.element.title, culture) || this.localize.term('jfbDesigner_pageNumber', found.index + 1);
    if (found.kind === 'group')
      return textIn(found.element.title, culture) || this.localize.term('jfbDesigner_groupNumber', found.index + 1);
    return (
      textIn(found.element.label, culture) ||
      textIn((found.element['settings'] as { text?: LocalizedText } | undefined)?.text, culture) ||
      this.#typeName(found.element.type)
    );
  }

  /** Whether the palette opens over the canvas: in a narrow view, or folded beside the inspector. */
  #paletteOverlays(): boolean {
    return this._narrow || this.#crowded();
  }

  #openPalette() {
    const over = this.#paletteOverlays();
    if (over && !this._open) this.#opener = deepFocus();
    if (over) this._open = true;
    else this._folded = false;
    this.#palette?.focusSearch();
    void this.updateComplete.then(() => this.#palette?.focusSearch());
  }

  /** Closes the palette over the canvas; without a pick, the focus goes back to what opened it. */
  #closePalette(giveBack = false) {
    const open = this._open;
    this._open = false;
    this._insertAt = null;
    this.#addTo = null;
    const opener = this.#opener;
    this.#opener = null;
    if (!open) return;
    if (!giveBack) {
      // A pick over a narrow canvas selected what it added, whose inspector the palette kept out of reach until now.
      void this.updateComplete.then(() => {
        if (this.#inspectorMode() === 'overlay' && this._view?.state.selection) this.#inspector?.focusHeading();
      });
      return;
    }
    void this.updateComplete.then(() => {
      if (opener?.isConnected) opener.focus();
      else this.#canvas?.shadowRoot?.querySelector<HTMLElement>('#jfb-canvas-form')?.focus();
    });
  }

  async #onCanvas(request: CanvasRequest) {
    const state = this.#state();
    const model = this.#context?.model;
    if (!state || !model) return;
    switch (request.kind) {
      case 'select':
        this._insertAt = null;
        await this.#apply(select(state, request.key), '');
        return;
      case 'insertAfter': {
        const found = locate(state.definition, request.key);
        if (found?.kind !== 'field') return;
        this._insertAt = { container: found.container.key, index: found.index + 1 };
        this.#openPalette();
        return;
      }
      case 'insertFirst':
        this._insertAt = { container: request.container, index: 0 };
        this.#openPalette();
        return;
      case 'addField': {
        const page = state.definition.pages.find((each) => each.key === request.page);
        const last = page?.groups.at(-1);
        this._insertAt = last ? { container: last.key, index: last.fields.length } : null;
        this.#addTo = request.page;
        this.#openPalette();
        return;
      }
      case 'duplicate': {
        const name = this.#label(request.key);
        await this.#apply(duplicate(state, request.key, model), this.localize.term('jfbDesigner_saidDuplicated', name));
        return;
      }
      case 'delete':
        await this.#delete(request.key);
        return;
      case 'editLabel':
        if (this.#editable(request.key)) this._editing = request.key;
        return;
      case 'label':
        await this.#relabel(request.key, request.text);
        return;
      case 'show':
        this._shown = request.shown;
        this._insertAt = null;
        if (state.selection && request.shown !== 'after') {
          const page = pageOf(state.definition, state.selection);
          if (page?.key !== state.definition.pages[request.shown]?.key) this.#context?.edit(select(state, null), '');
        }
        return;
      case 'addPage': {
        const page = newPage(model.newKey);
        const added = await this.#apply(
          insert(state, 'page', page, { container: null, index: state.definition.pages.length }, model),
          this.localize.term('jfbDesigner_saidPageAdded'),
        );
        if (added) {
          this._shown = state.definition.pages.length;
          await this.#canvas?.updateComplete;
          this.#canvas?.focusAddField();
        }
        return;
      }
      case 'width':
        this._width = request.width;
        try {
          localStorage.setItem(WIDTH_KEY, request.width);
        } catch {
          // A browser that keeps nothing starts at Desktop next time.
        }
        return;
      case 'undo':
      case 'redo':
        this.#travel(request.kind);
        return;
      case 'paste':
        await this.#paste();
        return;
      case 'changeOutcome':
        this.#toSettings();
        return;
      case 'drop':
        await this.#drop(request.source, request.place);
        return;
      case 'refused':
        this.#refuseInPlace(request.source.kind === 'new' ? null : request.source.key, request.reason);
        return;
      case 'moveTo':
        await this.#moveTo(request.key);
        return;
      case 'changeType':
        await this.#changeType(request.key);
        return;
      case 'shortcuts':
        await this.#shortcuts();
        return;
      case 'newPageAfter':
        await this.#newPageAfter(request.key);
        return;
      case 'copyAlias':
        await this.#copyAlias(request.key);
        return;
      case 'culture':
        this._culture = request.culture;
        await this.#readLook();
        return;
    }
  }

  // What the inspector asks: a change of the element it shows, its fold, its Close, and the element's own actions.
  async #onInspector(request: InspectorRequest) {
    switch (request.kind) {
      case 'change':
        await this.#inspected(request.key, request.update, request.said, request.merge);
        return;
      case 'alias':
        await this.#realias(request.key, request.alias, request.revert);
        return;
      // The focus moves once the view has drawn the inspector's new mode: the button pressed is gone by then.
      case 'fold':
        this._inspectorFolded = true;
        void this.updateComplete.then(() => this.#inspector?.focusUnfold());
        return;
      case 'unfold':
        this._inspectorFolded = false;
        void this.updateComplete.then(() => this.#inspector?.focusHeading());
        return;
      case 'close':
        this.#letGo();
        return;
      case 'element':
        await this.#act(request.action, request.key);
        return;
    }
  }

  // A change from the inspector: an edit like any other, which leaves the focus in the box typed in and says nothing
  // aloud, since the box says it already.
  async #inspected(key: string, update: Parameters<typeof change>[2], said: string, merge: string | undefined) {
    const context = this.#context;
    const state = this.#state();
    if (!context || !state) return;
    this.#shownLink = true;
    this._refusal = null;
    const result = context.edit(change(state, key, update, context.model), said, merge);
    if (result.ok) this.#closeUndo();
    else await this.#refuse(result.refusal);
  }

  // An alias the inspector checked. Where entries hold answers under the alias the field is saved with, the change
  // asks first and keeps that alias for the merge tags that use it (section 4.3.1); one changed back gives it up.
  async #realias(key: string, alias: string, revert: () => void) {
    const context = this.#context;
    const found = (() => {
      const state = this.#state();
      return state ? locate(state.definition, key) : undefined;
    })();
    if (!context || found?.kind !== 'field') return;
    const kept = context.keptAliasOf(key);
    const before = found.element.alias ?? '';
    if (kept && before === kept && alias !== kept) {
      const sure = await confirmText(this, {
        headline: this.localize.term('jfbInspector_aliasConfirmHeadline'),
        content: this.localize.term('jfbInspector_aliasConfirmText', kept, alias),
        confirmLabel: this.localize.term('jfbInspector_aliasConfirm'),
        color: 'warning',
      }).then(
        () => true,
        () => false,
      );
      if (!sure) {
        revert();
        return;
      }
    }
    // The field as it is once the question is answered: the form may have changed meanwhile.
    const state = this.#state();
    const now = state ? locate(state.definition, key) : undefined;
    if (!state || now?.kind !== 'field') return;
    const former = [...(now.element.formerAliases ?? [])].filter((one) => one !== alias);
    if (kept && alias !== kept && !former.includes(kept)) former.push(kept);
    const edit = change<DesignField>(
      state,
      key,
      (field) =>
        withKey<unknown>({ ...field, alias }, 'formerAliases', former.length > 0 ? former : undefined) as DesignField,
      context.model,
    );
    const result = context.edit(
      edit,
      this.localize.term('jfbInspector_saidChanged', this.localize.term('jfbInspector_property_alias')),
    );
    if (result.ok) {
      this.#closeUndo();
      this.#say(this.localize.term('jfbInspector_saidChanged', this.localize.term('jfbInspector_property_alias')));
    } else {
      revert();
      await this.#refuse(result.refusal);
    }
  }

  // Close, and Escape in the inspector: the selection goes, and the focus goes back to the element on the canvas.
  #letGo() {
    const state = this.#state();
    const key = state?.selection;
    if (!state || !key) return;
    this.#context?.edit(select(state, null), '');
    void this.#focus(key);
  }

  // The selected element's actions, from the inspector's bar or the element's "...".
  async #act(action: ElementAction, key: string) {
    switch (action) {
      case 'moveTo':
        await this.#moveTo(key);
        return;
      case 'duplicate':
        await this.#onCanvas({ kind: 'duplicate', key });
        return;
      case 'delete':
        await this.#delete(key);
        return;
      case 'changeType':
        await this.#changeType(key);
        return;
      case 'newPageAfter':
        await this.#newPageAfter(key);
        return;
      case 'copyAlias':
        await this.#copyAlias(key);
        return;
    }
  }

  // Start a new page after this: what follows the element on its page moves onto a new page, which is selected.
  async #newPageAfter(key: string) {
    const state = this.#state();
    const model = this.#context?.model;
    const found = state ? locate(state.definition, key) : undefined;
    if (!state || !model || !found || found.kind === 'page') return;
    const name = this.#label(key);
    await this.#apply(
      splitPage(state, { container: found.container.key, index: found.index + 1 }, model),
      this.localize.term('jfbDesigner_saidNewPage', name),
    );
  }

  // Copy alias: the field's alias to the clipboard, said when it is there.
  async #copyAlias(key: string) {
    const state = this.#state();
    const found = state ? locate(state.definition, key) : undefined;
    const alias = found?.kind === 'field' ? found.element.alias : null;
    if (!alias) return;
    try {
      await navigator.clipboard.writeText(alias);
      this.#say(this.localize.term('jfbDesigner_saidCopied', alias));
    } catch {
      (await this.#notifications())?.peek('warning', {
        data: { message: this.localize.term('jfbDesigner_copyFailed') },
      });
    }
  }

  #newFieldOf(alias: string): DesignField | null {
    const type = Object.hasOwn(this._types, alias) ? this._types[alias] : undefined;
    const model = this.#context?.model;
    if (!type || !model) return null;
    return newField(type, this.localize.string(type.name), model.newKey, this.#newWords());
  }

  /**
   * Why the form can't take what is dragged where it would land, asked as the pointer goes from place to place; it
   * changes nothing. A drop where it already is is no refusal: it just stays.
   */
  readonly #refusalOf = (source: DragSource, place: Place | null): string | null => {
    const state = this.#state();
    const model = this.#context?.model;
    if (!state || !model) return null;
    if (!place) return this.#refusalText('wrongPlace');
    const edit = dropEdit(state, source, place, model, (type) => this.#newFieldOf(type));
    if (!edit || edit.ok || edit.refusal === 'unchanged') return null;
    return this.#refusalText(edit.refusal, source.kind === 'new' ? source.label : undefined);
  };

  // A drop where the form takes it: what is dragged moves there, or a field of the type dragged is added there.
  async #drop(source: DragSource, place: Place) {
    const state = source.kind === 'new' ? this.#state() : this.#selectFirst(source.key);
    const model = this.#context?.model;
    if (!state || !model) return;
    const edit = dropEdit(state, source, place, model, (type) => this.#newFieldOf(type));
    if (!edit) return;
    if (source.kind !== 'new') {
      // A page dragged by its step keeps the focus on its step.
      await this.#moved(source.key, edit, { toast: true, control: source.kind === 'page' ? 'step' : 'select' });
      return;
    }
    const said = this.localize.term('jfbDesigner_saidAdded', source.label);
    if (await this.#apply(edit, said, { type: source.label })) this._recent = keepRecent(source.type);
  }

  /**
   * Makes a move: it says where the element is now and, after a drop or Move to..., a toast offers Undo. Keyboard
   * moves come one after another with Ctrl+Z at hand, so they get none.
   */
  async #moved(key: string, edit: Edit, how: { readonly toast: boolean; readonly control?: Control }) {
    const done = await this.#apply(edit, this.localize.term('jfbDesigner_saidMoved', this.#label(key)), {
      // Named after the move: a page or group without a title is named by its place.
      announce: (state) => this.#whereNow(state, key, this.#label(key)),
      ...(how.control ? { control: how.control } : {}),
    });
    if (!done || !how.toast) return;
    const kind = positionOf(this.#state()?.definition ?? { pages: [] }, key)?.kind;
    if (kind) await this.#offerUndo(this.localize.term(`jfbDesigner_moved_${kind}`));
  }

  #pageRef(definition: DesignDefinition, index: number): string {
    return this.localize.term(
      'jfbDesigner_pageRef',
      index + 1,
      textIn(definition.pages[index]?.title, this._look?.culture ?? ''),
    );
  }

  // Where an element is after a move: "Moved. Phone: position 1 of 5 in Address, Page 1: About you."
  #whereNow(state: EditorState, key: string, name: string): string {
    const at = positionOf(state.definition, key);
    if (!at) return this.localize.term('jfbDesigner_saidMoved', name);
    if (at.kind === 'page') return this.localize.term('jfbDesigner_saidMovedPage', name, at.position, at.count);
    const page = this.#pageRef(state.definition, at.page);
    if (at.kind === 'group') return this.localize.term('jfbDesigner_saidMovedGroup', name, at.position, at.count, page);
    const container = at.container ? this.#label(at.container) : '';
    return this.localize.term('jfbDesigner_saidMovedField', name, at.position, at.count, container, page);
  }

  // Alt+Up and Alt+Down: one place up or down, into the next group or page at the end of its own; the focus stays on
  // the control the keys were pressed on. A move the form refuses says why at the element.
  async #step(key: string, by: 1 | -1, control: Control) {
    const state = this.#selectFirst(key);
    const model = this.#context?.model;
    if (!state || !model) return;
    const edit = step(state, key, by, model);
    if (!edit.ok) {
      if (edit.refusal !== 'unchanged') this.#refuseInPlace(key, this.#refusalText(edit.refusal));
      return;
    }
    await this.#moved(key, edit, { toast: false, control });
  }

  #placeName(place: MovePlace): string {
    const state = this.#state();
    const own =
      place.kind === 'page' && state
        ? this.#pageRef(
            state.definition,
            state.definition.pages.findIndex((page) => page.key === place.key),
          )
        : place.kind === 'repeater' && place.within
          ? this.localize.term('jfbDesigner_moveToInside', this.#label(place.key), this.#label(place.within))
          : this.#label(place.key);
    return place.current ? this.localize.term('jfbDesigner_moveToHere', own) : own;
  }

  // Move to...: the places the element can go to the end of, each the form refuses with why. The move is made on the
  // form as it is when the dialog closes; Cancel gives the focus back to the element.
  async #moveTo(key: string) {
    const state = this.#state();
    const model = this.#context?.model;
    if (!state || !model) return;
    const name = this.#label(key);
    const sections = moveToPlaces(state, key, model);
    if (sections.length === 0) return;
    const data: MoveToModalData = {
      name,
      sections: sections.map((section) => ({
        heading: section.page >= 0 ? this.#pageRef(state.definition, section.page) : null,
        places: section.places.map((place) => ({
          key: place.key,
          name: this.#placeName(place),
          refusal: !place.edit.ok && place.edit.refusal !== 'unchanged' ? this.#refusalText(place.edit.refusal) : null,
          disabled: !place.edit.ok,
          inside: place.kind === 'repeater',
        })),
      })),
    };
    const value = await umbOpenModal(this, MOVE_TO_MODAL, { data }).catch(() => undefined);
    const now = this.#state();
    const end = value && now ? endOf(now.definition, value.key) : null;
    if (!now || !end) {
      await this.#focus(key);
      return;
    }
    await this.#moved(key, move(now, key, end, model), { toast: true });
  }

  #fieldType(alias: string): FieldTypeResponseModel | undefined {
    return Object.hasOwn(this._types, alias) ? this._types[alias] : undefined;
  }

  // Change type...: the types the field may become (ADR-0059), each with what it takes from the field and what the
  // field then needs, and each the form or the site refuses with why. The change is made on the form as it is when the
  // dialog closes, and a toast offers Undo; Cancel gives the focus back to the field.
  async #changeType(key: string) {
    if (this.#changingType) return;
    this.#changingType = true;
    try {
      await this.#changeTypeOf(key);
    } finally {
      this.#changingType = false;
    }
  }

  async #changeTypeOf(key: string) {
    const context = this.#context;
    const before = this.#state();
    const found = before ? locate(before.definition, key) : undefined;
    if (!context || found?.kind !== 'field') return;
    const changes = await context.changesOf(found.element.type);
    if (!changes) {
      (await this.#notifications())?.peek('danger', {
        data: { message: this.localize.term('jfbDesigner_changeTypeFailed') },
      });
      await this.#focus(key);
      return;
    }
    // The form as it is once the changes are read, which the dialog shows.
    const state = this.#state();
    const field = state ? locate(state.definition, key) : undefined;
    if (!state || field?.kind !== 'field') return;
    const from = this.#fieldType(field.element.type);
    const hasEntries = context.keptAliasOf(key) !== null;
    const words = {
      term: (term: string, ...args: unknown[]) => this.localize.term(term, ...args),
      say: (text: string | null | undefined) => (text ? this.localize.string(text) : ''),
      culture: this._look?.culture ?? '',
      nameOf: (element: string) => this.#label(element),
    };
    const targets = changes.flatMap((each): ChangeTypeTarget[] => {
      const to = this.#fieldType(each.to);
      if (!to) return [];
      const name = this.localize.string(to.name);
      const edit = change(state, key, (one) => changedField(one as DesignField, to, each), context.model);
      const refusal = !to.availability.isAvailable
        ? this.localize.string(to.availability.reason ?? '')
        : !edit.ok && edit.refusal !== 'unchanged'
          ? edit.refusal === 'notInRepeater'
            ? this.localize.term('jfbDesigner_changeTypeNotInRepeater', name)
            : this.#refusalText(edit.refusal, name)
          : null;
      const group = this._groups.find((one) => one.alias === to.paletteGroup);
      return [
        {
          alias: to.alias,
          name,
          group: group ? this.localize.string(group.name) : to.paletteGroup,
          refusal,
          words: changeWords(
            words,
            field.element,
            previewChange(state.definition, field.element, from, to, each),
            hasEntries,
          ),
        },
      ];
    });
    const name = this.#label(key);
    const value = await umbOpenModal(this, CHANGE_TYPE_MODAL, {
      data: { name, type: this.#typeName(field.element.type), targets },
    }).catch(() => undefined);
    // A type the dialog shows as refused is never changed to.
    const offered = targets.find((target) => target.alias === value?.to && target.refusal === null);
    const chosen = offered ? changes.find((each) => each.to === offered.alias) : undefined;
    const to = chosen ? this.#fieldType(chosen.to) : undefined;
    // The field changed is the one selected, so the focus goes back to it.
    const now = chosen ? this.#selectFirst(key) : this.#state();
    if (!now || !chosen || !to) {
      await this.#focus(key);
      return;
    }
    const type = this.#typeName(to.alias);
    const done = await this.#apply(
      change(now, key, (one) => changedField(one as DesignField, to, chosen), context.model),
      this.localize.term('jfbDesigner_saidTypeChanged', name, type),
      { type },
    );
    if (done) await this.#offerUndo(this.localize.term('jfbDesigner_typeChanged', type));
  }

  async #shortcuts() {
    const opener = deepFocus();
    await umbOpenModal(this, SHORTCUTS_MODAL).catch(() => undefined);
    if (opener?.isConnected) opener.focus();
  }

  // Whether a field's label can be edited in place: a field with a label, a heading's text, one box without its own text.
  #editable(key: string): boolean {
    const state = this.#state();
    const found = state ? locate(state.definition, key) : undefined;
    if (found?.kind !== 'field') return false;
    const type = Object.hasOwn(this._types, found.element.type) ? this._types[found.element.type] : undefined;
    const culture = this._look?.culture ?? '';
    if (found.element.type === 'heading')
      return textIn((found.element['settings'] as { text?: LocalizedText } | undefined)?.text, culture).length > 0;
    if (!type?.holdsValue || found.element.type === 'hidden') return false;
    const own = (found.element['settings'] as { text?: LocalizedText } | undefined)?.text;
    return !(
      (found.element.type === 'checkbox' || found.element.type === 'consent') &&
      textIn(own, culture).length > 0
    );
  }

  async #relabel(key: string, text: string | null) {
    this._editing = null;
    const state = this.#state();
    const model = this.#context?.model;
    const kept = text?.trim() ?? '';
    if (!state || !model || kept.length === 0) {
      await this.#focus(key);
      return;
    }
    const culture = this._look?.culture ?? '';
    const standard = this._look?.languages.find((language) => language.isDefault)?.culture ?? culture;
    const isDefault = culture.toLowerCase() === standard.toLowerCase();
    // In another language than the default, the default's words kept as the canvas showed them are no text of that
    // language's own: the field stays as it was.
    const written = (texts: LocalizedText | null | undefined) =>
      !isDefault && !ownText(texts, culture) && kept === textIn(texts, culture)
        ? texts
        : withText(texts, culture, kept, isDefault);
    const edit = change<DesignField>(
      state,
      key,
      (field) => {
        if (field.type !== 'heading') return { ...field, label: written(field.label) ?? null };
        const settings = (field['settings'] as Readonly<Record<string, unknown>> | undefined) ?? {};
        return { ...field, settings: { ...settings, text: written(settings['text'] as LocalizedText | undefined) } };
      },
      model,
    );
    if (!(await this.#apply(edit, this.localize.term('jfbDesigner_saidRenamed', kept)))) await this.#focus(key);
  }

  async #delete(key: string) {
    const state = this.#selectFirst(key);
    const found = state ? locate(state.definition, key) : undefined;
    if (!state || !found) return;
    const name = this.#label(key);
    const done = await this.#apply(removeElement(state, key), this.localize.term('jfbDesigner_saidDeleted', name));
    if (done) await this.#offerUndo(this.localize.term(`jfbDesigner_deleted_${found.kind}`, name));
  }

  /**
   * Selects the element a key or a drop acts on before it changes, so the step Undo goes back to holds it selected and
   * the focus returns to it. Selecting is no step of its own.
   */
  #selectFirst(key: string): EditorState | undefined {
    const state = this.#state();
    if (state && state.selection !== key && locate(state.definition, key)) this.#context?.edit(select(state, key), '');
    return this.#state();
  }

  // After an undo or a redo that leaves nothing selected, a focus whose element went goes to the page's Add field.
  async #keepFocus() {
    await this.updateComplete;
    await this.#canvas?.updateComplete;
    const now = deepFocus();
    if (!now || now === document.body) this.#canvas?.focusAddField();
  }

  // A toast with Undo for the change just made, closed by the next one: its Undo would take back another.
  async #offerUndo(message: string) {
    const made = this.#state();
    const data: UndoNotificationData = {
      message,
      undoLabel: this.localize.term('jfbDesigner_undo'),
      undo: async () => {
        if (this.#state() === made) this.#travel('undo');
      },
    };
    this.#undoToast = (await this.#notifications())?.peek('positive', {
      elementName: UNDO_NOTIFICATION,
      duration: UNDO_DURATION_MS,
      data,
    });
  }

  /** Undoes or redoes the last change, focusing what it restored; `stay` leaves the focus where it is. */
  #travel(way: 'undo' | 'redo', stay = false) {
    const context = this.#context;
    if (!context) return;
    const said = way === 'undo' ? context.undo() : context.redo();
    if (said === undefined) return;
    this.#inspector?.forget();
    this.#closeUndo();
    this._refusal = null;
    this.#say(this.localize.term(way === 'undo' ? 'jfbDesigner_saidUndone' : 'jfbDesigner_saidRedone', said));
    const state = context.getEditorState();
    const { definition } = state;
    const { selection, dropped } = this.#settle(state);
    if (selection) {
      const index = definition.pages.findIndex((page) => page.key === pageOf(definition, selection)?.key);
      if (index >= 0) this._shown = index;
      if (!stay) void this.#focus(selection);
    } else if (dropped) {
      void this.#focusAddField();
    } else {
      void this.#keepFocus();
    }
  }

  // Where a pick from the palette goes, and the page it goes on.
  #target(): { place: Place | null; page: string } | undefined {
    const state = this.#state();
    if (!state) return undefined;
    const shown = typeof this._shown === 'number' ? this._shown : Math.max(0, state.definition.pages.length - 1);
    const place = placeFor(state.definition, this._insertAt, state.selection, shown);
    const page =
      this.#addTo ??
      (place?.container ? pageOf(state.definition, place.container)?.key : undefined) ??
      state.definition.pages[shown]?.key;
    return page ? { place, page } : undefined;
  }

  async #onPalette(pick: PalettePick) {
    const state = this.#state();
    const model = this.#context?.model;
    if (!state || !model) return;
    switch (pick.kind) {
      case 'fold':
        this._folded = true;
        this.#palette?.focusUnfold();
        return;
      case 'unfold':
        // Folded beside the inspector, the palette opens over the canvas; folded by its own button, beside it again.
        if (this.#crowded()) this.#openPalette();
        else {
          this._folded = false;
          this.#palette?.focusSearch();
        }
        return;
      case 'close':
        this.#closePalette(true);
        return;
      case 'unavailable':
        (await this.#notifications())?.peek('warning', { data: { message: pick.reason } });
        return;
      case 'paste':
        await this.#paste();
        return;
      case 'drag':
        this.#canvas?.dragNew(pick.event, pick.handle, pick.type, pick.label);
        return;
      case 'field': {
        const type = Object.hasOwn(this._types, pick.type) ? this._types[pick.type] : undefined;
        const target = this.#target();
        if (!type || !target) return;
        const name = this.localize.string(type.name);
        const field = newField(type, name, model.newKey, this.#newWords());
        const done = await this.#apply(
          addFields(state, [field], target.place, target.page, model),
          this.localize.term('jfbDesigner_saidAdded', name),
          { type: name },
        );
        if (done) {
          this._recent = keepRecent(pick.type);
          this.#closePalette();
        }
        return;
      }
      case 'group': {
        const target = this.#target();
        if (!target) return;
        if (
          await this.#apply(
            addGroup(state, target.place, target.page, model),
            this.localize.term('jfbDesigner_saidGroupAdded'),
          )
        )
          this.#closePalette();
        return;
      }
      case 'page': {
        const target = this.#target();
        if (!target) return;
        const index = state.definition.pages.findIndex((page) => page.key === target.page);
        const place =
          this._insertAt ?? (state.selection ? placeFor(state.definition, null, state.selection, index) : null);
        const edit = place
          ? splitPage(state, place, model)
          : insert(state, 'page', newPage(model.newKey), { container: null, index: index + 1 }, model);
        if (await this.#apply(edit, this.localize.term('jfbDesigner_saidPageAdded'))) this.#closePalette();
        return;
      }
    }
  }

  #newWords() {
    return {
      option: (number: number) => this.localize.term('jfbDesigner_newOption', number),
      row: (number: number) => this.localize.term('jfbDesigner_newRow', number),
    };
  }

  async #paste() {
    const names = Object.fromEntries(
      Object.values(this._types)
        .filter((type) => type.availability.isAvailable)
        .map((type) => [type.alias, this.localize.string(type.name)]),
    );
    const value = await umbOpenModal(this, PASTE_QUESTIONS_MODAL, { data: { types: names } }).catch(() => undefined);
    const state = this.#state();
    const model = this.#context?.model;
    const target = this.#target();
    if (!value || !state || !model || !target) return;
    const fields = value.questions.flatMap((question) => {
      const type = Object.hasOwn(this._types, question.type) ? this._types[question.type] : undefined;
      return type ? [newField(type, question.label, model.newKey, this.#newWords())] : [];
    });
    const done = await this.#apply(
      addFields(state, fields, target.place, target.page, model),
      this.localize.term('jfbDesigner_saidAddedMany', fields.length),
    );
    if (done) this.#closePalette();
  }

  // The Settings view, where a form's outcomes are edited.
  #toSettings() {
    const path = window.location.pathname.replace(/\/view\/[^/]+\/?$/, '') + '/view/settings';
    history.pushState(null, '', path);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }

  #onKey(event: KeyboardEvent) {
    // While a drag lasts the form stays as it is: the drag takes Escape itself.
    if (isDragging()) return;
    const path = event.composedPath();
    const target = path[0] as HTMLElement | undefined;
    // A box that takes text has its own Undo; a list box takes Escape, a switch neither.
    const entry =
      (target instanceof HTMLInputElement && !NOT_TEXT.has(target.type)) ||
      target instanceof HTMLTextAreaElement ||
      target?.isContentEditable === true;
    const typing = entry || target instanceof HTMLSelectElement;
    const inInspector = path.some((node) => node instanceof HTMLElement && node.localName === 'jfb-designer-inspector');
    const ctrl = event.ctrlKey || event.metaKey;
    const key = event.key.toLowerCase();
    const state = this.#state();
    const selection = state?.selection ?? null;
    if (key === 'escape') {
      // An open menu closes itself and gives the focus back to its button.
      if (path.some((node) => node instanceof HTMLElement && node.matches('[popover]:popover-open'))) return;
      if (this._open) this.#closePalette(true);
      else if (this._insertAt) this._insertAt = null;
      else if (state && selection && !typing && inInspector) this.#letGo();
      else if (state && selection && !typing) this.#context?.edit(select(state, null), '');
      else return;
      event.preventDefault();
      return;
    }
    if (entry) return;
    if (inInspector) {
      // From the inspector, Undo and Redo leave the focus where it is, ? lists the shortcuts, and nothing else acts.
      if (ctrl && key === 'z' && !event.shiftKey) this.#travel('undo', true);
      else if (ctrl && (key === 'y' || (key === 'z' && event.shiftKey))) this.#travel('redo', true);
      else if (event.key === '?' && !ctrl && !event.altKey) void this.#shortcuts();
      else return;
      event.preventDefault();
      return;
    }
    if (typing) return;
    // The keys act on the element the focus is on, which Up and Down move to, else on the selection.
    const control = this.#controlOf(event);
    const acted = control?.key ?? selection;
    const vertical = event.key === 'ArrowUp' ? -1 : event.key === 'ArrowDown' ? 1 : 0;
    if (vertical !== 0 && !ctrl && !event.shiftKey) {
      if (event.altKey) {
        if (!acted) return;
        void this.#step(acted, vertical, control?.control ?? 'select');
      } else if (control && control.control !== 'step') this.#canvas?.focusNext(control.key, vertical);
      else return;
      event.preventDefault();
      return;
    }
    if (event.key === '?' && !ctrl && !event.altKey) void this.#shortcuts();
    else if (ctrl && key === 'z' && !event.shiftKey) this.#travel('undo');
    else if (ctrl && (key === 'y' || (key === 'z' && event.shiftKey))) this.#travel('redo');
    else if (ctrl && key === 'd' && acted && this.#selectFirst(acted))
      void this.#onCanvas({ kind: 'duplicate', key: acted });
    else if (event.key === 'F2' && acted && this.#selectFirst(acted))
      void this.#onCanvas({ kind: 'editLabel', key: acted });
    else if (event.key === 'Delete' && acted) void this.#delete(acted);
    else return;
    event.preventDefault();
  }

  // The canvas control a key was pressed on: the button that selects an element, its handle, or a page's step.
  #controlOf(event: Event): { key: string; control: Control } | null {
    const target = event.composedPath()[0];
    if (!(target instanceof HTMLElement)) return null;
    const key = target.dataset['key'];
    const control = target.dataset['control'];
    return key && (control === 'select' || control === 'handle' || control === 'step') ? { key, control } : null;
  }

  #failed() {
    return html`<div class="failed" role="alert">
      <p>${this.localize.term('jfbDesigner_canvasFailed')}</p>
      <uui-button
        look="secondary"
        label=${this.localize.term('jfbDesigner_tryAgain')}
        @click=${() => void this.#readLook()}
      ></uui-button>
    </div>`;
  }

  #paletteTypes(): FieldTypeResponseModel[] {
    if (this.#typeList.of !== this._types) this.#typeList = { of: this._types, list: Object.values(this._types) };
    return this.#typeList.list;
  }

  /** Whether the palette folds for the inspector: an element is selected in a view up to 1024 px. */
  #crowded(): boolean {
    return this._crowded && !this._narrow && Boolean(this._view?.state.selection) && !this._inspectorFolded;
  }

  #mode(): PaletteMode {
    if (this._narrow) return 'overlay';
    if (this.#crowded()) return this._open ? 'overlay' : 'rail';
    return this._folded ? 'rail' : 'docked';
  }

  #inspectorMode(): InspectorMode {
    if (this._narrow) return 'overlay';
    return this._inspectorFolded ? 'rail' : 'docked';
  }

  override render() {
    const view = this._view;
    const mode = this.#mode();
    const selection = view?.state.selection ?? null;
    const inspector = this.#inspectorMode();
    const inspects = selection !== null && this._look !== undefined && !this._failed;
    // A button, not a link: Umbraco's router takes every link's click for a move to another place.
    return html`<button
        type="button"
        class="skip"
        @click=${() => this.#canvas?.shadowRoot?.querySelector<HTMLElement>('#jfb-canvas-form')?.focus()}
      >
        ${this.localize.term('jfbDesigner_skipToForm')}
      </button>
      ${
        mode === 'overlay' && this._open
          ? html`<div class="scrim" @click=${() => this.#closePalette(true)}></div>`
          : nothing
      }
      ${
        mode !== 'overlay' || this._open
          ? html`<jfb-designer-palette
              .types=${this.#paletteTypes()}
              .groups=${this._groups}
              .recent=${this._recent}
              mode=${mode}
            ></jfb-designer-palette>`
          : nothing
      }
      ${this._failed ? this.#failed() : nothing}
      <jfb-form-canvas
        ?hidden=${this._failed}
        ?inert=${(mode === 'overlay' && this._open) || (inspects && inspector === 'overlay')}
        .definition=${view?.state.definition ?? { pages: [] }}
        .selection=${view?.state.selection ?? null}
        .shown=${this._shown}
        .look=${this._look}
        .sheets=${this._sheets}
        .types=${this._types}
        .mediaUrls=${this._media}
        .resolvedOptions=${this._resolved}
        .width=${this._width}
        .insertAt=${this._insertAt}
        .editing=${this._editing}
        .canUndo=${view?.canUndo ?? false}
        .canRedo=${view?.canRedo ?? false}
        .refusalOf=${this.#refusalOf}
        .refusal=${this._refusal}
      ></jfb-form-canvas>
      ${
        inspects
          ? html`<jfb-designer-inspector
              ?inert=${mode === 'overlay' && this._open}
              .definition=${view?.state.definition ?? { pages: [] }}
              .selection=${selection}
              .types=${this._types}
              .culture=${this._look?.culture ?? ''}
              .languages=${this._look?.languages ?? []}
              .form=${this.#context?.getUnique() ?? ''}
              mode=${inspector}
            ></jfb-designer-inspector>`
          : nothing
      }
      <div class="visually-hidden" role="status" aria-live="polite">${this._said}</div>`;
  }
}

if (!customElements.get('jfb-form-design-view')) customElements.define('jfb-form-design-view', FormDesignViewElement);

export { FormDesignViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-design-view': FormDesignViewElement;
  }
}
