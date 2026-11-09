// The legal hold sidebar of Privacy > People (ROADMAP 13.1 part 13.2, IA 9, ADR-0222, ADR-0223): which of the person's
// entries, the reason on one line, and the day the hold ends, after today and at most a year ahead. An entry held already
// says until when; holding it again sets the new end. Each problem is said under what it is about, once Set is pressed.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { peopleHold, type PersonEntryResponseModel } from '../api';
import { codeOf } from '../library/library-changes';
import { controlStyles } from '../designer/inspector/control.styles';
import { jfbBackofficeTokens } from '../styles/tokens';
import { bodyOf } from './people-api';
import type { PeopleHoldModalData, PeopleHoldModalValue } from './people-hold.modal.token';

/** The most days ahead a hold may end (ADR-0222). */
export const MOST_HOLD_DAYS = 365;

/** The longest reason (ADR-0222). */
export const MOST_REASON = 500;

type Problem = 'entries' | 'reason' | 'until';

const JOIN = ` ${String.fromCharCode(0xb7)} `;

/** A day as a date box takes it, in UTC. */
const dayOf = (date: Date) => date.toISOString().slice(0, 10);

/** The days a hold may end on: from tomorrow to a year ahead. */
export function holdDays(now: Date): { readonly first: string; readonly last: string } {
  const day = 24 * 60 * 60 * 1000;
  return { first: dayOf(new Date(now.getTime() + day)), last: dayOf(new Date(now.getTime() + MOST_HOLD_DAYS * day)) };
}

