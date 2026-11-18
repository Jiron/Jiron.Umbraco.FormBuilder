// Privacy > Retention (ROADMAP 13.1, IA 9, ADR-0222): how long the site keeps entries in each state, the forms with days
// of their own, and what retention deletes within the next 7, 30 or 90 days per form, with the files that go along. The
// dry run counts with the days as they are typed, before they are saved, with no button to press. A privacy officer
// reads it all; changing the days needs manageSettings, and a host that locks the privacy section says so once. A state
// is kept for good only where the host allows it, and where it doesn't, the box names the key that would. Its
// its place's tabs along its top name it, so it has no title of its own (ADR-0257).
import { css, html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  retentionDryRun,
  retentionGet,
  type RetentionDaysRequestModel,
  type RetentionDryRunResponseModel,
  type RetentionFormResponseModel,
  type RetentionResponseModel,
} from '../api';
import { boxValue } from '../designer/inspector/box-value';
import { controlStyles } from '../designer/inspector/control.styles';
import { parseWhole } from '../designer/settings/settings-model';
import { codeOf } from '../library/library-changes';
import { formPath } from '../section/entity-types';
import { myPermissions } from '../section/my-permissions';
import { saveSection } from '../settings/settings-api';
import { loadSettingsWords } from '../settings/settings-words';
import { learnMore } from '../docs/learn-more.element';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { jfbPhoneColumn } from '../styles/phone';
import { configKey } from '../ui/config-key';
import { LeaveGuard } from '../ui/leave-guard';
import '../section/place-tabs.element';

/** The states retention keeps entries in, in the order the screen names them. */
export const STATES = ['submitted', 'approved', 'rejected', 'spam'] as const;
export type RetentionState = (typeof STATES)[number];

/** Days per state; 0 keeps a state's entries for good. */
export type RetentionDays = Readonly<Record<RetentionState, number>>;

/** The spans the dry run counts, in days (IA 9). */
export const SPANS = [7, 30, 90] as const;
type Span = (typeof SPANS)[number];

/** The days of a state kept for good (ADR-0222). */
export const FOR_GOOD = 0;

/** The most days a state keeps its entries: ten years; longer is kept for good (ADR-0226). */
export const MOST_DAYS = 3_650;

/** How long typing rests before the dry run counts again. */
export const COUNT_DELAY_MS = 300;

/** Where this browser keeps the span last chosen. */
const SPAN_KEY = 'jfb.privacy.retention.span';

const DEFAULTS: RetentionDays = { submitted: 365, approved: 365, rejected: 90, spam: 30 };

function readSpan(): Span {
  try {
    const kept = Number(localStorage.getItem(SPAN_KEY));
    return (SPANS as readonly number[]).includes(kept) ? (kept as Span) : 30;
  } catch {
    return 30;
  }
}

function keepSpan(span: Span) {
  try {
    localStorage.setItem(SPAN_KEY, String(span));
  } catch {
    // Kept for this page only.
  }
}

const same = (a: RetentionDays, b: RetentionDays) => STATES.every((state) => a[state] === b[state]);

type Alert = '' | 'conflict' | 'lockedNow' | 'invalid';

/** Where a form's name leads, by what the user may open somewhere; a form they can't open says so itself. */
interface Links {
  readonly entries: boolean;
  readonly settings: boolean;
}

export class RetentionScreenElement extends UmbLitElement {
  static override properties = {
    _model: { state: true },
    _failed: { state: true },
    _days: { state: true },
    _problems: { state: true },
    _canChange: { state: true },
    _links: { state: true },
    _saving: { state: true },
    _alert: { state: true },
    _span: { state: true },
    _count: { state: true },
    _counting: { state: true },
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
        max-inline-size: 760px;
        margin-inline: auto;
      }

      p {
        margin: 0;
      }

      .note {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
        color: var(--jfb-bo-color-text-muted);
      }

      .note uui-icon {
        flex: none;
        margin-block-start: 0.2em;
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }

      .note + .rows {
        margin-block-start: var(--uui-size-space-5);
      }

      .row {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 14rem);
        gap: var(--uui-size-space-3) var(--uui-size-space-6);
        align-items: start;
        padding-block: var(--uui-size-space-4);
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .row:first-child {
        padding-block-start: 0;
        border-block-start: 0;
      }

      .what,
      .value {
        display: grid;
        gap: var(--uui-size-space-2);
        min-inline-size: 0;
      }

      .with-unit input {
        inline-size: 6.5rem;
      }

