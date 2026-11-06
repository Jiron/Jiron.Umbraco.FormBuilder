// Review both versions (ROADMAP 10.3c, IA 5.7, ADR-0200): after a save someone else's save refused, each difference
// between the draft they saved and the user's, under what it is about: the two versions to choose from head the two
// columns of values they keep, so the choice sits right above what it keeps. A difference only one side made starts on
// that side; one both made waits, marked "Changed in both", as does one whose maker isn't known. Save says how many
// still wait, once, marks each with words and goes to the first. Where the sidebar is narrow, the choice comes first and
// each row stacks its values under its name, each said by its version, as History's comparison does.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import type { Side } from './review-model';
import type { ReviewDifference, ReviewModalData, ReviewModalValue } from './review.modal.token';

export class ReviewModalElement extends UmbModalBaseElement<ReviewModalData, ReviewModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _chosen: { state: true },
    _missing: { state: true },
    _alert: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      .room {
        display: grid;
        gap: var(--uui-size-space-5);
        container-type: inline-size;
      }

      p {
        margin: 0;
      }

      .alert,
      .wait {
        color: var(--jfb-bo-color-danger-text);
      }

      .difference {
        display: grid;
        gap: var(--uui-size-space-3);
        padding-block-start: var(--uui-size-space-5);
        /* The sidebar's body is the background colour, where the plain divider doesn't show. */
        border-top: 1px solid var(--uui-color-divider-standalone);
      }

      .head {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      h4 {
        margin: 0;
        font-size: var(--uui-type-default-size);
        font-weight: 700;
        overflow-wrap: anywhere;
      }

      .missing h4 {
        color: var(--jfb-bo-color-danger-text);
      }

      table {
        width: 100%;
        border-collapse: collapse;
        table-layout: fixed;
      }

      /* The table's own header names the columns for a screen reader; the choice above heads them for sight. */
      thead {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
      }

      th,
      td {
        padding: var(--uui-size-space-2) var(--uui-size-space-4) var(--uui-size-space-2) 0;
        text-align: start;
        vertical-align: top;
        overflow-wrap: anywhere;
      }

      tbody th {
        font-weight: 400;
      }

      /* The same three columns as the table: the versions over the values they keep. */
      uui-radio-group {
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: var(--uui-size-space-2) 0;
      }

      uui-radio-group uui-radio:first-of-type {
        grid-column: 2;
      }

      /* A narrow sidebar stacks each row: what differs, then each version's value under its name. */
      @container (width < 32rem) {
        uui-radio-group {
          grid-template-columns: minmax(0, 1fr);
        }

        uui-radio-group uui-radio:first-of-type {
          grid-column: 1;
        }

        tr {
          display: grid;
          gap: var(--uui-size-space-1);
          padding-block: var(--uui-size-space-2);
        }

        tbody th,
        tbody td {
          padding: 0;
        }

        tbody th {
          font-weight: 700;
        }

        tbody td::before {
          /* The cell's own header names it for a screen reader; the version's name is only for sight. */
          content: attr(data-side) ': ' / '';
          color: var(--jfb-bo-color-text-muted);
        }
      }
    `,
  ];

  /** The side chosen for each difference by id, as it starts or as the user chose. */
  declare private _chosen: Readonly<Record<string, Side>>;
  /** Whether Save found differences still waiting for a choice, which are then marked until chosen. */
  declare private _missing: boolean;
  /** Whether Save's alert shows: from a Save that found differences waiting until the next choice. */
  declare private _alert: boolean;

  constructor() {
    super();
    this._chosen = {};
    this._missing = false;
    this._alert = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    this._chosen = Object.fromEntries(
      (this.data?.differences ?? []).flatMap((difference) =>
        difference.start ? [[difference.id, difference.start]] : [],
      ),
    );
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbHistory_${key}`, ...args);

  #waiting(): ReviewDifference[] {
    return (this.data?.differences ?? []).filter((difference) => this._chosen[difference.id] === undefined);
  }

  async #save() {
    const waiting = this.#waiting();
    if (waiting.length > 0) {
      this._missing = true;
      this._alert = true;
      await this.updateComplete;
      const index = this.data?.differences.indexOf(waiting[0] as ReviewDifference) ?? -1;
      await this.shadowRoot?.querySelector<HTMLElement>(`#choice-${index}`)?.focus();
      return;
    }
    this.value = {
      fromYours: Object.entries(this._chosen)
        .filter(([, side]) => side === 'yours')
        .map(([id]) => id),
    };
    this._submitModal();
  }

  // A choice ends Save's alert, which would otherwise say its count again with every choice.
  #choose(id: string, side: string) {
    if (side !== 'theirs' && side !== 'yours') return;
    this._chosen = { ...this._chosen, [id]: side };
    this._alert = false;
  }

  #difference(difference: ReviewDifference, index: number, theirs: string, yours: string): TemplateResult {
    const chosen = this._chosen[difference.id];
    const missing = this._missing && chosen === undefined;
    return html`<section class="difference ${missing ? 'missing' : ''}" aria-labelledby=${`title-${index}`}>
      <div class="head">
        <h4 id=${`title-${index}`}>${difference.title}</h4>
        ${difference.both ? html`<uui-tag look="secondary">${this.#term('review_both')}</uui-tag>` : nothing}
      </div>
      ${missing ? html`<p class="wait" id=${`wait-${index}`}>${this.#term('review_choose')}</p>` : nothing}
      <uui-radio-group
        id=${`choice-${index}`}
        aria-labelledby=${`title-${index}`}
        aria-describedby=${missing ? `wait-${index}` : nothing}
        .value=${chosen ?? ''}
        @change=${(event: Event) => this.#choose(difference.id, String((event.target as HTMLInputElement).value))}
      >
        <uui-radio value="theirs" label=${theirs}></uui-radio>
        <uui-radio value="yours" label=${yours}></uui-radio>
      </uui-radio-group>
      <table>
        <thead>
          <tr>
            <th scope="col"><span class="visually-hidden">${this.#term('compare_changed')}</span></th>
            <th scope="col">${theirs}</th>
            <th scope="col">${yours}</th>
          </tr>
        </thead>
        <tbody>
          ${difference.rows.map(
            (row) =>
              html`<tr>
                <th scope="row">${row.what}</th>
                <td data-side=${theirs}>${row.older}</td>
                <td data-side=${yours}>${row.newer}</td>
              </tr>`,
          )}
        </tbody>
      </table>
    </section>`;
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const yours = this.#term('review_yours');
    const waiting = this._alert ? this.#waiting().length : 0;
    const lead =
      data.differences.length === 0
        ? 'review_none'
        : data.differences.some((difference) => difference.start)
          ? 'review_lead'
          : 'review_leadOpen';
    return html`<umb-body-layout headline=${this.#term('review_headline')}>
      <div class="room">
        <p>${this.#term(lead)}</p>
        ${waiting > 0 ? html`<p class="alert" role="alert">${this.#term('review_missing', waiting)}</p>` : nothing}
        ${data.differences.map((difference, index) => this.#difference(difference, index, data.theirs, yours))}
      </div>
      <uui-button slot="actions" label=${this.#term('review_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.#term('review_save')}
        @click=${() => void this.#save()}
      ></uui-button>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-review-modal')) customElements.define('jfb-review-modal', ReviewModalElement);

export { ReviewModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-review-modal': ReviewModalElement;
  }
}
