// The form's History view (ROADMAP 10.3a, IA 5.6, ADR-0196): its published versions newest first, the draft saves too
// with "Show draft saves", each row when and by whom, what changed against the one before, its comment and pin, and a
// "..." with Pin and Add comment.... Ticking two versions shows Compare; a version's name opens it compared with the current
// draft, which can restore it as a new draft; a row's "..." restores it at once, with Undo (ROADMAP 10.3b, ADR-0198).
// Both comparisons open Umbraco's medium sidebar, in words (version-actions.ts). It reads again when a save moves the
// draft or a publish moves the live version.
import { css, html, nothing, repeat, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { FormVersionEntryResponseModel } from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { pressOf } from '../../styles/press';
import { jfbSkeleton, skeletonLines } from '../../ui/skeleton';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { FormWorkspaceContext } from '../form-workspace.context';
import { COMMENT_MODAL } from './comment.modal.token';
import { madeAt, momentText, rowName, summaryWords } from './history-model';
import { loadHistoryWords } from './history-words';
import { VersionActions } from './version-actions';

type Version = FormVersionEntryResponseModel['version'];

/** The versions newest first as the server lists them, with the current draft first, what the others compare with. */
const currentFirst = (rows: readonly FormVersionEntryResponseModel[]): readonly FormVersionEntryResponseModel[] => [
  ...rows.filter((row) => row.version.isDraft),
  ...rows.filter((row) => !row.version.isDraft),
];

export class FormHistoryViewElement extends UmbLitElement {
  static override properties = {
    _state: { state: true },
    _rows: { state: true },
    _total: { state: true },
    _drafts: { state: true },
    _ticked: { state: true },
    _more: { state: true },
    _moreFailed: { state: true },
    _said: { state: true },
    _words: { state: true },
    _draft: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    pressOf('.more'),
    jfbVisuallyHidden,
    jfbSkeleton,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
        container: history / inline-size;
      }

      .toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
        min-block-size: 40px;
        margin-block-end: var(--uui-size-space-4);
      }

      .toolbar uui-toggle {
        margin-inline-start: auto;
      }

      .box--flush {
        --uui-box-default-padding: 0;
      }

      table {
        width: 100%;
        border-collapse: collapse;
      }

      th {
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        font-weight: 700;
        text-align: start;
      }

      td,
      tbody th {
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border-top: 1px solid var(--uui-color-divider);
        font-weight: 400;
        vertical-align: top;
      }

      .tick {
        inline-size: 1px;
        padding-inline-end: 0;
      }

      .end {
        inline-size: 1px;
        text-align: end;
        white-space: nowrap;
      }

      .version {
        display: grid;
        gap: 2px;
      }

      .title {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .name {
        padding: 0;
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        font-weight: 700;
        text-align: start;
        cursor: pointer;
      }

      .name:hover,
      .name:focus-visible {
        color: var(--uui-color-interactive-emphasis);
        text-decoration: underline;
      }

      .name:focus-visible,
      .more:focus-visible,
      .menu button:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .plain {
        font-weight: 700;
      }

      tr.draft .plain,
      tr.draft .name {
        font-weight: 400;
      }

      .made,
      .muted,
      tr.draft .changed {
        color: var(--jfb-bo-color-text-muted);
      }

      .made {
        font-size: var(--uui-type-small-size);
      }

      .changed,
      .comment {
        overflow-wrap: anywhere;
      }

      /* The comment in its own column where the view has room; narrower, under what changed, so neither text is
         squeezed into a narrow column (the wireframe's 620 px). */
      .below {
        display: none;
      }

      @container history (width < 56rem) {
        .comment-column {
          display: none;
        }

        .below {
          display: flex;
          gap: var(--uui-size-space-2);
          margin-block-start: var(--uui-size-space-1);
          color: var(--jfb-bo-color-text-muted);
          font-size: var(--uui-type-small-size);
        }
      }

      .below uui-icon {
        flex: none;
        margin-block-start: 2px;
      }

      uui-icon.pin {
        color: var(--jfb-bo-color-text-muted);
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
        min-inline-size: 200px;
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
        .more:hover,
        .menu button:hover {
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

      .note {
        margin: var(--uui-size-space-4) 0 0;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .empty {
        max-width: 36rem;
        margin: var(--uui-size-layout-2) auto;
        text-align: center;
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }
    `,
  ];

  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _rows: readonly FormVersionEntryResponseModel[];
  declare private _total: number;
  declare private _drafts: boolean;
  /** The ticked versions' numbers, at most two. */
  declare private _ticked: readonly number[];
  declare private _more: boolean;
  /** Whether the last Show more failed, which its footer says. */
  declare private _moreFailed: boolean;
  declare private _said: string;
  /** Whether the view's words are loaded: nothing is drawn before them, so no key ever shows. */
  declare private _words: boolean;
  /** The current draft's number, which a version's name compares it with; null before the form is read. */
  declare private _draft: number | null;

  #context: FormWorkspaceContext | undefined;
  #form: string | undefined;
  /** The live version's number as last seen; undefined before the form is read. */
  #published: number | null | undefined;
  #actions: VersionActions | undefined;
  /** Counts the reads of the list, so only the latest one's answer is said. */
  #reads = 0;

  constructor() {
    super();
    this._state = 'loading';
    this._rows = [];
    this._total = 0;
    this._drafts = false;
    this._ticked = [];
    this._more = false;
    this._moreFailed = false;
    this._said = '';
    this._words = false;
    this._draft = null;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.#actions = context ? new VersionActions(this, context) : undefined;
      if (!context) return;
      this.observe(
        context.unique,
        (unique) => {
          if (!unique || unique === this.#form) return;
          // Another form: nothing of the one before stays. Its draft and live version come by their own observers,
          // which only call when they differ, so the numbers last seen stay; a read too many is dropped by #reads.
          this.#form = unique;
          this._rows = [];
          this._total = 0;
          this._ticked = [];
          this._moreFailed = false;
          void this.#read();
        },
        'unique',
      );
      // A save adds a version and moves the draft to it: the list reads again.
      this.observe(
        context.draftVersion,
        (draft) => {
          const changed = this._draft !== null && draft !== this._draft;
          this._draft = draft ?? null;
          if (changed) void this.#read();
        },
        'draft',
      );
      // A publish adds the live version and leaves the draft where it was: the list reads again, its chip moved.
      this.observe(
        context.publishedVersion,
        (published) => {
          const changed = this.#published !== undefined && published !== undefined && published !== this.#published;
          if (published !== undefined) this.#published = published;
          if (changed) void this.#read();
        },
        'published',
      );
    });
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbHistory_${key}`, ...args);
  #full = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  // ---- Reading ------------------------------------------------------------------------------------------------------

  async #read() {
    const read = ++this.#reads;
    if (this._rows.length === 0) this._state = 'loading';
    const [words, page] = await Promise.all([
      loadHistoryWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      this.#context?.versionsOf(this._drafts, 0),
    ]);
    if (read !== this.#reads) return;
    this._words ||= words;
    if (!words || !page) {
      this._state = 'failed';
      return;
    }
    this._rows = currentFirst(page.items);
    this._total = page.total;
    this._moreFailed = false;
    // A ticked version the list no longer shows is unticked.
    this._ticked = this._ticked.filter((number) => page.items.some((item) => item.version.number === number));
    this._state = 'ready';
  }

  async #showMore() {
    const read = this.#reads;
    this._more = true;
    const before = this._rows.length;
    const page = await this.#context?.versionsOf(this._drafts, before);
    this._more = false;
    if (read !== this.#reads) return;
    // A page that can't be read is said beside Show more, which stays to try again.
    this._moreFailed = !page;
    if (!page) return;
    const known = new Set(this._rows.map((row) => row.version.number));
    this._rows = currentFirst([...this._rows, ...page.items.filter((item) => !known.has(item.version.number))]);
    this._total = page.total;
    await this.updateComplete;
    // The focus goes to the first new row, where the keyboard reads on, as the button may be gone.
    this.shadowRoot
      ?.querySelectorAll<HTMLElement>('tbody tr')
      [before]?.querySelector<HTMLElement>('.name, .more')
      ?.focus();
  }

  #showDrafts(event: Event) {
    this._drafts = (event.target as HTMLInputElement).checked;
    void this.#read();
  }

  // ---- Names ---------------------------------------------------------------------------------------------------------

  #now = () => new Date();

  /** A row's name as its controls and what is said of it name it, a draft save with its moment: there may be many. */
  #label(version: Version): string {
    const name = rowName(version, this.#full);
    if (version.kind !== 'draft' || version.isDraft) return name;
    return this.#term('saveAt', name, momentText(madeAt(version), this.#now(), this.localize.lang(), this.#full));
  }

  #made(entry: FormVersionEntryResponseModel): string {
    const when = momentText(madeAt(entry.version), this.#now(), this.localize.lang(), this.#full);
    return entry.by ? this.#term('whenBy', when, entry.by) : when;
  }

  #changes(entry: FormVersionEntryResponseModel): string {
    if (entry.changes) return summaryWords(entry.changes, this.#full);
    // A publish is compared with the publish before it, a draft save with the version before it: one without changes is
    // the first of its kind, unless an older one of its kind is listed, when one of the two can't be read.
    const version = entry.version;
    const published = version.kind === 'published';
    const older = this._rows.some(
      (row) => row.version.number < version.number && (!published || row.version.kind === 'published'),
    );
    return this.#term(older ? 'changes_unknown' : published ? 'changes_firstPublished' : 'changes_first');
  }

  // ---- Ticking and comparing -------------------------------------------------------------------------------------------

  #tick(number: number, ticked: boolean) {
    this._ticked = ticked
      ? [...this._ticked.filter((one) => one !== number), number].slice(-2)
      : this._ticked.filter((one) => one !== number);
    this._said = this.#term('ticked', this._ticked.length);
  }

  #compareTicked() {
    const ticked = this._rows
      .map((row) => row.version)
      .filter((version) => this._ticked.includes(version.number))
      .sort((a, b) => a.number - b.number);
    const [older, newer] = ticked;
    if (older && newer) void this.#actions?.compare(older, newer);
  }

  /** Opens a version compared with the current draft, the version first, which can restore it there, and publish it. */
  async #compareWithDraft(entry: FormVersionEntryResponseModel) {
    if ((await this.#actions?.offer(entry)) === 'publish') await this.#actions?.publish();
  }

  /** Restores a version as a new draft at once; the toast names what changed and offers Undo. */
  #restore(version: Version) {
    this.#closeMenu(version.number);
    void this.#actions?.restore(version);
  }

  // ---- Pins and comments ---------------------------------------------------------------------------------------------

  #closeMenu(number: number) {
    this.shadowRoot?.querySelector<HTMLElement & { hidePopover?: () => void }>(`#more-${number}`)?.hidePopover?.();
  }

  #replace(version: Version) {
    this._rows = this._rows.map((row) => (row.version.number === version.number ? { ...row, version } : row));
  }

  async #pin(version: Version) {
    this.#closeMenu(version.number);
    const pinned = !version.isPinned;
    // A refusal is said by the workspace's notification; the row stays as it was.
    const now = await this.#context?.pinVersion(version.number, pinned);
    if (!now) return;
    this.#replace(now);
    this._said = this.#term(pinned ? 'saidPinned' : 'saidUnpinned', this.#label(version));
  }

  async #comment(version: Version) {
    this.#closeMenu(version.number);
    const answer = await umbOpenModal(this, COMMENT_MODAL, {
      data: { name: this.#label(version), comment: version.comment ?? '' },
    }).catch(() => undefined);
    if (!answer || answer.comment === (version.comment ?? '')) return;
    const now = await this.#context?.commentVersion(version.number, answer.comment);
    if (!now) return;
    this.#replace(now);
    this._said = this.#term(now.comment ? 'saidCommented' : 'saidUncommented', this.#label(version));
  }

  // ---- Drawing -------------------------------------------------------------------------------------------------------

  #menu(version: Version, name: string, restorable: boolean): TemplateResult {
    const id = `more-${version.number}`;
    return html`<button
        type="button"
        class="more"
        popovertarget=${id}
        aria-label=${this.#term('moreFor', name)}
        title=${this.#term('moreFor', name)}
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
            restorable
              ? html`<button type="button" @click=${() => this.#restore(version)}>
                  <uui-icon name="icon-undo" aria-hidden="true"></uui-icon>${this.#term('restore')}
                </button>`
              : nothing
          }
          <button type="button" @click=${() => void this.#pin(version)}>
            <uui-icon name="icon-pushpin" aria-hidden="true"></uui-icon
            >${this.#term(version.isPinned ? 'unpin' : 'pin')}
          </button>
          <button type="button" @click=${() => void this.#comment(version)}>
            <uui-icon name="icon-notepad" aria-hidden="true"></uui-icon>${this.#term(
              version.comment ? 'changeComment' : 'addComment',
            )}
          </button>
        </div>
      </uui-popover-container>`;
  }

  #row(entry: FormVersionEntryResponseModel): TemplateResult {
    const version = entry.version;
    const name = rowName(version, this.#full);
    const label = this.#label(version);
    const ticked = this._ticked.includes(version.number);
    // The draft is what the others are compared with; a version that is the draft has nothing to compare it with.
    const comparable = this._draft !== null && version.number !== this._draft;
    const comment = version.comment ?? '';
    return html`<tr class=${version.kind === 'draft' ? 'draft' : ''} data-number=${version.number}>
      <td class="tick">
        <!-- Named by aria-label, which the box's input takes, so no label text shows beside it. -->
        <uui-checkbox
          aria-label=${this.#term('select', label)}
          .checked=${ticked}
          ?disabled=${!ticked && this._ticked.length >= 2}
          @change=${(event: Event) => this.#tick(version.number, (event.target as HTMLInputElement).checked)}
        ></uui-checkbox>
      </td>
      <th scope="row">
        <div class="version">
          <span class="title">
            ${
              comparable
                ? html`<button
                    type="button"
                    class="name"
                    aria-label=${this.#term('compareWithDraft', label)}
                    @click=${() => void this.#compareWithDraft(entry)}
                  >
                    ${name}
                  </button>`
                : html`<span class="plain">${name}</span>`
            }
            ${
              version.isLive
                ? html`<uui-tag color="positive" look="primary">${this.#term('published')}</uui-tag>`
                : nothing
            }
            ${
              version.isPinned
                ? html`<uui-icon
                      class="pin"
                      name="icon-pushpin"
                      title=${this.#term('pinned')}
                      aria-hidden="true"
                    ></uui-icon
                    ><span class="visually-hidden">${this.#term('pinned')}</span>`
                : nothing
            }
          </span>
          <span class="made">${this.#made(entry)}</span>
        </div>
      </th>
      <td class="changed">
        ${this.#changes(entry)}
        ${
          comment
            ? html`<span class="below"
                ><uui-icon name="icon-notepad" aria-hidden="true"></uui-icon
                ><span><span class="visually-hidden">${this.#term('commentLabel')} </span>${comment}</span></span
              >`
            : nothing
        }
      </td>
      <td class="comment comment-column ${comment ? '' : 'muted'}">${comment || this.#term('none')}</td>
      <td class="end">${this.#menu(version, label, comparable)}</td>
    </tr>`;
  }

  #list(): TemplateResult {
    if (this._state === 'loading')
      return html`<uui-box class="box--flush">${skeletonLines(this.#term('loading'), [70, 64, 58])}</uui-box>`;
    if (this._state === 'failed')
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#term('failed')}</span>
          <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    if (this._rows.length === 0) return html`<p class="empty">${this.#term('empty')}</p>`;
    return html`<uui-box class="box--flush">
      <table>
        <thead>
          <tr>
            <th scope="col" class="tick"><span class="visually-hidden">${this.#term('column_select')}</span></th>
            <th scope="col">${this.#term('column_version')}</th>
            <th scope="col">${this.#term('column_changed')}</th>
            <th scope="col" class="comment-column">${this.#term('column_comment')}</th>
            <th scope="col" class="end"><span class="visually-hidden">${this.#term('column_more')}</span></th>
          </tr>
        </thead>
        <tbody>
          ${
            // Keyed by version, so a focused name or "..." stays with its version when a read adds a row above it.
            repeat(
              this._rows,
              (row) => row.version.number,
              (row) => this.#row(row),
            )
          }
        </tbody>
      </table>
      ${
        this._rows.length < this._total
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

  override render() {
    // Until its words are loaded, the view's place holds a skeleton that names nothing; a failed load says so in the
    // backoffice's start words, which are always there.
    if (!this._words)
      return this._state === 'failed'
        ? html`<uui-box>
            <div class="failed" role="alert">
              <span>${this.#full('jfbDesigner_historyFailed')}</span>
              <uui-button
                look="outline"
                label=${this.#full('jfbSettings_retry')}
                @click=${() => void this.#read()}
              ></uui-button>
            </div>
          </uui-box>`
        : html`<uui-box class="box--flush">${skeletonLines('', [70, 64, 58])}</uui-box>`;
    return html`<div class="toolbar">
        ${
          this._ticked.length === 2
            ? html`<uui-button
                look="outline"
                label=${this.#term('compare')}
                @click=${() => this.#compareTicked()}
              ></uui-button>`
            : nothing
        }
        <uui-toggle
          label=${this.#term('showDrafts')}
          .checked=${this._drafts}
          @change=${(event: Event) => this.#showDrafts(event)}
        ></uui-toggle>
      </div>
      ${this.#list()}
      ${this._state === 'ready' && this._drafts && this._rows.length > 0 ? html`<p class="note">${this.#term('kept')}</p>` : nothing}
      <div class="visually-hidden" role="status">${this._said}</div>`;
  }
}

if (!customElements.get('jfb-form-history-view'))
  customElements.define('jfb-form-history-view', FormHistoryViewElement);

export { FormHistoryViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-history-view': FormHistoryViewElement;
  }
}
