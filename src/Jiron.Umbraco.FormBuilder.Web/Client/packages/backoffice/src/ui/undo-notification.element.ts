// A toast with Undo (ROADMAP 4.13: reversible actions act at once and offer Undo; docs/ux/copy.md, Confirmations): Umbraco's own toast
// layout with one button, which runs the undo once and closes the toast. Umbraco creates it by name for a notification.
import { css, html, LitElement } from '@umbraco-cms/backoffice/external/lit';
import type { UmbNotificationHandler } from '@umbraco-cms/backoffice/notification';

/** What the toast says, the button's label, and what the button does. */
export interface UndoNotificationData {
  message: string;
  undoLabel: string;
  undo: () => Promise<void>;
}

export const UNDO_NOTIFICATION = 'jfb-undo-notification';

/** How long an Undo toast stays, longer than Umbraco's six seconds so there is time to reach the button. */
export const UNDO_DURATION_MS = 10_000;

export class UndoNotificationElement extends LitElement {
  static override properties = {
    data: { type: Object },
    notificationHandler: { attribute: false },
  };

  static override styles = css`
    #message {
      white-space: pre-line;
    }
  `;

  declare data: UndoNotificationData;
  declare notificationHandler: UmbNotificationHandler | undefined;
  #undoing = false;

  async #undo() {
    if (this.#undoing) return;
    this.#undoing = true;
    this.notificationHandler?.close();
    await this.data.undo();
  }

  override render() {
    return html`<uui-toast-notification-layout>
      <div id="message">${this.data.message}</div>
      <uui-button slot="actions" look="secondary" label=${this.data.undoLabel} @click=${this.#undo}></uui-button>
    </uui-toast-notification-layout>`;
  }
}

if (!customElements.get(UNDO_NOTIFICATION)) customElements.define(UNDO_NOTIFICATION, UndoNotificationElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-undo-notification': UndoNotificationElement;
  }
}
