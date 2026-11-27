// The entry panel (ROADMAP 11.2, IA 6.3, ADR-0208): one entry beside its list, covering it on a narrow screen, or alone
// from its own link. Its header says only what the row beside it doesn't; its tabs are Answers, Timeline and Notes; its
// bar holds the actions of the entry's state that the user may use, the primary on the right. A change of state keeps
// the panel on the entry and offers Undo for its window; nothing animates, as these are done hundreds of times a day.
import { css, html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UMB_CURRENT_USER_CONTEXT } from '@umbraco-cms/backoffice/current-user';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_EDIT_MEMBER_WORKSPACE_PATH_PATTERN } from '@umbraco-cms/backoffice/member';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type {
  EntryMoveAsksResponseModel,
  EntryNoteResponseModel,
  EntryPanelAnswerResponseModel,
  EntryPanelEditResponseModel,
  EntryPanelFileResponseModel,
  EntryPanelResponseModel,
  EntryTimelineResponseModel,
} from '../api';
import { saveBlob } from '../library/actions/export-form.action';
import { formPath } from '../section/entity-types';
import { pressOf } from '../styles/press';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { confirmList, confirmText } from '../ui/confirm';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { UNDO_NOTIFICATION, type UndoNotificationData } from '../ui/undo-notification.element';
import { changeState, deleteEntries, rerunActions, undoChange } from './entries-api';
import { receivedText, stateText } from './entry-list-model';
import { ENTRY_FORWARD_MODAL, ENTRY_IMAGE_MODAL } from './entry-modals';
import {
  addNote,
  changeNote,
  editAnswers,
  fileBlob,
  openEntry,
  readNotes,
  readTimeline,
  retryRun,
  revealAnswer,
} from './entry-panel-api';
import {
  PANEL_MOVE,
  asksLines,
  detailsLine,
  holdEnd,
  panelBar,
  problemAnswer,
  problemText,
  sizeText,
  timelineLines,
  type PanelAction,
} from './entry-panel-model';
import { loadEntryWords } from './entry-words';
import { writeExportFile } from './export-api';
import { printDocument } from './export-print';
import { refreshUnseen } from './unseen-poll';

/** Where the panel is: beside its list, covering it on a narrow screen, or alone from the entry's own link. */
export type PanelPlace = 'beside' | 'covering' | 'alone';

type Tab = 'answers' | 'timeline' | 'notes';

/** The tone of a state's chip, as the list has it. */
const STATE_COLOR: Record<string, string> = { submitted: 'warning', approved: 'positive' };

/** What a sensitive answer shows while it is masked. */
const MASKED = String.fromCharCode(0x2022).repeat(6);

/** What joins the parts of a line: a middle dot between spaces. */
const JOIN = ` ${String.fromCharCode(0xb7)} `;

/** The longest Undo, and Umbraco's own time for the toast once its window has passed. */
const UNDO_LONGEST_MS = 60_000;
const UNDO_LATE_MS = 6_000;

/** The longest note, as the server takes it. */
const MOST_NOTE = 10_000;

/** What the panel tells its list: the entry's state now, that it was opened, that it is gone, or to close. */
export interface EntryMovedDetail {
  readonly key: string;
  readonly state: string;
}

// Umbraco's Members section, whose editor the member who sent an entry links to (ADR-0231).
const MEMBERS_SECTION = 'Umb.Section.Members';

