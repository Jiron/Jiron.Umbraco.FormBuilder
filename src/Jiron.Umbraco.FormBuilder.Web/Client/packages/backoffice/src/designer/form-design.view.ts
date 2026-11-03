// The Design view (ROADMAP 9.3b, docs/ux/information-architecture.md section 5.1): the palette beside the canvas. It
// takes the canvas's and the palette's requests and makes each an edit of the editor model through the workspace, says
// what happened for screen readers, offers Undo after a delete, and moves the focus to what an edit left selected. A
// link that names a field (`?field=`, ADR-0166) opens the view on that field's page with the field selected.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UmbMediaUrlRepository } from '@umbraco-cms/backoffice/media';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { FieldGroupResponseModel, FieldTypeResponseModel } from '../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../ui/undo-notification.element';
import { CANVAS_REQUEST, pageIsBare, type CanvasRequest, type CanvasShown } from './canvas/form-canvas.element';
import { CANVAS_WIDTHS, type CanvasWidth } from './canvas/widths';
import type { FormCanvasElement } from './canvas/form-canvas.element';
import './canvas/form-canvas.element';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { locate, pageOf, type DesignDefinition, type DesignField, type LocalizedText } from './definition';
import type { EditorState } from './editor-model';
import {
  change,
  duplicate,
  insert,
  removeElement,
  select,
  splitPage,
  type Edit,
  type Place,
  type Refusal,
} from './editor-model';
import type { EditorView, FormWorkspaceContext } from './form-workspace.context';
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
import { keyIn, textIn } from './preview/field-models';
import { readLook, themeSheets, type CanvasLook } from './preview/look';

/**
 * Up to this width, the palette opens over the canvas; wider, it stays beside it (IA 5.1). The narrowest workspace,
 * 620 px, is in this range; Umbraco's own header keeps the view from getting narrower.
 */
export const NARROW = 700;

/** Whether a view this wide puts the palette over the canvas. */
export const isNarrow = (width: number) => width <= NARROW;

