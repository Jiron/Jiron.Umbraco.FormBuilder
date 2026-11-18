// Privacy > People (ROADMAP 13.1 part 13.2, IA 9, ADR-0223): a person's entries in every form, restricted ones included,
// found by an email address or by a member chosen with Umbraco's member picker. After Find, the results box offers Export
// everything (prepared in the background, its progress said in the box and its file linked from a toast), Set legal
// hold... (a sidebar) and Erase... (its preview first, then delete or anonymise). Each row opens its entry. Once erased, one
// line in the list's place says what went and where it is recorded. Every request names the person again; the server
// finds the entries again, and the audit log never names whom they were about. Its place's tabs name it.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UMB_CURRENT_USER_CONTEXT } from '@umbraco-cms/backoffice/current-user';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_MEMBER_PICKER_MODAL, UmbMemberItemRepository } from '@umbraco-cms/backoffice/member';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT, type UmbNotificationOptions } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  peopleExport,
  peopleFind,
  peoplePreview,
  peopleRelease,
  type EntryExportStatusResponseModel,
  type PersonEntryResponseModel,
  type PersonErasedResponseModel,
  type PersonFoundResponseModel,
} from '../api';
import { controlStyles } from '../designer/inspector/control.styles';
import { readyToast, type Notices } from '../entries/export-follow';
import { loadEntryWords } from '../entries/entry-words';
import { codeOf } from '../library/library-changes';
import { ENTRY_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { myPermissions } from '../section/my-permissions';
import { loadSettingsWords } from '../settings/settings-words';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { ACTION_NOTIFICATION, type ActionNotificationData } from '../ui/action-notification.element';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { bodyOf, downloadPersonExport, personExportFollower, type Person } from './people-api';
import { startIdleGate } from '../ui/idle-gate';
import { PEOPLE_ERASE_MODAL } from './people-erase.modal.token';
import { PEOPLE_HOLD_MODAL } from './people-hold.modal.token';
import { jfbPhoneColumn } from '../styles/phone';
import '../section/place-tabs.element';

/** Umbraco's Members section, whose users may list members in its picker. */
const MEMBERS_SECTION = 'Umb.Section.Members';

/** The longest address (RFC 5321). */
const MOST_ADDRESS = 254;

/** Whether a text looks like one address; the server checks it fully. */
export const looksLikeAddress = (text: string): boolean =>
  text.length > 2 && text.length <= MOST_ADDRESS && /^[^\s@]+@[^\s@]+$/.test(text);

/** Where the audit log is, for the line after an erase. */
const AUDIT_LOG = 'jfb-audit-log';

type Found = PersonFoundResponseModel | 'failed' | undefined;

export class PeopleScreenElement extends UmbLitElement {
  static override properties = {
    _ready: { state: true },
    _byMember: { state: true },
    _email: { state: true },
    _emailProblem: { state: true },
    _person: { state: true },
    _who: { state: true },
    _finding: { state: true },
    _found: { state: true },
    _export: { state: true },
    _exportFailed: { state: true },
    _erased: { state: true },
    _canAudit: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    controlStyles,
    css`
      :host {
        display: block;
        block-size: 100%;
      }

      .page {
        display: grid;
        gap: var(--uui-size-space-5);
        max-inline-size: 960px;
        margin-inline: auto;
      }

      p {
        margin: 0;
      }

      .find {
        display: flex;
        flex-wrap: wrap;
        align-items: flex-end;
        gap: var(--uui-size-space-3) var(--uui-size-space-4);
      }

      .find .item {
        flex: 1 1 18rem;
      }

      .find + .help,
      .find + .why {
        margin-block-start: var(--uui-size-space-3);
      }

      .why + .help {
        margin-block-start: var(--uui-size-space-2);
      }

      .member {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      .member strong {
        overflow-wrap: anywhere;
      }

      button.link {
        padding: 0;
        border: 0;
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        text-decoration: underline;
        cursor: pointer;
      }

      @media (hover: hover) {
        button.link:hover {
          color: var(--uui-color-interactive-emphasis);
        }
      }

      button.link:focus-visible,
      a:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
      }

      .progress {
        display: grid;
        gap: var(--uui-size-space-2);
        margin-block-end: var(--uui-size-space-4);
      }

      .note {
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }

      .more {
        margin-block-end: var(--uui-size-space-4);
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

      .number {
        text-align: end;
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
      }

      .muted,
      .small {
        color: var(--jfb-bo-color-text-muted);
      }

      .small {
        display: block;
        font-size: var(--uui-type-small-size);
      }

      .name {
        display: block;
      }

      .held {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2) var(--uui-size-space-3);
        margin-block-start: var(--uui-size-space-2);
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

      .done {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
      }

      .done uui-icon {
        flex: none;
        margin-block-start: 0.2em;
        color: var(--jfb-bo-color-positive-text);
      }

      .done p + p {
        margin-block-start: var(--uui-size-space-2);
      }

      /* At 760 px and narrower, when an entry came moves under the entry, and its form gives way (ADR-0221). */
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
    jfbPhoneColumn,
  ];

  declare private _ready: boolean;
  declare private _byMember: boolean;
  declare private _email: string;
  declare private _emailProblem: boolean;
  /** The person the results are about. */
  declare private _person: Person | undefined;
  /** How the screen names them: their address, or the member's name. */
  declare private _who: string;
  declare private _finding: boolean;
  declare private _found: Found;
  declare private _export: EntryExportStatusResponseModel | undefined;
  declare private _exportFailed: boolean;
  declare private _erased: { readonly erased: PersonErasedResponseModel; readonly who: string } | undefined;
  declare private _canAudit: boolean;
  /** Whether the user may open Umbraco's Members section, whose member picker the member search needs (16.6). */
  declare private _membersAllowed: boolean;

  #notices: Notices | undefined;

  constructor() {
    super();
    this._ready = false;
    this._byMember = false;
    this._email = '';
    this._emailProblem = false;
    this._person = undefined;
    this._who = '';
    this._finding = false;
    this._found = undefined;
    this._export = undefined;
    this._exportFailed = false;
    this._erased = undefined;
    this._canAudit = false;
    this._membersAllowed = false;
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (notices) => (this.#notices = notices));
    // Umbraco lists members only to users who may open its Members section; the others find a member's entries by the
    // member's address, which finds what the member search finds (ADR-0223, ADR-0241).
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (user) => {
      this.observe(user?.allowedSections, (sections) => {
        this._membersAllowed = sections?.includes(MEMBERS_SECTION) ?? false;
        if (!this._membersAllowed) this._byMember = false;
      });
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#load();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbGlobalSettings_${key}`, ...args);

  #count = (key: string, count: number) => this.#term(key, this.localize.number(count), count === 1);

  #date = (iso: string) =>
    new Intl.DateTimeFormat(this.localize.lang(), { day: 'numeric', month: 'short', year: 'numeric' }).format(
      new Date(iso),
    );

  // A hold ends at the start of its day in UTC, which names the day it was set for wherever the reader is.
  #day = (iso: string) =>
    new Intl.DateTimeFormat(this.localize.lang(), {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    }).format(new Date(iso));

  async #load() {
    const lang = this.localize.lang();
    const [permissions] = await Promise.all([
      myPermissions(this),
      loadSettingsWords(lang).catch(() => undefined),
      loadEntryWords(lang).catch(() => undefined),
    ]);
    this._canAudit = permissions?.global.includes('manageSettings') ?? false;
    this._ready = true;
  }

  // ---- Finding ------------------------------------------------------------------------------------------------------

  async #findByEmail(event: Event) {
    event.preventDefault();
    const email = this._email.trim();
    if (!looksLikeAddress(email)) {
      this._emailProblem = true;
      await this.updateComplete;
      this.shadowRoot?.getElementById('email')?.focus();
      return;
    }
    this._emailProblem = false;
    await this.#find({ email }, email);
  }

  /** Finds by address or by member; switched to members, the picker opens at once, so a member is one click away (16.6). */
  #switchWay() {
    this._byMember = !this._byMember;
    if (this._byMember) void this.#chooseMember();
  }

  async #chooseMember() {
    const value = await umbOpenModal(this, UMB_MEMBER_PICKER_MODAL, { data: { multiple: false } }).catch(
      () => undefined,
    );
    const key = value?.selection.find((unique): unique is string => typeof unique === 'string');
    if (!key) return;
    const { data } = await new UmbMemberItemRepository(this).requestItems([key]);
    const name = data?.find((item) => item.unique === key)?.name || this.#term('people_someMember');
    await this.#find({ member: key, name }, name);
  }

  async #find(person: Person, who: string) {
    this._finding = true;
    this._erased = undefined;
    this._exportFailed = false;
    const { data, error } = await tryExecute(this, peopleFind({ body: bodyOf(person) }), {
      disableNotifications: true,
    });
    this._finding = false;
    if (codeOf(error) === 'email_invalid') {
      this._emailProblem = true;
      return;
    }
    this._person = person;
    this._who = who;
    this._found = data ?? 'failed';
  }

  // ---- Export -------------------------------------------------------------------------------------------------------

  async #exportAll() {
    const person = this._person;
    if (!person || this._export) return;
    this._exportFailed = false;
    const { data } = await tryExecute(this, peopleExport({ body: bodyOf(person) }), { disableNotifications: true });
    if (!data) {
      this._exportFailed = true;
      return;
    }
    this._export = data;
    // Its status is polled, which must not keep an idle person signed in (ADR-0209, 16.1's review).
    await startIdleGate(this);
    personExportFollower.follow('', data, {
      progress: (now) => (this._export = now),
      finished: (done) => {
        this._export = undefined;
        if (done.state === 'failed') {
          this._exportFailed = true;
          return;
        }
        const notices = this.#notices;
        if (notices)
          notices.stay<UmbNotificationOptions<ActionNotificationData>>('positive', {
            elementName: ACTION_NOTIFICATION,
            data: readyToast(notices, '', done, downloadPersonExport),
          });
      },
      lost: () => {
        this._export = undefined;
        this._exportFailed = true;
      },
    });
  }

  // ---- Holds --------------------------------------------------------------------------------------------------------

  async #hold() {
    const person = this._person;
    const found = this._found;
    if (!person || !found || found === 'failed') return;
    const value = await umbOpenModal(this, PEOPLE_HOLD_MODAL, {
      data: { person, entries: found.entries.filter((entry) => entry.state !== 'draft') },
    }).catch(() => undefined);
    if (!value) return;
    const kept = new Set(value.keys);
    this._found = {
      ...found,
      entries: found.entries.map((entry) => (kept.has(entry.key) ? { ...entry, heldUntil: value.until } : entry)),
    };
    this.#notices?.peek('positive', {
      data: { message: this.#term('people_holdDone', this.#count('people_entries', value.held)) },
    });
  }

  async #release(entry: PersonEntryResponseModel) {
    const person = this._person;
    const found = this._found;
    if (!person || !found || found === 'failed') return;
    const { data } = await tryExecute(this, peopleRelease({ body: { ...bodyOf(person), entries: [entry.key] } }), {
      disableNotifications: true,
    });
    if (!data) return;
    this._found = {
      ...found,
      entries: found.entries.map((each) => (each.key === entry.key ? { ...each, heldUntil: null } : each)),
    };
    this.#notices?.peek('positive', { data: { message: this.#term('people_released') } });
  }

  // ---- Erase --------------------------------------------------------------------------------------------------------

  async #erase() {
    const person = this._person;
    if (!person) return;
    const { data: preview } = await tryExecute(this, peoplePreview({ body: bodyOf(person) }), {
      disableNotifications: true,
    });
    if (!preview) {
      this.#notices?.peek('danger', { data: { message: this.#term('people_eraseFailed') } });
      return;
    }
    const value = await umbOpenModal(this, PEOPLE_ERASE_MODAL, { data: { person, who: this._who, preview } }).catch(
      () => undefined,
    );
    if (!value) return;
    this._erased = { erased: value.erased, who: this._who };
    this._found = undefined;
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #findBox(): TemplateResult {
    const member = this._person && 'member' in this._person ? this._person : undefined;
    return html`<uui-box headline-variant="h2" headline=${this.#term('people_findBox')}>
      ${
        this._byMember
          ? html`<div class="member">
              ${member ? html`<span>${this.#term('people_member')}: <strong>${member.name}</strong></span>` : nothing}
              <uui-button
                look=${member ? 'secondary' : 'primary'}
                label=${this.#term('people_chooseMember')}
                .state=${this._finding ? 'waiting' : undefined}
                @click=${() => void this.#chooseMember()}
              ></uui-button>
            </div>`
          : html`<form class="find" @submit=${(event: Event) => void this.#findByEmail(event)} novalidate>
              <div class="item">
                <label class="label" for="email">${this.#term('people_email')}</label>
                <input
                  type="email"
                  id="email"
                  autocomplete="off"
                  spellcheck="false"
                  maxlength=${MOST_ADDRESS}
                  placeholder=${this.#term('people_emailPlaceholder')}
                  aria-invalid=${this._emailProblem ? 'true' : 'false'}
                  aria-describedby=${this._emailProblem ? 'email-why find-help' : 'find-help'}
                  .value=${this._email}
                  @input=${(event: Event) => {
                    this._email = (event.target as HTMLInputElement).value;
                    this._emailProblem = false;
                  }}
                />
              </div>
              <uui-button
                type="submit"
                look="primary"
                label=${this.#term('people_find')}
                .state=${this._finding ? 'waiting' : undefined}
              ></uui-button>
            </form>`
      }
      ${this._emailProblem && !this._byMember ? html`<p class="why" id="email-why">${this.#term('people_emailInvalid')}</p>` : nothing}
      <p class="help" id="find-help">
        ${this.#term('people_findHelp')}
        ${
          this._membersAllowed
            ? html`<button type="button" class="link" @click=${() => this.#switchWay()}>
                ${this.#term(this._byMember ? 'people_byEmail' : 'people_byMember')}
              </button>`
            : nothing
        }
      </p>
    </uui-box>`;
  }

  #entryCell(entry: PersonEntryResponseModel): TemplateResult {
    const name = this.#term('people_entry', String(entry.number));
    const label =
      entry.state === 'draft'
        ? html`${this.#term('people_draft')}`
        : html`<a href=${screenPath(ENTRY_ENTITY_TYPE, entry.key)}>${name}</a>`;
    // The link stands on a line of its own, so no text beside it needs telling apart from it by colour (WCAG 1.4.1).
    return html`<span class="name">${label}</span>
      ${entry.test ? html`<span class="small">${this.#term('people_test')}</span>` : nothing}
      <span class="small narrow">${entry.form.name}, ${this.#date(entry.received)}</span>
      ${
        entry.heldUntil
          ? html`<span class="held">
              <uui-tag look="secondary" color="warning"
                >${this.#term('people_heldUntil', this.#day(entry.heldUntil))}</uui-tag
              >
              <button
                type="button"
                class="link"
                aria-label=${this.#term('people_releaseLabel', name)}
                @click=${() => void this.#release(entry)}
              >
                ${this.#term('people_release')}
              </button>
            </span>`
          : nothing
      }`;
  }

  #amount(count: number): string {
    return count > 0 ? this.localize.number(count) : this.#term('people_noneCount');
  }

  #actions(found: PersonFoundResponseModel): TemplateResult {
    const sent = found.entries.some((entry) => entry.state !== 'draft');
    return html`<div class="actions" slot="header-actions">
      <uui-button
        compact
        look="secondary"
        label=${this.#term('people_export')}
        ?disabled=${this._export !== undefined}
        @click=${() => void this.#exportAll()}
        ><uui-icon name="icon-download-alt" aria-hidden="true"></uui-icon>${this.#term('people_export')}</uui-button
      >
      ${
        sent
          ? html`<uui-button
              compact
              look="secondary"
              label=${this.#term('people_hold')}
              @click=${() => void this.#hold()}
            ></uui-button>`
          : nothing
      }
      <uui-button
        compact
        look="secondary"
        label=${this.#term('people_erase')}
        @click=${() => void this.#erase()}
      ></uui-button>
    </div>`;
  }

  #progress(): TemplateResult | typeof nothing {
    const running = this._export;
    if (running) {
      const total = Math.max(running.total, 1);
      return html`<div class="progress" role="status">
        <uui-progress-bar
          aria-label=${this.#term('people_exportLabel')}
          progress=${Math.round((100 * running.done) / total)}
        ></uui-progress-bar>
        <span
          >${this.#term('people_exporting', this.localize.number(running.done), this.localize.number(running.total))}</span
        >
      </div>`;
    }
    return this._exportFailed
      ? html`<p class="alert progress" role="alert">${this.#term('people_exportFailed')}</p>`
      : nothing;
  }

  #results(found: PersonFoundResponseModel): TemplateResult {
    if (found.entries.length === 0)
      return html`<uui-box>
        <p role="status">
          ${this._person && 'email' in this._person && this._membersAllowed ? this.#term('people_none', this._who) : this.#term('people_noneFound', this._who)}
        </p>
      </uui-box>`;
    const headline = this.#term(
      'people_found',
      this.#count('people_entries', found.entries.length),
      this.#count('people_forms', found.forms),
    );
    return html`<uui-box headline-variant="h2" headline=${headline}>
      ${this.#actions(found)} ${this.#progress()}
      ${found.more ? html`<p class="note more">${this.#term('people_more')}</p>` : nothing}
      <table>
        <thead>
          <tr>
            <th scope="col">${this.#term('people_columnEntry')}</th>
            <th scope="col" class="wide">${this.#term('people_columnForm')}</th>
            <th scope="col" class="wide">${this.#term('people_columnReceived')}</th>
            <th scope="col" class="number">${this.#term('people_columnFiles')}</th>
            <th scope="col" class="number">${this.#term('people_columnEmails')}</th>
          </tr>
        </thead>
        <tbody>
          ${found.entries.map(
            (entry) =>
              html`<tr>
                <td>${this.#entryCell(entry)}</td>
                <td class="wide muted">
                  ${entry.form.name}
                  ${entry.form.trashed ? html`<span class="small">${this.#term('people_trashed')}</span>` : nothing}
                </td>
                <td class="wide muted">${this.#date(entry.received)}</td>
                <td class="number">${this.#amount(entry.files)}</td>
                <td class="number">${this.#amount(entry.emails)}</td>
              </tr>`,
          )}
        </tbody>
      </table>
    </uui-box>`;
  }

  #erasedNote(): TemplateResult | typeof nothing {
    const done = this._erased;
    if (!done) return nothing;
    const { erased, who } = done;
    // What was deleted besides the entries, where there was any: anonymised entries stay, and their drafts go.
    const deleted = [
      ...(erased.drafts > 0 ? [this.#count('people_drafts', erased.drafts)] : []),
      ...(erased.files > 0 ? [this.#count('people_files', erased.files)] : []),
      ...(erased.emails > 0 ? [this.#count('people_emails', erased.emails)] : []),
    ];
    const entries = this.#count('people_entries', erased.entries);
    const said =
      erased.mode === 'anonymise' && erased.entries > 0
        ? this.#term('people_erasedAnonymised', who, entries, erased.entries === 1, deleted)
        : this.#term('people_erased', who, [...(erased.entries > 0 ? [entries] : []), ...deleted]);
    return html`<uui-box>
      <div class="done" role="status">
        <uui-icon name="icon-check" aria-hidden="true"></uui-icon>
        <div>
          <p>
            ${said}
            ${
              erased.held > 0
                ? this.#term('people_erasedHeld', this.#count('people_entries', erased.held), erased.held === 1)
                : nothing
            }
          </p>
          <p>
            ${
              this._canAudit
                ? html`<a href=${screenPath(AUDIT_LOG)}>${this.#term('people_auditOpen')}</a>`
                : this.#term('people_auditRecorded')
            }
          </p>
        </div>
      </div>
    </uui-box>`;
  }

  override render() {
    if (!this._ready)
      return html`<umb-body-layout
        ><jfb-place-tabs tab="people"></jfb-place-tabs>
        <div class="page">
          <uui-box>${skeletonLines(this.localize.term('general_loading'), [64, 48])}</uui-box>
        </div></umb-body-layout
      >`;
    const found = this._found;
    return html`<umb-body-layout>
      <jfb-place-tabs tab="people"></jfb-place-tabs>
      <div class="page">
        ${this.#findBox()} ${this.#erasedNote()}
        ${
          found === 'failed'
            ? html`<uui-box><p class="alert" role="alert">${this.#term('people_failed')}</p></uui-box>`
            : found
              ? this.#results(found)
              : nothing
        }
      </div>
    </umb-body-layout>`;
  }
}

customElements.define('jfb-people-screen', PeopleScreenElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-people-screen': PeopleScreenElement;
  }
}