export class EntryPanelElement extends UmbLitElement {
  static override properties = {
    form: { type: String },
    entry: { type: String },
    place: { type: String },
    showsForm: { type: Boolean, attribute: 'shows-form' },
    hidesState: { type: Boolean, attribute: 'hides-state' },
    rowShowsTitle: { type: Boolean, attribute: 'row-shows-title' },
    _words: { state: true },
    _members: { state: true },
    _state: { state: true },
    _panel: { state: true },
    _tab: { state: true },
    _more: { state: true },
    _revealed: { state: true },
    _revealFailed: { state: true },
    _editing: { state: true },
    _draft: { state: true },
    _problems: { state: true },
    _saving: { state: true },
    _timeline: { state: true },
    _timelineFailed: { state: true },
    _notes: { state: true },
    _notesFailed: { state: true },
    _note: { state: true },
    _noteEditing: { state: true },
    _noteDraft: { state: true },
    _noteFailed: { state: true },
    _busy: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    pressOf('.link, .reveal, .file'),
    css`
      :host {
        display: flex;
        flex-direction: column;
        min-block-size: 0;
        block-size: 100%;
        background: var(--uui-color-background);
      }

      .head {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-4);
        flex: none;
        min-block-size: 60px;
        padding: 0 var(--uui-size-space-5);
        border-block-end: 1px solid var(--uui-color-border);
        background: var(--uui-color-surface);
      }

      .names {
        display: grid;
        flex: 1;
        min-inline-size: 0;
        padding-block: var(--uui-size-space-3);
      }

      h2 {
        margin: 0;
        font-size: var(--uui-type-h5-size);
        overflow-wrap: anywhere;
      }

      h2:focus {
        outline: none;
      }

      .form-link {
        justify-self: start;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .form-link:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      .form-link:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      /* UUI's tab group takes all the height it is given, as in a workspace's header; here it takes its tabs' own. */
      .tabs {
        flex: none;
        block-size: auto;
        border-block-end: 1px solid var(--uui-color-border);
        background: var(--uui-color-surface);
      }

      .body {
        flex: 1;
        min-block-size: 0;
        position: relative;
        overflow: auto;
        padding: var(--uui-size-space-5);
      }

      .details {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--uui-size-space-2) var(--uui-size-space-4);
        margin: 0 0 var(--uui-size-space-5);
        color: var(--jfb-bo-color-text-muted);
      }

      .details-top {
        display: flex;
        flex-wrap: wrap;
        align-items: flex-start;
        justify-content: space-between;
        gap: var(--uui-size-space-3);
      }

      /* Edit answers, wrapped under the details in a narrow panel, keeps a gap to the first answer under it. */
      .details-top > uui-button {
        margin-block-end: var(--uui-size-space-3);
      }

      .link,
      .reveal,
      .file {
        padding: 0;
        border: 0;
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        text-decoration: underline;
        cursor: pointer;
      }

      .link:hover,
      .reveal:hover,
      .file:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      .link:focus-visible,
      .reveal:focus-visible,
      .file:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .more {
        display: grid;
        grid-template-columns: minmax(6rem, min(14rem, 35%)) minmax(0, 1fr);
        gap: var(--uui-size-space-2) var(--uui-size-space-5);
        margin: 0 0 var(--uui-size-space-5);
      }

      /* The member's link in Umbraco's link colour: the browser's own blue is 1.4:1 on the dark theme's box. */
      .more a {
        color: var(--uui-color-interactive);
      }

      .more a:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      .more a:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .spam {
        margin: 0 0 var(--uui-size-space-5);
      }

      .held {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
        margin: 0 0 var(--uui-size-space-4);
      }

      .held uui-icon {
        flex: none;
        margin-block-start: 0.2em;
      }

      .spam ul {
        margin: var(--uui-size-space-2) 0 0;
        padding-inline-start: var(--uui-size-space-5);
      }

      h3 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-3);
        font-size: var(--uui-type-default-size);
      }

      h4 {
        margin: var(--uui-size-space-4) 0 var(--uui-size-space-2);
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        font-weight: 700;
      }

      /* The labels take what they need, up to a third of the panel, so the answers stay near them when it is wide. */
      dl {
        display: grid;
        grid-template-columns: minmax(6rem, min(14rem, 35%)) minmax(0, 1fr);
        gap: var(--uui-size-space-3) var(--uui-size-space-5);
        align-items: baseline;
        margin: 0;
      }

      /* Beside a field, a label sits at its middle. */
      dl.editing {
        align-items: center;
      }

      dt {
        color: var(--jfb-bo-color-text-muted);
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      /* An answer's own line breaks show; the template's never do. */
      .line {
        display: block;
        white-space: pre-line;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .small {
        display: block;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .line + .line {
        margin-block-start: var(--uui-size-space-1);
      }

      .files {
        display: grid;
        gap: var(--uui-size-space-1);
      }

      .file {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        text-align: start;
      }

      .edit-note {
        margin: 0 0 var(--uui-size-space-4);
        color: var(--jfb-bo-color-text-muted);
      }

      .choices {
        display: grid;
        gap: var(--uui-size-space-2);
      }

      .problem {
        display: block;
        margin-block-start: var(--uui-size-space-1);
        color: var(--jfb-bo-color-danger-text);
        font-size: var(--uui-type-small-size);
      }

      select {
        min-block-size: 36px;
        inline-size: 100%;
        padding: 0 var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      select:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 1px;
      }

      uui-input,
      uui-textarea {
        inline-size: 100%;
      }

      .events {
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .event {
        display: grid;
        grid-template-columns: 20px minmax(0, 1fr) max-content;
        gap: var(--uui-size-space-3);
        align-items: start;
        padding: var(--uui-size-space-4) 0;
      }

      .event + .event {
        border-block-start: 1px solid var(--uui-color-divider-standalone);
      }

      .event uui-icon {
        margin-block-start: 2px;
        color: var(--jfb-bo-color-text-muted);
      }

      .event.tone-danger uui-icon,
      .event.tone-danger .text {
        color: var(--jfb-bo-color-danger-text);
      }

      .event.tone-warning uui-icon {
        color: var(--jfb-bo-color-warning-text);
      }

      .when {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        white-space: nowrap;
      }

      .new-note {
        display: grid;
        gap: var(--uui-size-space-2);
        margin-block-end: var(--uui-size-space-5);
      }

      .new-note uui-button,
      .note-actions {
        justify-self: end;
      }

      .note-actions {
        display: flex;
        gap: var(--uui-size-space-2);
      }

      .note {
        padding: var(--uui-size-space-4) 0;
      }

      .note + .note {
        border-block-start: 1px solid var(--uui-color-divider-standalone);
      }

      .note-head {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--uui-size-space-3);
        margin-block-end: var(--uui-size-space-2);
      }

      .note-body {
        margin: 0;
        white-space: pre-line;
        overflow-wrap: anywhere;
      }

      .bar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: flex-end;
        gap: var(--uui-size-space-2);
        flex: none;
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border-block-start: 1px solid var(--uui-color-border);
        background: var(--uui-color-surface);
      }

      .menu {
        display: grid;
        min-inline-size: 14rem;
        padding: var(--uui-size-space-2) 0;
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-3);
      }

      .menu button {
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      .menu button:hover,
      .menu button:focus-visible {
        background: var(--uui-color-surface-emphasis);
        outline: none;
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .failed span {
        flex: 1 1 100%;
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  /** The entry's form. */
  declare form: string;
  /** The entry. */
  declare entry: string;
  declare place: PanelPlace;
  /** Whether the header names the form, as on the Inbox where rows come from many forms. */
  declare showsForm: boolean;
  /** Whether the header leaves out the state chip, as in Spam, where the place says it (IA 6.3). */
  declare hidesState: boolean;
  /** Whether the row beside the panel shows the entry's title, so the header names its number. */
  declare rowShowsTitle: boolean;

  declare private _words: boolean;
  declare private _members: boolean;
  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _panel: EntryPanelResponseModel | undefined;
  declare private _tab: Tab;
  declare private _more: boolean;
  /** The answers revealed, by field, until the panel moves on. */
  declare private _revealed: ReadonlyMap<string, EntryPanelAnswerResponseModel>;
  declare private _revealFailed: string | undefined;
  declare private _editing: boolean;
  /** The answers being edited, by field, in the wire form. */
  declare private _draft: Readonly<Record<string, readonly string[]>>;
  /** Each edited field's problem, by field. */
  declare private _problems: Readonly<Record<string, string>>;
  declare private _saving: boolean;
  declare private _timeline: EntryTimelineResponseModel | undefined;
  declare private _timelineFailed: boolean;
  declare private _notes: readonly EntryNoteResponseModel[] | undefined;
  declare private _notesFailed: boolean;
  declare private _note: string;
  declare private _noteEditing: number | undefined;
  declare private _noteDraft: string;
  declare private _noteFailed: boolean;
  declare private _busy: boolean;

  /** Counts the reads of an entry, so only the latest one's answer is shown. */
  #reads = 0;

  // The member who sent the entry: their name, linked to their editor where the user may open it; a member Umbraco no
  // longer has is said so.
  #member(key: string, name: string | null | undefined): TemplateResult {
    if (!name) return html`${this.#term('details_memberGone')}`;
    return this._members
      ? html`<a href=${UMB_EDIT_MEMBER_WORKSPACE_PATH_PATTERN.generateAbsolute({ unique: key })}>${name}</a>`
      : html`${name}`;
  }

  constructor() {
    super();
    this.form = '';
    this.entry = '';
    this.place = 'beside';
    this.showsForm = false;
    this.hidesState = false;
    this.rowShowsTitle = true;
    this._words = false;
    this._members = false;
    // The member's name links to their editor for a user who may open the Members section (ADR-0231).
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (user) => {
      this.observe(user?.allowedSections, (sections) => {
        this._members = sections?.includes(MEMBERS_SECTION) ?? false;
      });
    });
    this._state = 'loading';
    this._tab = 'answers';
    this._more = false;
    this._revealed = new Map();
    this._editing = false;
    this._draft = {};
    this._problems = {};
    this._saving = false;
    this._timelineFailed = false;
    this._notesFailed = false;
    this._note = '';
    this._noteDraft = '';
    this._noteFailed = false;
    this._busy = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void loadEntryWords(this.localize.lang()).then(() => (this._words = true));
  }

