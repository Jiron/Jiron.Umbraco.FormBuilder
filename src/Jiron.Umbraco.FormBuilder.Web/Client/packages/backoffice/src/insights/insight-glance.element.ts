// The Overview's Forms at a glance (ROADMAP 11.5, IA 2, ADR-0211): the forms the user may view entries of, those with
// the most entries first, each with its views, starts, entries and conversion and each one's change from the period
// before, over the period its menu chooses (the last 30 days by default). A row's form opens its Insights view. It loads
// and fails on its own, and says so in place, as the Overview's other boxes do.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { InsightGlanceItemResponseModel, InsightTotalsResponseModel } from '../api';
import { FORM_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { readGlance } from './insight-api';
import { changeOf, compareOf, pointsOf, PRESETS, spanOf, utcToday, type Preset } from './insight-model';
import { loadInsightWords } from './insight-words';

type Period = Exclude<Preset, 'custom'>;

export class InsightGlanceElement extends UmbLitElement {
  static override properties = {
    _words: { state: true },
    _state: { state: true },
    _items: { state: true },
    _period: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: block;
      }

      .box--flush {
        --uui-box-default-padding: 0;
      }

      .title {
        font-size: var(--jfb-bo-type-title);
        font-weight: 700;
      }

      select {
        min-block-size: 32px;
        padding: 0 var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      .scroll {
        position: relative;
        overflow-x: auto;
        container-type: inline-size;
      }

      table {
        inline-size: 100%;
        border-collapse: collapse;
        font-variant-numeric: tabular-nums;
      }

      th,
      td {
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border-block-start: 1px solid var(--uui-color-divider);
        text-align: end;
        vertical-align: top;
        font-weight: 400;
      }

      thead th {
        border-block-start: 0;
        font-weight: 700;
      }

      /* In a narrow box the numbers sit closer, so the German table fits a 768 px window without scrolling (16.6). */
      @container (width < 40rem) {
        th,
        td {
          padding-inline: var(--uui-size-space-3);
        }

        th:first-child,
        td:first-child {
          padding-inline-start: var(--uui-size-space-5);
        }
      }

      th:first-child,
      td:first-child {
        text-align: start;
      }

      th a {
        color: var(--uui-color-text);
        text-decoration: none;
      }

      th a:hover,
      th a:focus-visible {
        color: var(--uui-color-interactive-emphasis);
        text-decoration: underline;
      }

      /* A change is read as one phrase, "no change", "6% more": the form's name gives way instead (the Phase 11 review). */
      .change {
        display: block;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        white-space: nowrap;
      }

      .muted,
      p {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .skeleton {
        display: block;
        block-size: 12px;
        margin-block: var(--uui-size-space-3);
        border-radius: 6px;
        background: var(--uui-color-surface-alt);
      }

      @media (prefers-reduced-motion: no-preference) {
        .skeleton {
          animation: pulse 1.6s ease-in-out infinite;
        }
      }

      @keyframes pulse {
        50% {
          opacity: 0.5;
        }
      }
    `,
  ];

  declare private _words: boolean;
  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _items: readonly InsightGlanceItemResponseModel[];
  declare private _period: Period;

  #reads = 0;

  constructor() {
    super();
    this._words = false;
    this._state = 'loading';
    this._items = [];
    this._period = 'last30';
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbInsights_${key}`, ...args);

  async #read() {
    const read = ++this.#reads;
    this._state = 'loading';
    const span = spanOf(this._period, utcToday(new Date()));
    const [words, items] = await Promise.all([
      loadInsightWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readGlance(this, span, compareOf(this._period, span)),
    ]);
    if (read !== this.#reads) return;
    this._words ||= words;
    if (!words || !items) {
      this._state = 'failed';
      return;
    }
    this._items = items;
    this._state = 'ready';
  }

  #number = (value: number): string => new Intl.NumberFormat(this.localize.lang()).format(value);

  #percent = (value: number): string =>
    new Intl.NumberFormat(this.localize.lang(), { style: 'percent', maximumFractionDigits: 1 }).format(value);

  /** A number with its change from the period before, said short. */
  #cell(now: number, before: number): TemplateResult {
    const change = changeOf(now, before);
    const said =
      change.kind === 'same'
        ? this.#term('short_same')
        : change.kind === 'fromNone'
          ? this.#term('short_new')
          : this.#term(`short_${change.kind}`, this.#percent(change.percent / 100));
    return html`<td>${this.#number(now)} <span class="change">${said}</span></td>`;
  }

  #conversion(totals: InsightTotalsResponseModel, before: InsightTotalsResponseModel): TemplateResult {
    const now = totals.conversion;
    if (now === null || now === undefined) return html`<td>${this.#term('noData')}</td>`;
    const earlier = before.conversion;
    const said =
      earlier === null || earlier === undefined
        ? this.#term('short_new')
        : pointsOf(now, earlier) === 0
          ? this.#term('short_same')
          : this.#term(now > earlier ? 'short_pointsUp' : 'short_pointsDown', this.#number(pointsOf(now, earlier)));
    return html`<td>${this.#percent(now)} <span class="change">${said}</span></td>`;
  }

  #menu(): TemplateResult {
    return html`<label slot="header-actions">
      <span class="visually-hidden">${this.#term('glancePeriod')}</span>
      <select
        .value=${this._period}
        @change=${(event: Event) => {
          this._period = (event.target as HTMLSelectElement).value as Period;
          void this.#read();
        }}
      >
        ${PRESETS.map(
          (preset) =>
            html`<option value=${preset} ?selected=${preset === this._period}>
              ${this.#term(`range_${preset}`)}
            </option>`,
        )}
      </select>
    </label>`;
  }

  #table(): TemplateResult {
    return html`<div class="scroll">
      <table>
        <caption class="visually-hidden">
          ${this.#term('glance')}
        </caption>
        <thead>
          <tr>
            <th scope="col">${this.#term('col_form')}</th>
            <th scope="col">${this.#term('col_views')}</th>
            <th scope="col">${this.#term('col_starts')}</th>
            <th scope="col">${this.#term('col_entries')}</th>
            <th scope="col">${this.#term('col_conversion')}</th>
          </tr>
        </thead>
        <tbody>
          ${this._items.map(
            (item) =>
              html`<tr>
                <th scope="row">
                  <a href=${`${screenPath(FORM_ENTITY_TYPE, item.key)}/view/insights`}>${item.name}</a>
                </th>
                ${this.#cell(item.totals.views, item.before.views)}
                ${this.#cell(item.totals.starts, item.before.starts)}
                ${this.#cell(item.totals.entries, item.before.entries)} ${this.#conversion(item.totals, item.before)}
              </tr>`,
          )}
        </tbody>
      </table>
    </div>`;
  }

  override render() {
    // Before its words are there, the box holds a skeleton that names nothing, as the Overview's others do.
    if (!this._words)
      return this._state === 'failed'
        ? html`<uui-box headline-variant="h2">
            <div class="failed" role="alert">
              <span>${this.localize.term('jfbOverview_glanceFailed')}</span>
              <uui-button
                look="outline"
                label=${this.localize.term('jfbOverview_tryAgain')}
                @click=${() => void this.#read()}
              ></uui-button>
            </div>
          </uui-box>`
        : html`<uui-box headline-variant="h2" aria-busy="true">
            <span class="skeleton" style="width: 62%"></span><span class="skeleton" style="width: 41%"></span>
          </uui-box>`;
    const headline = html`<span slot="headline" class="title">${this.#term('glance')}</span>`;
    if (this._state === 'failed')
      return html`<uui-box headline-variant="h2">
        ${headline} ${this.#menu()}
        <div class="failed" role="alert">
          <span>${this.#term('glanceFailed')}</span>
          <uui-button look="outline" label=${this.#term('tryAgain')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    if (this._state === 'loading' && this._items.length === 0)
      return html`<uui-box headline-variant="h2" aria-busy="true">
        ${headline} ${this.#menu()}
        <span class="skeleton" style="width: 62%"></span><span class="skeleton" style="width: 41%"></span>
        <span class="visually-hidden">${this.#term('glanceLoading')}</span>
      </uui-box>`;
    if (this._items.length === 0)
      return html`<uui-box headline-variant="h2"
        >${headline} ${this.#menu()}
        <p>${this.#term('glanceEmpty')}</p></uui-box
      >`;
    return html`<uui-box headline-variant="h2" class="box--flush">
      ${headline} ${this.#menu()} ${this.#table()}
      ${this._state === 'loading' ? html`<span class="visually-hidden" role="status">${this.#term('glanceLoading')}</span>` : nothing}
    </uui-box>`;
  }
}

if (!customElements.get('jfb-insight-glance')) customElements.define('jfb-insight-glance', InsightGlanceElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-insight-glance': InsightGlanceElement;
  }
}
