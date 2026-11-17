// The Design view (ROADMAP 9.3b and 22.1, docs/ux/information-architecture.md section 5.1, ADR-0256): the canvas and
// the inspector beside it. It takes the canvas's and the inspector's requests and makes each an edit of the editor
// model through the workspace, says what happened for screen readers, offers Undo after a delete, and moves the focus
// to what an edit left selected. Every Add field and insert point opens the field picker, whose pick lands where it was
// opened. A page is renamed, duplicated and deleted at its tab. A link that names a field (`?field=`, ADR-0166) opens
// the view on that field's page with the field selected. Moves (ADR-0016, ADR-0169) come from a drop, Move to... and
// Alt+Up and Alt+Down; each says where the element is now, a move the form refuses says why at the element, and the
// focus stays on the element moved. Up and Down go through the elements of the page, and ? opens the Keyboard
// shortcuts. The inspector (ADR-0170) shows the selected element, else the open page, in its column beside the canvas,
// so selecting moves nothing (ADR-0182), or over the canvas up to 700 px. Its changes are edits too, typing in one box
// one step to undo, and an alias that entries know changes only after a question. A field that takes its options from
// elsewhere shows them as the server resolves the draft's (ADR-0172). After sending's outcomes stand in the inspector's
// column beside the canvas, else under what visitors get; a Fix it in an outcome, and a rule of one in the rules
// overview, open its sidebar there.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { ImageCropModeModel } from '@umbraco-cms/backoffice/external/backend-api';
import { UmbImagingRepository } from '@umbraco-cms/backoffice/imaging';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { confirmText } from '../ui/confirm';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import type { FieldGroupResponseModel, FieldTypeResponseModel } from '../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../ui/undo-notification.element';
import type { DragSource } from './canvas/canvas-drag';
import {
  CANVAS_REQUEST,
  pageIsBare,
  type CanvasRequest,
  type CanvasShown,
  type CheckBadge,
  type InPlaceRefusal,
} from './canvas/form-canvas.element';
import { bySeverity, parsePath, type FixTarget } from './check/form-check';
import type { CheckState } from './check/form-checker';
import { FORM_CHECK_MODAL } from './check/form-check.modal.token';
import { followFix, routerSettled } from './form-views';
import './outcomes/form-outcomes.element';
import type { FormOutcomesElement } from './outcomes/form-outcomes.element';
import { keepWidth, readWidth, type CanvasWidth } from './canvas/widths';
import { isDragging } from './drag/pointer-drag';
import type { FormCanvasElement } from './canvas/form-canvas.element';
import './canvas/form-canvas.element';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { createExtensionApi } from '@umbraco-cms/backoffice/extension-api';
import { designerActions, EXTENSIONS_CHANGED } from '../extending/extension-registry';
import { designerHost } from './designer-host';
import type { DesignerActionItem } from './canvas/form-canvas.element';
import { allFields, locate, pageOf, type DesignDefinition, type DesignField, type LocalizedText } from './definition';
import { resolveOptions } from './inspector/option-data';
import type { ResolvedOption } from './preview/field-models';
import type { EditorState } from './editor-model';
import {
  change,
  duplicate,
  freshCopyWith,
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
import { endOf, moveToPlaces, positionOf, type MovePlace } from './moves/moves';
import { SHORTCUTS_MODAL } from './moves/shortcuts.modal.token';
import { addFields, addGroup, newField, newPage, placeFor } from './new-elements';
import { FIELD_PICKER_MODAL, type FieldPick } from './picker/field-picker.modal.token';
import { maySaveGroups, readSavedGroups, saveGroup, type SavedGroup } from './saved-groups';
import { sayRefusal } from '../library/library-changes';
import { TEMPLATE_NAME_MODAL } from '../library/modals/tokens';
import { PASTE_QUESTIONS_MODAL } from './picker/paste-questions.modal.token';
import { RULE_BUILDER_MODAL } from './rules/rule-builder.modal.token';
import { builderEffectsOf, clashOf, sameRule, type Effect } from './rules/rule-draft';
import { dropBuilt, landBuilt } from './rules/rule-edits';
import { rulesOn } from './rules/rule-list';
import { elementNames, typeFactsOf } from './rules/rule-names';
import { RULES_OVERVIEW_MODAL } from './rules/rules-overview.modal.token';
import { textIn } from './preview/field-models';
import { DictionaryReader, dictionaryKeysIn, type DictionaryLookups } from './dictionary-texts';
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
 * Up to this width the canvas is alone, and the inspector lies over it for a selection (IA 5.1); wider, the inspector's
 * column stands beside it. The narrowest workspace, 620 px, is in this range; Umbraco's own header keeps the view from
 * getting narrower.
 */
export const NARROW = 700;

/** Whether a view this wide puts the inspector over the canvas. */
export const isNarrow = (width: number) => width <= NARROW;

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

/** The control of an element the focus goes back to: the button that selects it, its handle, or a page's tab. */
type Control = 'select' | 'handle' | 'tab';

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

/**
 * What the form check's badge says of its state: nothing before the first ask, Checking... until the first answer, the
 * last answer's errors and warnings while the next is awaited, or that the check gave no answer.
 */
export function badgeOf(check: CheckState | undefined): CheckBadge {
  if (!check || check.status === 'idle') return { kind: 'none' };
  if (check.status === 'failed') return { kind: 'failed' };
  if (!check.definition) return { kind: 'checking' };
  const { error, warning } = bySeverity(check.issues);
  return { kind: 'checked', errors: error.length, warnings: warning.length };
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
    _lookups: { state: true },
    _shown: { state: true },
    _width: { state: true },
    _insertAt: { state: true },
    _editing: { state: true },
    _narrow: { state: true },
    _inspectorFolded: { state: true },
    _culture: { state: true },
    _said: { state: true },
    _failed: { state: true },
    _refusal: { state: true },
    _badge: { state: true },
    _saved: { state: true },
    _saveGroups: { state: true },
    _actions: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        position: relative;
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        grid-template-rows: minmax(0, 1fr);
        height: 100%;
        /* The view takes the width it is given, so it can measure when that is too narrow for the inspector beside it. */
        min-width: 0;
        min-height: 0;
        /*
         * The workspace's scroll container fades its scrollbar in and out as the pointer or the focus comes and goes,
         * by a transition of this inherited property; set here, it stops at the view, so a form of hundreds of fields
         * is not restyled at every frame of the fade (section 4.12). The canvas and the inspector keep the colour the
         * container shows while in use.
         */
        scrollbar-color: var(--uui-color-disabled-standalone) transparent;
      }

      :host([narrow]) {
        grid-template-columns: minmax(0, 1fr);
      }

      jfb-form-canvas,
      .failed {
        grid-column: 1;
        grid-row: 1;
        min-width: 0;
        min-height: 0;
      }

      jfb-designer-inspector {
        grid-column: 2;
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
        /* The whole view: in its one column, line 2 is the view's end edge, which would leave the inspector no width. */
        grid-column: 1 / -1;
        inset: 0;
        z-index: 10;
        width: auto;
        border-inline-start: 0;
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
      }

      /* Out of sight without a box until it takes the focus: one moved above the view lay over Umbraco's back button
         and the form's name, which axe then found too small to hit (ADR-0182). */
      .skip:not(:focus) {
        inline-size: 1px;
        block-size: 1px;
        padding: 0;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }

      .skip:focus {
        outline: 2px solid var(--jfb-bo-color-focus);
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
  /** The dictionary items the draft's texts name in the canvas's language, as the server finds them (ADR-0228). */
  declare private _lookups: DictionaryLookups;
  declare private _shown: CanvasShown;
  declare private _width: CanvasWidth;
  declare private _insertAt: Place | null;
  declare private _editing: string | null;
  declare private _narrow: boolean;
  declare private _inspectorFolded: boolean;
  /** The language the canvas and the inspector show; null for the site's default. */
  declare private _culture: string | null;
  declare private _said: string;
  /** Whether what the canvas draws with could not be read. */
  declare private _failed: boolean;
  /** Why the form refused the last move, shown at the element until the next change. */
  declare private _refusal: InPlaceRefusal | null;
  /** What the form check's badge says, which changes only with the check's answers. */
  declare private _badge: CheckBadge;
  /** The site's saved groups, which the field picker offers (ADR-0217). */
  declare private _saved: readonly SavedGroup[];
  /** Whether a group's "..." saves it as a template: the user may manage the library. */
  declare private _saveGroups: boolean;
  declare private _actions: readonly DesignerActionItem[];

  #context: FormWorkspaceContext | undefined;
  /** The page the picker adds to when it was opened from a page's Add field and the page has no group. */
  #addTo: string | null = null;
  /** Whether the selection a link named was brought into sight, or the user changed something first. */
  #shownLink = false;
  /** Whether Change type's dialog is opening or open, so a second ask does nothing. */
  #changingType = false;
  /** Whether the field picker is opening or open, so a second ask does nothing. */
  #picking = false;
  /** Whether the rules builder is open, so a second ask does nothing. */
  #buildingRule = false;
  /** The field selected whose changes of type were read ahead, with its type. */
  #readAhead: string | null = null;
  #mediaRead = new Set<string>();
  /** What each field's options were last asked for as, so a field is asked again only when they or the language change. */
  #optionsAsked = new Map<string, string>();
  readonly #dictionary = new DictionaryReader();
  // The keys and language the lookups shown were asked for: an answer for others is dropped.
  #dictionaryAsked = '';
  /** The asks waiting for the typing to pause, by field. */
  #optionsWaiting = new Map<string, ReturnType<typeof setTimeout>>();
  #imaging: UmbImagingRepository | undefined;
  /** The toast that offers Undo after a delete, closed by the next change: its Undo would take back another. */
  #undoToast: { close(): void } | undefined;
  #resize = new ResizeObserver(([entry]) => {
    const width = entry?.contentRect.width;
    const narrow = width !== undefined && isNarrow(width);
    if (narrow !== this._narrow) this._narrow = narrow;
  });

  constructor() {
    super();
    this._types = {};
    this._groups = [];
    this._sheets = [];
    this._media = {};
    this._resolved = new Map();
    this._lookups = new Map();
    this._shown = 0;
    this._width = readWidth();
    this._insertAt = null;
    this._editing = null;
    this._narrow = false;
    this._inspectorFolded = false;
    this._culture = null;
    this._said = '';
    this._failed = false;
    this._refusal = null;
    this._badge = { kind: 'none' };
    this._saved = [];
    this._saveGroups = false;
    this._actions = [];
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      // The language the Settings view's switch chose, which this view shows too.
      const kept = context?.getLanguage() ?? null;
      if (kept !== this._culture) {
        this._culture = kept;
        void this.#readLook();
      }
      this.observe(context?.editor, (view) => this.#viewChanged(view), 'editor');
      this.observe(context?.fieldTypes, (types) => (this._types = types ?? {}), 'types');
      this.observe(context?.typeGroups, (groups) => (this._groups = groups ?? []), 'groups');
      this.observe(
        context?.check,
        (check) => {
          // The canvas is drawn again only when the badge says something else.
          const badge = badgeOf(check);
          if (JSON.stringify(badge) !== JSON.stringify(this._badge)) this._badge = badge;
        },
        'check',
      );
      // A Fix it from the publish dialog, made once the view can show it.
      this.observe(
        context?.fixRequest,
        (target) => {
          if (target) void this.#fixPending();
        },
        'fix',
      );
    });
    void this.#readLook();
    this.addEventListener(CANVAS_REQUEST, (event) => void this.#onCanvas((event as CustomEvent<CanvasRequest>).detail));
    this.addEventListener(
      INSPECTOR_REQUEST,
      (event) => void this.#onInspector((event as CustomEvent<InspectorRequest>).detail),
    );
    this.addEventListener('keydown', (event) => this.#onKey(event));
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#resize.observe(this);
    void this.#readSaved();
    window.addEventListener(EXTENSIONS_CHANGED, this.#readActions);
    this.#readActions();
  }

  // The site's designer actions, as the row's "..." lists them after its own (ADR-0018).
  readonly #readActions = () => {
    this._actions = designerActions().map((manifest) => ({ alias: manifest.alias, label: manifest.meta.label }));
  };

  // Runs a site's designer action with the open form's designer; a failing one is the site's, and the designer stays.
  async #designerAction(alias: string) {
    const manifest = designerActions().find((candidate) => candidate.alias === alias);
    const context = this.#context;
    if (!manifest || !context) return;
    const api = await createExtensionApi(this, manifest);
    await api?.execute(designerHost(context));
  }

  async #readSaved() {
    const [saved, may] = await Promise.all([readSavedGroups(this), maySaveGroups(this)]);
    this._saved = saved;
    this._saveGroups = may;
  }

  override disconnectedCallback() {
    window.removeEventListener(EXTENSIONS_CHANGED, this.#readActions);
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
    if (this._view) {
      this.#readOptions(this._view.state.definition);
      this.#readDictionary(this._view.state.definition);
    }
    void this.#fixPending();
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
    this.#readDictionary(view.state.definition);
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

  // The texts of the dictionary items the draft's texts name in the canvas's language (ADR-0228): each key is asked once
  // per language, and an answer for an earlier draft or language is dropped.
  #readDictionary(definition: DesignDefinition) {
    const culture = this._look?.culture;
    if (culture === undefined) return;
    const keys = dictionaryKeysIn(definition, culture);
    const asked = `${culture}|${[...keys].sort().join(' ')}`;
    if (asked === this.#dictionaryAsked) return;
    this.#dictionaryAsked = asked;
    if (keys.size === 0) {
      if (this._lookups.size > 0) this._lookups = new Map();
      return;
    }
    void this.#dictionary.read(this, keys, culture).then((lookups) => {
      if (this.#dictionaryAsked === asked) this._lookups = lookups;
    });
  }

  // After an item was picked from Umbraco's dictionary, whose texts may have changed in the meantime, every text is read
  // again.
  #forgetDictionary() {
    this.#dictionary.forget();
    this.#dictionaryAsked = '';
    if (this._view) this.#readDictionary(this._view.state.definition);
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

  get #inspector(): InspectorElement | null {
    return this.shadowRoot?.querySelector('jfb-designer-inspector') ?? null;
  }

  get #outcomes(): FormOutcomesElement | null {
    return this.shadowRoot?.querySelector('jfb-form-outcomes') ?? null;
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
    const { selection, dropped, page } = this.#settle(result.state);
    if (selection) {
      const index = result.state.definition.pages.findIndex(
        (each) => each.key === pageOf(result.state.definition, selection)?.key,
      );
      if (index >= 0) this._shown = index;
      await this.#focus(selection, applied.control);
    } else if (page) {
      await this.#showPage(page);
    } else if (dropped) {
      await this.#focusAddField();
    }
    return true;
  }

  /** Shows a page, with the focus on its tab. */
  async #showPage(key: string) {
    const index = this.#state()?.definition.pages.findIndex((each) => each.key === key) ?? -1;
    if (index >= 0) this._shown = index;
    await this.#focus(key, 'tab');
  }

  // A page without fields draws its one bare group as the page's sentence, so a selection on that group, which a
  // delete of the page's last field leaves, is let go: Delete would take a group the user can't see. A page an edit
  // leaves selected (a page duplicated or deleted, and Undo of one) is let go too and shown instead: the canvas draws no
  // selection for a page, and over a narrow canvas its inspector would cover the form (ADR-0256).
  #settle(state: EditorState): { selection: string | null; dropped: boolean; page: string | null } {
    const selection = state.selection;
    if (!selection) return { selection: null, dropped: false, page: null };
    const found = locate(state.definition, selection);
    if (found?.kind === 'page') {
      this.#context?.edit(select(state, null), '');
      return { selection: null, dropped: false, page: selection };
    }
    if (found?.kind !== 'group' || !pageIsBare(found.container, this._look?.culture ?? ''))
      return { selection, dropped: false, page: null };
    this.#context?.edit(select(state, null), '');
    return { selection: null, dropped: true, page: null };
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

  /**
   * Opens the field picker for the place set, and lands its pick there (ADR-0256). The place stays marked on the canvas
   * while the picker is open. Close and Esc land nothing, and the focus is back on what opened the picker.
   */
  async #pick() {
    this.#picking = true;
    const opener = deepFocus();
    try {
      let shown = false;
      let gone: (() => void) | undefined;
      const closed = new Promise<void>((done) => (gone = done));
      const value = await umbOpenModal(this, FIELD_PICKER_MODAL, {
        data: {
          types: Object.values(this._types),
          groups: this._groups,
          saved: this._saved,
          opened: () => (shown = true),
          closed: () => gone?.(),
        },
      }).catch(() => undefined);
      // The sidebar answers before it slides out, and its dialog, as it closes, gives the focus back to what opened it:
      // the focus a pick sets waits until it is gone, so it stays.
      if (shown) await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
      if (value) await this.#place(value.pick);
      else if (opener?.isConnected && [null, document.body].includes(deepFocus())) opener.focus();
    } finally {
      this.#picking = false;
      this._insertAt = null;
      this.#addTo = null;
    }
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
      case 'clear':
        if (state.selection) this.#context?.edit(select(state, null), '');
        return;
      case 'insertAfter': {
        const found = locate(state.definition, request.key);
        if (found?.kind !== 'field' || this.#picking) return;
        this._insertAt = { container: found.container.key, index: found.index + 1 };
        await this.#pick();
        return;
      }
      case 'insertFirst':
        if (this.#picking) return;
        this._insertAt = { container: request.container, index: 0 };
        await this.#pick();
        return;
      case 'addField': {
        if (this.#picking) return;
        const page = state.definition.pages.find((each) => each.key === request.page);
        const last = page?.groups.at(-1);
        this._insertAt = last ? { container: last.key, index: last.fields.length } : null;
        this.#addTo = request.page;
        await this.#pick();
        return;
      }
      case 'renamePage':
        await this.#renamePage(request.key);
        return;
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
        // A selection on another page is let go, and on After sending, whose column holds the outcomes.
        if (
          state.selection &&
          (request.shown === 'after' ||
            pageOf(state.definition, state.selection)?.key !== state.definition.pages[request.shown]?.key)
        )
          this.#context?.edit(select(state, null), '');
        return;
      case 'addPage': {
        const page = newPage(model.newKey);
        const inserted = insert(state, 'page', page, { container: null, index: state.definition.pages.length }, model);
        // The new page is shown with the focus on its Add field, not selected: over a narrow canvas a selection's
        // inspector would cover the very button the page is added for (16.6, ADR-0241).
        const added = await this.#apply(
          inserted.ok ? { ...inserted, state: { ...inserted.state, selection: null } } : inserted,
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
        keepWidth(request.width);
        return;
      case 'undo':
      case 'redo':
        this.#travel(request.kind);
        return;
      case 'paste':
        await this.#paste();
        return;
      case 'drop':
        await this.#drop(request.source, request.place);
        return;
      case 'refused':
        this.#refuseInPlace(request.source.key, request.reason);
        return;
      case 'moveTo':
        await this.#moveTo(request.key);
        return;
      case 'shortcuts':
        await this.#shortcuts();
        return;
      case 'rulesOverview':
        await this.#rulesOverview();
        return;
      case 'designerAction':
        await this.#designerAction(request.alias);
        return;
      case 'formCheck':
        await this.#formCheck();
        return;
      case 'newPageAfter':
        await this.#newPageAfter(request.key);
        return;
      case 'saveGroup':
        await this.#saveGroup(request.key);
        return;
      case 'copyAlias':
        await this.#copyAlias(request.key);
        return;
      case 'culture':
        this._culture = request.culture;
        // The workspace keeps null for the site's default, as its readers (and a site's designer actions) expect.
        this.#context?.setLanguage(this.#isDefaultCulture(request.culture) ? null : request.culture);
        await this.#readLook();
        return;
    }
  }

  #isDefaultCulture(culture: string): boolean {
    const standard = this._look?.languages.find((language) => language.isDefault)?.culture;
    return standard !== undefined && culture.toLowerCase() === standard.toLowerCase();
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
      case 'rule':
        await this.#buildRule(request.key, request.rule);
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
      case 'saveGroup':
        await this.#saveGroup(key);
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

  // Save group as template: the group as the canvas shows it, unsaved changes included, with the validation rules of its
  // fields, under a name and a description; the toast says how many rules and validation rules stayed out for naming a
  // field outside it. A group the server can't read is something the form check shows, which the refusal points to.
  async #saveGroup(key: string) {
    const state = this.#state();
    const found = state ? locate(state.definition, key) : undefined;
    if (found?.kind !== 'group') return;
    const name = textIn(found.element.title, this._look?.culture ?? '') || this.#label(key);
    const value = await umbOpenModal(this, TEMPLATE_NAME_MODAL, {
      data: {
        headline: this.localize.term('jfbDesigner_saveGroupHeadline', name),
        confirmLabel: this.localize.term('jfbLibrary_saveTemplate'),
        name,
        note: this.localize.term('jfbDesigner_saveGroupNote'),
      },
    }).catch(() => undefined);
    if (!value || !state) return;
    const { leftOut, error } = await saveGroup(this, state.definition, found.element, value.name, value.description);
    if (!leftOut) {
      if (UmbApiError.isUmbApiError(error) && error.status === 400)
        (await this.#notifications())?.peek('danger', {
          data: { message: this.localize.term('jfbDesigner_saveGroupRefused') },
        });
      else await sayRefusal(this, error);
      return;
    }

    const message =
      leftOut.rules + leftOut.checks > 0
        ? this.localize.term('jfbDesigner_savedGroupLeftOut', value.name, leftOut.rules, leftOut.checks)
        : this.localize.term('jfbDesigner_savedGroup', value.name);
    this.#say(message);
    (await this.#notifications())?.peek('positive', { data: { message } });
    this._saved = await readSavedGroups(this);
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

  /**
   * Why the form can't take what is dragged where it would land, asked as the pointer goes from place to place; it
   * changes nothing. A drop where it already is is no refusal: it just stays.
   */
  readonly #refusalOf = (source: DragSource, place: Place | null): string | null => {
    const state = this.#state();
    const model = this.#context?.model;
    if (!state || !model) return null;
    if (!place) return this.#refusalText('wrongPlace');
    const edit = move(state, source.key, place, model);
    if (edit.ok || edit.refusal === 'unchanged') return null;
    return this.#refusalText(edit.refusal);
  };

  // A drop where the form takes it: what is dragged moves there. A page dragged by its tab keeps the focus on its tab.
  async #drop(source: DragSource, place: Place) {
    const state = this.#selectFirst(source.key);
    const model = this.#context?.model;
    if (!state || !model) return;
    await this.#moved(source.key, move(state, source.key, place, model), {
      toast: true,
      control: source.kind === 'page' ? 'tab' : 'select',
    });
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

  // The rules builder (ADR-0175, ADR-0178): a rule of the element, a validation rule that shows its message on it, or a
  // new one with the first effect it can take. Its answer lands as one step to undo: a rule changed or moved selects the
  // element it is on, with the focus on its line in the inspector; a rule deleted offers Undo, and the focus goes to the
  // element's next rule or to Add a rule.
  async #buildRule(key: string, ruleKey: string | null) {
    if (this.#buildingRule) return;
    this.#buildingRule = true;
    try {
      await this.#buildRuleOf(key, ruleKey);
    } finally {
      this.#buildingRule = false;
    }
  }

  async #buildRuleOf(key: string, ruleKey: string | null) {
    const context = this.#context;
    const state = this.#state();
    const found = state ? locate(state.definition, key) : undefined;
    if (!context || !state || !found) return;
    const held = ruleKey === null ? null : rulesOn(state.definition, key).find((one) => one.rule.key === ruleKey);
    if (held === undefined) return;
    const rule = held?.rule ?? null;
    const facts = typeFactsOf(this._types);
    const effects = builderEffectsOf(state.definition, key, (type) => facts[type]);
    // A rule kept opens even where its element takes no effect any more; the builder then says so.
    const effect =
      effects.find((one) => !clashOf(state.definition, key, one, '')) ??
      effects[0] ??
      (rule?.effect as Effect | undefined);
    if (!effect) return;
    const culture = this._look?.culture ?? '';
    const term = (word: string, ...args: unknown[]) => this.localize.term(word, ...args);
    // The builder answers before its sidebar slides out, and its dialog, as it closes, gives the focus back to what
    // opened it; the focus set here waits until the builder is gone, so it stays.
    let shown = false;
    let gone: (() => void) | undefined;
    const closed = new Promise<void>((done) => (gone = done));
    const value = await umbOpenModal(this, RULE_BUILDER_MODAL, {
      data: {
        opened: () => (shown = true),
        closed: () => gone?.(),
        definition: state.definition,
        form: context.getUnique() ?? '',
        origin: key,
        rule,
        effect,
        culture,
        languages: (this._look?.languages ?? []).map((language) => ({
          culture: language.culture,
          name: language.name,
          isDefault: language.isDefault,
        })),
        types: facts,
        names: elementNames(state.definition, culture, term),
        refusal: (target, next) => {
          const edit = landBuilt(this.#state() ?? state, held?.holder ?? null, target, next, context.model);
          return !edit.ok && edit.refusal === 'cycle' ? 'cycle' : null;
        },
      },
    }).catch(() => undefined);
    if (shown) await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
    const now = this.#state();
    if (!value || !now) {
      this.#inspector?.focusRule(ruleKey);
      return;
    }
    if (value.rule === null) {
      if (!held) return;
      const result = context.edit(
        dropBuilt(now, held.holder, held.rule.key, context.model),
        this.localize.term('jfbRules_saidDeleted', this.#label(key)),
      );
      if (!result.ok) return;
      this.#closeUndo();
      const left = rulesOn(result.state.definition, key);
      this.#inspector?.focusRule(left[0]?.rule.key ?? null);
      await this.#offerUndo(this.localize.term('jfbRules_deleted'));
      return;
    }
    // Update on a rule left as it was makes no step to undo.
    if (rule && value.target === key && sameRule(rule, value.rule)) {
      this.#inspector?.focusRule(rule.key);
      return;
    }
    const edit = landBuilt(now, held?.holder ?? null, value.target, value.rule, context.model);
    const said = this.localize.term(rule ? 'jfbRules_saidChanged' : 'jfbRules_saidAdded', this.#label(value.target));
    const result = context.edit(edit, said);
    if (!result.ok) {
      if (result.refusal === 'unchanged') this.#inspector?.focusRule(ruleKey);
      else await this.#refuse(result.refusal);
      return;
    }
    this.#closeUndo();
    // The rule's element is the one the inspector shows now, with the focus on the rule's line.
    this.#selectFirst(value.target);
    const page = pageOf(result.state.definition, value.target);
    const index = result.state.definition.pages.findIndex((each) => each.key === page?.key);
    if (index >= 0) this._shown = index;
    await this.updateComplete;
    this.#inspector?.focusRule(value.rule.key);
  }

  // The rules overview (ADR-0178): every rule of the form by page. A row selects its element on its page, with the
  // focus on it; an outcome's opens it on After sending; Add rule selects the element chosen and opens
  // the builder on it for a new rule. Its sidebar answers before it slides out and gives the focus back to the "..."
  // as it closes, so the focus set here waits until it is gone.
  async #rulesOverview() {
    const state = this.#state();
    if (!state) return;
    let shown = false;
    let gone: (() => void) | undefined;
    const closed = new Promise<void>((done) => (gone = done));
    const value = await umbOpenModal(this, RULES_OVERVIEW_MODAL, {
      data: {
        definition: state.definition,
        types: this._types,
        culture: this._look?.culture ?? '',
        languages: (this._look?.languages ?? []).map((language) => ({
          culture: language.culture,
          name: language.name,
        })),
        resolved: this._resolved,
        opened: () => (shown = true),
        closed: () => gone?.(),
      },
    }).catch(() => undefined);
    if (!value) return;
    if (shown) await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
    if (value.go === 'outcomes') {
      // A rule of an outcome opens the outcome on its rule.
      await this.#fixOutcome(`$.outcomes[${value.index}].rules[0]`);
      return;
    }
    const now = this.#selectFirst(value.key);
    if (!now || !locate(now.definition, value.key)) return;
    const page = pageOf(now.definition, value.key);
    const index = now.definition.pages.findIndex((each) => each.key === page?.key);
    if (index >= 0) this._shown = index;
    if (value.go === 'add') {
      await this.updateComplete;
      await this.#buildRule(value.key, null);
      return;
    }
    await this.#focus(value.key);
  }

  // The form check (ADR-0181): its sidebar lists what the check of the draft found, asked again as it opens; a Fix it
  // leads to the problem's place once the sidebar is gone, which gives the focus back to the badge as it closes.
  async #formCheck() {
    const context = this.#context;
    if (!context) return;
    void context.checkNow();
    let shown = false;
    let gone: (() => void) | undefined;
    const closed = new Promise<void>((done) => (gone = done));
    const value = await umbOpenModal(this, FORM_CHECK_MODAL, {
      data: {
        check: context.check,
        retry: () => void context.checkNow(),
        types: this._types,
        culture: this._look?.culture ?? '',
        opened: () => (shown = true),
        closed: () => gone?.(),
      },
    }).catch(() => undefined);
    if (!value) return;
    if (shown) await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
    await this.#fix(value.fix);
  }

  // A Fix it the publish dialog asked for, made once the canvas can show it: its look is read. One in the form's
  // settings or actions is the other view's.
  async #fixPending() {
    if (!this._look) return;
    const target = this.#context?.takeFix((one) => one.kind !== 'view');
    if (target) await this.#fix(target, true);
  }

  /**
   * Fix it: the element selected on its page, with the inspector on the setting the problem is about; the rules builder
   * on a rule; the field picker for the end of a page without fields; After sending for an outcome; or the view where
   * the form's settings or actions are edited. An element gone since the check leads nowhere.
   */
  async #fix(target: FixTarget, handedOn = false) {
    // The user's own choice ends the wait for a link's selection, so its focus never takes this one's.
    this.#shownLink = true;
    if (target.kind === 'view') {
      followFix(target, (fix) => this.#context?.requestFix(fix));
      return;
    }
    if (target.kind === 'outcome') {
      await this.#fixOutcome(target.path, target.key, handedOn);
      return;
    }
    if (target.kind === 'picker') {
      if (target.page === null) await this.#onCanvas({ kind: 'addPage' });
      else if (this.#state()?.definition.pages.some((page) => page.key === target.page)) {
        const state = this.#state();
        if (state?.selection) this.#context?.edit(select(state, null), '');
        this._shown = state?.definition.pages.findIndex((page) => page.key === target.page) ?? 0;
        await this.#onCanvas({ kind: 'addField', page: target.page });
      }
      return;
    }
    const before = this.#state();
    if (!before || !locate(before.definition, target.key)) return;
    if (target.kind === 'element' && target.culture !== undefined) await this.#showLanguage(target.culture);
    const now = this.#selectFirst(target.key);
    if (!now) return;
    const page = pageOf(now.definition, target.key);
    const index = now.definition.pages.findIndex((each) => each.key === page?.key);
    if (index >= 0) this._shown = index;
    this._inspectorFolded = false;
    await this.updateComplete;
    if (target.kind === 'rule') {
      // A Fix it handed on from another view opens the builder once the router that showed this one stays still, as a
      // navigation's end closes it (ADR-0227).
      if (handedOn) await routerSettled();
      if (this.isConnected) await this.#buildRule(target.key, target.rule);
    } else if (target.item) this.#inspector?.focusItem(target.item);
    else await this.#focus(target.key);
  }

  // On a site with more than one language, the language of the text a Fix it is about ('' the default), so the
  // inspector edits that text.
  async #showLanguage(culture: string) {
    const languages = this._look?.languages ?? [];
    if (languages.length < 2) return;
    const wanted =
      culture === ''
        ? languages.find((language) => language.isDefault)
        : languages.find((language) => language.culture.toLowerCase() === culture.toLowerCase());
    if (!wanted || wanted.culture.toLowerCase() === this._look?.culture.toLowerCase()) return;
    await this.#onCanvas({ kind: 'culture', culture: wanted.culture });
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
    const before = context.getEditorState().definition;
    const said = way === 'undo' ? context.undo() : context.redo();
    if (said === undefined) return;
    this.#inspector?.forget();
    this.#closeUndo();
    this._refusal = null;
    this.#say(this.localize.term(way === 'undo' ? 'jfbDesigner_saidUndone' : 'jfbDesigner_saidRedone', said));
    const state = context.getEditorState();
    const { definition } = state;
    const { selection, dropped, page } = this.#settle(state);
    if (this._shown === 'after' && !selection && !page) {
      // On After sending, the focus goes to the outcome brought back or changed, as the toast's Undo puts it.
      void this.#outcomes?.focusChange(before);
    } else if (selection) {
      const index = definition.pages.findIndex((each) => each.key === pageOf(definition, selection)?.key);
      if (index >= 0) this._shown = index;
      if (!stay) void this.#focus(selection);
    } else if (page) {
      if (stay)
        this._shown = Math.max(
          0,
          definition.pages.findIndex((each) => each.key === page),
        );
      else void this.#showPage(page);
    } else if (dropped) {
      void this.#focusAddField();
    } else {
      void this.#keepFocus();
    }
  }

  // Where a pick from the field picker goes, and the page it goes on.
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

  // A pick lands where the picker was opened: it is added there, selected and given the focus; Paste questions...
  // opens its dialog for the same place.
  async #place(pick: FieldPick) {
    const state = this.#state();
    const model = this.#context?.model;
    const target = this.#target();
    if (!state || !model || !target) return;
    switch (pick.kind) {
      case 'paste':
        await this.#paste();
        return;
      case 'field': {
        const type = this.#fieldType(pick.type);
        if (!type) return;
        const name = this.localize.string(type.name);
        await this.#apply(
          addFields(state, [newField(type, name, model.newKey, this.#newWords())], target.place, target.page, model),
          this.localize.term('jfbDesigner_saidAdded', name),
          { type: name },
        );
        return;
      }
      case 'savedGroup': {
        const copy = freshCopyWith(state.definition, pick.saved.group, pick.saved.checks, model);
        await this.#apply(
          addGroup(state, target.place, target.page, model, copy.element, copy.checks),
          this.localize.term('jfbDesigner_saidSavedGroupAdded', pick.saved.name),
        );
        return;
      }
      case 'group':
        await this.#apply(
          addGroup(state, target.place, target.page, model),
          this.localize.term('jfbDesigner_saidGroupAdded'),
        );
        return;
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
    const added = addFields(state, fields, target.place, target.page, model);
    // One question is selected to be edited, as a picked field is; several are left to be looked over, so no inspector
    // over a narrow canvas hides them, and the focus goes to the first of them (16.6, ADR-0241).
    const several = fields.length > 1;
    const done = await this.#apply(
      several && added.ok ? { ...added, state: { ...added.state, selection: null } } : added,
      this.localize.term('jfbDesigner_saidAddedMany', fields.length),
    );
    if (!done) return;
    const first = fields[0];
    if (several && first) await this.#focus(first.key);
  }

  /**
   * After sending, on the sidebar of the outcome a path names (by its key where given, as the definition checked held
   * it) at the setting it names, in the language of its message; on Add outcome for a path at the list itself or an
   * outcome gone since (ADR-0185, ADR-0256). A selection is let go, so no inspector over a narrow canvas hides the list.
   */
  async #fixOutcome(path: string, key?: string, handedOn = false) {
    const steps = parsePath(path) ?? ['outcomes'];
    const state = this.#state();
    if (state?.selection) this.#context?.edit(select(state, null), '');
    this._shown = 'after';
    this._insertAt = null;
    const [, , property, language] = steps;
    if (property === 'message') await this.#showLanguage(typeof language === 'string' ? language : '');
    await this.updateComplete;
    await this.#outcomes?.fix(key, steps, handedOn);
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
    // The outcomes are the inspector's column's even where they lie under the sheet.
    const inInspector = path.some(
      (node) =>
        node instanceof HTMLElement &&
        (node.localName === 'jfb-designer-inspector' || node.localName === 'jfb-form-outcomes'),
    );
    const ctrl = event.ctrlKey || event.metaKey;
    const key = event.key.toLowerCase();
    const state = this.#state();
    const selection = state?.selection ?? null;
    if (key === 'escape') {
      // An open menu closes itself and gives the focus back to its button.
      if (path.some((node) => node instanceof HTMLElement && node.matches('[popover]:popover-open'))) return;
      if (state && selection && !typing && inInspector) this.#letGo();
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
      } else if (control && control.control !== 'tab') this.#canvas?.focusNext(control.key, vertical);
      else return;
      event.preventDefault();
      return;
    }
    if (event.key === '?' && !ctrl && !event.altKey) void this.#shortcuts();
    else if (ctrl && key === 'z' && !event.shiftKey) this.#travel('undo');
    else if (ctrl && (key === 'y' || (key === 'z' && event.shiftKey))) this.#travel('redo');
    else if (ctrl && key === 'd' && acted && this.#selectFirst(acted))
      void this.#onCanvas({ kind: 'duplicate', key: acted });
    else if (event.key === 'F2' && control?.control === 'tab') void this.#renamePage(control.key);
    else if (event.key === 'F2' && acted && this.#selectFirst(acted))
      void this.#onCanvas({ kind: 'editLabel', key: acted });
    else if (event.key === 'Delete' && acted) void this.#delete(acted);
    else return;
    event.preventDefault();
  }

  // The canvas control a key was pressed on: the button that selects an element, its handle, or a page's tab.
  #controlOf(event: Event): { key: string; control: Control } | null {
    const target = event.composedPath()[0];
    if (!(target instanceof HTMLElement)) return null;
    const key = target.dataset['key'];
    const control = target.dataset['control'];
    return key && (control === 'select' || control === 'handle' || control === 'tab') ? { key, control } : null;
  }

  // Rename, at a page's tab: the page's Title in the inspector, its text selected (ADR-0256). Beside the canvas the
  // inspector shows the page as the open one, unfolded; over a narrow canvas the page is selected, so the inspector opens
  // over the form.
  async #renamePage(key: string) {
    const state = this.#state();
    const index = state?.definition.pages.findIndex((page) => page.key === key) ?? -1;
    if (!state || index < 0) return;
    this.#shownLink = true;
    this._shown = index;
    this._insertAt = null;
    if (this._narrow) {
      if (state.selection !== key) this.#context?.edit(select(state, key), '');
    } else if (state.selection) this.#context?.edit(select(state, null), '');
    this._inspectorFolded = false;
    await this.updateComplete;
    await this.#inspector?.updateComplete;
    this.#inspector?.focusItem({ kind: 'text', property: 'title' }, true);
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

  #inspectorMode(): InspectorMode {
    if (this._narrow) return 'overlay';
    return this._inspectorFolded ? 'rail' : 'docked';
  }

  /** The key of the page the canvas shows; none after sending. */
  #openPage(): string | null {
    const pages = this._view?.state.definition.pages ?? [];
    return typeof this._shown === 'number' ? (pages[Math.min(this._shown, pages.length - 1)]?.key ?? null) : null;
  }

  override render() {
    const view = this._view;
    const selection = view?.state.selection ?? null;
    const inspector = this.#inspectorMode();
    // Beside the canvas the inspector's column is always there, so a selection moves nothing; over it, only for one.
    const inspects = this._look !== undefined && !this._failed && (selection !== null || inspector !== 'overlay');
    // After sending's outcomes: in the inspector's column beside the canvas, else under what visitors get (ADR-0256).
    const after = this._shown === 'after' && this._look !== undefined && !this._failed;
    const inColumn = after && inspects && inspector === 'docked';
    const outcomes = html`<jfb-form-outcomes
      slot="after"
      .look=${this._look}
      ?headed=${!inColumn}
    ></jfb-form-outcomes>`;
    // A button, not a link: Umbraco's router takes every link's click for a move to another place.
    return html`<button
        type="button"
        class="skip"
        @click=${() => this.#canvas?.shadowRoot?.querySelector<HTMLElement>('#jfb-canvas-form')?.focus()}
      >
        ${this.localize.term('jfbDesigner_skipToForm')}
      </button>
      ${this._failed ? this.#failed() : nothing}
      <jfb-form-canvas
        ?hidden=${this._failed}
        ?inert=${inspects && inspector === 'overlay'}
        .definition=${view?.state.definition ?? { pages: [] }}
        .selection=${view?.state.selection ?? null}
        .shown=${this._shown}
        .look=${this._look}
        .sheets=${this._sheets}
        .types=${this._types}
        .mediaUrls=${this._media}
        .resolvedOptions=${this._resolved}
        .lookups=${this._lookups}
        .width=${this._width}
        .insertAt=${this._insertAt}
        .editing=${this._editing}
        .canUndo=${view?.canUndo ?? false}
        .canRedo=${view?.canRedo ?? false}
        .refusalOf=${this.#refusalOf}
        .refusal=${this._refusal}
        .check=${this._badge}
        .saveGroups=${this._saveGroups}
        .designerActions=${this._actions}
        >${after && !inColumn ? outcomes : nothing}</jfb-form-canvas
      >
      ${
        inspects
          ? html`<jfb-designer-inspector
              .definition=${view?.state.definition ?? { pages: [] }}
              .selection=${selection}
              .page=${this.#openPage()}
              .afterSending=${inColumn}
              .types=${this._types}
              .culture=${this._look?.culture ?? ''}
              .languages=${this._look?.languages ?? []}
              .form=${this.#context?.getUnique() ?? ''}
              .resolvedOptions=${this._resolved}
              .lookups=${this._lookups}
              .forgetDictionary=${() => this.#forgetDictionary()}
              .saveGroups=${this._saveGroups}
              mode=${inspector}
              >${inColumn ? outcomes : nothing}</jfb-designer-inspector
            >`
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