  protected override willUpdate(changed: PropertyValues<this>) {
    if ((changed.has('entry') || changed.has('form')) && this.form && this.entry) void this.#open();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);
  #full = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  get #permissions(): readonly string[] {
    return this._panel?.permissions ?? [];
  }

  // Whether the focus was asked for and not given yet: while the entry is read the panel has nothing to give it to.
  #focusWanted = false;

  /**
   * Moves the focus to the panel's heading, as opening an entry does. Asked while the entry is still read, it is given
   * once it is: to the heading, or to Try again where the entry could not be shown, so a panel that covers its list
   * never leaves the focus on the page behind it.
   */
  focusHeading() {
    this.#focusWanted = true;
    void this.updateComplete.then(() => this.#giveFocus());
  }

  #giveFocus() {
    if (!this.#focusWanted) return;
    const target = this.shadowRoot?.querySelector<HTMLElement>('h2, .failed uui-button');
    if (!target) return;
    this.#focusWanted = false;
    target.focus();
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    this.#giveFocus();
  }

  /** Runs an action of the bar, as its key does: nothing when the entry's bar has no such action. */
  act(action: PanelAction) {
    // While the next entry loads, a key acts on nothing: never on the entry shown before.
    if (!this._panel || this._state !== 'ready' || this._panel.key !== this.entry || this._editing) return;
    const bar = panelBar(this._panel.state, this._panel.manualApproval, this.#permissions, !!this._panel.heldUntil);
    if (bar.primary !== action && !bar.visible.includes(action) && !bar.more.includes(action)) return;
    void this.#run(action);
  }

  // ---- Reading ------------------------------------------------------------------------------------------------------

  async #open() {
    const read = ++this.#reads;
    this._state = 'loading';
    this._tab = 'answers';
    this._more = false;
    this._revealed = new Map();
    this._revealFailed = undefined;
    this._editing = false;
    this._draft = {};
    this._problems = {};
    this._timeline = undefined;
    this._notes = undefined;
    this._noteEditing = undefined;
    const panel = await openEntry(this, this.form, this.entry);
    if (read !== this.#reads) return;
    this._panel = panel;
    this._state = panel ? 'ready' : 'failed';
    if (panel) this.#tell('jfb-entry-seen', { key: panel.key, state: panel.state });
  }

  /** Reads the entry again where it is, as after its list changed it: its tab, and any edit, stay. */
  async reload() {
    const read = this.#reads;
    const panel = await openEntry(this, this.form, this.entry);
    if (!panel || read !== this.#reads) return;
    this._panel = panel;
    this._timeline = undefined;
    if (this._tab === 'timeline') void this.#readTimeline();
  }

  #tell(name: string, detail: EntryMovedDetail) {
    this.dispatchEvent(new CustomEvent(name, { detail, bubbles: true, composed: true }));
    // Opening, moving or deleting an entry changes the Inbox's unseen count too (ADR-0209).
    refreshUnseen();
  }

  async #notify(color: 'positive' | 'warning' | 'danger', message: string) {
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    notifications?.peek(color, { data: { message } });
  }

  async #showTab(tab: Tab) {
    this._tab = tab;
    if (tab === 'timeline' && !this._timeline) await this.#readTimeline();
    if (tab === 'notes' && !this._notes) await this.#readNotes();
  }

