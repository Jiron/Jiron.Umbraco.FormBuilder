// The insights' charts (ROADMAP 11.5, ADR-0211): small SVG drawings, no chart library. Columns stand for groups of days
// or hours, each beside a quieter column for its counterpart in the period before; bars lie in a list for pages,
// fields, website pages and the options of an answer (11.5b, ADR-0212), whose bars are shares of the answers. A drawing is one image to assistive technology, named by what it shows, and each box's
// "Show as table" reads every value; a column's own title says its numbers under the pointer. Nothing moves.
import { css, html, nothing, svg } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens } from '../styles/tokens';
import { sharesOf } from './insight-model';

/** One value of a chart: what it is, its number, and that of its counterpart before. */
export interface ChartValue {
  readonly label: string;
  readonly value: number;
  /** Its counterpart in the period compared with; null where it has none, undefined without a comparison. */
  readonly before?: number | null;
  /** What the pointer's title says of it, with its numbers. */
  readonly title: string;
}

/** One bar of a list: a name, what is said of its number, and the number its bar is drawn from. */
export interface BarValue {
  readonly name: string;
  readonly said: string;
  readonly value: number;
}

const HEIGHT = 120;
const SLOT = 12;

/** Columns over groups of days or hours, each beside its counterpart before. */
export class InsightColumnsElement extends UmbLitElement {
  static override properties = {
    values: { attribute: false },
    label: { type: String },
    axis: { attribute: false },
    legend: { attribute: false },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }

      svg {
        display: block;
        inline-size: 100%;
        block-size: ${HEIGHT}px;
        overflow: visible;
      }

      .now {
        fill: var(--jfb-bo-color-selected);
      }

      .before {
        fill: var(--jfb-bo-color-control-border);
      }

      .base {
        stroke: var(--uui-color-divider-emphasis);
        stroke-width: 1;
        vector-effect: non-scaling-stroke;
      }

      .axis,
      .legend {
        display: flex;
        flex-wrap: wrap;
        justify-content: space-between;
        gap: var(--uui-size-space-2) var(--uui-size-space-5);
        margin: var(--uui-size-space-2) 0 0;
        padding: 0;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        list-style: none;
      }

      .legend {
        justify-content: flex-start;
        margin-block-start: var(--uui-size-space-3);
      }

      .key {
        display: inline-block;
        inline-size: 12px;
        block-size: 12px;
        margin-inline-end: var(--uui-size-space-2);
        border-radius: 2px;
        vertical-align: -1px;
        background: var(--jfb-bo-color-selected);
      }

      .key.before {
        background: var(--jfb-bo-color-control-border);
      }
    `,
  ];

  declare values: readonly ChartValue[];
  declare label: string;
  /** The labels under the columns, spread from the first to the last. */
  declare axis: readonly string[];
  /** What the two kinds of column stand for, with a comparison. */
  declare legend: readonly [now: string, before: string] | undefined;

  constructor() {
    super();
    this.values = [];
    this.label = '';
    this.axis = [];
    this.legend = undefined;
  }

  override render() {
    const compared = this.values.some((value) => value.before !== undefined);
    const shares = sharesOf(this.values.flatMap((value) => [value.value, value.before ?? 0]));
    const width = Math.max(1, this.values.length) * SLOT;
    // A column of a value above none shows at least a sliver, so a small day is not read as an empty one.
    const height = (share: number, value: number) => (value > 0 ? Math.max(2, share * HEIGHT) : 0);
    return html`<svg role="img" aria-label=${this.label} viewBox=${`0 0 ${width} ${HEIGHT}`} preserveAspectRatio="none">
        ${this.values.map((value, index) => {
          const x = index * SLOT;
          const now = height(shares[index * 2] ?? 0, value.value);
          const before = height(shares[index * 2 + 1] ?? 0, value.before ?? 0);
          return compared
            ? svg`<g><title>${value.title}</title>
                <rect class="before" x=${x + 1.5} y=${HEIGHT - before} width="4" height=${before} rx="1"></rect>
                <rect class="now" x=${x + 6} y=${HEIGHT - now} width="4.5" height=${now} rx="1"></rect></g>`
            : svg`<g><title>${value.title}</title>
                <rect class="now" x=${x + 2} y=${HEIGHT - now} width=${SLOT - 4} height=${now} rx="1"></rect></g>`;
        })}
        <line class="base" x1="0" x2=${width} y1=${HEIGHT} y2=${HEIGHT}></line>
      </svg>
      <ul class="axis" aria-hidden="true">
        ${this.axis.map((label) => html`<li>${label}</li>`)}
      </ul>
      ${
        compared && this.legend
          ? html`<ul class="legend">
              <li><span class="key" aria-hidden="true"></span>${this.legend[0]}</li>
              <li><span class="key before" aria-hidden="true"></span>${this.legend[1]}</li>
            </ul>`
          : nothing
      }`;
  }
}

/** Bars in a list, each its value's share of the most or of a whole, with what is said of it. */
export class InsightBarsElement extends UmbLitElement {
  static override properties = {
    values: { attribute: false },
    ordered: { type: Boolean },
    whole: { type: Number },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }

      ol,
      ul {
        display: grid;
        gap: var(--uui-size-space-4);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .line {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        justify-content: space-between;
        gap: 0 var(--uui-size-space-4);
        margin-block-end: var(--uui-size-space-2);
      }

      .name {
        min-inline-size: 0;
        overflow-wrap: anywhere;
      }

      .said {
        color: var(--jfb-bo-color-text-muted);
        font-variant-numeric: tabular-nums;
      }

      svg {
        display: block;
        inline-size: 100%;
        block-size: 8px;
      }

      .track {
        fill: var(--uui-color-surface-alt);
      }

      .bar {
        fill: var(--jfb-bo-color-selected);
      }
    `,
  ];

  declare values: readonly BarValue[];
  /** Whether the order of the bars says something, as a funnel's does. */
  declare ordered: boolean;
  /** The number each bar is a share of, as an option's bar is of the answers; 0 for shares of the most. */
  declare whole: number;

  constructor() {
    super();
    this.values = [];
    this.ordered = false;
    this.whole = 0;
  }

  override render() {
    const whole = this.whole;
    const shares =
      whole > 0
        ? this.values.map((value) => Math.min(1, Math.max(0, value.value) / whole))
        : sharesOf(this.values.map((value) => value.value));
    const items = this.values.map(
      (value, index) =>
        html`<li>
          <div class="line"><span class="name">${value.name}</span><span class="said">${value.said}</span></div>
          <svg viewBox="0 0 100 8" preserveAspectRatio="none" aria-hidden="true">
            <rect class="track" x="0" y="0" width="100" height="8" rx="2"></rect>
            ${
              (shares[index] ?? 0) > 0
                ? svg`<rect class="bar" x="0" y="0" width=${Math.max(1, (shares[index] ?? 0) * 100)} height="8" rx="2"></rect>`
                : nothing
            }
          </svg>
        </li>`,
    );
    return this.ordered
      ? html`<ol>
          ${items}
        </ol>`
      : html`<ul>
          ${items}
        </ul>`;
  }
}

if (!customElements.get('jfb-insight-columns')) customElements.define('jfb-insight-columns', InsightColumnsElement);
if (!customElements.get('jfb-insight-bars')) customElements.define('jfb-insight-bars', InsightBarsElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-insight-columns': InsightColumnsElement;
    'jfb-insight-bars': InsightBarsElement;
  }
}
