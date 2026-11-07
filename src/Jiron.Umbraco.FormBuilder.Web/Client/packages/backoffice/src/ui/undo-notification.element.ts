// A toast with Undo (ROADMAP 4.13: reversible actions act at once and offer Undo; docs/ux/copy.md, Confirmations): Umbraco's own toast
// layout with one button, which runs the undo once and closes the toast. Umbraco creates it by name for a notification.
// An undo the server holds open for a time (an entry's change of state, ADR-0023) gives the toast that time: when it
// ends, the button leaves and the toast says it is too late, so it never offers an Undo the server refuses.
import { css, html, LitElement, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import type { UmbNotificationHandler } from '@umbraco-cms/backoffice/notification';
import { jfbBackofficeTokens } from '../styles/tokens';

/** What the toast says, the button's label, and what the button does. */
export interface UndoNotificationData {
  message: string;
  undoLabel: string;
  undo: () => Promise<void>;
  /** When the undo's window ends (milliseconds since the epoch), and what the toast says from then on in its place. */
  until?: number;
  tooLate?: string;
}

export const UNDO_NOTIFICATION = 'jfb-undo-notification';

/** How long an Undo toast stays, longer than Umbraco's six seconds so there is time to reach the button. */
export const UNDO_DURATION_MS = 10_000;

export class UndoNotificationElement extends LitElement {
  static override properties = {
    data: { type: Object },
    notificationHandler: { attribute: false },
    _late: { state: true },
  };

  // The toast sits in Umbraco's notification container, outside the section's elements, so it brings the tokens itself:
  // on the dark theme's positive toast, Umbraco's grey label is 4.4:1 and white 5:1 (ADR-0182).
  static override styles = [
    jfbBackofficeTokens,
    css`
      #message {
        white-space: pre-line;
        color: var(--uui-color-positive-contrast);
      }
    `,
  ];

  declare data: UndoNotificationData;
  declare notificationHandler: UmbNotificationHandler | undefined;
  declare _late: boolean;
  #undoing = false;
  #lateTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    super();
    this._late = false;
  }

  override willUpdate(changed: PropertyValues<this>) {
    if (changed.has('data')) this.#watchWindow();
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#watchWindow();
  }

  override disconnectedCallback() {
    clearTimeout(this.#lateTimer);
    super.disconnectedCallback();
  }

  /** Ends the Undo when the window does, by the clock rather than by how long the toast has stayed. */
  #watchWindow() {
    clearTimeout(this.#lateTimer);
    const until = this.data?.until;
    if (until === undefined || !this.isConnected) return;
    const left = until - Date.now();
    if (left <= 0) this._late = true;
    else this.#lateTimer = setTimeout(() => (this._late = true), left);
  }

  async #undo() {
    if (this.#undoing) return;
    this.#undoing = true;
    this.notificationHandler?.close();
    await this.data.undo();
  }

  override render() {
    const late = this._late && this.data.tooLate !== undefined;
    return html`<uui-toast-notification-layout>
      <div id="message">${late ? `${this.data.message}\n${this.data.tooLate}` : this.data.message}</div>
      ${
        late
          ? nothing
          : html`<uui-button
              slot="actions"
              look="secondary"
              label=${this.data.undoLabel}
              @click=${this.#undo}
            ></uui-button>`
      }
    </uui-toast-notification-layout>`;
  }
}

if (!customElements.get(UNDO_NOTIFICATION)) customElements.define(UNDO_NOTIFICATION, UndoNotificationElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-undo-notification': UndoNotificationElement;
  }
}
