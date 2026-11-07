// A form's Insights view (ROADMAP 11.5, IA 5.5, ADR-0211): what visitors do with the form over a date range, grouped by
// day, week or month, and compared with the period before when the switch says so. One strip of numbers, each change
// said in words, not in red and green; entries per group of days; who reaches each page with the median time to
// complete; the fields that stop visitors most, with the kind of error; the website pages entries come from; entries by
// hour of the site's day; and the members who sent entries, where any did. Every chart has "Show as table", which
// swaps it for its table in place and is kept for the user. While another range loads, the numbers shown stay, dimmed
// and marked busy; a read that fails says so in their place, as numbers of another range would mislead. A form whose
// insights are off says where to turn them on, and nothing else; one with no visit yet says when numbers appear.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { InsightReportResponseModel, InsightTotalsResponseModel } from '../../api';
import { keepTables, readReport } from '../../insights/insight-api';
import '../../insights/insight-chart.element';
import type { BarValue, ChartValue } from '../../insights/insight-chart.element';
import {
  axisOf,
  changeOf,
  compareOf,
  completionOf,
  dateOf,
  isSpan,
  kindKey,
  mostAt,
  pointsOf,
  PRESETS,
  GROUPINGS,
  spanOf,
  type Grouping,
  type Preset,
  type Span,
} from '../../insights/insight-model';
import { loadInsightWords } from '../../insights/insight-words';
import { pressOf } from '../../styles/press';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../../ui/skeleton';
import { FORM_WORKSPACE_CONTEXT } from '../constants';

/** The boxes with a chart, by the name the user's choice of tables keeps them under. */
type Box = 'entries' | 'funnel' | 'fields' | 'origins' | 'hours';

const HOUR_AXIS = ['00:00', '06:00', '12:00', '18:00', '23:00'];

export class FormInsightsViewElement extends UmbLitElement {
  static override properties = {
    _words: { state: true },
    _state: { state: true },
    _report: { state: true },
    _preset: { state: true },
    _span: { state: true },
    _grouping: { state: true },
    _compare: { state: true },
    _tables: { state: true },
    _choosing: { state: true },
    _rangeOpen: { state: true },
    _draft: { state: true },
    _draftInvalid: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    pressOf('.menu button'),
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
        container: insights / inline-size;
      }