      .shown {
        font-variant-numeric: tabular-nums;
      }

      .for-good {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        inline-size: fit-content;
        font-size: var(--uui-type-small-size);
        cursor: pointer;
      }

      .for-good input {
        margin: 0;
      }

      .rows + .help {
        padding-block-start: var(--uui-size-space-4);
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .help + .note {
        margin-block-start: var(--uui-size-space-3);
        font-size: var(--uui-type-small-size);
      }

      @media (max-width: 600px) {
        .row {
          grid-template-columns: minmax(0, 1fr);
        }
      }

      .spans {
        display: inline-flex;
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        overflow: hidden;
      }

      .spans button {
        min-block-size: 32px;
        padding: 0 var(--uui-size-space-4);
        border: 0;
        border-inline-start: 1px solid var(--jfb-bo-color-control-border);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
        font-size: var(--uui-type-small-size);
        white-space: nowrap;
        cursor: pointer;
      }

      .spans button:first-child {
        border-inline-start: 0;
      }

      @media (hover: hover) {
        .spans button:hover {
          background: var(--uui-color-surface-emphasis);
        }
      }

      .spans button[aria-pressed='true'] {
        background: var(--uui-color-selected);
        color: var(--uui-color-selected-contrast);
        font-weight: 700;
      }

      .spans button:focus-visible {
        position: relative;
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -4px;
      }

      .spans button[aria-pressed='true']:focus-visible {
        outline-color: var(--uui-color-selected-contrast);
      }

      .count {
        display: grid;
        gap: var(--uui-size-space-4);
      }

      .total {
        font-weight: 600;
      }

      .counted {
        transition: opacity 150ms ease;
      }

      .counted[aria-busy='true'] {
        opacity: 0.6;
      }

      @media (prefers-reduced-motion: reduce) {
        .counted {
          transition: none;
        }
      }

      .footnote {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
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

      .own {
        margin: 0;
      }

      .own div {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
        gap: var(--uui-size-space-2) var(--uui-size-space-6);
        padding-block: var(--uui-size-space-3);
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .own div:first-child {
        padding-block-start: 0;
        border-block-start: 0;
      }

      .own dt,
      .own dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      @media (max-width: 600px) {
        .own div {
          grid-template-columns: minmax(0, 1fr);
        }
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

      .rules {
        color: var(--jfb-bo-color-text-muted);
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

      a:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .trashed {
        display: block;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }
    `,
    jfbPhoneColumn,
  ];

  declare private _model: RetentionResponseModel | undefined;
  declare private _failed: false | 'failed' | 'forbidden';
  /** The days as the boxes hold them, those not saved yet among them. */
  declare private _days: RetentionDays;
  /** Why a box's value can't be kept, by state. */
  declare private _problems: ReadonlyMap<RetentionState, string>;
  declare private _canChange: boolean;
  declare private _links: Links;
  declare private _saving: boolean;
  declare private _alert: Alert;
  declare private _span: Span;
  declare private _count: RetentionDryRunResponseModel | 'failed' | undefined;
  declare private _counting: boolean;

  /** Counts the dry runs asked for, so only the answer to the last one is shown. */
  #asked = 0;
  #timer: ReturnType<typeof setTimeout> | undefined;
  /** What a state held before Keep for good, to give back when it is unticked. */
  #beforeForGood = new Map<RetentionState, number>();

  constructor() {
    super();
    // Days not saved yet are asked about before the backoffice leaves the screen.
    new LeaveGuard(this, () => this.#changed());
    this._model = undefined;
    this._failed = false;
    this._days = DEFAULTS;
    this._problems = new Map();
    this._canChange = false;
    this._links = { entries: false, settings: false };
    this._saving = false;
    this._alert = '';
    this._span = readSpan();
    this._count = undefined;
    this._counting = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
  }

  #words = (key: string, ...args: unknown[]) => this.localize.term(`jfbGlobalSettings_${key}`, ...args);

  #number = (value: number) => this.localize.number(value);

  // ---- Reading ------------------------------------------------------------------------------------------------------

  async #read() {
    this._failed = false;
    const [words, permissions, answer] = await Promise.all([
      loadSettingsWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      myPermissions(this),
      tryExecute(this, retentionGet(), { disableNotifications: true }),
    ]);
    if (!words || !answer.data) {
      this._failed = codeOf(answer.error) === 'forbidden' ? 'forbidden' : 'failed';
      return;
    }
    this._canChange = permissions?.global.includes('manageSettings') ?? false;
    this._links = {
      entries: permissions?.anywhere.includes('viewEntries') ?? false,
      settings: permissions?.anywhere.includes('design') ?? false,
    };
    this.#show(answer.data);
    await this.#count();
  }

  #show(model: RetentionResponseModel) {
    this._model = model;
    this._days = { ...model.days };
    this._problems = new Map();
    this.#beforeForGood.clear();
  }

  /** Whether the boxes hold days not saved yet. */
  #changed(): boolean {
    return this._model !== undefined && !same(this._days, this._model.days);
  }

  #editable(): boolean {
    return this._canChange && !this._model?.lockedBy;
  }

  // ---- Counting -----------------------------------------------------------------------------------------------------

  /** Counts again once typing rests; a span chosen counts at once. */
  #countSoon() {
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => void this.#count(), COUNT_DELAY_MS);
  }

  async #count() {
    clearTimeout(this.#timer);
    const asked = ++this.#asked;
    this._counting = true;
    const days: RetentionDaysRequestModel = { ...this._days };
    const { data } = await tryExecute(this, retentionDryRun({ body: { days, span: this._span } }), {
      disableNotifications: true,
    });
    // A later count wins over the answer to this one.
    if (asked !== this.#asked) return;
    this._counting = false;
    this._count = data ?? 'failed';
  }

  #chooseSpan(span: Span) {
    if (span === this._span) return;
    this._span = span;
    keepSpan(span);
    void this.#count();
  }

  // ---- Changes ------------------------------------------------------------------------------------------------------

  #problem(state: RetentionState, text: string | null) {
    if ((this._problems.get(state) ?? null) === text) return;
    const next = new Map(this._problems);
    if (text === null) next.delete(state);
    else next.set(state, text);
    this._problems = next;
  }

  #typed(state: RetentionState, raw: string, done: boolean) {
    const parsed = parseWhole(raw, 1, MOST_DAYS);
    if (parsed === null || parsed === undefined) {
      if (done) this.#problem(state, this.#words('range', this.#number(1), this.#number(MOST_DAYS)));
      return;
    }
    this.#problem(state, null);
    if (this._days[state] === parsed) return;
    this._days = { ...this._days, [state]: parsed };
    this._alert = '';
    this.#countSoon();
  }

  #forGood(state: RetentionState, on: boolean) {
    if (on) {
      this.#beforeForGood.set(state, this._days[state]);
      this.#problem(state, null);
    }
    const back = this.#beforeForGood.get(state);
    const restored = back !== undefined && back !== FOR_GOOD ? back : DEFAULTS[state];
    this._days = { ...this._days, [state]: on ? FOR_GOOD : restored };
    void this.#count();
  }

  async #save() {
    const model = this._model;
    if (!model || this._saving || !this.#editable()) return;
    if (this._problems.size > 0) {
      this._alert = 'invalid';
      await this.#focusProblem();
      return;
    }
    this._alert = '';
    this._saving = true;
    const answer = await saveSection(this, 'privacy', {
      version: model.version,
      site: null,
      values: { retention: { ...this._days } },
    });
    this._saving = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    switch (answer.kind) {
      case 'saved': {
        const saved = (answer.section.values as { retention?: RetentionDays } | null)?.retention ?? this._days;
        this.#show({ ...model, version: answer.section.version, days: { ...saved } });
        notifications?.peek('positive', { data: { message: this.#words('saved') } });
        await this.#count();
        return;
      }
      case 'invalid': {
        for (const [path, refused] of answer.fields) {
          const state = path.split('.')[1] as RetentionState | undefined;
          if (state && (STATES as readonly string[]).includes(state))
            this.#problem(
              state,
              refused.code === 'range'
                ? this.#words('range', this.#number(1), this.#number(MOST_DAYS))
                : refused.message,
            );
        }
        this._alert = 'invalid';
        await this.#focusProblem();
        return;
      }
      case 'conflict':
        this._alert = 'conflict';
        await this.#read();
        return;
      case 'locked':
        this._alert = 'lockedNow';
        await this.#read();
        return;
      default:
        notifications?.peek('danger', { data: { message: this.#words('saveFailed') } });
    }
  }

  async #focusProblem() {
    await this.updateComplete;
    const state = [...this._problems.keys()][0];
    const box = state ? this.shadowRoot?.getElementById(`days-${state}`) : null;
    box?.focus();
    box?.scrollIntoView?.({ block: 'center' });
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #stateName(state: RetentionState): string {
    return this.#words(`privacy_retention_${state}`);
  }

  #note(text: string | TemplateResult, icon = 'icon-lock'): TemplateResult {
    return html`<p class="note" role="note">
      <uui-icon name=${icon} aria-hidden="true"></uui-icon><span>${text}</span>
    </p>`;
  }

  #day(state: RetentionState, model: RetentionResponseModel): TemplateResult {
    const id = `days-${state}`;
    const days = this._days[state];
    const forGood = days === FOR_GOOD;
    const problem = this._problems.get(state);
    if (!this.#editable())
      return html`<div class="row">
        <div class="what">
          <span class="label" id=${`${id}-label`}>${this.#stateName(state)}</span>
          <p class="help">${this.#words(`privacy_retention_${state}Help`)}</p>
        </div>
        <p class="value shown" aria-labelledby=${`${id}-label`}>
          ${forGood ? this.#words('retentionForGoodShown') : this.#words('retentionDaysShown', this.#number(days))}
        </p>
      </div>`;
    const described = [`${id}-help`, `${id}-unit`, ...(problem ? [`${id}-why`] : [])].join(' ');
    return html`<div class="row">
      <div class="what">
        <label class="label" for=${id}>${this.#stateName(state)}</label>
        <p class="help" id=${`${id}-help`}>${this.#words(`privacy_retention_${state}Help`)}</p>
      </div>
      <div class="value">
        <div class="with-unit">
          <input
            type="text"
            id=${id}
            inputmode="numeric"
            autocomplete="off"
            ?disabled=${forGood}
            aria-describedby=${described}
            aria-invalid=${problem ? 'true' : 'false'}
            .value=${boxValue(forGood ? '' : String(days), problem !== undefined)}
            @input=${(event: Event) => this.#typed(state, (event.target as HTMLInputElement).value, false)}
            @change=${(event: Event) => this.#typed(state, (event.target as HTMLInputElement).value, true)}
          />
          <span id=${`${id}-unit`}>${this.#words(`privacy_retention_${state}Unit`)}</span>
        </div>
        ${
          model.keepForGood
            ? html`<label class="for-good">
                <input
                  type="checkbox"
                  .checked=${live(forGood)}
                  @change=${(event: Event) => this.#forGood(state, (event.target as HTMLInputElement).checked)}
                />
                ${this.#words('retentionForGood')}
              </label>`
            : nothing
        }
        ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing}
      </div>
    </div>`;
  }

  #daysBox(model: RetentionResponseModel): TemplateResult {
    return html`<uui-box headline-variant="h2" headline=${this.#words('privacyBox_retention')}>
      ${model.lockedBy ? this.#note(this.#words('locked', model.lockedBy)) : nothing}
      <div class="rows">${STATES.map((state) => this.#day(state, model))}</div>
      <p class="help">${this.#words('privacy_retentionHelp')} ${learnMore('retention')}</p>
      ${
        this.#editable() && !model.keepForGood
          ? this.#note(html`${this.#words('retentionForGoodLocked')} ${configKey(model.keepForGoodKey)}`)
          : nothing
      }
    </uui-box>`;
  }

  #formName(form: { key: string; name: string; trashed: boolean }, view: 'entries' | 'settings'): TemplateResult {
    if (form.trashed) return html`${form.name} <span class="trashed">${this.#words('retentionTrashed')}</span>`;
    return this._links[view]
      ? html`<a href=${`${formPath(form.key)}/view/${view}`}>${form.name}</a>`
      : html`${form.name}`;
  }

  #ownOf(form: RetentionFormResponseModel): string {
    const parts = STATES.flatMap((state) => {
      const days = form[state];
      return days === null || days === undefined
        ? []
        : [this.#words('retentionOwnState', this.#stateName(state), this.#number(days))];
    });
    if (form.storeDays !== null && form.storeDays !== undefined)
      parts.push(this.#words('retentionOwnStore', this.#number(form.storeDays)));
    return parts.join('; ');
  }

  #ownBox(model: RetentionResponseModel): TemplateResult {
    return html`<uui-box headline-variant="h2" headline=${this.#words('retentionOwnBox')}>
      ${
        model.forms.length === 0
          ? html`<p class="help">${this.#words('retentionOwnNone')}</p>`
          : html`<dl class="own">
              ${model.forms.map(
                (form) =>
                  html`<div>
                    <dt>${this.#formName(form, 'settings')}</dt>
                    <dd class="rules">${this.#ownOf(form)}</dd>
                  </div>`,
              )}
            </dl>`
      }
    </uui-box>`;
  }

  #spans(): TemplateResult {
    return html`<div class="spans" slot="header-actions" role="group" aria-label=${this.#words('retentionSpan')}>
      ${SPANS.map(
        (days) =>
          html`<button
            type="button"
            aria-pressed=${days === this._span ? 'true' : 'false'}
            @click=${() => this.#chooseSpan(days)}
          >
            ${this.#words(`retentionSpan_${days}`)}
          </button>`,
      )}
    </div>`;
  }

  #footnote(count: RetentionDryRunResponseModel): string {
    return [
      this.#changed() ? this.#words('retentionUnsaved') : '',
      count.held > 0
        ? this.#words('retentionHeld', this.#number(count.held), count.held === 1)
        : this.#words('retentionHeldStay'),
      this.#words('retentionByItself'),
    ]
      .filter(Boolean)
      .join(' ');
  }

  #countBox(): TemplateResult {
    const count = this._count;
    let body: TemplateResult;
    if (count === undefined) body = skeletonLines(this.#words('retentionCounting'), [48, 72]);
    else if (count === 'failed')
      body = html`<div class="failed" role="alert">
        <span>${this.#words('retentionCountFailed')}</span>
        <uui-button look="outline" label=${this.#words('retry')} @click=${() => void this.#count()}></uui-button>
      </div>`;
    else {
      const going = count.forms.filter((form) => form.entries > 0);
      const files = count.files > 0;
      body = html`<div class="count counted" aria-busy=${this._counting ? 'true' : 'false'}>
        <p class="total" role="status">
          ${
            count.entries === 0
              ? this.#words('retentionNothing')
              : this.#words(
                  'retentionTotal',
                  this.#number(count.entries),
                  this.#number(going.length),
                  count.entries === 1,
                  going.length === 1,
                )
          }
        </p>
        ${
          going.length > 0
            ? html`<table>
                <thead>
                  <tr>
                    <th scope="col">${this.#words('retentionColumnForm')}</th>
                    <th scope="col" class="number">${this.#words('retentionColumnEntries')}</th>
                    ${files ? html`<th scope="col" class="number">${this.#words('retentionColumnFiles')}</th>` : nothing}
                  </tr>
                </thead>
                <tbody>
                  ${going.map(
                    (form) =>
                      html`<tr>
                        <td>${this.#formName(form, 'entries')}</td>
                        <td class="number">${this.#number(form.entries)}</td>
                        ${
                          files
                            ? html`<td class="number">
                                ${form.files > 0 ? this.#number(form.files) : this.#words('retentionNoFiles')}
                              </td>`
                            : nothing
                        }
                      </tr>`,
                  )}
                </tbody>
              </table>`
            : nothing
        }
        <p class="footnote">${this.#footnote(count)}</p>
      </div>`;
    }
    return html`<uui-box headline-variant="h2" headline=${this.#words('retentionWhatBox')}>
      ${this.#spans()} ${body}
    </uui-box>`;
  }

  #body(): TemplateResult {
    if (this._failed === 'forbidden')
      return html`<uui-box>
        <p class="note" role="alert">
          ${this.localize.term('jfbLibrary_missingPermission', this.localize.term('jfbLibrary_permission_privacyTools'))}
        </p>
      </uui-box>`;
    if (this._failed)
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#words('failed')}</span>
          <uui-button look="outline" label=${this.#words('retry')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    const model = this._model;
    if (!model) return html`<uui-box>${skeletonLines(this.#words('loading'), [64, 48, 56])}</uui-box>`;
    const alert =
      this._alert === 'conflict'
        ? this.#words('conflict')
        : this._alert === 'lockedNow'
          ? this.#words('lockedNow')
          : this._alert === 'invalid'
            ? this.#words('invalid')
            : '';
    return html`${alert ? html`<p class="alert" role="alert">${alert}</p>` : nothing}
    ${!this._canChange && !model.lockedBy ? this.#note(this.#words('retentionReadOnly')) : nothing}
    ${this.#daysBox(model)} ${this.#ownBox(model)} ${this.#countBox()}`;
  }

  override render() {
    const savable = this._model !== undefined && !this._failed && this.#editable();
    return html`<umb-body-layout>
      <jfb-place-tabs tab="retention"></jfb-place-tabs>
      <div class="page">${this.#body()}</div>
      ${
        savable
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.#words('save_retention')}
              .state=${this._saving ? 'waiting' : undefined}
              @click=${() => void this.#save()}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

customElements.define('jfb-retention-screen', RetentionScreenElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-retention-screen': RetentionScreenElement;
  }
}
