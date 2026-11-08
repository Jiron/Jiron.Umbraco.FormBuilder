// A delivery's details (ROADMAP 12.4b, IA 9, ADR-0219), in a sidebar over the list: its state and when, what happened in
// the Actions view's words, its tries, whom it went to, its action and form, and its entry. A failed or uncertain one
// offers Retry to who may manage its form's actions; an uncertain one asks first, as it may deliver twice. A limited mail
// names the limit that held it back and offers no retry, as it is never sent later.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { DeliveryDetailResponseModel } from '../api';
import { ENTRY_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { confirmText } from '../ui/confirm';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readDelivery, retryDelivery } from './activity-api';
import { dateOf, fullTimeOf, loadDeliveryWords } from './activity-words';
import type { DeliveryModalData, DeliveryModalValue } from './delivery.modal.token';
import { headlineOf, isProblem, storyOf, toneOf, whatOf } from './delivery-words';

/** What the sidebar says once, at its top. */
type Alert = '' | 'failed' | 'gone' | 'changed' | 'forbidden';

export class DeliveryModalElement extends UmbModalBaseElement<DeliveryModalData, DeliveryModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _detail: { state: true },
    _alert: { state: true },
    _retrying: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    css`
      .body {
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        gap: var(--uui-size-space-5);
      }

      .state {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin: 0;
      }

      .story p {
        margin: 0 0 var(--uui-size-space-3);
      }

      .story p:last-child {
        margin-bottom: 0;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      dl {
        display: grid;
        grid-template-columns: max-content minmax(0, 1fr);
        gap: var(--uui-size-space-3) var(--uui-size-space-5);
        margin: 0;
      }

      dt {
        font-weight: 600;
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      a {
        color: var(--uui-color-interactive);
        text-decoration: underline;
      }

      a:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      code {
        font-size: 0.95em;
        overflow-wrap: anywhere;
      }
    `,
  ];

  declare private _detail: DeliveryDetailResponseModel | undefined;
  declare private _alert: Alert;
  declare private _retrying: boolean;

  constructor() {
    super();
    this._detail = undefined;
    this._alert = '';
    this._retrying = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActivity_${key}`, ...args);

  #runTerm = (key: string, ...args: unknown[]) => this.localize.term(`jfbActions_${key}`, ...args);

  #typeName = (name: string) => this.localize.string(name);

  async #read() {
    const data = this.data;
    if (!data) return;
    const [words, detail] = await Promise.all([
      loadDeliveryWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readDelivery(this, data.kind, data.id),
    ]);
    if (!words || detail === undefined) {
      this._alert = 'failed';
      return;
    }
    if (detail === 'gone') {
      this._alert = 'gone';
      this._detail = undefined;
      return;
    }
    this._detail = detail;
  }

  async #retry() {
    const data = this.data;
    const detail = this._detail;
    if (!data || !detail || this._retrying) return;
    const uncertain = detail.delivery.state === 'uncertain';
    if (uncertain) {
      const confirmed = await confirmText(this, {
        headline: this.#term('confirmHeadline'),
        content: this.#term('confirmText'),
        confirmLabel: this.#term('confirmButton'),
        color: 'danger',
      }).then(
        () => true,
        () => false,
      );
      if (!confirmed) return;
    }
    this._alert = '';
    this._retrying = true;
    const answer = await retryDelivery(this, data.kind, data.id, uncertain);
    this._retrying = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    switch (answer.kind) {
      case 'queued':
        notifications?.peek('positive', { data: { message: this.#term('retried') } });
        this.value = { retried: true };
        this._submitModal();
        return;
      case 'changed':
      case 'needsConfirmation':
        this._alert = 'changed';
        await this.#read();
        return;
      case 'forbidden':
        this._alert = 'forbidden';
        return;
      case 'gone':
        this._alert = 'gone';
        this._detail = undefined;
        return;
      default:
        notifications?.peek('danger', { data: { message: this.#term('retryFailed') } });
    }
  }

  // A sentence with a configuration key in it, the key as code.
  #withKey(line: string, key: string): TemplateResult {
    const at = line.lastIndexOf(key);
    return at < 0 ? html`${line}` : html`${line.slice(0, at)}<code>${key}</code>${line.slice(at + key.length)}`;
  }

  #body(detail: DeliveryDetailResponseModel): TemplateResult {
    const delivery = detail.delivery;
    const culture = this.localize.lang();
    const now = new Date();
    const story = storyOf(detail, now, culture, this.#term, this.#runTerm);
    const queued = dateOf(delivery.createdUtc);
    const full = queued ? fullTimeOf(queued, culture) : undefined;
    const action = whatOf(delivery, this.#term, this.#typeName);
    return html`<div class="body">
      <p class="state">
        <uui-tag look=${isProblem(delivery) ? 'primary' : 'secondary'} color=${toneOf(delivery.state)}
          >${this.#term(`state_${delivery.state}`)}</uui-tag
        >
      </p>
      ${
        story.lines.length > 0
          ? html`<div class="story">
              ${story.lines.map((line) => html`<p>${detail.limit ? this.#withKey(line, detail.limit.key) : line}</p>`)}
              ${story.tries ? html`<p class="muted">${story.tries}</p>` : nothing}
            </div>`
          : nothing
      }
      <dl>
        ${
          delivery.recipients.length > 1
            ? html`<dt>${this.#term('labelTo')}</dt>
                <dd>${delivery.recipients.join(', ')}</dd>`
            : nothing
        }
        <dt>${this.#term('labelAction')}</dt>
        <dd>${delivery.formName ? this.#term('actionIn', action, delivery.formName) : action}</dd>
        ${
          delivery.entryKey && delivery.entryNumber !== null && delivery.entryNumber !== undefined
            ? html`<dt>${this.#term('labelEntry')}</dt>
                <dd>
                  <a href=${screenPath(ENTRY_ENTITY_TYPE, delivery.entryKey)} @click=${() => this._rejectModal()}
                    >${this.#term('entryNumber', delivery.entryNumber)}</a
                  >
                </dd>`
            : nothing
        }
        ${
          full
            ? html`<dt>${this.#term('labelQueued')}</dt>
                <dd>${this.#term('whenWithZone', full.time, full.zone)}</dd>`
            : nothing
        }
      </dl>
      ${detail.retryable && !detail.mayRetry ? html`<p class="muted">${this.#term('mayNotRetry')}</p>` : nothing}
    </div>`;
  }

  #alertText(): string {
    switch (this._alert) {
      case 'failed':
        return this.#term('deliveriesFailed');
      case 'gone':
        return this.#term('deliveryGone');
      case 'changed':
        return this.#term('retryChanged');
      case 'forbidden':
        return this.#term('mayNotRetry');
      default:
        return '';
    }
  }

  override render() {
    const detail = this._detail;
    const alert = this.#alertText();
    const retry = detail?.retryable && detail.mayRetry;
    const uncertain = detail?.delivery.state === 'uncertain';
    return html`<umb-body-layout headline=${detail ? headlineOf(detail.delivery, this.#term, this.#typeName) : ''}>
      ${alert ? html`<p class="alert" role="alert">${alert}</p>` : nothing}
      ${detail ? this.#body(detail) : this._alert ? nothing : skeletonLines('', [64, 48])}
      <div slot="actions">
        ${detail || this._alert ? html`<uui-button label=${this.#term('close')} @click=${this._rejectModal}></uui-button>` : nothing}
        ${
          retry
            ? html`<uui-button
                look="primary"
                color="positive"
                label=${this.#term(uncertain ? 'retryUncertain' : 'retryNow')}
                .state=${this._retrying ? 'waiting' : undefined}
                @click=${() => void this.#retry()}
              ></uui-button>`
            : nothing
        }
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-delivery-modal')) customElements.define('jfb-delivery-modal', DeliveryModalElement);

export { DeliveryModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-delivery-modal': DeliveryModalElement;
  }
}