      .toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
        min-block-size: 40px;
        margin-block-end: var(--uui-size-space-5);
      }

      .toolbar uui-toggle {
        margin-inline-start: auto;
      }

      .menu {
        display: grid;
        min-inline-size: 15rem;
        padding: var(--uui-size-space-2) 0;
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-3);
      }

      .menu button {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
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
      }

      .menu button[aria-pressed='true'] {
        font-weight: 700;
      }

      .segments {
        display: inline-flex;
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        overflow: hidden;
      }

      .menu .segments button {
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      .menu .segments button + button {
        border-inline-start: 1px solid var(--jfb-bo-color-control-border);
      }

      .menu .segments button[aria-pressed='true'] {
        background: var(--uui-color-selected);
        color: var(--uui-color-selected-contrast);
      }

      .menu .mark {
        inline-size: 1em;
      }

      .dates {
        display: grid;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-3) var(--uui-size-space-5) var(--uui-size-space-4);
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .dates label {
        display: grid;
        gap: var(--uui-size-space-1);
      }

      .dates input {
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      .why {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      .group-by {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-3) var(--uui-size-space-5) var(--uui-size-space-2);
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .line {
        margin: 0;
      }

      .stats {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        margin: 0 0 var(--uui-size-space-5);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .stat {
        display: grid;
        align-content: start;
        gap: var(--uui-size-space-1);
        padding: var(--uui-size-space-5);
      }

      .stat + .stat {
        border-inline-start: 1px solid var(--uui-color-divider-standalone);
      }

      .stat dt {
        color: var(--jfb-bo-color-text-muted);
      }

      .stat dd {
        margin: 0;
      }

      .number {
        font-size: var(--jfb-bo-type-display);
        font-weight: 700;
        font-variant-numeric: tabular-nums;
      }

      .change {
        color: var(--jfb-bo-color-text-muted);
      }

      .report {
        transition: opacity 150ms ease;
      }

      .report[aria-busy='true'] {
        opacity: 0.6;
      }

      .boxes {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: var(--uui-size-space-5);
        align-items: start;
      }

      .wide {
        grid-column: 1 / -1;
      }

      .note {
        margin: var(--uui-size-space-4) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      table {
        inline-size: 100%;
        border-collapse: collapse;
        font-variant-numeric: tabular-nums;
      }

      th,
      td {
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border-block-start: 1px solid var(--uui-color-divider);
        text-align: start;
        font-weight: 400;
      }

      thead th {
        border-block-start: 0;
        font-weight: 700;
      }

      th:first-child,
      td:first-child {
        padding-inline-start: 0;
      }

      .numeric {
        text-align: end;
      }

      .scroll {
        max-block-size: 22rem;
        overflow-y: auto;
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      @container insights (max-width: 899px) {
        .boxes {
          grid-template-columns: minmax(0, 1fr);
        }
      }

      @container insights (max-width: 700px) {
        .stats {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }

        .stat:nth-child(3) {
          border-inline-start: 0;
        }

        .stat:nth-child(n + 3) {
          border-block-start: 1px solid var(--uui-color-divider-standalone);
        }

        .toolbar uui-toggle {
          margin-inline-start: 0;
        }
      }
    `,
  ];

  declare private _words: boolean;
  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _report: InsightReportResponseModel | undefined;
  declare private _preset: Preset;
  declare private _span: Span;
  declare private _grouping: Grouping;
  declare private _compare: boolean;
  /** The boxes the user shows as tables. */
  declare private _tables: ReadonlySet<string>;
  /** Whether the range's menu shows the boxes of chosen dates. */
  declare private _choosing: boolean;
  /** Whether the range's menu is open, which its button's chevron shows. */
  declare private _rangeOpen: boolean;
  /** The chosen dates as written, before they are shown. */
  declare private _draft: Span;
  declare private _draftInvalid: boolean;

  #form: string | undefined;
  #reads = 0;
  /** The saves of the user's tables, one after the other, so the last choice is the one kept. */
  #keeping: Promise<unknown> = Promise.resolve();

  constructor() {
    super();
    this._words = false;
    this._state = 'loading';
    this._report = undefined;
    this._preset = 'last30';
    this._span = spanOf('last30', new Date());
    this._grouping = 'day';
    this._compare = false;
    this._tables = new Set();
    this._choosing = false;
    this._rangeOpen = false;
    this._draft = this._span;
    this._draftInvalid = false;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      if (!context) return;
      this.observe(
        context.unique,
        (unique) => {
          if (!unique || unique === this.#form) return;
          this.#form = unique;
          this._report = undefined;
          void this.#read();
        },
        'unique',
      );
    });
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbInsights_${key}`, ...args);
  #full = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  // ---- Reading ------------------------------------------------------------------------------------------------------

  async #read() {
    const form = this.#form;
    if (!form) return;
    const read = ++this.#reads;
    this._state = 'loading';
    const compare = this._compare ? compareOf(this._preset, this._span) : null;
    const [words, report] = await Promise.all([
      loadInsightWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readReport(this, form, this._span, this._grouping, compare),
    ]);
    if (read !== this.#reads) return;
    this._words ||= words;
    if (!words || !report) {
      this._state = 'failed';
      return;
    }
    this._report = report;
    this._tables = new Set(report.tables);
    this._state = 'ready';
  }

  #choose(preset: Exclude<Preset, 'custom'>) {
    this._preset = preset;
    this._span = spanOf(preset, new Date());
    this._choosing = false;
    this.#closeRange();
    void this.#read();
  }

  #showDates() {
    const draft = this._draft;
    if (!isSpan(draft)) {
      this._draftInvalid = true;
      return;
    }
    this._draftInvalid = false;
    this._preset = 'custom';
    this._span = draft;
    this.#closeRange();
    void this.#read();
  }

  #group(grouping: Grouping) {
    if (grouping === this._grouping) return;
    this._grouping = grouping;
    void this.#read();
  }

  #closeRange() {
    this.shadowRoot?.querySelector<HTMLElement & { hidePopover?: () => void }>('#range')?.hidePopover?.();
  }

  #toggleTable(box: Box) {
    const tables = new Set(this._tables);
    if (tables.has(box)) tables.delete(box);
    else tables.add(box);
    this._tables = tables;
    // The choice is the user's on every device; a keep that fails leaves this view as chosen. Saves go one after the
    // other, so two quick switches never land in the wrong order.
    const kept = [...tables];
    this.#keeping = this.#keeping.then(() => keepTables(this, kept));
  }

  // ---- Words --------------------------------------------------------------------------------------------------------

  #number = (value: number): string => new Intl.NumberFormat(this.localize.lang()).format(value);

  #percent = (value: number): string =>
    new Intl.NumberFormat(this.localize.lang(), { style: 'percent', maximumFractionDigits: 1 }).format(value);

  #day = (day: string, options: Intl.DateTimeFormatOptions): string =>
    new Intl.DateTimeFormat(this.localize.lang(), options).format(dateOf(day));

  /** The range's name on its button: the preset's, or the chosen dates. */
  #rangeName(): string {
    if (this._preset !== 'custom') return this.#term(`range_${this._preset}`);
    const options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short', year: 'numeric' };
    return this.#term('rangeDates', this.#day(this._span.from, options), this.#day(this._span.to, options));
  }

  /** A number's change from the one before, in words; nothing without a comparison. */
  #change(now: number, before: number | undefined): string | undefined {
    if (before === undefined) return undefined;
    const change = changeOf(now, before);
    switch (change.kind) {
      case 'same':
        return this.#term('same', this.#number(before));
      case 'fromNone':
        return this.#term('upFromNone');
      default:
        return this.#term(change.kind, this.#percent(change.percent / 100), this.#number(before));
    }
  }

  /** The conversion's change in points of a percent. */
  #pointsChange(now: number | null | undefined, before: number | null | undefined): string | undefined {
    if (now === null || now === undefined || before === null || before === undefined) return undefined;
    const points = pointsOf(now, before);
    if (points === 0) return this.#term('same', this.#percent(before));
    return this.#term(now > before ? 'pointsUp' : 'pointsDown', this.#number(points), this.#percent(before));
  }

  #periodName(start: string, short = false): string {
    switch (this._grouping) {
      case 'month':
        return this.#day(start, { month: short ? 'short' : 'long', year: 'numeric' });
      case 'week':
        return this.#day(start, { day: 'numeric', month: 'short' });
      default:
        return this.#day(
          start,
          short ? { day: 'numeric', month: 'short' } : { weekday: 'short', day: 'numeric', month: 'short' },
        );
    }
  }

  #completion(bucket: string | null | undefined): string | undefined {
    const time = bucket ? completionOf(bucket) : undefined;
    if (!time) return undefined;
    switch (time.unit) {
      case 'longer':
        return this.#term('medianLonger');
      case 'hour':
        return this.#term('median', this.#term('hour'));
      default:
        return this.#term('median', this.#term(time.unit, time.count));
    }
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #rangeMenu(): TemplateResult {
    // The button's name holds what it shows, so a voice command that says the range presses it (WCAG 2.5.3).
    return html`<uui-button look="outline" label=${this.#term('rangeButton', this.#rangeName())} popovertarget="range">
        ${this.#rangeName()} <uui-symbol-expand aria-hidden="true" ?open=${this._rangeOpen}></uui-symbol-expand>
      </uui-button>
      <uui-popover-container
        id="range"
        placement="bottom-start"
        @toggle=${(event: ToggleEvent) => {
          this._rangeOpen = event.newState === 'open';
          if (event.newState === 'open')
            (event.target as HTMLElement).querySelector<HTMLElement>('[aria-pressed="true"]')?.focus();
          else this._choosing = false;
        }}
      >
        <div class="menu" role="group" aria-label=${this.#term('range')}>
          ${PRESETS.map(
            (preset) =>
              html`<button
                type="button"
                aria-pressed=${this._preset === preset ? 'true' : 'false'}
                @click=${() => this.#choose(preset)}
              >
                <span class="mark" aria-hidden="true">${this._preset === preset ? '\u2713' : ''}</span
                >${this.#term(`range_${preset}`)}
              </button>`,
          )}
          <button
            type="button"
            aria-pressed=${this._preset === 'custom' ? 'true' : 'false'}
            aria-expanded=${this._choosing ? 'true' : 'false'}
            @click=${() => {
              this._draft = this._span;
              this._draftInvalid = false;
              this._choosing = !this._choosing;
            }}
          >
            <span class="mark" aria-hidden="true">${this._preset === 'custom' ? '\u2713' : ''}</span
            >${this.#term('range_custom')}
          </button>
          ${this._choosing ? this.#dates() : nothing}
          <div class="group-by">
            <span id="group-by">${this.#term('groupBy')}</span>
            <div class="segments" role="group" aria-labelledby="group-by">
              ${GROUPINGS.map(
                (grouping) =>
                  html`<button
                    type="button"
                    aria-pressed=${this._grouping === grouping ? 'true' : 'false'}
                    @click=${() => this.#group(grouping)}
                  >
                    ${this.#term(`group_${grouping}`)}
                  </button>`,
              )}
            </div>
          </div>
        </div>
      </uui-popover-container>`;
  }

  #dates(): TemplateResult {
    const invalid = this._draftInvalid;
    return html`<div class="dates">
      <label
        >${this.#term('rangeFrom')}
        <input
          type="date"
          .value=${this._draft.from}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${invalid ? 'dates-why' : nothing}
          @input=${(event: Event) => (this._draft = { ...this._draft, from: (event.target as HTMLInputElement).value })}
      /></label>
      <label
        >${this.#term('rangeTo')}
        <input
          type="date"
          .value=${this._draft.to}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${invalid ? 'dates-why' : nothing}
          @input=${(event: Event) => (this._draft = { ...this._draft, to: (event.target as HTMLInputElement).value })}
      /></label>
      ${invalid ? html`<p class="why" id="dates-why">${this.#term('rangeInvalid')}</p>` : nothing}
      <uui-button look="primary" label=${this.#term('rangeApply')} @click=${() => this.#showDates()}></uui-button>
    </div>`;
  }

  #toolbar(): TemplateResult {
    return html`<div class="toolbar">
      ${this.#rangeMenu()}
      <uui-toggle
        label=${this.#term('compare')}
        .checked=${this._compare}
        @change=${(event: Event) => {
          this._compare = (event.target as HTMLInputElement).checked;
          void this.#read();
        }}
      ></uui-toggle>
    </div>`;
  }

  #stat(label: string, value: string, change: string | undefined, extra?: string): TemplateResult {
    return html`<div class="stat">
      <dt>${label}</dt>
      <dd class="number">${value}</dd>
      ${change ? html`<dd class="change">${change}</dd>` : nothing}
      ${extra ? html`<dd class="change">${extra}</dd>` : nothing}
    </div>`;
  }

  #stats(totals: InsightTotalsResponseModel, before: InsightTotalsResponseModel | null | undefined): TemplateResult {
    const conversion = totals.conversion;
    return html`<dl class="stats">
      ${this.#stat(this.#term('stat_views'), this.#number(totals.views), this.#change(totals.views, before?.views))}
      ${this.#stat(this.#term('stat_starts'), this.#number(totals.starts), this.#change(totals.starts, before?.starts))}
      ${this.#stat(
        this.#term('stat_entries'),
        this.#number(totals.entries),
        this.#change(totals.entries, before?.entries),
        totals.spam > 0 ? this.#term('spam', this.#number(totals.spam)) : undefined,
      )}
      ${this.#stat(
        this.#term('stat_conversion'),
        conversion === null || conversion === undefined ? '\u2013' : this.#percent(conversion),
        before ? this.#pointsChange(conversion, before.conversion) : undefined,
      )}
    </dl>`;
  }

  /** A box with its chart, or its table where the user chose it, and the switch between them. */
  #box(
    box: Box,
    heading: string,
    chart: () => TemplateResult,
    table: () => TemplateResult,
    after?: TemplateResult,
    wide = false,
  ) {
    const asTable = this._tables.has(box);
    return html`<uui-box class=${wide ? 'wide' : ''} headline-variant="h2" headline=${heading}>
      <uui-button
        slot="header-actions"
        compact
        look="secondary"
        label=${asTable ? this.#term('showChart') : this.#term('showTable')}
        @click=${() => this.#toggleTable(box)}
      ></uui-button>
      ${asTable ? table() : chart()} ${after ?? nothing}
    </uui-box>`;
  }

  /** A box's table: the first column names each row, and the columns `numbers` says hold numbers are aligned to the end. */
  #table(
    caption: string,
    columns: readonly string[],
    rows: readonly (readonly string[])[],
    numbers: (column: number) => boolean = (column) => column > 0,
  ): TemplateResult {
    return html`<div class="scroll">
      <table>
        <caption class="visually-hidden">
          ${caption}
        </caption>
        <thead>
          <tr>
            ${columns.map((column, index) => html`<th scope="col" class=${numbers(index) ? 'numeric' : ''}>${column}</th>`)}
          </tr>
        </thead>
        <tbody>
          ${rows.map(
            (row) =>
              html`<tr>
                ${row.map((cell, index) =>
                  index === 0
                    ? html`<th scope="row">${cell}</th>`
                    : html`<td class=${numbers(index) ? 'numeric' : ''}>${cell}</td>`,
                )}
              </tr>`,
          )}
        </tbody>
      </table>
    </div>`;
  }

  #entries(report: InsightReportResponseModel): TemplateResult {
    const heading = this.#term(`entries_${this._grouping}`);
    const compared = report.before !== null && report.before !== undefined;
    const values: ChartValue[] = report.periods.map((period) => ({
      label: this.#periodName(period.start),
      value: period.entries,
      ...(compared ? { before: period.before ?? null } : {}),
      title: compared
        ? `${this.#periodName(period.start)}: ${this.#number(period.entries)} (${this.#number(period.before ?? 0)})`
        : `${this.#periodName(period.start)}: ${this.#number(period.entries)}`,
    }));
    const most = mostAt(values.map((value) => value.value));
    const mostName =
      most >= 0 ? `${values[most]?.label ?? ''} (${this.#number(values[most]?.value ?? 0)})` : this.#number(0);
    return this.#box(
      'entries',
      heading,
      () =>
        html`<jfb-insight-columns
          .values=${values}
          .label=${this.#term('entriesChart', heading, values.length, mostName)}
          .axis=${axisOf(report.periods.map((period) => this.#periodName(period.start, true)))}
          .legend=${[this.#term('legendNow'), this.#term('legendBefore')] as const}
        ></jfb-insight-columns>`,
      () =>
        this.#table(
          heading,
          [
            this.#term(`col_${this._grouping}`),
            this.#term('col_entries'),
            ...(compared ? [this.#term('col_before')] : []),
          ],
          [...report.periods]
            .reverse()
            .map((period) => [
              this.#periodName(period.start),
              this.#number(period.entries),
              ...(compared
                ? [period.before === null || period.before === undefined ? '\u2013' : this.#number(period.before)]
                : []),
            ]),
        ),
      undefined,
      true,
    );
  }

  #funnel(report: InsightReportResponseModel): TemplateResult {
    const starts = report.totals.starts;
    const steps = [
      { name: this.#term('funnelStarted'), value: starts },
      ...report.pages.map((page, index) => ({
        name: page.name ?? this.#term('funnelPage', index + 2),
        value: page.reached,
      })),
      { name: this.#term('funnelSent'), value: report.totals.entries },
    ];
    const share = (value: number) => (starts > 0 ? this.#percent(value / starts) : '\u2013');
    const median = this.#completion(report.medianCompletion);
    return this.#box(
      'funnel',
      this.#term('funnel'),
      () =>
        html`<jfb-insight-bars
          ordered
          .values=${steps.map((step): BarValue => ({ name: step.name, said: this.#number(step.value), value: step.value }))}
        ></jfb-insight-bars>`,
      () =>
        this.#table(
          this.#term('funnel'),
          [this.#term('col_page'), this.#term('col_visitors'), this.#term('col_share')],
          steps.map((step) => [step.name, this.#number(step.value), share(step.value)]),
        ),
      median ? html`<p class="note">${median}</p>` : undefined,
    );
  }

  #fields(report: InsightReportResponseModel): TemplateResult {
    const starts = report.totals.starts;
    const kind = (key: string) => this.#term(kindKey(key));
    const name = (label: string | null | undefined) => label ?? this.#term('fieldGone');
    return this.#box(
      'fields',
      this.#term('fields'),
      () =>
        report.fields.length === 0
          ? html`<p class="line">${this.#term('fieldsNone')}</p>`
          : html`<jfb-insight-bars
              .values=${report.fields.map((field): BarValue => ({
                name: name(field.label),
                said:
                  starts > 0
                    ? this.#term(
                        'fieldLine',
                        this.#number(field.stops),
                        this.#percent(field.stops / starts),
                        kind(field.kind),
                      )
                    : this.#term('fieldLineAlone', this.#number(field.stops), kind(field.kind)),
                value: field.stops,
              }))}
            ></jfb-insight-bars>`,
      () =>
        report.fields.length === 0
          ? html`<p class="line">${this.#term('fieldsNone')}</p>`
          : this.#table(
              this.#term('fields'),
              [this.#term('col_field'), this.#term('col_stops'), this.#term('col_share'), this.#term('col_mostly')],
              report.fields.map((field) => [
                name(field.label),
                this.#number(field.stops),
                starts > 0 ? this.#percent(field.stops / starts) : '\u2013',
                kind(field.kind),
              ]),
              // The kind that stops most is words.
              (column) => column === 1 || column === 2,
            ),
    );
  }

  #origins(report: InsightReportResponseModel): TemplateResult {
    const name = (value: string | null | undefined) => value ?? this.#term('originHidden');
    return this.#box(
      'origins',
      this.#term('origins'),
      () =>
        report.origins.length === 0
          ? html`<p class="line">${this.#term('originsNone')}</p>`
          : html`<jfb-insight-bars
              .values=${report.origins.map((origin): BarValue => ({
                name: name(origin.name),
                said: this.#term('entriesCount', this.#number(origin.entries)),
                value: origin.entries,
              }))}
            ></jfb-insight-bars>`,
      () =>
        report.origins.length === 0
          ? html`<p class="line">${this.#term('originsNone')}</p>`
          : this.#table(
              this.#term('origins'),
              [this.#term('col_websitePage'), this.#term('col_entries')],
              report.origins.map((origin) => [name(origin.name), this.#number(origin.entries)]),
            ),
    );
  }

  #hours(report: InsightReportResponseModel): TemplateResult {
    const hour = (index: number) => `${String(index).padStart(2, '0')}:00`;
    const values: ChartValue[] = report.hours.map((value, index) => ({
      label: hour(index),
      value,
      title: `${hour(index)}: ${this.#number(value)}`,
    }));
    const most = mostAt(report.hours);
    return this.#box(
      'hours',
      this.#term('hours'),
      () =>
        html`<jfb-insight-columns
          .values=${values}
          .label=${this.#term('hoursChart', this.#term('hours'), most >= 0 ? hour(most) : '\u2013')}
          .axis=${HOUR_AXIS}
        ></jfb-insight-columns>`,
      () =>
        this.#table(
          this.#term('hours'),
          [this.#term('col_hour'), this.#term('col_entries')],
          values.map((value) => [value.label, this.#number(value.value)]),
        ),
      html`<p class="note">${this.#term('hoursZone', report.timeZone)}</p>`,
    );
  }

  #members(report: InsightReportResponseModel): TemplateResult | typeof nothing {
    if (report.members === 0 && !report.membersBefore) return nothing;
    const members = this.#number(report.members);
    const entries = this.#number(report.memberEntries);
    const before = report.membersBefore;
    const line =
      before === null || before === undefined || before === report.members
        ? this.#term('membersLine', members, entries)
        : this.#term(
            report.members > before ? 'membersLineUp' : 'membersLineDown',
            members,
            entries,
            this.#number(before),
          );
    const others = report.totals.entries - report.memberEntries;
    return html`<uui-box class="wide" headline-variant="h2" headline=${this.#term('members')}>
      <p class="line">${line}${others > 0 ? ` ${this.#term('membersOther', this.#number(others))}` : ''}</p>
    </uui-box>`;
  }

  #body(): TemplateResult {
    const report = this._report;
    if (this._state === 'failed')
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#term('failed')}</span>
          <uui-button look="outline" label=${this.#term('tryAgain')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    if (!report) return html`<uui-box>${skeletonLines(this.#term('loading'), [72, 56, 64])}</uui-box>`;
    if (!report.siteOn) return html`<uui-box><p class="line">${this.#term('offSite')}</p></uui-box>`;
    if (!report.formOn) return html`<uui-box><p class="line">${this.#term('offForm')}</p></uui-box>`;
    const totals = report.totals;
    if (
      totals.views + totals.starts + totals.entries + totals.spam === 0 &&
      !report.before?.views &&
      !report.before?.entries
    )
      return html`<uui-box><p class="line">${this.#term('empty')}</p></uui-box>`;
    return html`<div class="report" aria-busy=${this._state === 'loading' ? 'true' : 'false'}>
      ${this.#stats(totals, report.before)}
      <div class="boxes">
        ${this.#entries(report)} ${this.#funnel(report)} ${this.#fields(report)} ${this.#origins(report)}
        ${this.#hours(report)} ${this.#members(report)}
      </div>
    </div>`;
  }

  override render() {
    // Until its words are loaded, the view's place holds a skeleton that names nothing; a failed load says so in the
    // backoffice's start words, which are always there.
    if (!this._words)
      return this._state === 'failed'
        ? html`<uui-box>
            <div class="failed" role="alert">
              <span>${this.#full('jfbDesigner_insightsFailed')}</span>
              <uui-button
                look="outline"
                label=${this.#full('jfbSettings_retry')}
                @click=${() => void this.#read()}
              ></uui-button>
            </div>
          </uui-box>`
        : html`<uui-box>${skeletonLines('', [72, 56, 64])}</uui-box>`;
    return html`${this.#toolbar()} ${this.#body()}
      <div class="visually-hidden" role="status">
        ${this._state === 'loading' && this._report ? this.#term('loading') : ''}
      </div>`;
  }
}

if (!customElements.get('jfb-form-insights-view'))
  customElements.define('jfb-form-insights-view', FormInsightsViewElement);

export { FormInsightsViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-insights-view': FormInsightsViewElement;
  }
}