const WIDTH_KEY = 'jfb.designer.width';

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
    _shown: { state: true },
    _width: { state: true },
    _insertAt: { state: true },
    _editing: { state: true },
    _folded: { state: true },
    _open: { state: true },
    _narrow: { state: true },
    _recent: { state: true },
    _said: { state: true },
    _failed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        position: relative;
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        height: 100%;
        /* The view takes the width it is given, so it can measure when that is too narrow for the docked palette. */
        min-width: 0;
        min-height: 0;
      }

      jfb-designer-palette {
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

      jfb-form-canvas {
        min-width: 0;
        min-height: 0;
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
  declare private _shown: CanvasShown;
  declare private _width: CanvasWidth;
  declare private _insertAt: Place | null;
  declare private _editing: string | null;
  declare private _folded: boolean;
  declare private _open: boolean;
  declare private _narrow: boolean;
  declare private _recent: readonly string[];
  declare private _said: string;
  /** Whether what the canvas draws with could not be read. */
  declare private _failed: boolean;

  #context: FormWorkspaceContext | undefined;
  /** The page the palette adds to when it was opened from a page's Add field and the page has no group. */
  #addTo: string | null = null;
  /** Whether the selection a link named was brought into sight. */
  #shownLink = false;
  #mediaRead = new Set<string>();
  #mediaUrls: UmbMediaUrlRepository | undefined;
  /** The toast that offers Undo after a delete, closed by the next change: its Undo would take back another. */
  #undoToast: { close(): void } | undefined;
  /** What opened the palette over the canvas, where the focus goes back when it closes without a pick. */
  #opener: HTMLElement | null = null;
  #resize = new ResizeObserver(([entry]) => {
    const narrow = entry ? isNarrow(entry.contentRect.width) : false;
    if (narrow !== this._narrow) this._narrow = narrow;
  });

  constructor() {
    super();
    this._types = {};
    this._groups = [];
    this._sheets = [];
    this._media = {};
    this._shown = 0;
    this._width = readWidth();
    this._insertAt = null;
    this._editing = null;
    this._folded = false;
    this._open = false;
    this._narrow = false;
    this._recent = readRecent();
    this._said = '';
    this._failed = false;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.editor, (view) => this.#viewChanged(view), 'editor');
      this.observe(context?.fieldTypes, (types) => (this._types = types ?? {}), 'types');
      this.observe(context?.paletteGroups, (groups) => (this._groups = groups ?? []), 'groups');
    });
    void this.#readLook();
    this.addEventListener(CANVAS_REQUEST, (event) => void this.#onCanvas((event as CustomEvent<CanvasRequest>).detail));
    this.addEventListener(PALETTE_PICK, (event) => void this.#onPalette((event as CustomEvent<PalettePick>).detail));
    this.addEventListener('keydown', (event) => this.#onKey(event));
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#resize.observe(this);
  }

  override disconnectedCallback() {
    this.#resize.disconnect();
    super.disconnectedCallback();
  }

  async #readLook() {
    this._failed = false;
    const look = await readLook(this);
    if (!look) {
      this._failed = true;
      return;
    }
    this._look = look;
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
  }

  async #readMedia(definition: DesignDefinition) {
    const wanted = mediaOf(definition).filter((key) => !this.#mediaRead.has(key));
    if (wanted.length === 0) return;
    for (const key of wanted) this.#mediaRead.add(key);
    this.#mediaUrls ??= new UmbMediaUrlRepository(this);
    const { data } = await this.#mediaUrls.requestItems(wanted);
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

  async #focus(key: string) {
    await this.updateComplete;
    const canvas = this.#canvas;
    if (!canvas) return;
    await canvas.updateComplete;
    canvas.focusElement(key);
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

  async #refuse(refusal: Refusal, type?: string) {
    if (refusal === 'unchanged') return;
    const message = this.localize.term(`jfbDesigner_refusal_${refusal}`, type ?? '');
    this.#say(message);
    (await this.#notifications())?.peek('warning', { data: { message } });
  }

  /** Applies an edit, says it, and moves the focus to what it left selected. */
  async #apply(edit: Edit, said: string, type?: string): Promise<boolean> {
    const context = this.#context;
    if (!context) return false;
    const result = context.edit(edit, said);
    if (!result.ok) {
      await this.#refuse(result.refusal, type);
      return false;
    }
    this.#closeUndo();
    if (said) this.#say(said);
    const { selection, dropped } = this.#settle(result.state);
    if (selection) {
      const page = pageOf(result.state.definition, selection);
      const index = result.state.definition.pages.findIndex((each) => each.key === page?.key);
      if (index >= 0) this._shown = index;
      await this.#focus(selection);
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

  #openPalette() {
    if (this._narrow && !this._open) this.#opener = deepFocus();
    if (this._narrow) this._open = true;
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
    if (!giveBack || !open) return;
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
    }
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
    const edit = change<DesignField>(
      state,
      key,
      (field) => {
        if (field.type !== 'heading')
          return { ...field, label: { ...(field.label ?? {}), [keyIn(field.label, culture)]: kept } };
        const text = (field['settings'] as { text?: LocalizedText } | undefined)?.text ?? {};
        return {
          ...field,
          settings: {
            ...((field['settings'] as object | undefined) ?? {}),
            text: { ...text, [keyIn(text, culture)]: kept },
          },
        };
      },
      model,
    );
    if (!(await this.#apply(edit, this.localize.term('jfbDesigner_saidRenamed', kept)))) await this.#focus(key);
  }

  async #delete(key: string) {
    const state = this.#state();
    if (!state) return;
    const found = locate(state.definition, key);
    if (!found) return;
    const name = this.#label(key);
    const done = await this.#apply(removeElement(state, key), this.localize.term('jfbDesigner_saidDeleted', name));
    if (!done) return;
    const deleted = this.#state();
    const data: UndoNotificationData = {
      message: this.localize.term(`jfbDesigner_deleted_${found.kind}`, name),
      undoLabel: this.localize.term('jfbDesigner_undo'),
      undo: async () => {
        if (this.#state() === deleted) this.#travel('undo');
      },
    };
    this.#undoToast = (await this.#notifications())?.peek('positive', {
      elementName: UNDO_NOTIFICATION,
      duration: UNDO_DURATION_MS,
      data,
    });
  }

  #travel(way: 'undo' | 'redo') {
    const context = this.#context;
    if (!context) return;
    const said = way === 'undo' ? context.undo() : context.redo();
    if (said === undefined) return;
    this.#closeUndo();
    this.#say(this.localize.term(way === 'undo' ? 'jfbDesigner_saidUndone' : 'jfbDesigner_saidRedone', said));
    const state = context.getEditorState();
    const { definition } = state;
    const { selection, dropped } = this.#settle(state);
    if (selection) {
      const index = definition.pages.findIndex((page) => page.key === pageOf(definition, selection)?.key);
      if (index >= 0) this._shown = index;
      void this.#focus(selection);
    } else if (dropped) {
      void this.#focusAddField();
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
        this._folded = false;
        this.#palette?.focusSearch();
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
      case 'field': {
        const type = Object.hasOwn(this._types, pick.type) ? this._types[pick.type] : undefined;
        const target = this.#target();
        if (!type || !target) return;
        const name = this.localize.string(type.name);
        const field = newField(type, name, model.newKey, this.#newWords());
        const done = await this.#apply(
          addFields(state, [field], target.place, target.page, model),
          this.localize.term('jfbDesigner_saidAdded', name),
          name,
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
    const target = event.composedPath()[0] as HTMLElement | undefined;
    const typing =
      target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target?.isContentEditable;
    const ctrl = event.ctrlKey || event.metaKey;
    const key = event.key.toLowerCase();
    const state = this.#state();
    const selection = state?.selection ?? null;
    if (key === 'escape') {
      if (this._open) this.#closePalette(true);
      else if (this._insertAt) this._insertAt = null;
      else if (state && selection && !typing) this.#context?.edit(select(state, null), '');
      else return;
      event.preventDefault();
      return;
    }
    if (typing) return;
    if (ctrl && key === 'z' && !event.shiftKey) this.#travel('undo');
    else if (ctrl && (key === 'y' || (key === 'z' && event.shiftKey))) this.#travel('redo');
    else if (ctrl && key === 'd' && selection) void this.#onCanvas({ kind: 'duplicate', key: selection });
    else if (event.key === 'F2' && selection) void this.#onCanvas({ kind: 'editLabel', key: selection });
    else if (event.key === 'Delete' && selection) void this.#delete(selection);
    else return;
    event.preventDefault();
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

  #mode(): PaletteMode {
    if (this._narrow) return 'overlay';
    return this._folded ? 'rail' : 'docked';
  }

  override render() {
    const view = this._view;
    const mode = this.#mode();
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
              .types=${Object.values(this._types)}
              .groups=${this._groups}
              .recent=${this._recent}
              mode=${mode}
            ></jfb-designer-palette>`
          : nothing
      }
      ${this._failed ? this.#failed() : nothing}
      <jfb-form-canvas
        ?hidden=${this._failed}
        ?inert=${mode === 'overlay' && this._open}
        .definition=${view?.state.definition ?? { pages: [] }}
        .selection=${view?.state.selection ?? null}
        .shown=${this._shown}
        .look=${this._look}
        .sheets=${this._sheets}
        .types=${this._types}
        .mediaUrls=${this._media}
        .width=${this._width}
        .insertAt=${this._insertAt}
        .editing=${this._editing}
        .canUndo=${view?.canUndo ?? false}
        .canRedo=${view?.canRedo ?? false}
      ></jfb-form-canvas>
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
