// The Details of an action's last run (ROADMAP 10.2f, IA 5.3, ADR-0195): the card's line again, what happened, the
// tries, and what to do, never the entry. Test sends a sample entry, as the card's Test does; who may retry the run, and
// where, is said, as the Actions view offers no retry (ADR-0013, amendment). Close is its only other button.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import type { ActionRunModalData } from './action-run.modal.token';

export class ActionRunModalElement extends UmbModalBaseElement<ActionRunModalData, never> {
  static override properties = {
    _testing: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      .room {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      .part {
        display: grid;
        gap: var(--uui-size-space-1);
      }

      h4 {
        margin: 0;
        font-size: var(--uui-type-default-size);
        font-weight: 600;
      }

      p {
        margin: 0;
        overflow-wrap: anywhere;
      }

      .line[data-tone='danger'] {
        color: var(--jfb-bo-color-danger-text);
      }

      .line[data-tone='warning'] {
        color: var(--jfb-bo-color-warning-text);
      }

      .help {
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  declare private _testing: boolean;

  /** Whether Test was pressed and has not answered, its question included: a second press waits for it. */
  #pending = false;

  constructor() {
    super();
    this._testing = false;
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActions_${key}`, ...args);

  // Busy, and said, only once the test runs: a webhook asks first, and a No runs nothing (ADR-0190).
  async #test() {
    const test = this.data?.test;
    if (!test || this.#pending) return;
    this.#pending = true;
    try {
      await test(() => (this._testing = true));
    } finally {
      this.#pending = false;
      this._testing = false;
    }
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const { line, happened, tries } = data.details;
    return html`<umb-body-layout headline=${data.name}>
      <div class="room">
        <div class="part">
          <h4>${this.#term('runLast')}</h4>
          <p class="line" data-tone=${line.tone}>${line.text}</p>
        </div>
        <div class="part">
          <h4>${this.#term('runHappened')}</h4>
          <p>${happened}</p>
        </div>
        <div class="part">
          <h4>${this.#term('runTries')}</h4>
          <p>${tries}</p>
        </div>
        <div class="part">
          <h4>${this.#term('runWhatNow')}</h4>
          ${
            data.test
              ? html`<p class="help">${this.#term(data.webhook ? 'runTestHelp_webhook' : 'runTestHelp')}</p>`
              : nothing
          }
          <p class="help">${this.#term('runRetryWho')}</p>
        </div>
        <div class="visually-hidden" role="status">${this._testing ? this.#term('testing') : ''}</div>
      </div>
      ${
        data.test
          ? html`<uui-button
              slot="footer-info"
              look="outline"
              label=${this.#term('test')}
              .state=${this._testing ? 'waiting' : undefined}
              @click=${() => void this.#test()}
            ></uui-button>`
          : nothing
      }
      <uui-button slot="actions" label=${this.localize.term('general_close')} @click=${this._rejectModal}></uui-button>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-action-run-modal')) customElements.define('jfb-action-run-modal', ActionRunModalElement);

export { ActionRunModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-action-run-modal': ActionRunModalElement;
  }
}
