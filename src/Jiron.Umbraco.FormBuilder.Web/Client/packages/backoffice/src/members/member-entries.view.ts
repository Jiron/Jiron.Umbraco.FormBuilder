// The member editor's Form entries view (ROADMAP 14.3, IA 10, ADR-0231): the entries the member sent while signed in, of
// the forms the user may view entries of, newest first, 50 at a time; forms the user may not view are left out without a
// trace. A row names the entry by its number, since its title would mostly repeat the member's name, and opens the entry
// on its own screen in the Form Builder section. The view adds nothing to the footer.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_MEMBER_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/member';
import { memberEntriesEntries, type EntryListPageResponseModel, type EntryListRowResponseModel } from '../api';
import { loadEntryWords } from '../entries/entry-words';
import { ENTRY_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';

/** A page of a member's entries, or why there is none: the server didn't answer. */
export type MemberEntriesPage = EntryListPageResponseModel | 'failed';

/** Reads a page of the member's entries; 'failed' when the server didn't answer as it should. */
export async function readMemberEntries(member: string, after?: string): Promise<MemberEntriesPage> {
  try {
    const { data } = await memberEntriesEntries({ path: { key: member }, query: after ? { after } : {} });
    return data ?? 'failed';
  } catch {
    return 'failed';
  }
}

export class MemberEntriesViewElement extends UmbLitElement {
  static override properties = {
    _words: { state: true },
    _rows: { state: true },
    _forms: { state: true },
    _total: { state: true },
    _next: { state: true },
    _state: { state: true },
    _more: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }

      p {
        margin: 0;
      }

      table {
        inline-size: 100%;
        border-collapse: collapse;
      }

      th {
        padding: 0 var(--uui-size-space-4) var(--uui-size-space-3);
        font-weight: 700;
        text-align: start;
      }

      td {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-block-start: 1px solid var(--uui-color-divider);
        vertical-align: top;
        overflow-wrap: anywhere;
      }

      th:first-child,
      td:first-child {
        padding-inline-start: 0;
      }

      th:last-child,
      td:last-child {
        padding-inline-end: 0;
      }

      .muted,
      .small {
        color: var(--jfb-bo-color-text-muted);
      }

      .name,
      .small {
        display: block;
      }

      .small {
        font-size: var(--uui-type-small-size);
      }

      a {
        color: var(--uui-color-interactive);
        text-decoration: none;
      }

      @media (hover: hover) {
        a:hover {
          color: var(--uui-color-interactive-emphasis);
          text-decoration: underline;
        }
      }

      .more {
        margin-block-start: var(--uui-size-space-5);
      }

      /* Narrower, the form and the time move under the entry, so nothing is squashed (D18). */
      @media (max-width: 760px) {
        .wide {
          display: none;
        }
      }

      @media (min-width: 761px) {
        .narrow {
          display: none;
        }
      }
    `,
  ];

  declare private _words: boolean;
  declare private _rows: readonly EntryListRowResponseModel[];
  declare private _forms: ReadonlyMap<string, string>;
  declare private _total: number | undefined;
  declare private _next: string | null | undefined;
  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _more: boolean;
  #member: string | undefined;

  constructor() {
    super();
    this._words = false;
    this._rows = [];
    this._forms = new Map();
    this._state = 'loading';
    this._more = false;
    void loadEntryWords(this.localize.lang()).then(() => (this._words = true));
    this.consumeContext(UMB_MEMBER_WORKSPACE_CONTEXT, (workspace) => {
      this.observe(workspace?.unique, (unique) => {
        if (!unique || unique === this.#member) return;
        this.#member = unique;
        void this.#load();
      });
    });
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  async #load() {
    const member = this.#member;
    if (!member) return;
    this._state = 'loading';
    const page = await readMemberEntries(member);
    if (member !== this.#member) return;
    if (page === 'failed') {
      this._state = 'failed';
      return;
    }
    this._rows = page.items;
    this._forms = new Map(page.forms.map((form) => [form.key, form.name]));
    this._total = page.total ?? page.items.length;
    this._next = page.next;
    this._state = 'ready';
  }

  async #loadMore() {
    const member = this.#member;
    if (!member || !this._next || this._more) return;
    this._more = true;
    const page = await readMemberEntries(member, this._next);
    this._more = false;
    if (member !== this.#member || page === 'failed') return;
    const forms = new Map(this._forms);
    for (const form of page.forms) forms.set(form.key, form.name);
    this._forms = forms;
    this._rows = [...this._rows, ...page.items];
    this._next = page.next;
  }

  #date(iso: string): string {
    return this.localize.date(iso, { dateStyle: 'medium', timeStyle: 'short' });
  }

  override render() {
    if (!this._words || this._state === 'loading')
      return html`<uui-box
        >${skeletonLines(this._words ? this.#term('memberEntries_loading') : '', [40, 70, 55])}</uui-box
      >`;
    if (this._state === 'failed')
      return html`<uui-box>
        <p role="alert">${this.#term('memberEntries_failed')}</p>
        <uui-button
          class="more"
          look="secondary"
          label=${this.#term('memberEntries_retry')}
          @click=${() => void this.#load()}
        ></uui-button>
      </uui-box>`;
    if (this._rows.length === 0) return html`<uui-box><p>${this.#term('memberEntries_empty')}</p></uui-box>`;
    return html`<uui-box headline-variant="h2" headline=${this.#term('memberEntries_count', this._total ?? 0)}>
      <table>
        <thead>
          <tr>
            <th scope="col">${this.#term('memberEntries_columnEntry')}</th>
            <th scope="col" class="wide">${this.#term('memberEntries_columnForm')}</th>
            <th scope="col">${this.#term('memberEntries_columnState')}</th>
            <th scope="col" class="wide">${this.#term('memberEntries_columnReceived')}</th>
          </tr>
        </thead>
        <tbody>
          ${this._rows.map((row) => this.#row(row))}
        </tbody>
      </table>
      ${
        this._next
          ? html`<uui-button
              class="more"
              look="secondary"
              label=${this.#term('memberEntries_more')}
              .state=${this._more ? 'waiting' : undefined}
              @click=${() => void this.#loadMore()}
            ></uui-button>`
          : nothing
      }
    </uui-box>`;
  }

  #row(row: EntryListRowResponseModel): TemplateResult {
    const form = this._forms.get(row.form) ?? '';
    // The link stands on a line of its own, so no text beside it needs telling apart from it by colour (WCAG 1.4.1).
    return html`<tr>
      <td>
        <span class="name"
          ><a href=${screenPath(ENTRY_ENTITY_TYPE, row.key)}
            >${this.#term('memberEntries_entry', String(row.number))}</a
          ></span
        >
        <span class="small narrow">${form}, ${this.#date(row.received)}</span>
      </td>
      <td class="wide muted">${form}</td>
      <td>${this.#term(`state_${row.state}`)}</td>
      <td class="wide muted">${this.#date(row.received)}</td>
    </tr>`;
  }
}

export default MemberEntriesViewElement;

customElements.define('jfb-member-entries-view', MemberEntriesViewElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-member-entries-view': MemberEntriesViewElement;
  }
}
