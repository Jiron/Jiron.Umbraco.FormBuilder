// The Answers of a form's Insights view (ROADMAP 11.5b, IA 5.5, ADR-0212): what the entries of a date range and of the
// entries list's filter answered, a box per field in the form's order. Options are bars of their share of the answers, a
// ranking's options stand by their average position, a grid goes row by row, a number has its figures and a 0-to-10
// scale its three groups and recommendation score, a checkbox is ticked or not, and free-text answers are listed newest
// first, each opening its entry, never summarised; any other field says how often it was answered, and the fields
// nobody answered share one line at the end. The toolbar's one switch shows every chart as its table. While another
// range or filter loads, the answers shown stay, dimmed and marked busy; a read that fails says so in their place.
import { css, html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type {
  EntryListFilterRequestModel,
  InsightAnswerOptionResponseModel,
  InsightAnswerResponseModel,
  InsightAnswersResponseModel,
} from '../api';
import { isFiltered, receivedText } from '../entries/entry-list-model';
import { ENTRY_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import {
  addsUp,
  gridColumns,
  lineKey,
  rankBars,
  shareOf,
  splitAnswered,
  withPage,
  type LineName,
  type TextsShown,
} from './answer-model';
import { readAnswers, readAnswerTexts } from './insight-api';
import './insight-chart.element';
import type { BarValue } from './insight-chart.element';
import type { Span } from './insight-model';
import { insightTable, jfbInsightTable } from './insight-table';

/** A field's free-text answers as shown, with a read of the next ones on its way or failed. */
interface Texts extends TextsShown {
  readonly busy: boolean;
  readonly failed: boolean;
}

export class InsightAnswersElement extends UmbLitElement {
  static override properties = {
    form: { type: String },
    span: { attribute: false },
    filter: { attribute: false },
    tables: { type: Boolean },
    _state: { state: true },
    _answers: { state: true },
    _texts: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    jfbInsightTable,
    css`
      :host {
        display: block;
        container: answers / inline-size;
      }

      .answers {
        transition: opacity 150ms ease;
      }

      .answers[aria-busy='true'] {
        opacity: 0.6;
      }

      .lead {
        margin: 0 0 var(--uui-size-space-4);
        color: var(--jfb-bo-color-text-muted);
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

      .count {
        color: var(--jfb-bo-color-text-muted);
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
      }

      .line {
        margin: 0;
      }

      .note {
        margin: var(--uui-size-space-4) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .unanswered {
        margin: var(--uui-size-space-5) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      /* A grid's rows side by side, so their bars stay short enough to compare; one under another where it is narrow. */
      .grid-rows {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
        gap: var(--uui-size-space-6) var(--uui-size-layout-1);
      }

      .row {
        margin: 0 0 var(--uui-size-space-3);
        font-size: inherit;
        font-weight: 700;
      }

      .figures {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        gap: var(--uui-size-space-4);
        margin: 0;
      }

      .figures div {
        display: grid;
        gap: var(--uui-size-space-1);
      }

      .figures dt {
        color: var(--jfb-bo-color-text-muted);
      }

      .figures dd {
        margin: 0;
        font-size: var(--uui-type-h4-size, 1.25rem);
        font-weight: 700;
        font-variant-numeric: tabular-nums;
      }

      .groups {
        margin-block-start: var(--uui-size-space-5);
      }

      .score {
        margin: var(--uui-size-space-5) 0 0;
        font-weight: 700;
      }

      .score + .note {
        margin-block-start: var(--uui-size-space-1);
      }

      .texts {
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .texts li {
        display: flex;
        align-items: baseline;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-3) 0;
      }

      .texts li + li {
        border-block-start: 1px solid var(--uui-color-divider-standalone);
      }

      .texts li:first-child {
        padding-block-start: 0;
      }

      /* Two lines of each answer: enough to read on, while the entry holds the whole. */
      .texts a {
        flex: 1 1 auto;
        min-inline-size: 0;
        display: -webkit-box;
        -webkit-box-orient: vertical;
        -webkit-line-clamp: 2;
        overflow: hidden;
        color: var(--uui-color-text);
        overflow-wrap: anywhere;
        text-decoration: underline;
        text-decoration-color: var(--uui-color-divider-emphasis);
        text-underline-offset: 0.2em;
        transition: text-decoration-color 120ms ease;
      }

      .texts a:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
        text-decoration-color: currentColor;
      }

      @media (hover: hover) {
        .texts a:hover {
          text-decoration-color: currentColor;
        }
      }

      .when {
        flex: none;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        white-space: nowrap;
      }

      .more {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
        margin-block-start: var(--uui-size-space-4);
      }

      .why {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      @container answers (max-width: 899px) {
        .boxes {
          grid-template-columns: minmax(0, 1fr);
        }
      }

      @container answers (max-width: 480px) {
        .figures {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }

        .texts li {
          flex-direction: column;
          gap: var(--uui-size-space-1);
        }
      }
    `,
  ];

  /** The form whose entries' answers are summarised. */
  declare form: string;
  /** The UTC days the entries were received on. */
  declare span: Span;
  /** The entries list's filter. */
  declare filter: EntryListFilterRequestModel;
  /** Whether every chart shows as its table. */
  declare tables: boolean;
  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _answers: InsightAnswersResponseModel | undefined;
  /** The free-text answers shown per field, with the next ones read on Show more. */
  declare private _texts: ReadonlyMap<string, Texts>;

  #reads = 0;

  constructor() {
    super();
    this.form = '';
    this.span = { from: '', to: '' };
    this.filter = { states: [], fields: [], forms: [] };
    this.tables = false;
    this._state = 'loading';
    this._answers = undefined;
    this._texts = new Map();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbInsights_${key}`, ...args);
  #full = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  override willUpdate(changed: PropertyValues<this>) {
    if (changed.has('form') || changed.has('span') || changed.has('filter')) void this.#read();
  }

  // ---- Reading ------------------------------------------------------------------------------------------------------

  async #read() {
    const form = this.form;
    if (!form) return;
    const read = ++this.#reads;
    this._state = 'loading';
    const answers = await readAnswers(this, form, this.span, this.filter);
    if (read !== this.#reads) return;
    if (!answers) {
      this._state = 'failed';
      return;
    }
    this._answers = answers;
    this._texts = new Map(
      answers.fields
        .filter((field) => field.kind === 'texts')
        .map((field) => [field.key, { texts: field.texts, more: field.more ?? null, busy: false, failed: false }]),
    );
    this._state = 'ready';
  }

  /** Show more: the field's next answers after those shown; the first of them takes the focus. */
  async #more(field: InsightAnswerResponseModel) {
    const shown = this._texts.get(field.key);
    if (!shown?.more || shown.busy) return;
    const read = this.#reads;
    const waiting: Texts = { ...shown, busy: true, failed: false };
    this.#setTexts(field.key, waiting);
    const page = await readAnswerTexts(this, this.form, field.key, this.span, this.filter, shown.more);
    // Another range or filter came meanwhile, or came while one was on its way: its answers replace these.
    if (read !== this.#reads || this._texts.get(field.key) !== waiting) return;
    if (!page) {
      this.#setTexts(field.key, { ...shown, busy: false, failed: true });
      return;
    }
    this.#setTexts(field.key, { ...withPage(shown, page), busy: false, failed: false });
    await this.updateComplete;
    // The button may be gone with the last page, so the focus moves on to where the list grew.
    this.shadowRoot
      ?.querySelector<HTMLElement>(`[data-field="${field.key}"] .texts li:nth-child(${shown.texts.length + 1}) a`)
      ?.focus();
  }

  #setTexts(key: string, texts: Texts) {
    this._texts = new Map(this._texts).set(key, texts);
  }

  // ---- Words --------------------------------------------------------------------------------------------------------

  #number = (value: number): string => new Intl.NumberFormat(this.localize.lang()).format(value);

  #figure = (value: number): string =>
    new Intl.NumberFormat(this.localize.lang(), { maximumFractionDigits: 2 }).format(value);

  /** A share in whole percent, but one above none and under 1% to its first figure, so it never reads as none. */
  #share = (value: number): string =>
    new Intl.NumberFormat(
      this.localize.lang(),
      value > 0 && value < 0.01
        ? { style: 'percent', maximumSignificantDigits: 1 }
        : { style: 'percent', maximumFractionDigits: 0 },
    ).format(value);

  /** A grid's row by its label, marked where the live grid no longer asks it. */
  #rowName = (row: { readonly label: string; readonly removed: boolean }): string =>
    row.removed ? this.#term('answerRemoved', row.label) : row.label;

  #answersCount = (count: number): string => this.#term('answerCount', count, this.#number(count));

  #lineName = (line: LineName): string =>
    line.other ? this.#term('optionOther') : line.older ? this.#term('optionOlder', line.label) : line.label;

  #heading = (field: InsightAnswerResponseModel): string =>
    field.removed ? this.#term('answerRemoved', field.label) : field.label;

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  /**
   * A field's box: its label, how many answered, its summary, and the group whose rows its answers are. A checkbox's
   * answers are its ticks, which its summary counts, so it says them once.
   */
  #box(field: InsightAnswerResponseModel, body: TemplateResult | typeof nothing, wide = false): TemplateResult {
    return html`<uui-box
      class=${wide ? 'wide' : ''}
      headline-variant="h2"
      headline=${this.#heading(field)}
      data-field=${field.key}
    >
      ${
        field.kind === 'checkbox'
          ? nothing
          : html`<span slot="header-actions" class="count">${this.#answersCount(field.answered)}</span>`
      }
      ${body} ${field.parentLabel ? html`<p class="note">${this.#term('answerRows', field.parentLabel)}</p>` : nothing}
    </uui-box>`;
  }

  /** Lines of counts as bars of their share of a whole, or as a table whose first column is named `first`. */
  #lines(
    caption: string,
    lines: readonly { readonly name: string; readonly count: number }[],
    whole: number,
    first = this.#term('col_option'),
  ): TemplateResult {
    const share = (count: number) => this.#share(shareOf(count, whole));
    return this.tables
      ? insightTable(
          caption,
          [first, this.#term('col_answers'), this.#term('col_answerShare')],
          lines.map((line) => [line.name, this.#number(line.count), share(line.count)]),
        )
      : html`<jfb-insight-bars
          .whole=${whole}
          .values=${lines.map((line): BarValue => ({
            name: line.name,
            said: this.#term('optionLine', share(line.count), this.#number(line.count)),
            value: line.count,
          }))}
        ></jfb-insight-bars>`;
  }

  #options(field: InsightAnswerResponseModel): TemplateResult {
    const lines = field.options.map((option) => ({ name: this.#lineName(option), count: option.count }));
    return this.#box(
      field,
      html`${this.#lines(field.label, lines, field.answered)}
      ${addsUp(field) ? html`<p class="note">${this.#term('optionSeveral')}</p>` : nothing}`,
    );
  }

  #ranking(field: InsightAnswerResponseModel): TemplateResult {
    const position = (option: InsightAnswerOptionResponseModel) =>
      option.position === null || option.position === undefined ? '\u2013' : this.#figure(option.position);
    const bars = rankBars(field.options);
    return this.#box(
      field,
      this.tables
        ? insightTable(
            field.label,
            [this.#term('col_option'), this.#term('col_position'), this.#term('col_answers')],
            field.options.map((option) => [this.#lineName(option), position(option), this.#number(option.count)]),
          )
        : html`<jfb-insight-bars
              ordered
              .whole=${bars.whole}
              .values=${field.options.map((option, index): BarValue => ({
                name: this.#lineName(option),
                said: this.#term('positionLine', position(option)),
                value: bars.values[index] ?? 0,
              }))}
            ></jfb-insight-bars>
            <p class="note">${this.#term('rankingNote')}</p>`,
    );
  }

  #grid(field: InsightAnswerResponseModel): TemplateResult {
    if (!this.tables)
      return this.#box(
        field,
        html`<div class="grid-rows">
          ${field.rows.map(
            (row) =>
              html`<div>
                <h3 class="row">${this.#rowName(row)}</h3>
                ${this.#lines(
                  this.#rowName(row),
                  row.options.map((option) => ({ name: this.#lineName(option), count: option.count })),
                  row.answered,
                )}
              </div>`,
          )}
        </div>`,
        true,
      );
    // The table holds a row of the grid in each of its rows and a column in each of its columns.
    const columns = gridColumns(field.rows);
    return this.#box(
      field,
      insightTable(
        field.label,
        [this.#term('col_row'), ...columns.map(this.#lineName)],
        field.rows.map((row) => {
          const counts = new Map(row.options.map((option) => [lineKey(option), option.count]));
          return [
            this.#rowName(row),
            ...columns.map((column) => {
              const count = counts.get(lineKey(column)) ?? 0;
              return this.#term('optionLine', this.#share(shareOf(count, row.answered)), this.#number(count));
            }),
          ];
        }),
      ),
      true,
    );
  }

  #numbers(field: InsightAnswerResponseModel): TemplateResult {
    const numbers = field.numbers;
    if (!numbers) return this.#box(field, nothing);
    const figures: readonly [string, number][] = [
      ['figure_average', numbers.average],
      ['figure_median', numbers.median],
      ['figure_lowest', numbers.lowest],
      ['figure_highest', numbers.highest],
    ];
    const groups = numbers.groups;
    const score = groups
      ? new Intl.NumberFormat(this.localize.lang(), { signDisplay: 'exceptZero' }).format(groups.score)
      : '';
    return this.#box(
      field,
      html`<dl class="figures">
          ${figures.map(
            ([key, value]) =>
              html`<div>
                <dt>${this.#term(key)}</dt>
                <dd>${this.#figure(value)}</dd>
              </div>`,
          )}
        </dl>
        ${
          groups
            ? html`<div class="groups">
                  ${this.#lines(
                    this.#term('scaleGroups', field.label),
                    [
                      { name: this.#term('scale_low'), count: groups.low },
                      { name: this.#term('scale_middle'), count: groups.middle },
                      { name: this.#term('scale_high'), count: groups.high },
                    ],
                    field.answered,
                    this.#term('col_group'),
                  )}
                </div>
                <p class="score">${this.#term('score', score)}</p>
                <p class="note">${this.#term('scoreHelp')}</p>`
            : nothing
        }`,
    );
  }

  // An unticked box stores nothing, so the ticks are a share of the entries; in a repeatable group, whose rows are not
  // counted, they are said as a number.
  #checkbox(field: InsightAnswerResponseModel, entries: number): TemplateResult {
    return this.#box(
      field,
      field.parent
        ? html`<p class="line">${this.#term('tickedRows', field.ticked, this.#number(field.ticked))}</p>`
        : this.#lines(
            field.label,
            [
              { name: this.#term('ticked'), count: field.ticked },
              { name: this.#term('notTicked'), count: Math.max(0, entries - field.ticked) },
            ],
            entries,
          ),
    );
  }

  #texts(field: InsightAnswerResponseModel): TemplateResult {
    const shown = this._texts.get(field.key) ?? {
      texts: field.texts,
      more: field.more ?? null,
      busy: false,
      failed: false,
    };
    const now = new Date();
    const lang = this.localize.lang();
    return this.#box(
      field,
      html`<ul class="texts" aria-label=${this.#term('textsLabel', field.label)}>
          ${shown.texts.map(
            (text) =>
              html`<li>
                <a href=${screenPath(ENTRY_ENTITY_TYPE, text.entry)}>${text.text}</a>
                <span class="when">${receivedText(text.received, now, lang, this.#full)}</span>
              </li>`,
          )}
        </ul>
        ${
          shown.more || shown.failed
            ? html`<div class="more">
                ${
                  shown.more
                    ? html`<uui-button
                        look="secondary"
                        compact
                        label=${this.#term('textsMore')}
                        .state=${shown.busy ? 'waiting' : undefined}
                        @click=${() => void this.#more(field)}
                      ></uui-button>`
                    : nothing
                }
                ${shown.failed ? html`<p class="why" role="alert">${this.#term('textsMoreFailed')}</p>` : nothing}
              </div>`
            : nothing
        }`,
    );
  }

  #answered(field: InsightAnswerResponseModel, entries: number): TemplateResult {
    // A field in a repeatable group is answered per row, which its note says; its share of the entries says nothing.
    return this.#box(
      field,
      field.parent
        ? nothing
        : html`<p class="line">${this.#term('answeredShare', this.#share(shareOf(field.answered, entries)))}</p>`,
    );
  }

  #field(field: InsightAnswerResponseModel, entries: number): TemplateResult {
    switch (field.kind) {
      case 'options':
        return this.#options(field);
      case 'ranking':
        return this.#ranking(field);
      case 'grid':
        return this.#grid(field);
      case 'number':
        return this.#numbers(field);
      case 'checkbox':
        return this.#checkbox(field, entries);
      case 'texts':
        return this.#texts(field);
      default:
        return this.#answered(field, entries);
    }
  }

  #body(): TemplateResult {
    const answers = this._answers;
    if (this._state === 'failed')
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#term('answersFailed')}</span>
          <uui-button look="outline" label=${this.#term('tryAgain')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    if (!answers) return html`<uui-box>${skeletonLines(this.#term('answersLoading'), [72, 56, 64])}</uui-box>`;
    const busy = this._state === 'loading' ? 'true' : 'false';
    if (answers.entries === 0)
      return html`<div class="answers" aria-busy=${busy}>
        <uui-box>
          <p class="line">${isFiltered(this.filter) ? this.#term('answersNoneFiltered') : this.#term('answersNone')}</p>
        </uui-box>
      </div>`;
    if (answers.fields.length === 0)
      return html`<div class="answers" aria-busy=${busy}>
        <uui-box><p class="line">${this.#term('answersNoFields')}</p></uui-box>
      </div>`;
    const { answered, unanswered } = splitAnswered(answers.fields);
    const names = new Intl.ListFormat(this.localize.lang(), { type: 'conjunction' }).format(
      unanswered.map(this.#heading),
    );
    return html`<div class="answers" aria-busy=${busy}>
      <p class="lead">${this.#term('answersFrom', answers.entries, this.#number(answers.entries))}</p>
      <div class="boxes">${answered.map((field) => this.#field(field, answers.entries))}</div>
      ${unanswered.length > 0 ? html`<p class="unanswered">${this.#term('answersEmpty', names)}</p>` : nothing}
    </div>`;
  }

  override render() {
    return html`${this.#body()}
      <div class="visually-hidden" role="status">
        ${this._state === 'loading' && this._answers ? this.#term('answersLoading') : ''}
      </div>`;
  }
}

if (!customElements.get('jfb-insight-answers')) customElements.define('jfb-insight-answers', InsightAnswersElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-insight-answers': InsightAnswersElement;
  }
}