export class PeopleHoldModalElement extends UmbModalBaseElement<PeopleHoldModalData, PeopleHoldModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _chosen: { state: true },
    _reason: { state: true },
    _until: { state: true },
    _problems: { state: true },
    _saving: { state: true },
    _failed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    controlStyles,
    css`
      .body {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
      }

      legend {
        margin-block-end: var(--uui-size-space-3);
        padding: 0;
        font-weight: 600;
      }

      .pick {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        gap: 0 var(--uui-size-space-3);
        align-items: baseline;
        padding-block: var(--uui-size-space-2);
        cursor: pointer;
      }

      .pick input {
        margin: 0;
      }

      .pick .of {
        grid-column: 2;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      input[type='date'] {
        max-inline-size: 12rem;
      }

      .note {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _chosen: ReadonlySet<string>;
  declare private _reason: string;
  declare private _until: string;
  declare private _problems: ReadonlySet<Problem>;
  declare private _saving: boolean;
  declare private _failed: boolean;

  constructor() {
    super();
    this._chosen = new Set();
    this._reason = '';
    this._until = '';
    this._problems = new Set();
    this._saving = false;
    this._failed = false;
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbGlobalSettings_${key}`, ...args);

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

  #toggle(key: string, on: boolean) {
    const next = new Set(this._chosen);
    if (on) next.add(key);
    else next.delete(key);
    this._chosen = next;
  }

  #check(now: Date): Set<Problem> {
    const problems = new Set<Problem>();
    const reason = this._reason.trim();
    const { first, last } = holdDays(now);
    if (this._chosen.size === 0) problems.add('entries');
    if (reason.length === 0 || reason.length > MOST_REASON || /[\r\n]/.test(reason)) problems.add('reason');
    if (!this._until || this._until < first || this._until > last) problems.add('until');
    return problems;
  }

  async #hold() {
    const data = this.data;
    if (!data || this._saving) return;
    const problems = this.#check(new Date());
    this._problems = problems;
    if (problems.size > 0) {
      await this.updateComplete;
      const first = (['entries', 'reason', 'until'] as const).find((problem) => problems.has(problem));
      this.shadowRoot
        ?.querySelector<HTMLElement>(first === 'entries' ? 'input[type="checkbox"]' : `#${first}`)
        ?.focus();
      return;
    }
    this._saving = true;
    this._failed = false;
    const until = `${this._until}T00:00:00Z`;
    const { data: held, error } = await tryExecute(
      this,
      peopleHold({
        body: {
          ...bodyOf(data.person),
          entries: [...this._chosen],
          until,
          reason: this._reason.trim(),
        },
      }),
      { disableNotifications: true },
    );
    this._saving = false;
    if (!held) {
      const code = codeOf(error);
      if (code === 'hold_end') this._problems = new Set(['until']);
      else if (code === 'hold_reason') this._problems = new Set(['reason']);
      else this._failed = true;
      return;
    }
    this.value = { held: held.entries, keys: [...this._chosen], until };
    this._submitModal();
  }

  #entry(entry: PersonEntryResponseModel): TemplateResult {
    const id = `entry-${entry.key}`;
    const of = [
      this.#term('people_holdOf', entry.form.name, this.#date(entry.received)),
      ...(entry.heldUntil ? [this.#term('people_heldUntil', this.#day(entry.heldUntil))] : []),
    ].join(JOIN);
    return html`<label class="pick">
      <input
        type="checkbox"
        .checked=${this._chosen.has(entry.key)}
        aria-describedby=${`${id}-of`}
        @change=${(event: Event) => this.#toggle(entry.key, (event.target as HTMLInputElement).checked)}
      />
      <span>${this.#term('people_entry', String(entry.number))}</span>
      <span class="of" id=${`${id}-of`}>${of}</span>
    </label>`;
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const { first, last } = holdDays(new Date());
    const count = this._chosen.size;
    const why = (problem: Problem, key: string) =>
      this._problems.has(problem) ? html`<p class="why" id=${`${problem}-why`}>${this.#term(key)}</p>` : nothing;
    return html`<umb-body-layout headline=${this.#term('people_holdHeadline')}>
      <uui-box>
        <div class="body">
          <fieldset aria-describedby=${this._problems.has('entries') ? 'entries-why' : nothing}>
            <legend>${this.#term('people_holdEntries')}</legend>
            ${data.entries.map((entry) => this.#entry(entry))} ${why('entries', 'people_holdChoose')}
          </fieldset>
          <div class="item">
            <label class="label" for="reason">${this.#term('people_holdReason')}</label>
            <input
              type="text"
              id="reason"
              maxlength=${MOST_REASON}
              autocomplete="off"
              aria-invalid=${this._problems.has('reason') ? 'true' : 'false'}
              aria-describedby=${['reason-help', ...(this._problems.has('reason') ? ['reason-why'] : [])].join(' ')}
              .value=${this._reason}
              @input=${(event: Event) => (this._reason = (event.target as HTMLInputElement).value)}
            />
            <p class="help" id="reason-help">${this.#term('people_holdReasonHelp')}</p>
            ${why('reason', 'people_holdReasonNeeded')}
          </div>
          <div class="item">
            <label class="label" for="until">${this.#term('people_holdUntil')}</label>
            <input
              type="date"
              id="until"
              min=${first}
              max=${last}
              aria-invalid=${this._problems.has('until') ? 'true' : 'false'}
              aria-describedby=${['until-help', ...(this._problems.has('until') ? ['until-why'] : [])].join(' ')}
              .value=${this._until}
              @input=${(event: Event) => (this._until = (event.target as HTMLInputElement).value)}
            />
            <p class="help" id="until-help">${this.#term('people_holdUntilHelp')}</p>
            ${why('until', 'people_holdEndInvalid')}
          </div>
          <p class="note">${this.#term('people_holdNote')}</p>
          ${this._failed ? html`<p class="alert" role="alert">${this.#term('people_holdFailed')}</p>` : nothing}
        </div>
      </uui-box>
      <div slot="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${
            count === 0
              ? this.#term('people_holdHeadline')
              : this.#term('people_holdConfirm', this.#term('people_entries', this.localize.number(count), count === 1))
          }
          .state=${this._saving ? 'waiting' : undefined}
          @click=${() => void this.#hold()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-people-hold-modal'))
  customElements.define('jfb-people-hold-modal', PeopleHoldModalElement);

export { PeopleHoldModalElement as element };
