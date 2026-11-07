// Forward... of the entry panel (ROADMAP 11.2, IA 6.3, ADR-0208): a sidebar that sends the entry to someone the site
// already trusts, a backoffice user who may view the form's entries or a team address of Settings, Email, with an
// optional message, and sensitive values only for a user who may see them and ticks them.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import type { EntryForwardRecipientsResponseModel } from '../api';
import { jfbBackofficeTokens } from '../styles/tokens';
import { skeletonLines, jfbSkeleton } from '../ui/skeleton';
import { forwardEntry, readRecipients } from './entry-panel-api';
import type { EntryForwardModalData, EntryForwardModalValue } from './entry-modals';

/** The longest message, as the server takes it. */
const MOST_MESSAGE = 2000;

export class EntryForwardModalElement extends UmbModalBaseElement<EntryForwardModalData, EntryForwardModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _recipients: { state: true },
    _failed: { state: true },
    _to: { state: true },
    _message: { state: true },
    _sensitive: { state: true },
    _sending: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      .field {
        display: grid;
        gap: var(--uui-size-space-2);
        margin-block-end: var(--uui-size-space-5);
      }

      .help {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      select {
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      select:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 1px;
      }

      uui-textarea {
        --uui-textarea-min-height: 6rem;
      }

      .failed {
        color: var(--jfb-bo-color-danger-text);
      }

      .actions {
        display: flex;
        flex-wrap: wrap;
        justify-content: flex-end;
        gap: var(--uui-size-space-2);
      }
    `,
  ];

  declare private _recipients: EntryForwardRecipientsResponseModel | undefined;
  declare private _failed: boolean;
  /** The chosen recipient: `user:<key>` or `address:<address>`. */
  declare private _to: string;
  declare private _message: string;
  declare private _sensitive: boolean;
  declare private _sending: boolean;

  constructor() {
    super();
    this._failed = false;
    this._to = '';
    this._message = '';
    this._sensitive = false;
    this._sending = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  async #read() {
    if (!this.data) return;
    this._failed = false;
    const recipients = await readRecipients(this, this.data.form, this.data.entry);
    this._failed = !recipients;
    this._recipients = recipients;
    const first = recipients?.users[0] ?? undefined;
    this._to = first ? `user:${first.key}` : recipients?.addresses[0] ? `address:${recipients.addresses[0]}` : '';
  }

  #name(): string {
    const [kind, value] = [this._to.slice(0, this._to.indexOf(':')), this._to.slice(this._to.indexOf(':') + 1)];
    return kind === 'user' ? (this._recipients?.users.find((user) => user.key === value)?.name ?? value) : value;
  }

  async #send() {
    if (!this.data || !this._to || this._sending) return;
    this._sending = true;
    const value = this._to.slice(this._to.indexOf(':') + 1);
    const sent = await forwardEntry(this, this.data.form, this.data.entry, {
      ...(this._to.startsWith('user:') ? { user: value } : { address: value }),
      message: this._message.trim() || null,
      includeSensitive: this.data.maySendSensitive && this._sensitive,
    });
    this._sending = false;
    if (!sent) {
      this._failed = true;
      return;
    }

    this.value = { to: this.#name() };
    this._submitModal();
  }

  #form() {
    const recipients = this._recipients;
    if (!recipients) {
      return this._failed
        ? html`<p class="failed" role="alert">${this.#term('forward_failed')}</p>`
        : skeletonLines(this.#term('panel_loading'), [70, 50]);
    }

    if (recipients.users.length === 0 && recipients.addresses.length === 0) {
      return html`<p>${this.#term('forward_nobody')}</p>`;
    }

    return html`<div class="field">
        <label for="to">${this.#term('forward_to')}</label>
        <select
          id="to"
          aria-describedby="to-help"
          .value=${this._to}
          @change=${(event: Event) => (this._to = (event.target as HTMLSelectElement).value)}
        >
          ${recipients.users.map(
            (user) =>
              html`<option value=${`user:${user.key}`} ?selected=${this._to === `user:${user.key}`}>
                ${user.name}
              </option>`,
          )}
          ${recipients.addresses.map(
            (address) =>
              html`<option value=${`address:${address}`} ?selected=${this._to === `address:${address}`}>
                ${address}
              </option>`,
          )}
        </select>
        <p class="help" id="to-help">${this.#term('forward_toHelp')}</p>
      </div>
      <div class="field">
        <label for="message">${this.#term('forward_message')}</label>
        <uui-textarea
          id="message"
          label=${this.#term('forward_message')}
          maxlength=${MOST_MESSAGE}
          .value=${this._message}
          @input=${(event: Event) => (this._message = (event.target as HTMLTextAreaElement).value)}
        ></uui-textarea>
      </div>
      ${
        this.data?.maySendSensitive
          ? html`<uui-checkbox
              label=${this.#term('forward_sensitive')}
              .checked=${this._sensitive}
              @change=${(event: Event) => (this._sensitive = (event.target as HTMLInputElement).checked)}
            ></uui-checkbox>`
          : nothing
      }
      ${this._failed ? html`<p class="failed" role="alert">${this.#term('forward_failed')}</p>` : nothing}`;
  }

  override render() {
    return html`<umb-body-layout headline=${this.#term('forward_headline', this.data?.number ?? 0)}>
      <uui-box>${this.#form()}</uui-box>
      <div slot="actions" class="actions">
        <uui-button label=${this.#term('notes_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.#term('forward_send')}
          ?disabled=${!this._to}
          .state=${this._sending ? 'waiting' : undefined}
          @click=${() => void this.#send()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-entry-forward-modal'))
  customElements.define('jfb-entry-forward-modal', EntryForwardModalElement);

export { EntryForwardModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-forward-modal': EntryForwardModalElement;
  }
}
