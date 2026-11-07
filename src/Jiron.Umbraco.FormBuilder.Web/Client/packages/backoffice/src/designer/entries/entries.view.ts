// A form's Entries view (ROADMAP 11.1, IA 5.4 and 6.1, ADR-0207): its entries newest first, 50 at a time with Show more,
// each named by its title or number, with its state, when it was received, its action status and the columns the user
// chose. The toolbar searches and filters, the chips under it say what is filtered and save it as a set, a column's header
// sorts, and the bulk bar acts on the ticked entries: a change of state with Undo until its window ends (ADR-0023), Delete...
// and Re-run actions after asking. The keys of section 4.13 move through the list without any motion. An entry opens in
// the entry panel beside the list (11.2, IA 6.3, ADR-0208), which narrows the rows to name, state and time, or covers the
// list where the view is too narrow for both; the list keeps its place, its ticks and its scroll meanwhile.
import {
  css,
  html,
  nothing,
  repeat,
  type PropertyValues,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import { UmbId } from '@umbraco-cms/backoffice/id';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type {
  EntryFilterSetResponseModel,
  EntryListFieldResponseModel,
  EntryListFilterRequestModel,
  EntryListRowResponseModel,
} from '../../api';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import { pressOf } from '../../styles/press';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { confirmList, confirmText } from '../../ui/confirm';
import { jfbSkeleton, skeletonLines } from '../../ui/skeleton';
import { UNDO_NOTIFICATION, type UndoNotificationData } from '../../ui/undo-notification.element';
import {
  changeState,
  deleteEntries,
  deleteSet,
  readPage,
  readView,
  rerunActions,
  saveColumns,
  saveSet,
  undoChange,
} from '../../entries/entries-api';
import {
  APPLIES,
  MOVE_STATE,
  bulkActions,
  chipsOf,
  cleanFilter,
  emptyFilter,
  fixedColumns,
  isFiltered,
  isTyping,
  leftOut,
  nextSort,
  receivedText,
  requestOf,
  rowLabel,
  rowName,
  runsText,
  stateText,
  type BulkAction,
  type Chip,
} from '../../entries/entry-list-model';
import {
  ENTRY_COLUMNS_MODAL,
  ENTRY_FILTER_MODAL,
  ENTRY_KEYS_MODAL,
  ENTRY_SET_NAME_MODAL,
  type FilterPart,
} from '../../entries/entry-modals';
import type { EntryMovedDetail, EntryPanelElement } from '../../entries/entry-panel.element';
import '../../entries/entry-panel.element';
import { asksLines, type PanelAction } from '../../entries/entry-panel-model';
import { loadEntryWords } from '../../entries/entry-words';

/** The widest the view is where the panel covers the list rather than sitting beside it (IA, "Rules": up to 700 px). */
const COVERS = 700;

/** Whether a step of an event's path is an open popover, such as a menu; false where the browser can't tell. */
function isOpenPopover(step: EventTarget): boolean {
  try {
    return step instanceof HTMLElement && step.matches(':popover-open');
  } catch {
    return false;
  }
}

/** The most entries one bulk action takes, as the server moves them. */
const MOST_TICKED = 500;

/** How long the list waits for the user to stop typing a search before it reads. */
const SEARCH_PAUSE_MS = 350;

/** How many bulk actions show before the "..." (IA 6.1). */
const SHOWN_ACTIONS = 3;

/** The longest an Undo toast offers its Undo, however long the server's window; and the shortest worth showing. */
const UNDO_LONGEST_MS = 60_000;
const UNDO_SHORTEST_MS = 1_000;

/** How long the toast stays once its window has ended, to say it is too late: Umbraco's own time for a toast. */
const UNDO_LATE_MS = 6_000;

/** What a cell of a sensitive answer shows: three bullets, as a masked answer reads everywhere (ADR-0009). */
const MASKED = String.fromCharCode(0x2022).repeat(3);

/** The tone of a state's chip: what waits for someone, what is done, and the rest. */
const STATE_COLOR: Record<string, string> = { submitted: 'warning', approved: 'positive' };

export class EntriesViewElement extends UmbLitElement {
  static override properties = {
    _words: { state: true },
    _state: { state: true },
    _fields: { state: true },
    _columns: { state: true },
    _sets: { state: true },
    _filter: { state: true },
    _search: { state: true },
    _rows: { state: true },
    _next: { state: true },
    _total: { state: true },
    _totalMore: { state: true },
    _more: { state: true },
    _moreFailed: { state: true },
    _ticked: { state: true },
    _cursor: { state: true },
    _busy: { state: true },
    _said: { state: true },
    _permissions: { state: true },
    _open: { state: true },
    _covering: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    pressOf('.chip button, .menu button, .choose, .sort'),
    css`
      /* The view takes its workspace's height, so the list scrolls on its own and keeps its place beside the panel. */
      :host {
        position: relative;
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        grid-template-rows: minmax(0, 1fr);
        block-size: 100%;
        min-inline-size: 0;
        min-block-size: 0;
        /* As in the Design view: the workspace's fading scrollbar colour stops here, so the rows are not restyled. */
        scrollbar-color: var(--uui-color-disabled-standalone) transparent;
      }

      :host([split]) {
        grid-template-columns: minmax(18rem, 1fr) minmax(24rem, 40rem);
      }

      .list {
        min-inline-size: 0;
        min-block-size: 0;
        overflow: auto;
        padding: var(--uui-size-layout-1);
        container: entries / inline-size;
      }

      jfb-entry-panel {
        min-inline-size: 0;
        min-block-size: 0;
        border-inline-start: 1px solid var(--uui-color-border);
      }

      :host([covering]) jfb-entry-panel {
        position: absolute;
        inset: 0;
        z-index: 2;
        border: 0;
      }

      .toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
        margin-block-end: var(--uui-size-space-4);
      }

      .search {
        flex: 1 1 16rem;
        max-inline-size: 28rem;
      }

      .search uui-icon {
        margin-inline-start: var(--uui-size-space-3);
        color: var(--jfb-bo-color-text-muted);
      }

      .count {
        margin-inline-start: auto;
        color: var(--jfb-bo-color-text-muted);
        white-space: nowrap;
      }

      .chips {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-block-end: var(--uui-size-space-4);
      }

      .chip {
        display: inline-flex;
        align-items: stretch;
        max-inline-size: 100%;
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .chip button {
        display: inline-flex;
        align-items: center;
        min-block-size: 32px;
        margin: 0;
        padding: 0 var(--uui-size-space-3);
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        cursor: pointer;
      }

      .chip .chip-text {
        overflow-wrap: anywhere;
        text-align: start;
      }

      .chip .chip-remove {
        border-inline-start: 1px solid var(--uui-color-border);
      }

      .box--flush {
        --uui-box-default-padding: 0;
      }

      .scroller {
        overflow-x: auto;
      }

      table {
        width: 100%;
        border-collapse: collapse;
      }

      th {
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        font-weight: 700;
        text-align: start;
        white-space: nowrap;
      }

      td,
      tbody th {
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border-top: 1px solid var(--uui-color-divider);
        font-weight: 400;
        vertical-align: top;
        white-space: normal;
      }

      tbody tr {
        outline: none;
      }

      /* The keyboard cursor: a ring around the row, no fill, so it never reads as ticked or open (IA 6.1). */
      tbody tr.cursor,
      tbody tr:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .tick {
        inline-size: 1px;
        padding-inline-end: 0;
      }

      .end {
        inline-size: 1px;
        text-align: end;
      }

      /* The open entry's row: Umbraco's colour for what is open, as the tree marks its item, under the cursor's ring. */
      tbody tr.open {
        background: var(--uui-color-current);
        color: var(--uui-color-current-contrast);
      }

      .name {
        display: inline-flex;
        align-items: baseline;
        gap: var(--uui-size-space-2);
        margin: 0;
        padding: 0;
        border: 0;
        background: none;
        color: inherit;
        font: inherit;
        text-align: start;
        overflow-wrap: anywhere;
        cursor: pointer;
      }

      .name:hover {
        text-decoration: underline;
      }

      .name:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      tr.unseen .name {
        font-weight: 700;
      }

      .dot {
        flex: none;
        inline-size: 8px;
        block-size: 8px;
        border-radius: 50%;
        background: var(--uui-color-interactive-emphasis);
        transform: translateY(-1px);
      }

      .received,
      .runs {
        white-space: nowrap;
      }

      .tone-danger {
        color: var(--jfb-bo-color-danger-text);
      }

      .tone-warning {
        color: var(--jfb-bo-color-warning-text, var(--uui-color-warning-emphasis));
      }

      .tone-muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .cell {
        display: -webkit-box;
        max-inline-size: 24rem;
        overflow: hidden;
        -webkit-box-orient: vertical;
        -webkit-line-clamp: 2;
        overflow-wrap: anywhere;
      }

      .below {
        display: none;
      }

      /* Narrower, the action status gives way and a failure shows under the state, so the names keep their width. */
      @container entries (width < 57.5rem) {
        .runs-column {
          display: none;
        }

        .below {
          display: block;
          margin-block-start: var(--uui-size-space-1);
          font-size: var(--uui-type-small-size);
        }

        table:not(.no-state) th .below {
          display: none;
        }
      }

      /* Beside the panel, a row is its name, state and time; a failure still shows, under the state. */
      .narrowed .runs-column {
        display: none;
      }

      .narrowed .below {
        display: block;
        margin-block-start: var(--uui-size-space-1);
        font-size: var(--uui-type-small-size);
      }

      .narrowed:not(.no-state) th .below {
        display: none;
      }

      .sort,
      .choose {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        margin: 0;
        padding: 0;
        border: 0;
        background: none;
        color: inherit;
        font: inherit;
        cursor: pointer;
      }

      .choose {
        display: inline-grid;
        place-items: center;
        inline-size: 32px;
        block-size: 32px;
        border-radius: var(--uui-border-radius);
      }

      .sort:focus-visible,
      .choose:focus-visible,
      .chip button:focus-visible,
      .menu button:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .menu {
        display: grid;
        min-inline-size: 220px;
        max-block-size: min(28rem, 70vh);
        overflow: auto;
        padding: var(--uui-size-space-1);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-3);
      }

      .menu h3 {
        margin: var(--uui-size-space-3) var(--uui-size-space-3) var(--uui-size-space-1);
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        font-weight: 700;
      }

      .menu button {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        min-block-size: 36px;
        margin: 0;
        padding: 0 var(--uui-size-space-3);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      .menu .set {
        display: flex;
        align-items: center;
      }

      .menu .set button:first-child {
        flex: 1 1 auto;
      }

      @media (hover: hover) {
        .menu button:hover,
        .choose:hover,
        .chip button:hover {
          background: var(--uui-color-surface-emphasis);
        }
      }

      .footer {
        display: flex;
        flex-wrap: wrap;
        justify-content: center;
        align-items: center;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-4);
        border-top: 1px solid var(--uui-color-divider);
      }

      .footer p {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      .empty {
        max-width: 36rem;
        margin: var(--uui-size-layout-2) auto;
        text-align: center;
      }

      .empty p {
        margin: 0 0 var(--uui-size-space-4);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      /* The bulk bar: at the foot of the view, right above the workspace's footer, never merged with it (IA 6.1). */
      .bulk {
        position: sticky;
        inset-block-end: 0;
        z-index: 1;
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-block-start: var(--uui-size-space-4);
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-2);
      }

      .bulk .selected {
        font-weight: 700;
      }

      .bulk .actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
        margin-inline-start: auto;
      }
    `,
  ];

  declare private _words: boolean;
  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _fields: readonly EntryListFieldResponseModel[];
  declare private _columns: readonly string[];
  declare private _sets: readonly EntryFilterSetResponseModel[];
  declare private _filter: EntryListFilterRequestModel;
  /** What the search box holds, which the list reads once the user stops typing. */
  declare private _search: string;
  declare private _rows: readonly EntryListRowResponseModel[];
  declare private _next: string | null;
  declare private _total: number | null;
  declare private _totalMore: boolean;
  declare private _more: boolean;
  declare private _moreFailed: boolean;
  declare private _ticked: readonly string[];
  /** The keyboard cursor's row, by its place; -1 for none. */
  declare private _cursor: number;
  declare private _busy: boolean;
  declare private _said: string;
  declare private _permissions: readonly string[];
  /** The entry open in the panel, by its key. */
  declare private _open: string | undefined;
  /** Whether the view is too narrow for the panel beside the list, so it covers the list. */
  declare private _covering: boolean;

  #form: string | undefined;
  /** Counts the reads of the list, so only the latest one's answer is shown. */
  #reads = 0;
  #searchTimer: ReturnType<typeof setTimeout> | undefined;
  #resize = new ResizeObserver(([entry]) => {
    const width = entry?.contentRect.width;
    const covering = width !== undefined && width <= COVERS;
    if (covering !== this._covering) this._covering = covering;
  });

  constructor() {
    super();
    this._words = false;
    this._state = 'loading';
    this._fields = [];
    this._columns = [];
    this._sets = [];
    this._filter = emptyFilter();
    this._search = '';
    this._rows = [];
    this._next = null;
    this._total = null;
    this._totalMore = false;
    this._more = false;
    this._moreFailed = false;
    this._ticked = [];
    this._cursor = -1;
    this._busy = false;
    this._said = '';
    this._permissions = [];
    this._covering = false;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      if (!context) return;
      this.observe(context.form, (form) => (this._permissions = form?.permissions ?? []), 'permissions');
      this.observe(
        context.unique,
        (unique) => {
          if (!unique || unique === this.#form) return;
          // Another form: nothing of the one before stays.
          this.#form = unique;
          this._filter = emptyFilter();
          this._search = '';
          this._rows = [];
          this._ticked = [];
          this._cursor = -1;
          this._open = undefined;
          void this.#open();
        },
        'unique',
      );
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    window.addEventListener('keydown', this.#keys);
    this.#resize.observe(this);
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.removeEventListener('keydown', this.#keys);
    this.#resize.disconnect();
    clearTimeout(this.#searchTimer);
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    this.toggleAttribute('split', this._open !== undefined && !this._covering);
    this.toggleAttribute('covering', this._open !== undefined && this._covering);
  }

  get #panel(): EntryPanelElement | null {
    return this.shadowRoot?.querySelector('jfb-entry-panel') ?? null;
  }

  // ---- The entry panel ----------------------------------------------------------------------------------------------

  /** Opens a row's entry in the panel; the focus goes there, unless the keys are moving through the list beside it. */
  async #openRow(index: number, focusPanel = true) {
    const row = this._rows[index];
    if (!row) return;
    this._cursor = index;
    this._open = row.key;
    await this.updateComplete;
    if (focusPanel || this._covering) this.#panel?.focusHeading();
  }

  async #closePanel() {
    const open = this._open;
    this._open = undefined;
    await this.updateComplete;
    const index = this._rows.findIndex((row) => row.key === open);
    if (index >= 0) this.#focusRow(index);
  }

  #rowSeen = (event: CustomEvent<EntryMovedDetail>) => {
    const { key, state } = event.detail;
    this._rows = this._rows.map((row) => (row.key === key ? { ...row, unseen: false, state } : row));
  };

  #rowMoved = (event: CustomEvent<EntryMovedDetail>) => {
    const { key, state } = event.detail;
    this._rows = this._rows.map((row) => (row.key === key ? { ...row, state } : row));
  };

  #rowGone = (event: CustomEvent<EntryMovedDetail>) => {
    const { key } = event.detail;
    const index = this._rows.findIndex((row) => row.key === key);
    this._rows = this._rows.filter((row) => row.key !== key);
    this._ticked = this._ticked.filter((one) => one !== key);
    if (this._total !== null && !this._totalMore) this._total = Math.max(0, this._total - 1);
    this._open = undefined;
    this._cursor = -1;
    // The keyboard reads on at the row that took its place.
    void this.updateComplete.then(() => this.#focusRow(Math.min(index, this._rows.length - 1)));
  };

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);
  #full = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  get #byKey(): ReadonlyMap<string, EntryListFieldResponseModel> {
    return new Map(this._fields.map((field) => [field.key, field]));
  }

  // ---- Reading ------------------------------------------------------------------------------------------------------

  /** Opens the form's list: its words, what the view offers and the first page. */
  async #open() {
    const form = this.#form;
    if (!form) return;
    const read = ++this.#reads;
    this._state = 'loading';
    const [words, view] = await Promise.all([
      loadEntryWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readView(this, form),
    ]);
    if (read !== this.#reads) return;
    this._words ||= words;
    if (!words || !view) {
      this._state = 'failed';
      return;
    }

    this._fields = view.fields;
    this._columns = view.columns;
    this._sets = view.filterSets;
    await this.#read();
  }

  /** Reads the first page again for the filter, the search and the columns as they are. */
  async #read(say = false) {
    const form = this.#form;
    if (!form) return;
    const read = ++this.#reads;
    if (this._rows.length === 0) this._state = 'loading';
    const page = await readPage(this, form, requestOf(this._filter, this.#shownColumns(), null));
    if (read !== this.#reads) return;
    if (!page) {
      this._state = 'failed';
      return;
    }

    this._rows = page.items;
    this._next = page.next ?? null;
    this._total = page.total ?? null;
    this._totalMore = page.more;
    this._moreFailed = false;
    this._ticked = this._ticked.filter((key) => page.items.some((row) => row.key === key));
    this._cursor = -1;
    this._state = 'ready';
    if (say) this._said = this.#countText();
  }

  async #showMore() {
    const form = this.#form;
    if (!form || !this._next) return;
    const read = this.#reads;
    this._more = true;
    const before = this._rows.length;
    const page = await readPage(this, form, requestOf(this._filter, this.#shownColumns(), this._next));
    this._more = false;
    if (read !== this.#reads) return;
    this._moreFailed = !page;
    if (!page) return;
    const known = new Set(this._rows.map((row) => row.key));
    const added = page.items.filter((row) => !known.has(row.key));
    this._rows = [...this._rows, ...added];
    this._next = page.next ?? null;
    this._said = this.#term('shownMore', added.length);
    await this.updateComplete;
    // The keyboard reads on at the first new row, as the button may be gone.
    this.#focusRow(before);
  }

  /** The columns the list asks for: the user's, but those a filter fixes, which the chips already say. */
  #shownColumns(): readonly string[] {
    const fixed = fixedColumns(this._filter).fields;
    return this._columns.filter((key) => !fixed.has(key));
  }

  #countText(): string {
    if (this._total === null) return '';
    return this._totalMore ? this.#term('countMore') : this.#term('count', this._total);
  }

  // ---- Filters, search and sort ---------------------------------------------------------------------------------------

  #filterBy(filter: EntryListFilterRequestModel) {
    this._filter = filter;
    void this.#read(true);
  }

  #typed(text: string) {
    this._search = text;
    clearTimeout(this.#searchTimer);
    this.#searchTimer = setTimeout(() => this.#searchNow(), SEARCH_PAUSE_MS);
  }

  #searchNow() {
    clearTimeout(this.#searchTimer);
    const search = this._search.trim();
    if ((this._filter.search ?? '') === search) return;
    this.#filterBy({ ...this._filter, ...(search ? { search } : { search: null }) });
  }

  #clearSearch() {
    this._search = '';
    this.#searchNow();
    this.shadowRoot?.querySelector<HTMLElement>('.search')?.focus();
  }

  #sortBy(field: string | null) {
    this.#filterBy({ ...this._filter, sort: nextSort(this._filter.sort, field) });
  }

  async #edit(part: FilterPart, field?: EntryListFieldResponseModel, index?: number) {
    this.#closeMenus();
    const answer = await umbOpenModal(this, ENTRY_FILTER_MODAL, {
      data: { part, filter: this._filter, ...(field ? { field } : {}), ...(index !== undefined ? { index } : {}) },
    }).catch(() => undefined);
    if (answer) this.#filterBy(answer.filter);
  }

  #editChip(chip: Chip) {
    if (chip.kind !== 'field') {
      void this.#edit(chip.kind);
      return;
    }

    const condition = this._filter.fields[chip.index ?? -1];
    const field = condition ? this.#byKey.get(condition.field) : undefined;
    if (field) void this.#edit('field', field, chip.index);
  }

  #removeChip(chip: Chip) {
    const filter = { ...this._filter };
    if (chip.kind === 'state') filter.states = [];
    else if (chip.kind === 'received') delete filter.received;
    else if (chip.kind === 'actions') delete filter.actions;
    else filter.fields = filter.fields.filter((_, index) => index !== chip.index);
    this.#filterBy(filter);
  }

  #clearFilters() {
    this.#filterBy({
      ...emptyFilter(),
      ...(this._filter.search ? { search: this._filter.search } : {}),
      ...(this._filter.sort ? { sort: this._filter.sort } : {}),
    });
  }

  #applySet(set: EntryFilterSetResponseModel) {
    this.#closeMenus();
    this._search = set.filter.search ?? '';
    this.#filterBy(cleanFilter(set.filter));
  }

  async #saveAs() {
    const form = this.#form;
    if (!form) return;
    await umbOpenModal(this, ENTRY_SET_NAME_MODAL, {
      data: {
        save: async (name) => {
          const { set, code } = await saveSet(this, UmbId.new(), name, form, cleanFilter(this._filter));
          if (!set) return this.#term(code === 'name_taken' ? 'set_taken' : 'set_failed');
          this._sets = [...this._sets, set];
          await this.#notify('positive', this.#term('set_saved', set.name));
          return undefined;
        },
      },
    }).catch(() => undefined);
  }

  async #deleteSet(set: EntryFilterSetResponseModel) {
    this.#closeMenus();
    if (!(await deleteSet(this, set.key))) {
      await this.#notify('danger', this.#term('set_failed'));
      return;
    }

    this._sets = this._sets.filter((one) => one.key !== set.key);
    await this.#notify('positive', this.#term('set_deleted', set.name));
  }

  async #chooseColumns() {
    const answer = await umbOpenModal(this, ENTRY_COLUMNS_MODAL, {
      data: { fields: this._fields, columns: this._columns },
    }).catch(() => undefined);
    const form = this.#form;
    if (!answer || !form) return;
    this._columns = answer.columns;
    void this.#read();
    if (!(await saveColumns(this, form, answer.columns))) await this.#notify('danger', this.#term('columnsFailed'));
  }

  #closeMenus() {
    for (const menu of this.shadowRoot?.querySelectorAll<HTMLElement & { hidePopover?: () => void }>(
      'uui-popover-container',
    ) ?? [])
      menu.hidePopover?.();
  }

  // ---- Ticking and the bulk bar -----------------------------------------------------------------------------------------

  #tick(key: string, ticked: boolean) {
    this._ticked = ticked
      ? [...this._ticked.filter((one) => one !== key), key].slice(-MOST_TICKED)
      : this._ticked.filter((one) => one !== key);
  }

  #tickPage(ticked: boolean) {
    this._ticked = ticked ? this._rows.slice(0, MOST_TICKED).map((row) => row.key) : [];
  }

  #tickedRows(): EntryListRowResponseModel[] {
    const ticked = new Set(this._ticked);
    return this._rows.filter((row) => ticked.has(row.key));
  }

  async #act(action: BulkAction, rows: readonly EntryListRowResponseModel[]) {
    this.#closeMenus();
    if (this._busy || rows.length === 0) return;
    this._busy = true;
    try {
      if (action === 'delete') await this.#delete(rows);
      else if (action === 'rerun') await this.#rerun(rows);
      else await this.#move(action, rows);
    } finally {
      this._busy = false;
    }
  }

  /** Moves the entries the action applies to; the rows change in place and the toast offers Undo until the window ends. */
  async #move(action: BulkAction, rows: readonly EntryListRowResponseModel[]) {
    const form = this.#form;
    const state = MOVE_STATE[action];
    if (!form || !state) return;
    const applying = rows.filter((row) => APPLIES[action](row.state));
    const left = leftOut(
      action,
      rows.map((row) => row.state),
    );
    if (applying.length === 0) return;
    const before = new Map(applying.map((row) => [row.key, row.state]));
    const keys = applying.map((row) => row.key);
    let change = await changeState(this, form, keys, state);
    // A move back onto full options or doubled unique answers moves nothing until the user agrees (ADR-0208).
    if (change?.asks) {
      const agreed = await confirmText(this, {
        headline: this.#term(keys.length === 1 ? 'asks_moveHeadline' : 'asks_moveHeadlineMany'),
        content: asksLines(change.asks, this.#full).join(' '),
        confirmLabel: this.#term('asks_moveConfirm'),
        color: 'warning',
      }).then(
        () => true,
        () => false,
      );
      if (!agreed) return;
      change = await changeState(this, form, keys, state, true);
    }

    if (!change) {
      await this.#notify('danger', this.#term('moveFailed'));
      return;
    }

    const reached = new Map(change.moved.map((moved) => [moved.key, moved.state]));
    this._rows = this._rows.map((row) =>
      reached.has(row.key) ? { ...row, state: reached.get(row.key) ?? row.state } : row,
    );
    this.#reloadPanel(reached);
    this._ticked = [];
    const name = action === 'markSpam' ? 'spam' : action === 'notSpam' ? 'notSpam' : (MOVE_STATE[action] ?? state);
    const unmoved = left + change.unmoved.length;
    const message = [
      this.#term(`moved_${name}`, change.moved.length),
      unmoved > 0 ? this.#term(`left_${name}`, unmoved) : '',
    ]
      .filter(Boolean)
      .join(' ');
    this._said = message;
    if (change.moved.length === 0) {
      await this.#notify('warning', message);
      return;
    }

    const until = change.undoUntilUtc ? new Date(change.undoUntilUtc).getTime() : 0;
    const open = until - Date.now();
    if (open < UNDO_SHORTEST_MS) {
      await this.#notify('positive', message);
      return;
    }

    const data: UndoNotificationData = {
      message,
      undoLabel: this.#term('undo'),
      until,
      tooLate: this.#term('undoTooLate'),
      undo: async () => {
        const undone = await undoChange(this, form, change.changeKey);
        if (undone === 'done') {
          this._rows = this._rows.map((row) =>
            before.has(row.key) && reached.has(row.key) ? { ...row, state: before.get(row.key) ?? row.state } : row,
          );
          this.#reloadPanel(reached);
          this._said = this.#term('undone');
          return;
        }

        await this.#notify(
          undone === 'tooLate' ? 'warning' : 'danger',
          this.#term(undone === 'tooLate' ? 'undoTooLate' : 'undoFailed'),
        );
      },
    };
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    notifications?.peek('positive', {
      elementName: UNDO_NOTIFICATION,
      duration: open > UNDO_LONGEST_MS ? UNDO_LONGEST_MS : open + UNDO_LATE_MS,
      data,
    });
  }

  /** Reads the open entry again when a change of the list moved it, so its bar and chip are those of its state now. */
  #reloadPanel(moved: ReadonlyMap<string, string>) {
    if (this._open !== undefined && moved.has(this._open)) void this.#panel?.reload();
  }

  /** Deletes the entries for good, after asking with what goes with them. */
  async #delete(rows: readonly EntryListRowResponseModel[]) {
    const form = this.#form;
    if (!form) return;
    const keys = rows.map((row) => row.key);
    const plan = await deleteEntries(this, form, keys, false);
    if (!plan) {
      await this.#notify('danger', this.#term('deleteFailed'));
      return;
    }

    if (plan.entries === 0) return;
    const confirmed = await confirmText(this, {
      headline: this.#term('delete_headline', plan.entries),
      content: this.#term('delete_body', plan.entries, plan.files),
      confirmLabel: this.#term('delete_confirm', plan.entries),
      color: 'danger',
    }).then(
      () => true,
      () => false,
    );
    if (!confirmed) return;
    const done = await deleteEntries(this, form, keys, true);
    if (!done?.deleted) {
      await this.#notify('danger', this.#term('deleteFailed'));
      return;
    }

    const gone = new Set(keys);
    this._rows = this._rows.filter((row) => !gone.has(row.key));
    this._ticked = [];
    this._cursor = -1;
    if (this._open !== undefined && gone.has(this._open)) this._open = undefined;
    if (this._total !== null && !this._totalMore) this._total = Math.max(0, this._total - done.entries);
    await this.#notify('positive', this.#term('deleted', done.entries));
  }

  /** Runs the entries' actions again, after asking with the actions that run. */
  async #rerun(rows: readonly EntryListRowResponseModel[]) {
    const form = this.#form;
    if (!form) return;
    const keys = rows.map((row) => row.key);
    const plan = await rerunActions(this, form, keys, false);
    if (!plan) {
      await this.#notify('danger', this.#term('rerunFailed'));
      return;
    }

    if (plan.entries === 0) {
      await this.#notify('warning', this.#term('rerun_none'));
      return;
    }

    const confirmed = await confirmList(this, {
      headline: this.#term('rerun_headline', plan.entries),
      content: this.#term('rerun_body'),
      items: plan.actions.map((action) => ({
        name: action.name || action.type,
        detail: this.#term(`rerun_trigger_${action.trigger}`),
      })),
      confirmLabel: this.#term('rerun_confirm', plan.entries),
    }).then(
      () => true,
      () => false,
    );
    if (!confirmed) return;
    const done = await rerunActions(this, form, keys, true);
    if (!done?.queued) {
      await this.#notify('danger', this.#term('rerunFailed'));
      return;
    }

    this._ticked = [];
    await this.#notify(
      'positive',
      [this.#term('rerun_done', done.entries), done.leftOut > 0 ? this.#term('left_rerun', done.leftOut) : '']
        .filter(Boolean)
        .join(' '),
    );
  }

  async #notify(color: 'positive' | 'warning' | 'danger', message: string) {
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    notifications?.peek(color, { data: { message } });
  }

  // ---- The keyboard -------------------------------------------------------------------------------------------------

  /** The keys of section 4.13 while the list is where the user is: nothing they type in a box, and no motion. */
  #keys = (event: KeyboardEvent) => {
    if (!this._words || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
    const path = event.composedPath();
    if (!path.includes(this) && document.activeElement !== document.body) return;
    if (isTyping(path[0] ?? null)) return;
    const row = this._rows[this._cursor];
    switch (event.key) {
      case 'j':
        this.#moveCursor(1);
        break;
      case 'k':
        this.#moveCursor(-1);
        break;
      case 'Enter':
        // Only on the cursor's row itself: Enter on a button or a box inside it is that control's.
        if (!row || !(path[0] instanceof HTMLTableRowElement)) return;
        void this.#openRow(this._cursor);
        break;
      case 'a':
      case 'r':
      case 's': {
        const action: BulkAction & PanelAction =
          event.key === 'a' ? 'approve' : event.key === 'r' ? 'reject' : 'markSpam';
        // With an entry open, the keys act on it, as its bar would; otherwise on the cursor's row.
        if (this._open !== undefined) {
          this.#panel?.act(action);
          break;
        }

        if (!row || !bulkActions([row.state], this._permissions).includes(action)) return;
        void this.#act(action, [row]);
        break;
      }
      case '/':
        this.shadowRoot?.querySelector<HTMLElement>('.search')?.focus();
        break;
      case 'Escape':
        // Esc with a menu open closes the menu only, wherever the focus is (on the button that opened it, say).
        if (path.some(isOpenPopover) || this.#menuOpen()) return;
        if (this._open !== undefined) {
          void this.#closePanel();
          break;
        }

        if (this._cursor < 0) return;
        this._cursor = -1;
        (document.activeElement as HTMLElement | null)?.blur?.();
        break;
      case '?':
        void umbOpenModal(this, ENTRY_KEYS_MODAL).catch(() => undefined);
        break;
      default:
        return;
    }

    event.preventDefault();
  };

  /** Whether a menu of the view, or of the open entry's panel, is open. */
  #menuOpen(): boolean {
    return [this.shadowRoot, this.#panel?.shadowRoot].some((root) =>
      [...(root?.querySelectorAll('uui-popover-container') ?? [])].some(isOpenPopover),
    );
  }

  /** Moves the cursor; with an entry open, the panel follows it, so `j` reads on through the entries. */
  #moveCursor(by: number) {
    if (this._rows.length === 0) return;
    const at = this._cursor < 0 ? (by > 0 ? 0 : this._rows.length - 1) : this._cursor + by;
    const index = Math.max(0, Math.min(this._rows.length - 1, at));
    if (this._open === undefined) {
      this.#focusRow(index);
      return;
    }

    if (!this._covering) this.#focusRow(index);
    void this.#openRow(index, false);
  }

  #focusRow(index: number) {
    const row = this._rows[index];
    if (!row) return;
    this._cursor = index;
    const element = this.shadowRoot?.querySelector<HTMLElement>(`tbody tr[data-key="${row.key}"]`);
    element?.focus();
    element?.scrollIntoView({ block: 'nearest' });
  }

  // ---- Drawing -------------------------------------------------------------------------------------------------------

  #menuButton(label: string, act: () => void, icon?: string): TemplateResult {
    return html`<button type="button" @click=${act}>
      ${icon ? html`<uui-icon name=${icon} aria-hidden="true"></uui-icon>` : nothing}${label}
    </button>`;
  }

  #filterMenu(): TemplateResult {
    const fields = this._fields.filter((field) => field.tests.length > 0);
    return html`<uui-button look="outline" label=${this.#term('filter')} popovertarget="filter-menu">
        <uui-icon name="icon-filter" aria-hidden="true"></uui-icon> ${this.#term('filter')}
      </uui-button>
      <uui-popover-container
        id="filter-menu"
        placement="bottom-start"
        @toggle=${(event: ToggleEvent) => {
          if (event.newState === 'open') (event.target as HTMLElement).querySelector<HTMLElement>('button')?.focus();
        }}
      >
        <div class="menu">
          ${this.#menuButton(this.#term('menu_state'), () => void this.#edit('state'))}
          ${this.#menuButton(this.#term('menu_received'), () => void this.#edit('received'))}
          ${this.#menuButton(this.#term('menu_actions'), () => void this.#edit('actions'))}
          ${
            fields.length > 0
              ? html`<h3>${this.#term('menu_fields')}</h3>
                  ${fields.map((field) =>
                    this.#menuButton(
                      field.removed ? `${field.label} (${this.#term('chooser_removed')})` : field.label,
                      () => void this.#edit('field', field),
                    ),
                  )}`
              : nothing
          }
          ${
            this._sets.length > 0
              ? html`<h3>${this.#term('menu_saved')}</h3>
                  ${this._sets.map(
                    (set) =>
                      html`<div class="set">
                        ${this.#menuButton(set.name, () => this.#applySet(set))}
                        <button
                          type="button"
                          aria-label=${this.#term('menu_deleteSet', set.name)}
                          title=${this.#term('menu_deleteSet', set.name)}
                          @click=${() => void this.#deleteSet(set)}
                        >
                          <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>
                        </button>
                      </div>`,
                  )}`
              : nothing
          }
        </div>
      </uui-popover-container>`;
  }

  #toolbar(): TemplateResult {
    return html`<div class="toolbar">
      <uui-input
        class="search"
        type="search"
        label=${this.#term('search')}
        placeholder=${this.#term('search')}
        .value=${this._search}
        @input=${(event: Event) => this.#typed((event.target as HTMLInputElement).value)}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key === 'Enter') this.#searchNow();
          if (event.key === 'Escape' && this._search) {
            event.stopPropagation();
            this.#clearSearch();
          }
        }}
      >
        <uui-icon slot="prepend" name="icon-search" aria-hidden="true"></uui-icon>
      </uui-input>
      ${this.#filterMenu()}
      <span class="count">${this.#countText()}</span>
      <uui-button compact look="secondary" label=${this.#term('more')} popovertarget="more-menu">
        <uui-symbol-more></uui-symbol-more>
      </uui-button>
      <uui-popover-container id="more-menu" placement="bottom-end">
        <div class="menu">
          ${this.#menuButton(
            this.#term('shortcuts'),
            () => {
              this.#closeMenus();
              void umbOpenModal(this, ENTRY_KEYS_MODAL).catch(() => undefined);
            },
            'icon-keyboard',
          )}
        </div>
      </uui-popover-container>
    </div>`;
  }

  #chips(): TemplateResult | typeof nothing {
    if (!isFiltered(this._filter)) return nothing;
    const chips = chipsOf(this._filter, this.#byKey, this.localize.lang(), this.#full);
    return html`<div class="chips" role="group" aria-label=${this.#term('filters')}>
      ${chips.map(
        (chip) =>
          html`<span class="chip">
            <button
              type="button"
              class="chip-text"
              aria-label=${this.#term('chip_edit', chip.text)}
              @click=${() => this.#editChip(chip)}
            >
              ${chip.text}
            </button>
            <button
              type="button"
              class="chip-remove"
              aria-label=${this.#term('chip_remove', chip.text)}
              title=${this.#term('chip_remove', chip.text)}
              @click=${() => this.#removeChip(chip)}
            >
              <uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
            </button>
          </span>`,
      )}
      <uui-button
        look="secondary"
        compact
        label=${this.#term('saveAs')}
        @click=${() => void this.#saveAs()}
      ></uui-button>
      <uui-button
        look="default"
        compact
        label=${this.#term('clearFilters')}
        @click=${() => this.#clearFilters()}
      ></uui-button>
    </div>`;
  }

  #sortHeader(label: string, field: string | null): TemplateResult {
    const sort = this._filter.sort;
    // Without a sort of its own, the list is newest first: Received is sorted.
    const sorted = (sort?.field ?? null) === field;
    const descending = sorted ? (sort?.descending ?? true) : undefined;
    const ariaSort = !sorted ? 'none' : descending ? 'descending' : 'ascending';
    return html`<th scope="col" aria-sort=${ariaSort}>
      <button type="button" class="sort" @click=${() => this.#sortBy(field)}>
        ${label}
        ${
          sorted
            ? html`<uui-icon name=${descending ? 'icon-arrow-down' : 'icon-arrow-up'} aria-hidden="true"></uui-icon>`
            : nothing
        }
      </button>
    </th>`;
  }

  #row(
    row: EntryListRowResponseModel,
    index: number,
    showState: boolean,
    columns: readonly EntryListFieldResponseModel[],
    narrowed: boolean,
  ): TemplateResult {
    const label = rowLabel(row, this.#full);
    const runs = runsText(row.actions, this.#full);
    const alarming = runs && (runs.tone === 'danger' || runs.tone === 'warning');
    const below = alarming ? html`<span class="below tone-${runs.tone}">${runs.text}</span>` : nothing;
    const open = row.key === this._open;
    return html`<tr
      data-key=${row.key}
      tabindex="-1"
      aria-current=${open ? 'true' : nothing}
      class=${[row.unseen ? 'unseen' : '', index === this._cursor ? 'cursor' : '', open ? 'open' : '']
        .filter(Boolean)
        .join(' ')}
      @focus=${() => (this._cursor = index)}
    >
      <td class="tick">
        <!-- Named by aria-label, which the box's input takes, so no label text shows beside it. -->
        <uui-checkbox
          aria-label=${this.#term('select', label)}
          .checked=${this._ticked.includes(row.key)}
          @change=${(event: Event) => this.#tick(row.key, (event.target as HTMLInputElement).checked)}
        ></uui-checkbox>
      </td>
      <th scope="row">
        <button type="button" class="name" @click=${() => void this.#openRow(index)}>
          ${row.unseen ? html`<span class="dot" aria-hidden="true"></span>` : nothing}${rowName(row, this.#full)}${
            row.unseen ? html`<span class="visually-hidden">, ${this.#term('unseen')}</span>` : nothing
          }
        </button>
        ${showState ? nothing : below}
      </th>
      ${
        showState
          ? html`<td>
              <uui-tag look="secondary" color=${STATE_COLOR[row.state] ?? 'default'}
                >${stateText(row.state, this.#full)}</uui-tag
              >
              ${below}
            </td>`
          : nothing
      }
      <td class="received">${receivedText(row.received, new Date(), this.localize.lang(), this.#full)}</td>
      <td class="runs runs-column tone-${runs?.tone ?? 'muted'}">${runs?.text ?? this.#term('none')}</td>
      ${columns.map((field) => {
        // An empty value reads "none", never a blank or a dash (section 4.13).
        const cell = row.cells.find((one) => one.field === field.key);
        const text = cell?.masked ? MASKED : (cell?.texts ?? []).join('; ');
        return html`<td>
          ${text ? html`<span class="cell">${text}</span>` : html`<span class="tone-muted">${this.#term('none')}</span>`}
        </td>`;
      })}
      ${narrowed ? nothing : html`<td class="end"></td>`}
    </tr>`;
  }

  #table(): TemplateResult {
    const fixed = fixedColumns(this._filter);
    const showState = !fixed.state;
    const byKey = this.#byKey;
    const narrowed = this._open !== undefined && !this._covering;
    const columns = narrowed
      ? []
      : this.#shownColumns()
          .map((key) => byKey.get(key))
          .filter((field): field is EntryListFieldResponseModel => !!field);
    const all =
      this._rows.length > 0 && this._rows.slice(0, MOST_TICKED).every((row) => this._ticked.includes(row.key));
    return html`<uui-box class="box--flush">
      <div class="scroller">
        <table class=${[showState ? '' : 'no-state', narrowed ? 'narrowed' : ''].filter(Boolean).join(' ')}>
          <thead>
            <tr>
              <th scope="col" class="tick">
                <uui-checkbox
                  aria-label=${this.#term('selectPage')}
                  .checked=${all}
                  .indeterminate=${!all && this._ticked.length > 0}
                  @change=${(event: Event) => this.#tickPage((event.target as HTMLInputElement).checked)}
                ></uui-checkbox>
              </th>
              <th scope="col">${this.#term('column_entry')}</th>
              ${showState ? html`<th scope="col">${this.#term('column_state')}</th>` : nothing}
              ${this.#sortHeader(this.#term('column_received'), null)}
              <th scope="col" class="runs-column">${this.#term('column_actions')}</th>
              ${columns.map((field) =>
                field.sorts ? this.#sortHeader(field.label, field.key) : html`<th scope="col">${field.label}</th>`,
              )}
              ${
                // Beside the panel the rows show no chosen columns, so there is nothing to choose, and no column for it.
                narrowed
                  ? nothing
                  : html`<th scope="col" class="end">
                      <button
                        type="button"
                        class="choose"
                        aria-label=${this.#term('column_choose')}
                        title=${this.#term('column_choose')}
                        @click=${() => void this.#chooseColumns()}
                      >
                        <uui-icon name="icon-columns" aria-hidden="true"></uui-icon>
                      </button>
                    </th>`
              }
            </tr>
          </thead>
          <tbody>
            ${repeat(
              this._rows,
              (row) => row.key,
              (row, index) => this.#row(row, index, showState, columns, narrowed),
            )}
          </tbody>
        </table>
      </div>
      ${
        this._next
          ? html`<div class="footer">
              ${this._moreFailed ? html`<p role="alert">${this.#term('moreFailed')}</p>` : nothing}
              <uui-button
                look="secondary"
                label=${this.#term('showMore')}
                .state=${this._more ? 'waiting' : undefined}
                @click=${() => void this.#showMore()}
              ></uui-button>
            </div>`
          : nothing
      }
    </uui-box>`;
  }

  #empty(): TemplateResult {
    const search = this._filter.search?.trim();
    if (search) {
      const sensitive = this._fields.some((field) => field.sensitive);
      return html`<div class="empty">
        <p>${this.#term(sensitive ? 'noSearchSensitive' : 'noSearch', search)}</p>
        <uui-button
          look="secondary"
          label=${this.#term('clearSearch')}
          @click=${() => this.#clearSearch()}
        ></uui-button>
      </div>`;
    }

    if (isFiltered(this._filter)) return html`<div class="empty"><p>${this.#term('noResults')}</p></div>`;
    return html`<div class="empty">
      <p>${this.#term(this._permissions.includes('design') ? 'empty_design' : 'empty_others')}</p>
    </div>`;
  }

  #bulk(): TemplateResult | typeof nothing {
    const rows = this.#tickedRows();
    if (rows.length === 0) return nothing;
    const offered = bulkActions(
      rows.map((row) => row.state),
      this._permissions,
    );
    const shown = offered.slice(0, SHOWN_ACTIONS);
    const rest = offered.slice(SHOWN_ACTIONS);
    return html`<div class="bulk" role="region" aria-label=${this.#term('selected', rows.length)}>
      <span class="selected">${this.#term('selected', rows.length)}</span>
      <uui-button
        look="default"
        compact
        label=${this.#term('clearSelection')}
        @click=${() => (this._ticked = [])}
      ></uui-button>
      <div class="actions">
        ${shown.map(
          (action) =>
            html`<uui-button
              look="outline"
              label=${this.#term(action)}
              ?disabled=${this._busy}
              @click=${() => void this.#act(action, rows)}
            ></uui-button>`,
        )}
        ${
          rest.length > 0
            ? html`<uui-button compact look="outline" label=${this.#term('bulk_more')} popovertarget="bulk-more">
                  <uui-symbol-more></uui-symbol-more>
                </uui-button>
                <uui-popover-container id="bulk-more" placement="top-end">
                  <div class="menu">
                    ${rest.map((action) => this.#menuButton(this.#term(action), () => void this.#act(action, rows)))}
                  </div>
                </uui-popover-container>`
            : nothing
        }
      </div>
    </div>`;
  }

  #list(): TemplateResult {
    if (this._state === 'loading')
      return html`<uui-box class="box--flush">${skeletonLines(this.#term('loading'), [70, 64, 58, 52])}</uui-box>`;
    if (this._state === 'failed')
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#term('failed')}</span>
          <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#open()}></uui-button>
        </div>
      </uui-box>`;
    return this._rows.length === 0 ? this.#empty() : this.#table();
  }

  override render() {
    // Until its words are loaded, the view's place holds a skeleton that names nothing; a failed load says so in the
    // workspace's words, which load with the workspace (ADR-0201).
    if (!this._words)
      return this._state === 'failed'
        ? html`<div class="list">
            <uui-box>
              <div class="failed" role="alert">
                <span>${this.#full('jfbDesigner_entriesFailed')}</span>
                <uui-button
                  look="outline"
                  label=${this.#full('jfbSettings_retry')}
                  @click=${() => void this.#open()}
                ></uui-button>
              </div>
            </uui-box>
          </div>`
        : html`<div class="list"><uui-box class="box--flush">${skeletonLines('', [70, 64, 58, 52])}</uui-box></div>`;
    // A form without any entry yet shows only the sentence: there is nothing to search, filter or tick (IA 6.1).
    const firstUse =
      this._state === 'ready' && this._rows.length === 0 && !isFiltered(this._filter) && !this._filter.search;
    const open = this._open;
    const row = open === undefined ? undefined : this._rows.find((one) => one.key === open);
    // Covered by the panel, the list is out of reach of the keyboard and of screen readers until the panel closes.
    const covered = open !== undefined && this._covering;
    return html`<div class="list" ?inert=${covered}>
        ${firstUse ? nothing : this.#toolbar()} ${this.#chips()} ${this.#list()} ${this.#bulk()}
        <div class="visually-hidden" role="status">${this._said}</div>
      </div>
      ${
        open !== undefined && this.#form
          ? html`<jfb-entry-panel
              .form=${this.#form}
              .entry=${open}
              place=${this._covering ? 'covering' : 'beside'}
              .rowShowsTitle=${row ? Boolean(row.title) : true}
              @jfb-entry-seen=${this.#rowSeen}
              @jfb-entry-moved=${this.#rowMoved}
              @jfb-entry-gone=${this.#rowGone}
              @jfb-entry-close=${() => void this.#closePanel()}
            ></jfb-entry-panel>`
          : nothing
      }`;
  }
}

if (!customElements.get('jfb-entries-view')) customElements.define('jfb-entries-view', EntriesViewElement);

export { EntriesViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entries-view': EntriesViewElement;
  }
}