  async #readTimeline() {
    this._timelineFailed = false;
    const timeline = await readTimeline(this, this.form, this.entry);
    this._timelineFailed = !timeline;
    this._timeline = timeline;
  }

  async #readNotes() {
    this._notesFailed = false;
    const notes = await readNotes(this, this.form, this.entry);
    this._notesFailed = !notes;
    this._notes = notes;
  }

  // ---- The actions --------------------------------------------------------------------------------------------------

  async #run(action: PanelAction) {
    const panel = this._panel;
    if (this._busy || !panel) return;
    this._busy = true;
    try {
      const state = PANEL_MOVE[action];
      if (state) await this.#move(panel, state);
      else if (action === 'forward') await this.#forward(panel);
      else if (action === 'delete') await this.#delete(panel);
      else if (action === 'print') await this.#print(panel);
      else if (action === 'rerun') await this.#rerun(panel);
    } finally {
      this._busy = false;
    }
  }

  /**
   * Print: the entry's printable view, its answers as the summary lists them and no sensitive field, in the browser's
   * print dialog (IA 6.3, ADR-0210).
   */
  async #print(panel: EntryPanelResponseModel) {
    const file = await writeExportFile(this, this.form, {
      format: 'print',
      entries: [panel.key],
      sensitive: false,
      // One entry's print leaves the export dialog's choices as they were.
      remember: false,
    });
    const printed = 'value' in file && (await printDocument(await file.value.text()));
    if (!printed) await this.#notify('danger', this.#term('print_failed'));
  }

  /** Asks first what a move back or an edit would overbook or double; whether the user agreed. */
  async #agrees(asks: EntryMoveAsksResponseModel, edit: boolean): Promise<boolean> {
    try {
      await confirmText(this, {
        headline: this.#term(edit ? 'asks_editHeadline' : 'asks_moveHeadline'),
        content: asksLines(asks, this.#full).join(' '),
        confirmLabel: this.#term(edit ? 'asks_editConfirm' : 'asks_moveConfirm'),
        color: 'warning',
      });
      return true;
    } catch {
      return false;
    }
  }

  async #move(panel: EntryPanelResponseModel, state: string) {
    // The entry's form, held from the press on: the Inbox's panel goes on to entries of other forms while a move is
    // on its way and while its Undo waits in a toast.
    const form = this.form;
    let change = await changeState(this, form, [panel.key], state);
    if (change?.asks) {
      if (!(await this.#agrees(change.asks, false))) return;
      change = await changeState(this, form, [panel.key], state, true);
    }

    const moved = change?.moved[0];
    if (!change || !moved) {
      await this.#notify('danger', this.#term('moveFailed'));
      return;
    }

    const before = panel.state;
    this.#tell('jfb-entry-moved', { key: panel.key, state: moved.state });
    // Only the panel that still shows the entry takes its new state: it may show another by now.
    if (this._panel?.key === panel.key) {
      this._panel = { ...this._panel, state: moved.state };
      this._timeline = undefined;
      if (this._tab === 'timeline') void this.#readTimeline();
    }

    const name = state === 'spam' ? 'spam' : before === 'spam' ? 'notSpam' : moved.state;
    const message = this.#term(`movedOne_${name}`, panel.number);
    const until = change.undoUntilUtc ? new Date(change.undoUntilUtc).getTime() : 0;
    const open = until - Date.now();
    if (open < 1_000) {
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
          this.#tell('jfb-entry-moved', { key: panel.key, state: before });
          if (this._panel?.key === panel.key) {
            this._panel = { ...this._panel, state: before };
            this._timeline = undefined;
            if (this._tab === 'timeline') void this.#readTimeline();
          }

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

  async #forward(panel: EntryPanelResponseModel) {
    try {
      const sent = await umbOpenModal(this, ENTRY_FORWARD_MODAL, {
        data: {
          form: this.form,
          entry: panel.key,
          number: panel.number,
          maySendSensitive: this.#permissions.includes('viewSensitive'),
        },
      });
      await this.#notify('positive', this.#term('forward_sent', sent.to));
      this._timeline = undefined;
      if (this._tab === 'timeline') void this.#readTimeline();
    } catch {
      // Closed without forwarding.
    }
  }

  async #delete(panel: EntryPanelResponseModel) {
    const plan = await deleteEntries(this, this.form, [panel.key], false);
    if (!plan) {
      await this.#notify('danger', this.#term('deleteFailed'));
      return;
    }

    // A hold set since the panel was read keeps the entry.
    if (plan.entries === 0) {
      await this.#notify('warning', this.#term(plan.held > 0 ? 'deleteOne_held' : 'deleteFailed'));
      return;
    }

    try {
      await confirmText(this, {
        headline: this.#term('deleteOne_headline', panel.number),
        content: this.#term('delete_body', 1, plan.files),
        confirmLabel: this.#term('deleteOne_confirm'),
        color: 'danger',
      });
    } catch {
      return;
    }

    const deleted = await deleteEntries(this, this.form, [panel.key], true);
    if (!deleted?.deleted) {
      await this.#notify('danger', this.#term('deleteFailed'));
      return;
    }

    await this.#notify('positive', this.#term('deletedOne', panel.number));
    this.#tell('jfb-entry-gone', { key: panel.key, state: panel.state });
  }

  async #rerun(panel: EntryPanelResponseModel) {
    const plan = await rerunActions(this, this.form, [panel.key], false);
    if (!plan) {
      await this.#notify('danger', this.#term('rerunFailed'));
      return;
    }

    if (plan.entries === 0) {
      await this.#notify('warning', this.#term('rerun_none'));
      return;
    }

    try {
      await confirmList(this, {
        headline: this.#term('rerun_headline', 1),
        content: this.#term('rerun_body'),
        items: plan.actions.map((action) => ({
          name: action.name || action.type,
          detail: this.#term(`rerun_trigger_${action.trigger}`),
        })),
        confirmLabel: this.#term('rerun_confirm', 1),
      });
    } catch {
      return;
    }

    const queued = await rerunActions(this, this.form, [panel.key], true);
    await this.#notify(
      queued?.queued ? 'positive' : 'danger',
      this.#term(queued?.queued ? 'rerun_done' : 'rerunFailed', 1),
    );
    this._timeline = undefined;
    if (this._tab === 'timeline') void this.#readTimeline();
  }

  // ---- Answers ------------------------------------------------------------------------------------------------------

  async #reveal(answer: EntryPanelAnswerResponseModel) {
    this._revealFailed = undefined;
    const revealed = await revealAnswer(this, this.form, this.entry, answer.field);
    if (!revealed) {
      this._revealFailed = answer.field;
      return;
    }

    this._revealed = new Map([...this._revealed, [answer.field, revealed]]);
  }

  #hide(answer: EntryPanelAnswerResponseModel) {
    const revealed = new Map(this._revealed);
    revealed.delete(answer.field);
    this._revealed = revealed;
  }

  async #openFile(answer: EntryPanelAnswerResponseModel, file: EntryPanelFileResponseModel) {
    if (file.image) {
      await umbOpenModal(this, ENTRY_IMAGE_MODAL, {
        data: {
          form: this.form,
          file: file.key,
          name: file.name,
          meta: this.#term('image_meta', answer.label, file.contentType, sizeText(file.size, this.localize.lang())),
        },
      }).catch(() => undefined);
      return;
    }

    const blob = await fileBlob(this, this.form, file.key);
    if (blob) saveBlob(file.name, blob);
    else await this.#notify('danger', this.#term('file_failed'));
  }

  /** The answers as they show now: each revealed one in place of its masked one. */
  #answers(): EntryPanelAnswerResponseModel[] {
    return (this._panel?.pages ?? [])
      .flatMap((page) => page.groups)
      .flatMap((group) => group.answers)
      .map((answer) => this._revealed.get(answer.field) ?? answer);
  }

  #startEditing() {
    const draft: Record<string, readonly string[]> = {};
    for (const answer of this.#answers()) if (answer.edit) draft[answer.field] = [...answer.edit.values];
    this._draft = draft;
    this._problems = {};
    this._editing = true;
  }

  #setDraft(field: string, values: readonly string[]) {
    this._draft = { ...this._draft, [field]: values };
  }

  async #save(overbook = false) {
    if (this._saving) return;
    const answers = this.#answers().filter(
      (answer) =>
        answer.edit &&
        this._draft[answer.field] &&
        (this._draft[answer.field] ?? []).join('\n') !== answer.edit.values.join('\n'),
    );
    if (answers.length === 0) {
      this._editing = false;
      return;
    }

    this._saving = true;
    const request = answers.map((answer) => ({
      field: answer.field,
      values: (this._draft[answer.field] ?? []).filter((value) => value !== ''),
    }));
    const { edited, problems } = await editAnswers(this, this.form, this.entry, request, overbook);
    this._saving = false;
    if (edited?.asks) {
      if (await this.#agrees(edited.asks, true)) await this.#save(true);
      return;
    }

    if (!edited) {
      const found: Record<string, string> = {};
      for (const problem of problems) {
        const at = problemAnswer(problem.path);
        const field = at === undefined ? undefined : request[at]?.field;
        if (field) found[field] = problemText(problem.code, this.#full);
      }

      this._problems = found;
      if (Object.keys(found).length === 0) await this.#notify('danger', this.#term('edit_failed'));
      return;
    }

    this._editing = false;
    this._revealed = new Map();
    await this.reload();
    await this.#notify('positive', this.#term('edit_saved'));
  }

  // ---- Notes --------------------------------------------------------------------------------------------------------

  async #addNote() {
    const body = this._note.trim();
    if (!body) return;
    this._noteFailed = false;
    const note = await addNote(this, this.form, this.entry, body);
    if (!note) {
      this._noteFailed = true;
      return;
    }

    this._note = '';
    this._notes = [note, ...(this._notes ?? [])];
    if (this._panel) this._panel = { ...this._panel, notes: this._panel.notes + 1 };
  }

  async #saveNote(note: EntryNoteResponseModel) {
    const body = this._noteDraft.trim();
    if (!body) return;
    this._noteFailed = false;
    const changed = await changeNote(this, this.form, this.entry, note.id, body);
    if (!changed) {
      this._noteFailed = true;
      return;
    }

    this._notes = (this._notes ?? []).map((one) => (one.id === changed.id ? changed : one));
    this._noteEditing = undefined;
  }

  // ---- Rendering ----------------------------------------------------------------------------------------------------

  #header(panel: EntryPanelResponseModel): TemplateResult {
    const alone = this.place === 'alone';
    const number = this.#term('panel_title', panel.number);
    // Covering the list, the panel names what the row would: the title, or the number where there is none.
    const covering = this.place === 'covering';
    const parts = [
      ...((alone || covering) && panel.title ? [panel.title] : []),
      ...(this.showsForm && !alone ? [panel.formName] : []),
      ...(alone || covering || this.rowShowsTitle ? [number] : []),
    ];
    const shown = parts.length > 0;
    const name = shown ? parts.join(JOIN) : number;
    const chip = (alone || covering) && !this.hidesState;
    return html`<header class="head">
      <div class="names">
        <h2 tabindex="-1" class=${shown ? '' : 'visually-hidden'}>${name}</h2>
        ${
          alone
            ? html`<a class="form-link" href=${`${formPath(this.form)}/view/entries`}>${panel.formName}</a>`
            : nothing
        }
      </div>
      ${
        chip
          ? html`<uui-tag look="secondary" color=${STATE_COLOR[panel.state] ?? 'default'}
              >${stateText(panel.state, this.#full)}</uui-tag
            >`
          : nothing
      }
      ${
        alone
          ? nothing
          : html`<uui-button
              compact
              label=${this.#term('panel_close')}
              title=${this.#term('panel_close')}
              @click=${() => this.#tell('jfb-entry-close', { key: panel.key, state: panel.state })}
              ><uui-icon name="icon-wrong" aria-hidden="true"></uui-icon
            ></uui-button>`
      }
    </header>`;
  }

  #tabs(panel: EntryPanelResponseModel): TemplateResult {
    const tab = (id: Tab, label: string) =>
      html`<uui-tab label=${label} .active=${this._tab === id} @click=${() => void this.#showTab(id)}
        >${label}</uui-tab
      >`;
    return html`<uui-tab-group class="tabs">
      ${tab('answers', this.#term('tab_answers'))} ${tab('timeline', this.#term('tab_timeline'))}
      ${tab('notes', panel.notes > 0 ? this.#term('tab_notesCount', panel.notes) : this.#term('tab_notes'))}
    </uui-tab-group>`;
  }

  #details(panel: EntryPanelResponseModel): TemplateResult {
    const spam = panel.state === 'spam' && panel.details.spamReasons.length > 0;
    const reasons = (codes: readonly string[]) =>
      codes.map((code) => (/^[a-z_]+$/.test(code) ? this.#term(`spam_${code}`) : code));
    const extra = Boolean(
      panel.details.member || panel.details.ipAddress || (!spam && panel.details.spamReasons.length),
    );
    return html`${
        panel.heldUntil
          ? html`<p class="held">
              <uui-icon name="icon-lock" aria-hidden="true"></uui-icon
              ><span>${this.#term('panel_held', holdEnd(panel.heldUntil, this.localize.lang()))}</span>
            </p>`
          : nothing
      }${
        spam
          ? html`<div class="spam">
              <strong id="why">${this.#term('spam_heading')}</strong>
              <ul aria-labelledby="why">
                ${reasons(panel.details.spamReasons).map((reason) => html`<li>${reason}</li>`)}
              </ul>
            </div>`
          : nothing
      }
      <p class="details">
        <span>${detailsLine(panel.details, this.localize.lang(), this.#full, panel.isTest).join(JOIN)}</span>
        ${
          extra
            ? html`<button
                type="button"
                class="link"
                aria-expanded=${this._more ? 'true' : 'false'}
                @click=${() => (this._more = !this._more)}
              >
                ${this.#term(this._more ? 'details_fewer' : 'details_more')}
              </button>`
            : nothing
        }
      </p>
      ${
        this._more
          ? html`<dl class="more">
              ${
                panel.details.member
                  ? html`<dt>${this.#term('details_member')}</dt>
                      <dd>${this.#member(panel.details.member, panel.details.memberName)}</dd>`
                  : nothing
              }
              ${
                panel.details.ipAddress
                  ? html`<dt>${this.#term('details_ip')}</dt>
                      <dd>${panel.details.ipAddress}</dd>`
                  : nothing
              }
              ${
                !spam && panel.details.spamReasons.length > 0
                  ? html`<dt>${this.#term('details_spam')}</dt>
                      <dd>
                        ${reasons(panel.details.spamReasons).map((reason) => html`<span class="line">${reason}</span>`)}
                      </dd>`
                  : nothing
              }
            </dl>`
          : nothing
      }`;
  }

  #value(answer: EntryPanelAnswerResponseModel): TemplateResult {
    if (this._editing && answer.edit) return this.#control(answer, answer.edit);
    const revealed = this._revealed.has(answer.field);
    if (answer.masked) {
      const mayReveal = this.#permissions.includes('viewSensitive');
      return mayReveal
        ? html`<span aria-hidden="true">${MASKED}</span>
            <button
              type="button"
              class="reveal"
              aria-label=${this.#term('answer_revealFor', answer.label)}
              @click=${() => void this.#reveal(answer)}
            >
              ${this.#term('answer_reveal')}
            </button>
            ${
              this._revealFailed === answer.field
                ? html`<span class="problem" role="alert">${this.#term('answer_revealFailed')}</span>`
                : nothing
            }`
        : html`<span class="muted"
            ><uui-icon name="icon-lock" aria-hidden="true"></uui-icon> ${this.#term('answer_hidden')}</span
          >`;
    }

    const parts: TemplateResult[] = [];
    if (answer.notKept) parts.push(html`<span class="muted">${this.#term('answer_notKept')}</span>`);
    if (answer.files.length > 0) {
      parts.push(
        html`<span class="files">
          ${answer.files.map((file) =>
            file.held
              ? html`<span class="muted">${file.name}${JOIN}${this.#term('file_held')}</span>`
              : html`<span>
                  <button
                    type="button"
                    class="file"
                    aria-label=${this.#term('file_open', file.name)}
                    @click=${() => void this.#openFile(answer, file)}
                  >
                    <uui-icon name=${file.image ? 'icon-picture' : 'icon-attachment'} aria-hidden="true"></uui-icon
                    >${file.name}
                  </button>
                  <span class="muted"> ${sizeText(file.size, this.localize.lang())}</span>
                </span>`,
          )}
        </span>`,
      );
    }

    if (answer.consent) parts.push(html`<span>${this.#term('answer_consent', answer.consent)}</span>`);
    else if (answer.files.length === 0 && !answer.notKept) {
      parts.push(
        answer.texts.length > 0
          ? html`${answer.texts.map((text) => html`<span class="line">${text}</span>`)}`
          : html`<span class="muted">${this.#term('none')}</span>`,
      );
    }

    if (answer.signedName)
      parts.push(html`<span class="small">${this.#term('answer_signedAs', answer.signedName)}</span>`);
    if (revealed) {
      parts.push(
        html`<button type="button" class="reveal" @click=${() => this.#hide(answer)}>
            ${this.#term('answer_hide')}
          </button>
          <span class="small">${this.#term('answer_revealed')}</span>`,
      );
    }

    return html`${parts}`;
  }

  #control(answer: EntryPanelAnswerResponseModel, edit: EntryPanelEditResponseModel): TemplateResult {
    const values = this._draft[answer.field] ?? [];
    const id = `edit-${answer.field}`;
    const problem = this._problems[answer.field];
    const described = problem ? `${id}-problem` : nothing;
    let control: TemplateResult;
    switch (edit.kind) {
      case 'longText':
        control = html`<uui-textarea
          id=${id}
          label=${answer.label}
          aria-describedby=${described}
          .value=${values[0] ?? ''}
          @input=${(event: Event) => this.#setDraft(answer.field, [(event.target as HTMLTextAreaElement).value])}
        ></uui-textarea>`;
        break;
      case 'choice':
        control = html`<select
          id=${id}
          aria-label=${answer.label}
          aria-describedby=${described}
          @change=${(event: Event) => this.#setDraft(answer.field, [(event.target as HTMLSelectElement).value])}
        >
          <option value="" ?selected=${!values[0]}>${this.#term('edit_choose')}</option>
          ${edit.options.map((option) => html`<option value=${option.value} ?selected=${values[0] === option.value}>${option.label}</option>`)}
        </select>`;
        break;
      case 'choices':
        control = html`<div class="choices" role="group" aria-label=${answer.label} aria-describedby=${described}>
          ${edit.options.map(
            (option) =>
              html`<uui-checkbox
                label=${option.label}
                .checked=${values.includes(option.value)}
                @change=${(event: Event) =>
                  this.#setDraft(
                    answer.field,
                    (event.target as HTMLInputElement).checked
                      ? edit.options
                          .map((one) => one.value)
                          .filter((one) => one === option.value || values.includes(one))
                      : values.filter((one) => one !== option.value),
                  )}
              ></uui-checkbox>`,
          )}
        </div>`;
        break;
      case 'checkbox':
        control = html`<uui-checkbox
          label=${answer.label}
          .checked=${values[0] === 'true'}
          @change=${(event: Event) => this.#setDraft(answer.field, (event.target as HTMLInputElement).checked ? ['true'] : [])}
        ></uui-checkbox>`;
        break;
      default:
        control = html`<uui-input
          id=${id}
          label=${answer.label}
          type=${edit.kind === 'number' ? 'number' : edit.kind === 'date' ? 'date' : 'text'}
          aria-describedby=${described}
          .value=${values[0] ?? ''}
          @input=${(event: Event) => this.#setDraft(answer.field, [(event.target as HTMLInputElement).value])}
        ></uui-input>`;
    }

    return html`${control}${problem ? html`<span class="problem" id=${`${id}-problem`}>${problem}</span>` : nothing}`;
  }

  #answersTab(panel: EntryPanelResponseModel): TemplateResult {
    const mayEdit = this.#permissions.includes('editEntries') && this.#answers().some((answer) => answer.edit);
    const titled = panel.pages.length > 1 || panel.pages.some((page) => page.title);
    return html`<div class="details-top">
        <div>${this.#details(panel)}</div>
        ${
          mayEdit && !this._editing
            ? html`<uui-button
                look="outline"
                compact
                label=${this.#term('edit_answers')}
                @click=${() => this.#startEditing()}
              >
                <uui-icon name="icon-edit" aria-hidden="true"></uui-icon> ${this.#term('edit_answers')}
              </uui-button>`
            : nothing
        }
      </div>
      ${this._editing ? html`<p class="edit-note">${this.#term('edit_note')}</p>` : nothing}
      ${panel.pages.map(
        (page) =>
          html`${titled && page.title ? html`<h3>${page.title}</h3>` : nothing}
          ${page.groups.map(
            (group) =>
              html`${group.title ? html`<h4>${group.title}</h4>` : nothing}
                <dl class=${this._editing ? 'editing' : ''}>
                  ${group.answers.map((shown) => {
                    const answer = this._revealed.get(shown.field) ?? shown;
                    return html`<dt>
                        ${answer.label}
                        ${answer.removed ? html`<span class="small">${this.#term('answer_removed')}</span>` : nothing}
                      </dt>
                      <dd>${this.#value(answer)}</dd>`;
                  })}
                </dl>`,
          )}`,
      )}`;
  }

  #timelineTab(): TemplateResult {
    if (!this._timeline) {
      return this._timelineFailed
        ? html`<p class="failed" role="alert">${this.#term('timeline_failed')}</p>`
        : skeletonLines(this.#term('panel_loading'), [70, 60, 50]);
    }

    const mayRetry = this.#permissions.includes('manageActions');
    const lines = timelineLines(this._timeline, this.localize.lang(), new Date(), this.#full);
    return html`<ol class="events">
      ${lines.map(
        (line) =>
          html`<li class="event tone-${line.tone}">
            <uui-icon name=${line.icon} aria-hidden="true"></uui-icon>
            <div>
              <span class="text">${line.text}</span>
              ${line.changes.map((change) => html`<span class="small">${change}</span>`)}
              ${line.retry && mayRetry ? this.#retryButton(line.retry) : nothing}
            </div>
            <span class="when">${line.when}</span>
          </li>`,
      )}
    </ol>`;
  }

  #retryButton(retry: { run: number; uncertain: boolean; name: string }): TemplateResult {
    return html`<uui-button
      look="outline"
      compact
      label=${this.#term('run_retryFor', retry.name)}
      @click=${() => void this.#retry(retry)}
      >${this.#term('run_retry')}</uui-button
    >`;
  }

  /** Retries a run; one that may have arrived asks first, as does one the server finds uncertain by now. */
  async #retry(retry: { run: number; uncertain: boolean }) {
    let answer = retry.uncertain ? 'confirm' : await retryRun(this, this.form, this.entry, retry.run, false);
    if (answer === 'confirm') {
      try {
        await confirmText(this, {
          headline: this.#term('run_retryHeadline'),
          content: this.#term('run_retryBody'),
          confirmLabel: this.#term('run_retryConfirm'),
          color: 'warning',
        });
      } catch {
        return;
      }

      answer = await retryRun(this, this.form, this.entry, retry.run, true);
    }

    await this.#notify(
      answer === 'queued' ? 'positive' : 'danger',
      this.#term(answer === 'queued' ? 'run_retried' : 'run_retryFailed'),
    );
    await this.#readTimeline();
  }

  #notesTab(): TemplateResult {
    return html`<div class="new-note">
        <label for="note">${this.#term('notes_new')}</label>
        <uui-textarea
          id="note"
          label=${this.#term('notes_new')}
          aria-describedby="note-help"
          maxlength=${MOST_NOTE}
          .value=${this._note}
          @input=${(event: Event) => (this._note = (event.target as HTMLTextAreaElement).value)}
        ></uui-textarea>
        <span class="small" id="note-help">${this.#term('notes_help')}</span>
        ${this._noteFailed ? html`<span class="problem" role="alert">${this.#term('notes_failed')}</span>` : nothing}
        <uui-button
          look="primary"
          label=${this.#term('notes_add')}
          ?disabled=${!this._note.trim()}
          @click=${() => void this.#addNote()}
        ></uui-button>
      </div>
      ${
        !this._notes
          ? this._notesFailed
            ? html`<p class="failed" role="alert">${this.#term('notes_readFailed')}</p>`
            : skeletonLines(this.#term('panel_loading'), [60, 40])
          : this._notes.length === 0
            ? html`<p class="muted">${this.#term('notes_none')}</p>`
            : this._notes.map((note) => this.#note(note))
      }`;
  }

  #note(note: EntryNoteResponseModel): TemplateResult {
    const editing = this._noteEditing === note.id;
    return html`<div class="note">
      <div class="note-head">
        <strong>${note.author}</strong>
        <span class="when">
          ${receivedText(note.created, new Date(), this.localize.lang(), this.#full)}${note.edited ? `${JOIN}${this.#term('notes_edited')}` : ''}
        </span>
        ${
          note.mine && !editing
            ? html`<button
                type="button"
                class="link"
                aria-label=${this.#term('notes_editFor', note.author)}
                @click=${() => {
                  this._noteEditing = note.id;
                  this._noteDraft = note.body;
                }}
              >
                ${this.#term('notes_edit')}
              </button>`
            : nothing
        }
      </div>
      ${
        editing
          ? html`<uui-textarea
                label=${this.#term('notes_edit')}
                maxlength=${MOST_NOTE}
                .value=${this._noteDraft}
                @input=${(event: Event) => (this._noteDraft = (event.target as HTMLTextAreaElement).value)}
              ></uui-textarea>
              <div class="note-actions">
                <uui-button
                  label=${this.#term('notes_cancel')}
                  @click=${() => (this._noteEditing = undefined)}
                ></uui-button>
                <uui-button
                  look="primary"
                  label=${this.#term('notes_save')}
                  ?disabled=${!this._noteDraft.trim()}
                  @click=${() => void this.#saveNote(note)}
                ></uui-button>
              </div>`
          : html`<p class="note-body">${note.body}</p>`
      }
    </div>`;
  }

  #actionLabel(action: PanelAction): string {
    switch (action) {
      case 'forward':
        return this.#term('action_forward');
      case 'backToWaiting':
        return this.#term('action_backToWaiting');
      case 'print':
        return this.#term('action_print');
      default:
        return this.#term(action);
    }
  }

  #bar(panel: EntryPanelResponseModel): TemplateResult {
    if (this._editing) {
      return html`<footer class="bar">
        <uui-button
          label=${this.#term('edit_cancel')}
          @click=${() => {
            this._editing = false;
            this._problems = {};
          }}
        ></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.#term('edit_save')}
          .state=${this._saving ? 'waiting' : undefined}
          @click=${() => void this.#save()}
        ></uui-button>
      </footer>`;
    }

    const bar = panelBar(panel.state, panel.manualApproval, this.#permissions, !!panel.heldUntil);
    const primary = bar.primary;
    if (!primary && bar.visible.length === 0) return html``;
    return html`<footer class="bar">
      ${
        bar.more.length > 0
          ? html`<uui-button look="secondary" compact label=${this.#term('action_more')} popovertarget="panel-more">
                <uui-symbol-more></uui-symbol-more>
              </uui-button>
              <uui-popover-container id="panel-more" placement="top-start">
                <div class="menu">
                  ${bar.more.map(
                    (action) =>
                      html`<button
                        type="button"
                        @click=${() => {
                          this.shadowRoot
                            ?.querySelector<HTMLElement & { hidePopover(): void }>('#panel-more')
                            ?.hidePopover();
                          void this.#run(action);
                        }}
                      >
                        ${this.#actionLabel(action)}
                      </button>`,
                  )}
                </div>
              </uui-popover-container>`
          : nothing
      }
      ${[...bar.visible]
        .reverse()
        .map(
          (action) =>
            html`<uui-button
              look="secondary"
              label=${this.#actionLabel(action)}
              ?disabled=${this._busy}
              @click=${() => void this.#run(action)}
            ></uui-button>`,
        )}
      ${
        primary
          ? html`<uui-button
              look="primary"
              color="positive"
              label=${this.#actionLabel(primary)}
              ?disabled=${this._busy}
              @click=${() => void this.#run(primary)}
            ></uui-button>`
          : nothing
      }
    </footer>`;
  }

  override render() {
    if (!this._words || this._state === 'loading' || !this._panel) {
      if (this._words && this._state === 'failed') {
        // With a way on: the read again, and back to the list the panel stands beside or over.
        return html`<div class="body">
          <div class="failed" role="alert">
            <span>${this.#term('panel_failed')}</span>
            <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#open()}></uui-button>
            ${
              this.place === 'alone'
                ? nothing
                : html`<uui-button
                    look="outline"
                    label=${this.#term('panel_close')}
                    @click=${() => this.#tell('jfb-entry-close', { key: this.entry, state: '' })}
                  ></uui-button>`
            }
          </div>
        </div>`;
      }

      return html`<div class="body">
        ${skeletonLines(this._words ? this.#term('panel_loading') : '', [60, 80, 70, 50])}
      </div>`;
    }

    const panel = this._panel;
    return html`${this.#header(panel)} ${this.#tabs(panel)}
      <div class="body" role="tabpanel">
        ${this._tab === 'answers' ? this.#answersTab(panel) : this._tab === 'timeline' ? this.#timelineTab() : this.#notesTab()}
      </div>
      ${this.#bar(panel)}`;
  }
}

if (!customElements.get('jfb-entry-panel')) customElements.define('jfb-entry-panel', EntryPanelElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-panel': EntryPanelElement;
  }
}
